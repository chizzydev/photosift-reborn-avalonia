namespace PhotoSiftReborn;

/// <summary>
/// Bounded LRU of encoded, display-normalized PNG previews, NOT native bitmaps.
/// The UI owns and disposes each Bitmap independently; cache can never dispose a
/// still-displayed image. Decoded/native allocations are separately limited.
/// </summary>
internal sealed class PreviewCache
{
    internal readonly record struct Key(Guid PhotoId, string Path, long Length, long WriteTicks);
    private sealed record Entry(Key Key, byte[] Png);
    private readonly int _maxBytes;
    private readonly int _maxEntries;
    private readonly LinkedList<Entry> _list = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _lookup = new();
    internal long Bytes { get; private set; }
    internal int Count => _lookup.Count;
    internal long Hits { get; private set; }
    internal long Evictions { get; private set; }

    internal PreviewCache(int maxBytes = 48 * 1024 * 1024, int maxEntries = 12)
    {
        if (maxBytes < 1024 || maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes; _maxEntries = maxEntries;
    }
    internal bool TryGet(Key key, out byte[]? bytes)
    {
        if (_lookup.TryGetValue(key, out var node))
        {
            _list.Remove(node); _list.AddFirst(node);
            bytes = node.Value.Png;
            Hits++;
            return true;
        }
        bytes = null;
        return false;
    }
    internal void Put(Key key, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > _maxBytes) return;
        if (_lookup.TryGetValue(key, out var current)) Drop(current);
        var node = _list.AddFirst(new Entry(key, bytes));
        _lookup[key] = node; Bytes += bytes.Length;
        while (Bytes > _maxBytes || _lookup.Count > _maxEntries)
        {
            Drop(_list.Last!); Evictions++;
        }
    }
    private void Drop(LinkedListNode<Entry> node)
    {
        _lookup.Remove(node.Value.Key);
        Bytes -= node.Value.Png.Length;
        _list.Remove(node);
    }
    internal void Clear()
    {
        _list.Clear(); _lookup.Clear(); Bytes = 0;
    }
}
