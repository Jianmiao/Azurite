using System;
using System.Collections.Generic;

namespace Azurite;

// Row cleanup removes the eviction node as well as the entry. Repeated native
// rebuilds therefore cannot leave an unbounded queue of dead native identities.
internal sealed class BoundedRowCache<TKey, TValue> where TKey : notnull
{
	private readonly int _capacity;
	private readonly Dictionary<TKey, (TValue Value, LinkedListNode<TKey> Node)> _entries = new();
	private readonly LinkedList<TKey> _order = new();
	internal BoundedRowCache(int capacity) => _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
	internal int Count => _entries.Count;
	internal int ResidentNodes => _order.Count;
	internal bool TryGetValue(TKey key, out TValue value)
	{
		if (_entries.TryGetValue(key, out var item)) { value = item.Value; return true; }
		value = default!; return false;
	}
	internal int Store(TKey key, TValue value)
	{
		int evicted = 0;
		Remove(key);
		if (_entries.Count >= _capacity) { Remove(_order.First!.Value); evicted++; }
		var node = _order.AddLast(key);
		_entries.Add(key, (value, node));
		return evicted;
	}
	internal bool Remove(TKey key)
	{
		if (!_entries.Remove(key, out var item)) return false;
		_order.Remove(item.Node); return true;
	}
	internal int Prune(Func<TValue, bool> isAlive, int budget)
	{
		int removed = 0, count = Math.Min(Math.Max(0, budget), Count);
		for (int i = 0; i < count; i++)
		{
			var node = _order.First!;
			var item = _entries[node.Value];
			if (!isAlive(item.Value)) { Remove(node.Value); removed++; }
			else { _order.Remove(node); _order.AddLast(node); }
		}
		return removed;
	}
	internal void Clear() { _entries.Clear(); _order.Clear(); }
}
