namespace Fallery.Services;

// 缓存只持有有限的最近使用数据；卡片回收不再决定缓存的寿命。
public sealed class BoundedCache<TKey, TValue>(long capacity, int countLimit) where TKey : notnull
{
    private sealed record Entry(TKey Key, TValue Value, long Weight);
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recent = new();
    private readonly object _gate = new();
    private long _weight;

    public long Weight { get { lock (_gate) return _weight; } }
    public int Count { get { lock (_gate) return _entries.Count; } }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value, long weight = 1)
    {
        lock (_gate)
        {
            if (_entries.Remove(key, out var old))
            {
                _recent.Remove(old);
                _weight -= old.Value.Weight;
            }
            if (weight <= 0 || weight > capacity || countLimit <= 0) return;
            while (_recent.Last is { } last && (_weight > capacity - weight || _entries.Count >= countLimit))
            {
                _entries.Remove(last.Value.Key);
                _recent.RemoveLast();
                _weight -= last.Value.Weight;
            }
            _entries.Add(key, _recent.AddFirst(new Entry(key, value, weight)));
            _weight += weight;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recent.Clear();
            _weight = 0;
        }
    }

    public void RemoveWhere(Func<TKey, bool> predicate)
    {
        lock (_gate)
        {
            foreach (var key in _entries.Keys.Where(predicate).ToArray())
            {
                var node = _entries[key];
                _entries.Remove(key); _recent.Remove(node); _weight -= node.Value.Weight;
            }
        }
    }
}
