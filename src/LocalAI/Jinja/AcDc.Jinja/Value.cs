// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AcDc.Jinja;

internal class Value : IEquatable<Value>, IComparable<Value>
{
	public delegate Value CallableType(Context context, ArgumentsValue args);

	private readonly IList<Value>? _array;

	private readonly OrderedDictionary<Value, Value>? _object;

	private readonly CallableType? _callable;

	private readonly object? _primitive;

	public static readonly Value Null = new Value();

	[MemberNotNullWhen(true, "_object")]
	public bool IsObject
	{
		[MemberNotNullWhen(true, "_object")]
		get
		{
			return _object != null;
		}
	}

	[MemberNotNullWhen(true, "_callable")]
	public bool IsCallable
	{
		[MemberNotNullWhen(true, "_callable")]
		get
		{
			return _callable != null;
		}
	}

	[MemberNotNullWhen(true, "_primitive")]
	public bool IsString
	{
		[MemberNotNullWhen(true, "_primitive")]
		get
		{
			return _primitive is string;
		}
	}

	[MemberNotNullWhen(true, "_primitive")]
	[MemberNotNullWhen(true, "_primitive")]
	public bool IsBoolean
	{
		[MemberNotNullWhen(true, "_primitive")]
		[MemberNotNullWhen(true, "_primitive")]
		get
		{
			return _primitive is bool;
		}
	}

	public bool IsNull
	{
		get
		{
			if (_object == null && _array == null && _primitive == null)
			{
				return _callable == null;
			}
			return false;
		}
	}

	public bool IsNumber
	{
		get
		{
			if (!IsInteger)
			{
				return IsDouble;
			}
			return true;
		}
	}

	[MemberNotNullWhen(true, "_primitive")]
	public bool IsInteger
	{
		[MemberNotNullWhen(true, "_primitive")]
		get
		{
			return _primitive is long;
		}
	}

	[MemberNotNullWhen(true, "_primitive")]
	public bool IsDouble
	{
		[MemberNotNullWhen(true, "_primitive")]
		get
		{
			return _primitive is double;
		}
	}

	public bool IsPrimitive
	{
		get
		{
			if (_object == null && _array == null)
			{
				return _callable == null;
			}
			return false;
		}
	}

	public bool IsIterable
	{
		get
		{
			if (!IsArray && !IsObject)
			{
				return IsString;
			}
			return true;
		}
	}

	[MemberNotNullWhen(true, "_array")]
	public bool IsArray
	{
		[MemberNotNullWhen(true, "_array")]
		get
		{
			return _array != null;
		}
	}

	private bool IsHashable => IsPrimitive;

	public int Count
	{
		get
		{
			if (IsObject)
			{
				return _object.Count;
			}
			if (IsArray)
			{
				return _array.Count;
			}
			if (IsString)
			{
				return ((string)_primitive).Length;
			}
			throw new JinjaException("Value is not an array or object: " + Dump());
		}
	}

	public Value this[int index]
	{
		get
		{
			if (IsNull)
			{
				throw new JinjaException("Undefined value or reference");
			}
			if (IsArray)
			{
				return _array[index];
			}
			if (IsObject)
			{
				return _object[index];
			}
			throw new JinjaException("Value is not an array or object: " + Dump());
		}
	}

	public IEnumerable<Value> Keys
	{
		get
		{
			if (!IsObject)
			{
				throw new JinjaException("Value is not an object: " + Dump());
			}
			return _object.Keys;
		}
	}

	private Value()
	{
	}

	private Value(IList<Value> array)
	{
		_array = array;
	}

	private Value(OrderedDictionary<Value, Value> @object)
	{
		_object = @object;
	}

	private Value(CallableType callable)
	{
		_object = new OrderedDictionary<Value, Value>();
		_callable = callable;
	}

	public Value(Value value)
	{
		if (value.IsObject)
		{
			_object = new OrderedDictionary<Value, Value>(value._object, value._object.Comparer);
		}
		else if (value.IsArray)
		{
			IList<Value>? array = value._array;
			int count = array.Count;
			List<Value> list = new List<Value>(count);
			CollectionsMarshal.SetCount(list, count);
			Span<Value> span = CollectionsMarshal.AsSpan(list);
			int num = 0;
			foreach (Value item in array)
			{
				span[num] = item;
				num++;
			}
			_array = list;
		}
		else
		{
			_primitive = value._primitive;
		}
	}

	public void ForEach(Action<Value> action)
	{
		if (IsNull)
		{
			throw new JinjaException("Undefined value or reference");
		}
		if (_array != null)
		{
			foreach (Value item in _array)
			{
				action(item);
			}
			return;
		}
		if (_object != null)
		{
			foreach (KeyValuePair<Value, Value> item2 in _object)
			{
				action(item2.Key);
			}
			return;
		}
		if (IsString)
		{
			string text = (string)_primitive;
			for (int i = 0; i < text.Length; i++)
			{
				action(new Value(text[i].ToString()));
			}
			return;
		}
		throw new JinjaException("Value is not iterable: " + Dump());
	}

	public Value(string value)
	{
		_primitive = value;
	}

	public Value(long value)
	{
		_primitive = value;
	}

	public Value(bool value)
	{
		_primitive = value;
	}

	public Value(double value)
	{
		_primitive = value;
	}

	public bool ToBoolean()
	{
		if (IsNull)
		{
			return false;
		}
		if (IsBoolean)
		{
			return Get<bool>();
		}
		if (IsInteger)
		{
			return Get<long>() != 0;
		}
		if (IsDouble)
		{
			return Get<double>() != 0.0;
		}
		if (IsString)
		{
			return !string.IsNullOrEmpty(Get<string>());
		}
		if (IsArray)
		{
			return Count > 0;
		}
		return true;
	}

	public void Set(Value key, Value value)
	{
		if (_object == null)
		{
			throw new JinjaException("Value is not an object: " + Dump());
		}
		if (!key.IsHashable)
		{
			throw new JinjaException("Unhashable type: " + Dump());
		}
		_object[key] = value;
	}

	public Value Get(Value key)
	{
		if (_array != null)
		{
			if (!key.IsInteger)
			{
				return Null;
			}
			long num = key.Get<long>();
			return _array[(int)((num < 0) ? (_array.Count + num) : num)];
		}
		if (_object != null)
		{
			if (!key.IsHashable)
			{
				throw new JinjaException("Unhashable type: " + key.Dump());
			}
			if (_object.TryGetValue(key, out Value value))
			{
				return value;
			}
		}
		return Null;
	}

	public Value Get(string key)
	{
		return Get(new Value(key));
	}

	public bool Contains(Value value)
	{
		if (IsNull)
		{
			throw new JinjaException("Undefined value or reference");
		}
		if (_array != null)
		{
			foreach (Value item in _array)
			{
				if (item.ToBoolean() && item.Equals(value))
				{
					return true;
				}
			}
			return false;
		}
		if (_object != null)
		{
			if (!value.IsHashable)
			{
				throw new JinjaException("Unhashable type: " + value.Dump());
			}
			return _object.ContainsKey(value);
		}
		throw new JinjaException("Contains can only be called on arrays or objects: " + Dump());
	}

	public void Set(string key, long value)
	{
		Set(new Value(key), new Value(value));
	}

	public void Set(string key, CallableType value)
	{
		Set(new Value(key), new Value(value));
	}

	public void Set(string key, bool value)
	{
		Set(new Value(key), new Value(value));
	}

	public void Set(string key, Value value)
	{
		Set(new Value(key), value);
	}

	public T Get<T>()
	{
		if (IsPrimitive)
		{
			return (T)_primitive;
		}
		throw new JinjaException("Get<T> is not defined for this value type " + Dump());
	}

	public string Dump(int indent = -1, bool toJson = false, string item_sep = ", ", string key_sep = ": ")
	{
		using StringWriter stringWriter = new StringWriter();
		Dump(stringWriter, indent, 0, toJson, item_sep, key_sep);
		return stringWriter.ToString();
	}

	public static Value FromArray(IEnumerable<Value>? values = null)
	{
		List<Value> list = new List<Value>();
		if (values != null)
		{
			list.AddRange(values);
		}
		return new Value(list);
	}

	public static Value Callable(CallableType callable)
	{
		return new Value(callable);
	}

	public static Value Object()
	{
		return new Value(new OrderedDictionary<Value, Value>());
	}

	public Value Call(Context context, ArgumentsValue args)
	{
		if (_callable == null)
		{
			throw new JinjaException("Value is not callable: " + Dump());
		}
		return _callable(context, args);
	}

	public void Add(Value item)
	{
		if (_array == null)
		{
			throw new JinjaException("Value is not an array " + Dump());
		}
		_array.Add(item);
	}

	// LocalAI change: serialize like Hugging Face's tojson (ensure_ascii=False): quotes become \" and
	// non-ASCII text stays as it is, where System.Text.Json's default encoder writes " and \uXXXX.
	private static readonly JsonSerializerOptions HuggingFaceJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	private void Dump(TextWriter writer, int indent, int level, bool toJson, string item_sep, string key_sep)
	{
		char c = (toJson ? '"' : '\'');
		if (IsNull)
		{
			writer.Write("null");
		}
		else if (_array != null)
		{
			writer.Write('[');
			PrintIndent(level + 1);
			for (int i = 0; i < _array.Count; i++)
			{
				if (i > 0)
				{
					PrintSubSeparator();
				}
				_array[i].Dump(writer, indent, level + 1, toJson, item_sep, key_sep);
			}
			PrintIndent(level);
			writer.Write(']');
		}
		else if (_object != null)
		{
			writer.Write('{');
			PrintIndent(level + 1);
			bool flag = true;
			foreach (KeyValuePair<Value, Value> item in _object)
			{
				if (!flag)
				{
					PrintSubSeparator();
				}
				flag = false;
				if (item.Key.IsString)
				{
					DumpString(item.Key, writer, c);
				}
				else
				{
					writer.Write(c);
					writer.Write(item.Key.Dump());
					writer.Write(c);
				}
				writer.Write(key_sep);
				item.Value.Dump(writer, indent, level + 1, toJson, item_sep, key_sep);
			}
			PrintIndent(level);
			writer.Write('}');
		}
		else
		{
			if (_callable != null)
			{
				throw new JinjaException("Cannot dump callable to JSON");
			}
			if (IsBoolean && !toJson)
			{
				writer.Write(ToBoolean() ? "True" : "False");
			}
			else if (IsString && !toJson)
			{
				DumpString(this, writer, c);
			}
			else
			{
				writer.Write(JsonSerializer.Serialize(_primitive, HuggingFaceJson));
			}
		}
		void PrintIndent(int num2)
		{
			if (indent > 0)
			{
				writer.Write('\n');
				int j = 0;
				for (int num = num2 * indent; j < num; j++)
				{
					writer.Write(' ');
				}
			}
		}
		void PrintSubSeparator()
		{
			if (indent < 0)
			{
				writer.Write(item_sep);
			}
			else
			{
				writer.Write(',');
				PrintIndent(level + 1);
			}
		}
	}

	private static void DumpString(Value primitive, TextWriter writer, char stringQuote = '\'')
	{
		if (!(primitive._primitive is string text))
		{
			throw new JinjaException($"Value is not a string: {primitive._primitive}");
		}
		writer.Write(stringQuote);
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == stringQuote)
			{
				writer.Write('\\');
			}
			writer.Write(text[i]);
		}
		writer.Write(stringQuote);
	}

	public bool Contains(string value)
	{
		if (_array != null)
		{
			return false;
		}
		if (_object != null)
		{
			return _object.ContainsKey(new Value(value));
		}
		throw new JinjaException("Contains can only be called on arrays or objects: " + Dump());
	}

	public Value Pop(Value index)
	{
		if (IsArray)
		{
			if (_array.Count == 0)
			{
				throw new JinjaException("pop from empty list");
			}
			if (index.IsNull)
			{
				Value result = _array[_array.Count - 1];
				_array.RemoveAt(_array.Count - 1);
				return result;
			}
			if (!index.IsInteger)
			{
				throw new JinjaException("pop index must be an integer: " + index.Dump());
			}
			long num = index.Get<long>();
			if (num < 0 || num >= _array.Count)
			{
				throw new JinjaException("pop index out of range: " + index.Dump());
			}
			if (num < 0)
			{
				num += _array.Count;
			}
			Value result2 = _array[(int)num];
			_array.RemoveAt((int)num);
			return result2;
		}
		if (IsObject)
		{
			if (!index.IsHashable)
			{
				throw new JinjaException("Unhashable type: " + index.Dump());
			}
			if (!_object.TryGetValue(index, out Value value))
			{
				throw new JinjaException("Key not found: " + index.Dump());
			}
			_object.Remove(index);
			return value;
		}
		throw new JinjaException("Value is not an array or object: " + Dump());
	}

	public void Insert(int index, Value item)
	{
		if (!IsArray)
		{
			throw new JinjaException("Value is not an array: " + Dump());
		}
		_array.Insert(index, item);
	}

	public bool Equals(Value? other)
	{
		if ((object)other == null)
		{
			return false;
		}
		if ((_callable != null || other._callable != null) && _callable != other._callable)
		{
			return false;
		}
		if (_array != null)
		{
			if (other._array == null)
			{
				return false;
			}
			if (_array.Count != other._array.Count)
			{
				return false;
			}
			for (int i = 0; i < _array.Count; i++)
			{
				if (!_array[i].ToBoolean() || !other._array[i].ToBoolean() || !_array[i].Equals(other._array[i]))
				{
					return false;
				}
			}
			return true;
		}
		if (_object != null)
		{
			if (other._object == null)
			{
				return false;
			}
			if (_object.Count != other._object.Count)
			{
				return false;
			}
			foreach (KeyValuePair<Value, Value> item in _object)
			{
				if (!item.Value.ToBoolean() || !other._object.TryGetValue(item.Key, out Value value) || !value.ToBoolean() || !item.Value.Equals(value))
				{
					return false;
				}
			}
			return true;
		}
		if (_primitive == null || other._primitive == null)
		{
			if (_primitive == null)
			{
				return other._primitive == null;
			}
			return false;
		}
		return _primitive.Equals(other._primitive);
	}

	public override bool Equals(object? obj)
	{
		if (obj is Value other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		if (_array != null)
		{
			return _array.Count;
		}
		if (_object != null)
		{
			return _object.Count;
		}
		if (_primitive != null)
		{
			return _primitive.GetHashCode();
		}
		if (_callable != null)
		{
			return _callable.GetHashCode();
		}
		return 0;
	}

	public override string ToString()
	{
		if (IsString)
		{
			return Get<string>();
		}
		if (IsInteger)
		{
			return Get<long>().ToString();
		}
		if (IsDouble)
		{
			return Get<double>().ToString();
		}
		if (IsBoolean)
		{
			if (!ToBoolean())
			{
				return "False";
			}
			return "True";
		}
		if (IsNull)
		{
			return "none";
		}
		return Dump();
	}

	public int CompareTo(Value? other)
	{
		if (IsNull)
		{
			throw new JinjaException("Undefined value or reference");
		}
		if ((object)other != null)
		{
			if (IsInteger && other.IsInteger)
			{
				return Get<long>().CompareTo(other.Get<long>());
			}
			if (IsDouble && other.IsDouble)
			{
				return Get<double>().CompareTo(other.Get<double>());
			}
			if (IsString && other.IsString)
			{
				return string.Compare(Get<string>(), other.Get<string>(), StringComparison.InvariantCulture);
			}
		}
		throw new JinjaException("Cannot compare values: " + Dump() + " and " + (other?.Dump() ?? "null"));
	}

	public static bool operator ==(Value left, Value right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(Value left, Value right)
	{
		return !left.Equals(right);
	}

	public static bool operator <(Value left, Value right)
	{
		return left.CompareTo(right) < 0;
	}

	public static bool operator >(Value left, Value right)
	{
		return left.CompareTo(right) > 0;
	}

	public static bool operator <=(Value left, Value right)
	{
		return left.CompareTo(right) <= 0;
	}

	public static bool operator >=(Value left, Value right)
	{
		return left.CompareTo(right) >= 0;
	}

	public static Value operator -(Value value)
	{
		if (value.IsInteger)
		{
			return new Value(-value.Get<long>());
		}
		if (value.IsDouble)
		{
			return new Value(0.0 - value.Get<double>());
		}
		throw new JinjaException("Unary minus not supported for type: " + value.Dump());
	}

	public static Value operator +(Value left, Value right)
	{
		if (left.IsString && right.IsString)
		{
			return new Value(left.Get<string>() + right.Get<string>());
		}
		if (left.IsInteger && right.IsInteger)
		{
			return new Value(left.Get<long>() + right.Get<long>());
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(left.Get<double>() + right.Get<double>());
		}
		if (left.IsArray && right.IsArray)
		{
			Value result = FromArray();
			left.ForEach((Value item) =>
			{
				result.Add(item);
			});
			right.ForEach((Value item) =>
			{
				result.Add(item);
			});
			return result;
		}
		throw new JinjaException("Cannot add values: " + left.Dump() + " + " + right.Dump());
	}

	public static Value operator -(Value left, Value right)
	{
		if (left.IsInteger && right.IsInteger)
		{
			return new Value(left.Get<long>() - right.Get<long>());
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(left.Get<double>() - right.Get<double>());
		}
		throw new JinjaException("Cannot subtract values: " + left.Dump() + " - " + right.Dump());
	}

	public static Value operator *(Value left, Value right)
	{
		if (left.IsString && right.IsInteger)
		{
			string element = left.Get<string>();
			long num = right.Get<long>();
			if (num < 0)
			{
				num = 0L;
			}
			return new Value(string.Concat(Enumerable.Repeat(element, (int)num)));
		}
		if (left.IsInteger && right.IsInteger)
		{
			return new Value(left.Get<long>() * right.Get<long>());
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(left.Get<double>() * right.Get<double>());
		}
		throw new JinjaException("Cannot multiply values: " + left.Dump() + " * " + right.Dump());
	}

	public static Value operator %(Value left, Value right)
	{
		if (left.IsInteger && right.IsInteger)
		{
			return new Value(left.Get<long>() % right.Get<long>());
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(left.Get<double>() % right.Get<double>());
		}
		throw new JinjaException("Cannot compute modulus of values: " + left.Dump() + " % " + right.Dump());
	}

	public static Value operator /(Value left, Value right)
	{
		if (left.IsInteger && right.IsInteger)
		{
			return new Value((double)left.Get<long>() / (double)right.Get<long>());
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(left.Get<double>() / right.Get<double>());
		}
		throw new JinjaException("Cannot divide values: " + left.Dump() + " / " + right.Dump());
	}

	public static Value FloorDiv(Value left, Value right)
	{
		if (left.IsInteger && right.IsInteger)
		{
			return new Value(left.Get<long>() / right.Get<long>());
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(Math.Floor(left.Get<double>() / right.Get<double>()));
		}
		throw new JinjaException("Cannot divide values: " + left.Dump() + " // " + right.Dump());
	}

	public static Value Pow(Value left, Value right)
	{
		if (left.IsInteger && right.IsInteger)
		{
			return new Value(Math.Pow(left.Get<long>(), right.Get<long>()));
		}
		if (left.IsDouble && right.IsDouble)
		{
			return new Value(Math.Pow(left.Get<double>(), right.Get<double>()));
		}
		throw new JinjaException("Cannot exponentiate values: " + left.Dump() + " ** " + right.Dump());
	}
}
