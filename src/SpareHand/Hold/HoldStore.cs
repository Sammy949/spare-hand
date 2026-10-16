using System.Text.Json;

namespace SpareHand.Hold;

internal sealed record HoldItem(string Path, DateTimeOffset AddedAt)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Exists => File.Exists(Path) || Directory.Exists(Path);
}

/// <summary>
/// The Hold shelf's contents: references to files, never copies. Nothing is ever copied, moved, modified or
/// expired. Research: Dropshelf turned held files into 0 KB files, NotchDrop deletes them after a day, NotchNook
/// moves the original on drag-out (docs/research.md, docs/research.md). Removals are kept in batches so they can be undone.
/// </summary>
internal sealed class HoldStore
{
    private const int RecentlyRemovedBatches = 20;

    private readonly string _path = AppPaths.File("hold.json");
    private readonly string _removedPath = AppPaths.File("hold-removed.json");
    private readonly List<HoldItem> _items;
    private readonly List<List<HoldItem>> _removed;

    public HoldStore()
    {
        _items = Load(_path, AppJson.Default.ListHoldItem) ?? [];
        _removed = Load(_removedPath, AppJson.Default.ListListHoldItem) ?? [];
    }

    public event Action? Changed;

    public IReadOnlyList<HoldItem> Items => _items;

    public bool CanUndoRemove => _removed.Count > 0;

    public int Add(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            if (_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _items.Insert(0, new HoldItem(path, DateTimeOffset.Now));
            added++;
        }

        Save();
        return added;
    }

    /// <summary>Removes the given paths as one undoable batch.</summary>
    public int Remove(IEnumerable<string> paths)
    {
        var set = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var batch = _items.Where(i => set.Contains(i.Path)).ToList();
        if (batch.Count == 0)
        {
            return 0;
        }

        _items.RemoveAll(i => set.Contains(i.Path));
        _removed.Insert(0, batch);
        if (_removed.Count > RecentlyRemovedBatches)
        {
            _removed.RemoveRange(RecentlyRemovedBatches, _removed.Count - RecentlyRemovedBatches);
        }

        Save();
        return batch.Count;
    }

    public int Clear() => Remove(_items.Select(i => i.Path).ToList());

    /// <summary>Removes without an undo entry: for the guide's practice file, which is deleted from disk too.</summary>
    public void Forget(string path)
    {
        if (_items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    /// <summary>Puts the most recently removed batch back where it was.</summary>
    public int UndoRemove()
    {
        if (_removed.Count == 0)
        {
            return 0;
        }

        var batch = _removed[0];
        _removed.RemoveAt(0);
        foreach (var item in batch.Where(b => !_items.Any(i => string.Equals(i.Path, b.Path, StringComparison.OrdinalIgnoreCase))))
        {
            _items.Add(item);
        }

        _items.Sort((a, b) => b.AddedAt.CompareTo(a.AddedAt));
        Save();
        return batch.Count;
    }

    /// <summary>Points a missing item at its new location, keeping its place in the list.</summary>
    public void Relocate(string oldPath, string newPath)
    {
        var index = _items.FindIndex(i => string.Equals(i.Path, oldPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _items[index] = _items[index] with { Path = Path.GetFullPath(newPath) };
            Save();
        }
    }

    private void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_items, AppJson.Default.ListHoldItem));
        File.WriteAllText(_removedPath, JsonSerializer.Serialize(_removed, AppJson.Default.ListListHoldItem));
        Changed?.Invoke();
    }

    private static T? Load<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), type) : default;
        }
        catch (Exception e)
        {
            Log.Write($"{Path.GetFileName(path)} unreadable, starting empty: {e.Message}");
            return default;
        }
    }
}
