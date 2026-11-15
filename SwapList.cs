using System;
using System.Collections;
using System.Collections.Generic;

namespace gesh;

/// <summary>A list where removing leaves a hole that the next Add reuses, so
/// indices of the other items stay valid.</summary>
class SwapList<T>
{
	public List<T> values = new();
	/// <summary>Removed slots, reused oldest first.</summary>
	Queue<int> free = new();
	/// <summary>Per slot, so IsAlive doesn't have to search the free queue.</summary>
	List<bool> alive = new();

	public int Add(T value)
	{
		if (free.Count == 0)
		{
			values.Add(value);
			alive.Add(true);
			return values.Count - 1;
		}

		int idx = free.Dequeue();
		values[idx] = value;
		alive[idx] = true;
		return idx;
	}

	public T Remove(int index)
	{
		if (!alive[index]) throw new InvalidOperationException($"Slot {index} was already removed");
		alive[index] = false;
		free.Enqueue(index);
		return values[index];
	}

	/// <summary>False for removed slots. Throws for indices that were never
	/// handed out, since asking about one is a bug.</summary>
	public bool IsAlive(int index) => alive[index];

	/// <summary>Indices of the items that haven't been removed.</summary>
	public IndexEnumerable Indices() => new(this);

	/// <summary>The items that haven't been removed.</summary>
	public ItemEnumerable Items() => new(this);

	public IEnumerable<int> FreeSlots() => free;

	/// <summary>An independent copy, slots and free order included, so
	/// indices mean the same in both. For undo.</summary>
	public SwapList<T> Clone() => new() { values = [.. values], free = new(free), alive = [.. alive] };

	// The mesh loops over these every frame, so they're structs: a foreach
	// over them allocates nothing and calls no delegates, unlike LINQ, which
	// measured slow on the Quest (plans/perf-log.md). LINQ still works on them
	// through IEnumerable, for code that isn't hot.

	public readonly struct IndexEnumerable(SwapList<T> list) : IEnumerable<int>
	{
		public IndexEnumerator GetEnumerator() => new(list);
		IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	public readonly struct ItemEnumerable(SwapList<T> list) : IEnumerable<T>
	{
		public ItemEnumerator GetEnumerator() => new(list);
		IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	public struct IndexEnumerator(SwapList<T> list) : IEnumerator<int>
	{
		int index = -1;

		public readonly int Current => index;
		readonly object IEnumerator.Current => index;

		public bool MoveNext()
		{
			List<bool> alive = list.alive;
			do index++; while (index < alive.Count && !alive[index]);
			return index < alive.Count;
		}

		public void Reset() => index = -1;
		public readonly void Dispose() { }
	}

	public struct ItemEnumerator(SwapList<T> list) : IEnumerator<T>
	{
		IndexEnumerator indices = new(list);

		public readonly T Current => list.values[indices.Current];
		readonly object? IEnumerator.Current => Current;

		public bool MoveNext() => indices.MoveNext();
		public void Reset() => indices.Reset();
		public readonly void Dispose() { }
	}
}
