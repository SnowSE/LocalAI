// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace AcDc.Jinja;

internal class OrderedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IEnumerable, IReadOnlyDictionary<TKey, TValue>, IReadOnlyCollection<KeyValuePair<TKey, TValue>>, IList<KeyValuePair<TKey, TValue>>, IReadOnlyList<KeyValuePair<TKey, TValue>> where TKey : notnull
{
	private readonly Dictionary<TKey, TValue> _inner;

	private readonly List<TKey> _order;

	public IEqualityComparer<TKey> Comparer => _inner.Comparer;

	public TValue this[TKey key]
	{
		get
		{
			return _inner[key];
		}
		set
		{
			_inner[key] = value;
			if (IndexOf(key) < 0)
			{
				_order.Add(key);
			}
		}
	}

	public TValue this[int index]
	{
		get
		{
			TKey key = _order[index];
			return _inner[key];
		}
		set
		{
			TKey key = _order[index];
			_inner[key] = value;
		}
	}

	public ICollection<TKey> Keys => _order;

	public ICollection<TValue> Values
	{
		get
		{
			List<TValue> list = new List<TValue>();
			list.AddRange(_order.Select((TKey key) => _inner[key]));
			return list;
		}
	}

	public int Count => _inner.Count;

	public bool IsReadOnly => false;

	IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

	IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

	KeyValuePair<TKey, TValue> IReadOnlyList<KeyValuePair<TKey, TValue>>.this[int index] => ((IList<KeyValuePair<TKey, TValue>>)this)[index];

	KeyValuePair<TKey, TValue> IList<KeyValuePair<TKey, TValue>>.this[int index]
	{
		get
		{
			TKey key = _order[index];
			return new KeyValuePair<TKey, TValue>(key, _inner[key]);
		}
		set
		{
			TKey key = _order[index];
			_order[index] = value.Key;
			_inner.Remove(key);
			_inner.Add(value.Key, value.Value);
		}
	}

	public OrderedDictionary()
	{
		_inner = new Dictionary<TKey, TValue>();
		_order = new List<TKey>();
	}

	public OrderedDictionary(IEqualityComparer<TKey>? comparer)
	{
		_inner = new Dictionary<TKey, TValue>(comparer);
		_order = new List<TKey>();
	}

	public OrderedDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer)
	{
		_inner = new Dictionary<TKey, TValue>(dictionary, comparer);
		_order = dictionary.Keys.ToList();
	}

	public void Add(TKey key, TValue value)
	{
		_inner.Add(key, value);
		_order.Add(key);
	}

	public void Add(KeyValuePair<TKey, TValue> item)
	{
		_inner.Add(item.Key, item.Value);
		_order.Add(item.Key);
	}

	public void Clear()
	{
		_inner.Clear();
		_order.Clear();
	}

	public bool Contains(KeyValuePair<TKey, TValue> item)
	{
		return ((ICollection<KeyValuePair<TKey, TValue>>)_inner).Contains(item);
	}

	public bool ContainsKey(TKey key)
	{
		return _inner.ContainsKey(key);
	}

	public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
	{
		foreach (TKey item in _order)
		{
			array[arrayIndex++] = new KeyValuePair<TKey, TValue>(item, _inner[item]);
		}
	}

	public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
	{
		foreach (TKey item in _order)
		{
			yield return new KeyValuePair<TKey, TValue>(item, _inner[item]);
		}
	}

	private int IndexOf(TKey key)
	{
		for (int i = 0; i < _order.Count; i++)
		{
			if (_inner.Comparer.Equals(_order[i], key))
			{
				return i;
			}
		}
		return -1;
	}

	private void RemoveKeyFromOrderList(TKey key)
	{
		int num = IndexOf(key);
		if (num < 0)
		{
			throw new InvalidOperationException($"Key {key} not found in order list");
		}
		_order.RemoveAt(num);
	}

	public bool Remove(TKey key)
	{
		if (_inner.Remove(key))
		{
			RemoveKeyFromOrderList(key);
			return true;
		}
		return false;
	}

	public bool Remove(KeyValuePair<TKey, TValue> item)
	{
		if (((ICollection<KeyValuePair<TKey, TValue>>)_inner).Remove(item))
		{
			RemoveKeyFromOrderList(item.Key);
			return true;
		}
		return false;
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
	{
		return _inner.TryGetValue(key, out value);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public int IndexOf(KeyValuePair<TKey, TValue> item)
	{
		int num = IndexOf(item.Key);
		if (num >= 0)
		{
			TValue x = _inner[item.Key];
			if (EqualityComparer<TValue>.Default.Equals(x, item.Value))
			{
				return num;
			}
		}
		return -1;
	}

	public void Insert(int index, KeyValuePair<TKey, TValue> item)
	{
		_inner.Add(item.Key, item.Value);
		_order.Insert(index, item.Key);
	}

	public void RemoveAt(int index)
	{
		TKey key = _order[index];
		_order.RemoveAt(index);
		_inner.Remove(key);
	}
}
