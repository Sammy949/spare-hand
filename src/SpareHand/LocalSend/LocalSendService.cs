using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SpareHand.LocalSend;

internal sealed record LocalSendDevice(string Ip, string Alias, string DeviceType, string DeviceModel, string Fingerprint, string Scheme, int Port, DateTimeOffset LastSeen);

internal enum SendOutcome
{
    Sent,
    Declined,
    Busy,
    PinRequired,
    Unreachable,
    Failed,
    Cancelled,
}

/// <summary>
/// LocalSend protocol v2 client (github.com/localsend/protocol). Discovery = multicast announce/listen plus an
/// HTTPS scan of the local /24, which finds phones that don't answer multicast (docs/research.md).
/// </summary>
internal sealed class LocalSendService
{
    private const int Port = 53317;
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.167");
    private static readonly TimeSpan ScanThrottle = TimeSpan.FromSeconds(30);

    private readonly LocalSendIdentity _identity;
    private readonly ConcurrentDictionary<string, LocalSendDevice> _devices = new();
    private readonly HttpClient _probe;
    private readonly HttpClient _transfer;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private int _scanning;

    public LocalSendService(LocalSendIdentity identity)
    {
        _identity = identity;
        _probe = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromSeconds(2) };
        _transfer = new HttpClient(NewHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Raised (on a worker thread) whenever a device is found or refreshed.</summary>
    public event Action? DevicesChanged;

    public IReadOnlyList<LocalSendDevice> RecentDevices(TimeSpan within) =>
        _devices.Values.Where(d => DateTimeOffset.UtcNow - d.LastSeen < within).OrderBy(d => d.Alias).ToList();

    public void Start()
    {
        _ = Task.Run(ListenMulticastAsync);
        Refresh();
    }

    /// <summary>
    /// The installed LocalSend app (for its icon), from its uninstall entry. Spare Hand speaks the protocol
    /// itself, so the app is not required.
    /// </summary>
    public static string? FindAppExe()
    {
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (var name in uninstall?.GetSubKeyNames() ?? [])
            {
                using var key = uninstall!.OpenSubKey(name);
                if ((key?.GetValue("DisplayName") as string)?.StartsWith("LocalSend", StringComparison.OrdinalIgnoreCase) == true &&
                    key.GetValue("DisplayIcon") is string icon)
                {
                    var exe = icon.Split(',')[0].Trim('"');
                    if (File.Exists(exe))
                    {
                        return exe;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Announce ourselves and rescan the subnet: at most every 30 s, or every 5 s when forced (picker open).</summary>
    public void Refresh(bool force = false)
    {
        var throttle = force ? TimeSpan.FromSeconds(5) : ScanThrottle;
        if (DateTimeOffset.UtcNow - _lastScan < throttle || Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            return;
        }

        _lastScan = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                await AnnounceAsync();
                await ScanSubnetAsync();
            }
            catch (Exception e)
            {
                Log.Write($"localsend refresh failed: {e.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        });
    }

    public async Task<SendOutcome> SendAsync(LocalSendDevice device, IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        var baseUrl = $"{device.Scheme}://{device.Ip}:{device.Port}/api/localsend/v2";
        var files = new JsonObject();
        var ids = new Dictionary<string, string>();
        foreach (var path in paths.Where(File.Exists))
        {
            var id = Guid.NewGuid().ToString("N");
            ids[id] = path;
            files[id] = new JsonObject
            {
                ["id"] = id,
                ["fileName"] = Path.GetFileName(path),
                ["size"] = new FileInfo(path).Length,
                ["fileType"] = MimeFor(path),
            };
        }

        if (ids.Count == 0)
        {
            return SendOutcome.Failed;
        }

        string? sessionId = null;
        try
        {
            var body = new JsonObject { ["info"] = Info(), ["files"] = files };
            using var prep = await _transfer.PostAsync($"{baseUrl}/prepare-upload", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
            Log.Write($"localsend prepare-upload to {device.Alias}: {(int)prep.StatusCode}");
            switch ((int)prep.StatusCode)
            {
                case 204: return SendOutcome.Sent; // receiver needs none of the files
                case 401: return SendOutcome.PinRequired;
                case 403: return SendOutcome.Declined;
                case 409: return SendOutcome.Busy;
                case not 200: return SendOutcome.Failed;
            }

            var session = JsonNode.Parse(await prep.Content.ReadAsStringAsync(ct))!;
            sessionId = (string)session["sessionId"]!;
            foreach (var (id, token) in session["files"]!.AsObject())
            {
                await using var stream = File.OpenRead(ids[id]);
                using var up = await _transfer.PostAsync($"{baseUrl}/upload?sessionId={sessionId}&fileId={id}&token={(string)token!}", new StreamContent(stream), ct);
                if (!up.IsSuccessStatusCode)
                {
                    Log.Write($"localsend upload {Path.GetFileName(ids[id])} -> {(int)up.StatusCode}");
                    return SendOutcome.Failed;
                }
            }

            return SendOutcome.Sent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Before the receiver accepted, dropping the request is all we can do; after, the protocol has a
            // cancel endpoint (protocol README §4.3) so the receiver stops waiting too.
            if (sessionId is not null)
            {
                try
                {
                    using var cancelTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var cancel = await _transfer.PostAsync($"{baseUrl}/cancel?sessionId={sessionId}", null, cancelTimeout.Token);
                    Log.Write($"localsend cancel sent to {device.Alias}: {(int)cancel.StatusCode}");
                }
                catch (Exception e)
                {
                    Log.Write($"localsend cancel to {device.Alias} failed: {e.Message}");
                }
            }
            else
            {
                Log.Write($"localsend send to {device.Alias} cancelled before it was accepted");
            }

            return SendOutcome.Cancelled;
        }
        catch (HttpRequestException e)
        {
            Log.Write($"localsend send to {device.Alias} unreachable: {e.Message}");
            return SendOutcome.Unreachable;
        }
    }

    private HttpClientHandler NewHandler()
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,

            // LocalSend uses self-signed certificates; pin the fingerprint the device announced, when we know it.
            ServerCertificateCustomValidationCallback = (request, cert, _, _) =>
            {
                if (cert is null)
                {
                    return false;
                }

                var host = request.RequestUri?.Host;
                return host is null || !_devices.TryGetValue(host, out var known) || string.IsNullOrEmpty(known.Fingerprint) ||
                       string.Equals(known.Fingerprint, Convert.ToHexString(SHA256.HashData(cert.RawData)), StringComparison.OrdinalIgnoreCase);
            },
        };
        handler.ClientCertificates.Add(_identity.Certificate);
        return handler;
    }

    private JsonObject Info() => new()
    {
        ["alias"] = $"Spare Hand ({Environment.MachineName})",
        ["version"] = "2.1",
        ["deviceModel"] = "Windows",
        ["deviceType"] = "desktop",
        ["fingerprint"] = _identity.Fingerprint,
        ["port"] = Port,
        ["protocol"] = "https",
        ["download"] = false,
    };

    private async Task AnnounceAsync()
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var announce = Info();
        announce["announce"] = true;
        var bytes = Encoding.UTF8.GetBytes(announce.ToJsonString());
        await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(Group, Port));
    }

    private async Task ListenMulticastAsync()
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            foreach (var local in LocalAddresses())
            {
                udp.JoinMulticastGroup(Group, local.Address);
            }

            while (true)
            {
                var r = await udp.ReceiveAsync();
                var j = JsonNode.Parse(Encoding.UTF8.GetString(r.Buffer));
                if (j is null || (string?)j["fingerprint"] == _identity.Fingerprint)
                {
                    continue;
                }

                Remember(r.RemoteEndPoint.Address.ToString(), j, (string?)j["protocol"] ?? "https");
            }
        }
        catch (Exception e)
        {
            Log.Write($"localsend multicast listener stopped: {e.Message}");
        }
    }

    private async Task ScanSubnetAsync()
    {
        var locals = LocalAddresses().ToList();
        var targets = locals.SelectMany(l =>
        {
            var b = l.Address.GetAddressBytes();
            return Enumerable.Range(1, 254).Select(i => new IPAddress([b[0], b[1], b[2], (byte)i])).Where(ip => !ip.Equals(l.Address));
        }).Distinct().ToList();

        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 64 }, async (ip, ct) =>
        {
            foreach (var scheme in new[] { "https", "http" })
            {
                try
                {
                    var j = JsonNode.Parse(await _probe.GetStringAsync($"{scheme}://{ip}:{Port}/api/localsend/v2/info", ct)) as JsonObject;
                    if (j is not null && (string?)j["fingerprint"] != _identity.Fingerprint)
                    {
                        Remember(ip.ToString(), j, scheme);
                    }

                    return;
                }
                catch
                {
                    // Not a LocalSend device (or not reachable); keep scanning.
                }
            }
        });
        Log.Write($"localsend scan done: {targets.Count} addresses, {_devices.Count} device(s) known");

        // The scan's connections and TLS buffers pulled idle memory from 12 MB back to 17 MB; give it back.
        MemoryTrim.Idle("localsend scan");
    }

    private void Remember(string ip, JsonNode j, string scheme)
    {
        // Our own machine's LocalSend app is not a useful destination.
        if (LocalAddresses().Any(l => l.Address.ToString() == ip))
        {
            return;
        }

        var device = new LocalSendDevice(
            ip,
            (string?)j["alias"] ?? ip,
            (string?)j["deviceType"] ?? "desktop",
            (string?)j["deviceModel"] ?? string.Empty,
            (string?)j["fingerprint"] ?? string.Empty,
            scheme,
            (int?)j["port"] ?? Port,
            DateTimeOffset.UtcNow);
        var isNew = !_devices.ContainsKey(ip);
        _devices[ip] = device;
        if (isNew)
        {
            Log.Write($"localsend device: {device.Alias} ({device.DeviceType}, {device.DeviceModel})");
        }

        DevicesChanged?.Invoke();
    }

    private static IEnumerable<UnicastIPAddressInformation> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) && !n.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address) && u.PrefixLength >= 24);

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };
}
