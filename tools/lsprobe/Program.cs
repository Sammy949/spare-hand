using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Usage:
//   LsProbe discover                  -> list LocalSend devices (multicast announce + subnet scan)
//   LsProbe send <ip> <file>          -> prepare-upload + upload one file to that device
const int Port = 53317;

// LocalSend identity: a self-signed certificate. Its SHA-256 is our fingerprint, and we present it as the TLS
// client certificate (newer LocalSend servers request one; Windows' schannel fails the handshake without it).
using var rsa = RSA.Create(2048);
var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Spare Hand", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var ephemeral = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
// schannel needs a persisted key for client auth; round-trip through PFX.
var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(ephemeral.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
var fingerprint = Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();

HttpClientHandler NewHandler()
{
    var h = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true, ClientCertificateOptions = ClientCertificateOption.Manual };
    h.ClientCertificates.Add(cert);
    return h;
}

if (args.Length == 2 && args[0] == "info")
{
    using var c = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromSeconds(5) };
    try { Console.WriteLine(await c.GetStringAsync($"https://{args[1]}:{Port}/api/localsend/v2/info")); }
    catch (Exception e) { Console.WriteLine($"info failed: {e.GetBaseException().Message}"); }
    return;
}
var me = new JsonObject
{
    ["alias"] = "Spare Hand (probe)",
    ["version"] = "2.1",
    ["deviceModel"] = "Windows",
    ["deviceType"] = "desktop",
    ["fingerprint"] = fingerprint,
    ["port"] = Port,
    ["protocol"] = "https",
    ["download"] = false,
};

// LocalSend uses self-signed certificates; the probe accepts them (the real app will pin the announced fingerprint).
var http = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromSeconds(90) };

if (args.Length > 0 && args[0] == "discover")
{
    var found = new Dictionary<string, string>();
    var localIps = NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(u => (n.Name, u)))
        .Where(x => x.u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.u.Address) && x.u.PrefixLength >= 16)
        .ToList();
    foreach (var (name, u) in localIps) Console.WriteLine($"interface {name}: {u.Address}/{u.PrefixLength}");

    // 1) Multicast announce + listen.
    var multicastTask = Task.Run(async () =>
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            var group = IPAddress.Parse("224.0.0.167");
            foreach (var (_, u) in localIps) udp.JoinMulticastGroup(group, u.Address);
            var announce = me.DeepClone().AsObject();
            announce["announce"] = true;
            var bytes = Encoding.UTF8.GetBytes(announce.ToJsonString());
            await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(group, Port));
            Console.WriteLine("multicast: announced, listening 12 s");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!cts.IsCancellationRequested)
            {
                var r = await udp.ReceiveAsync(cts.Token);
                var j = JsonNode.Parse(Encoding.UTF8.GetString(r.Buffer));
                if ((string?)j?["fingerprint"] == fingerprint) continue;
                lock (found) found[r.RemoteEndPoint.Address.ToString()] = $"multicast: {j?["alias"]} ({j?["deviceModel"]}, {j?["deviceType"]})";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Console.WriteLine($"multicast error: {e.Message}"); }
    });

    // 2) Subnet scan of /api/localsend/v2/info (what LocalSend calls its legacy HTTP discovery).
    var scan = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromSeconds(2) };
    var targets = localIps.Where(x => x.u.PrefixLength >= 24).SelectMany(x =>
    {
        var b = x.u.Address.GetAddressBytes();
        return Enumerable.Range(1, 254).Select(i => new IPAddress(new[] { b[0], b[1], b[2], (byte)i })).Where(ip => !ip.Equals(x.u.Address));
    }).Distinct().ToList();
    Console.WriteLine($"http scan: {targets.Count} addresses");
    await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 64 }, async (ip, ct) =>
    {
        foreach (var scheme in new[] { "https", "http" })
        {
            try
            {
                var j = await scan.GetFromJsonAsync<JsonObject>($"{scheme}://{ip}:{Port}/api/localsend/v2/info", ct);
                lock (found) found[ip.ToString()] = $"http({scheme}): {j?["alias"]} ({j?["deviceModel"]}, {j?["deviceType"]}, v{j?["version"]})";
                return;
            }
            catch { }
        }
    });
    await multicastTask;
    Console.WriteLine(found.Count == 0 ? "no devices found" : string.Join("\n", found.Select(kv => $"FOUND {kv.Key} -> {kv.Value}")));
}
else if (args.Length == 3 && args[0] == "send")
{
    var ip = args[1];
    var path = Path.GetFullPath(args[2]);
    var bytes = await File.ReadAllBytesAsync(path);
    var fileId = "f1";
    var body = new JsonObject
    {
        ["info"] = me.DeepClone(),
        ["files"] = new JsonObject
        {
            [fileId] = new JsonObject
            {
                ["id"] = fileId,
                ["fileName"] = Path.GetFileName(path),
                ["size"] = bytes.Length,
                ["fileType"] = "text/plain",
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            },
        },
    };

    string? scheme = null;
    foreach (var s in new[] { "https", "http" })
    {
        try { await http.GetAsync($"{s}://{ip}:{Port}/api/localsend/v2/info"); scheme = s; break; } catch { }
    }

    if (scheme is null) { Console.WriteLine("device not reachable"); return; }
    Console.WriteLine($"prepare-upload via {scheme} (accept on the phone; waiting up to 90 s)...");
    var prep = await http.PostAsync($"{scheme}://{ip}:{Port}/api/localsend/v2/prepare-upload", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    var prepText = await prep.Content.ReadAsStringAsync();
    Console.WriteLine($"prepare-upload -> {(int)prep.StatusCode} {prepText}");
    if (prep.StatusCode != HttpStatusCode.OK) return;

    var pj = JsonNode.Parse(prepText)!;
    var session = (string)pj["sessionId"]!;
    var token = (string)pj["files"]![fileId]!;
    var up = await http.PostAsync($"{scheme}://{ip}:{Port}/api/localsend/v2/upload?sessionId={session}&fileId={fileId}&token={token}", new ByteArrayContent(bytes));
    Console.WriteLine($"upload -> {(int)up.StatusCode} {await up.Content.ReadAsStringAsync()}");
}
else
{
    Console.WriteLine("usage: discover | send <ip> <file>");
}
