using System;
using System.Collections.Generic;
using System.Linq;

namespace gesh;

class SwapList<T>
{
	public List<T> values = new();
	public Queue<int> free = new();

	public int Add(T value)
	{
		if (free.Count == 0)
		{
			values.Add(value);
			return values.Count() - 1;
		}

		int idx = free.Dequeue();
		values[idx] = value;
		return idx;
	}

	public T Remove(int index)
	{
		if (!free.Contains(index))
		{
			free.Enqueue(index);
		}
		return values[index];
	}

	public IEnumerable<T> Items()
	{
		int i = 0;
		return values.Where((_) => !free.Contains(i++));
	}

	public void Map()
	{

	}
}
