// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcDc.Jinja;

internal sealed class Context
{
	private readonly Value _values;

	private readonly Context? _parent;

	internal Context(Value values, Context? parent = null)
	{
		if (!values.IsObject)
		{
			throw new JinjaException("Context values must be an object: " + values.Dump());
		}
		_values = values;
		_parent = parent;
	}

	private bool Contains(Value value)
	{
		if (_values.Contains(value))
		{
			return true;
		}
		if (_parent != null)
		{
			return _parent.Contains(value);
		}
		return false;
	}

	internal bool Contains(string value)
	{
		return Contains(new Value(value));
	}

	internal Value Get(Value value)
	{
		if (_values.Contains(value))
		{
			return _values.Get(value);
		}
		if (_parent != null)
		{
			return _parent.Get(value);
		}
		return Value.Null;
	}

	internal Value Get(string value)
	{
		return Get(new Value(value));
	}

	internal void Set(Value name, Value value)
	{
		_values.Set(name, value);
	}

	internal void Set(string name, Value value)
	{
		Set(new Value(name), value);
	}

	public void Set(string name, string value)
	{
		Set(new Value(name), new Value(value));
	}

	internal void DestructuringAssign(IReadOnlyCollection<string> variableNames, Value item)
	{
		if (variableNames.Count == 1)
		{
			Value name = new Value(variableNames.Single());
			Set(name, item);
			return;
		}
		if (!item.IsArray || item.Count != variableNames.Count)
		{
			throw new JinjaException("Mismatched number of variables and items in destructuring assignment");
		}
		int num = 0;
		foreach (string variableName in variableNames)
		{
			Set(variableName, item[num++]);
		}
	}

	internal static Context BuiltIns()
	{
		Value value = Value.Object();
		value.Set("raise_exception", SimpleFunction("raise_exception", new string[] { "message" }, (Context context, Value argsObj) =>
		{
			throw new JinjaException(argsObj.Get("message").Get<string>());
		}));
		value.Set("tojson", SimpleFunction("tojson", (new string[4] { "value", "indent", "ensure_ascii", "separators" }), (Context context, Value argsObj) =>
		{
			string item_sep = ", ";
			string key_sep = ": ";
			if (argsObj.Contains("separators"))
			{
				Value value4 = argsObj.Get("separators");
				if (value4.IsArray && value4.Count == 2)
				{
					item_sep = value4[0].Get<string>();
					key_sep = value4[1].Get<string>();
				}
			}
			long num = (argsObj.Contains("indent") ? argsObj.Get("indent").Get<long>() : (-1));
			return new Value(argsObj.Get("value").Dump((int)num, toJson: true, item_sep, key_sep));
		}));
		value.Set("items", SimpleFunction("items", new string[] { "object" }, (Context context, Value argsObj) =>
		{
			Value value4 = Value.FromArray();
			Value value5 = argsObj.Get("object");
			if (!value5.IsObject)
			{
				throw new JinjaException("Can only get item pairs from a mapping");
			}
			foreach (Value key2 in value5.Keys)
			{
				value4.Add(Value.FromArray((new Value[2]
				{
					new Value(key2),
					value5.Get(new Value(key2))
				})));
			}
			return value4;
		}));
		value.Set("first", SimpleFunction("first", new string[] { "items" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("items");
			if (!value4.IsArray)
			{
				throw new JinjaException("object is not a list");
			}
			return (value4.Count == 0) ? Value.Null : value4[0];
		}));
		value.Set("last", SimpleFunction("last", new string[] { "items" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("items");
			if (!value4.IsArray)
			{
				throw new JinjaException("object is not a list");
			}
			return (value4.Count == 0) ? Value.Null : value4[value4.Count - 1];
		}));
		value.Set("trim", SimpleFunction("trim", new string[] { "text" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("text");
			return (!value4.IsNull) ? new Value(StringUtils.Strip(value4.Get<string>())) : value4;
		}));
		value.Set("capitalize", SimpleFunction("capitalize", new string[] { "text" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("text");
			return (!value4.IsNull) ? new Value(StringUtils.Capitalize(value4.Get<string>())) : value4;
		}));
		value.Set("lower", CharTransformFunction("lower", char.ToLowerInvariant));
		value.Set("upper", CharTransformFunction("upper", char.ToUpperInvariant));
		value.Set("default", Value.Callable((Context context, ArgumentsValue args) =>
		{
			args.ExpectArgs("default", (Min: 2, Max: 3), (Min: 0, Max: 1));
			Value value4 = args.Args[0];
			Value value5 = args.Args[1];
			bool flag = false;
			if (args.Args.Count == 3)
			{
				flag = args.Args[2].Get<bool>();
			}
			else
			{
				Value item = args.Kwargs.FirstOrDefault(((string Name, Value Value) k) => k.Name == "boolean").Value;
				if ((object)item != null && !item.IsNull)
				{
					flag = item.Get<bool>();
				}
			}
			if (!flag)
			{
				if (!value4.IsNull)
				{
					return value4;
				}
				return value5;
			}
			return (!value4.ToBoolean()) ? value5 : value4;
		}));
		Value value2 = SimpleFunction("escape", new string[] { "text" }, (Context context, Value argsObj) => new Value(StringUtils.HtmlEscape(argsObj.Get("text").Get<string>())));
		value.Set("e", value2);
		value.Set("escape", value2);
		value.Set("joiner", SimpleFunction("joiner", new string[] { "sep" }, (Context context, Value argsObj) =>
		{
			string sep = (argsObj.Contains(new Value("sep")) ? argsObj.Get("sep").Get<string>() : "");
			bool first = true;
			return SimpleFunction("", Array.Empty<string>(), (Context ctx, Value _) =>
			{
				if (first)
				{
					first = false;
					return new Value("");
				}
				return new Value(sep);
			});
		}));
		value.Set("count", SimpleFunction("count", new string[] { "items" }, (Context context, Value argsObj) => new Value(argsObj.Get("items").Count)));
		value.Set("dictsort", SimpleFunction("dictsort", new string[] { "value" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("value");
			Value value5 = Value.FromArray();
			foreach (Value item3 in value4.Keys.OrderBy((Value k) => k))
			{
				value5.Add(Value.FromArray((new Value[2]
				{
					new Value(item3),
					value4.Get(new Value(item3))
				})));
			}
			return value5;
		}));
		value.Set("join", SimpleFunction("join", (new string[2] { "items", "d" }), (Context context, Value argsObj) =>
		{
			string sep = (argsObj.Contains(new Value("d")) ? argsObj.Get("d").Get<string>() : "");
			return argsObj.Contains(new Value("items")) ? DoJoin(argsObj.Get("items"), sep) : SimpleFunction("", new string[] { "items" }, (Context ctx, Value value5) =>
			{
				Value value4 = value5.Get("items");
				if (!value4.ToBoolean() || !value4.IsArray)
				{
					throw new JinjaException("join expects an array for items, got: " + value4.Dump());
				}
				return DoJoin(value4, sep);
			});
		}));
		value.Set("namespace", Value.Callable((Context context, ArgumentsValue args) =>
		{
			Value value4 = Value.Object();
			args.ExpectArgs("namespace", (Min: 0, Max: 0), (Min: 0, Max: int.MaxValue));
			foreach (var (key, value5) in args.Kwargs)
			{
				value4.Set(key, value5);
			}
			return value4;
		}));
		Value value3 = SimpleFunction("equalto", (new string[2] { "expected", "actual" }), (Context context, Value argsObj) => new Value(argsObj.Get("actual") == argsObj.Get("expected")));
		value.Set("equalto", value3);
		value.Set("==", value3);
		value.Set("length", SimpleFunction("length", new string[] { "items" }, (Context context, Value argsObj) => new Value(argsObj.Get("items").Count)));
		value.Set("safe", SimpleFunction("safe", new string[] { "value" }, (Context context, Value argsObj) => new Value(argsObj.Get("value").ToString())));
		value.Set("string", SimpleFunction("string", new string[] { "value" }, (Context context, Value argsObj) => new Value(argsObj.Get("value").ToString())));
		value.Set("int", SimpleFunction("int", new string[] { "value" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("value");
			long result;
			if (value4.IsNull)
			{
				result = 0L;
			}
			else if (value4.IsBoolean)
			{
				result = (value4.Get<bool>() ? 1 : 0);
			}
			else if (value4.IsInteger)
			{
				result = value4.Get<long>();
			}
			else if (value4.IsDouble)
			{
				result = (long)value4.Get<double>();
			}
			else if (value4.IsString)
			{
				if (!long.TryParse(value4.Get<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
				{
					result = 0L;
				}
			}
			else
			{
				result = 0L;
			}
			return new Value(result);
		}));
		value.Set("list", SimpleFunction("list", new string[] { "items" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("items");
			if (!value4.IsArray)
			{
				throw new JinjaException("object is not iterable");
			}
			return value4;
		}));
		value.Set("in", SimpleFunction("in", (new string[2] { "item", "items" }), (Context context, Value argsObj) => new Value(ValueUtils.In(argsObj.Get("item"), argsObj.Get("items")))));
		value.Set("unique", SimpleFunction("unique", new string[] { "items" }, (Context context, Value argsObj) =>
		{
			Value value4 = argsObj.Get("items");
			if (!value4.IsArray)
			{
				throw new JinjaException("object is not iterable");
			}
			HashSet<Value> hashSet = new HashSet<Value>();
			Value value5 = Value.FromArray();
			for (int i = 0; i < value4.Count; i++)
			{
				if (hashSet.Add(value4[i]))
				{
					value5.Add(value4[i]);
				}
			}
			return value5;
		}));
		value.Set("select", SelectOrReject(isSelect: true));
		value.Set("reject", SelectOrReject(isSelect: false));
		value.Set("map", Value.Callable((Context context, ArgumentsValue args) =>
		{
			Value value4 = Value.FromArray();
			if (args.Args.Count == 1 && ((args.Kwargs.Any(((string Name, Value Value) k) => k.Name == "attribute") && args.Kwargs.Count == 1) || (args.Kwargs.Any(((string Name, Value Value) k) => k.Name == "default") && args.Kwargs.Count == 2)))
			{
				Value value5 = args.Args[0];
				Value item = args.Kwargs.FirstOrDefault(((string Name, Value Value) k) => k.Name == "attribute").Value;
				Value item2 = args.Kwargs.FirstOrDefault(((string Name, Value Value) k) => k.Name == "default").Value;
				for (int num = 0; num < value5.Count; num++)
				{
					Value value6 = value5[num].Get(item);
					value4.Add(value6.IsNull ? item2 : value6);
				}
			}
			else
			{
				if (args.Kwargs.Count != 0 || args.Args.Count < 2)
				{
					throw new JinjaException("Invalid or unsupported arguments for map");
				}
				Value value7 = context.Get(args.Args[1].ToString() ?? "");
				if (value7.IsNull)
				{
					throw new JinjaException("Undefined filter: " + args.Args[1].Dump());
				}
				ArgumentsValue argumentsValue = new ArgumentsValue
				{
					Args = { Value.Null }
				};
				for (int num2 = 2; num2 < args.Args.Count; num2++)
				{
					argumentsValue.Args.Add(args.Args[num2]);
				}
				for (int num3 = 0; num3 < args.Args[0].Count; num3++)
				{
					Value value8 = args.Args[0][num3];
					argumentsValue.Args[0] = value8;
					value4.Add(value7.Call(context, argumentsValue));
				}
			}
			return value4;
		}));
		value.Set("indent", SimpleFunction("indent", (new string[3] { "text", "indent", "first" }), (Context context, Value argsObj) =>
		{
			string text = argsObj.Get("text").Get<string>();
			bool flag = argsObj.Contains(new Value("first")) && argsObj.Get("first").Get<bool>();
			string value4 = new string(' ', (int)(argsObj.Contains(new Value("indent")) ? argsObj.Get("indent").Get<long>() : 0));
			StringBuilder stringBuilder = new StringBuilder();
			bool flag2 = true;
			using (StringReader stringReader = new StringReader(text))
			{
				string value5;
				while ((value5 = stringReader.ReadLine()) != null)
				{
					bool flag3 = !flag2 | flag;
					if (flag2)
					{
						flag2 = false;
					}
					else
					{
						stringBuilder.Append('\n');
					}
					if (flag3)
					{
						stringBuilder.Append(value4);
					}
					stringBuilder.Append(value5);
				}
			}
			if (!string.IsNullOrEmpty(text) && text.EndsWith('\n'))
			{
				stringBuilder.Append('\n');
			}
			return new Value(stringBuilder.ToString());
		}));
		value.Set("selectattr", SelectOrRejectAttr(isSelect: true));
		value.Set("rejectattr", SelectOrRejectAttr(isSelect: false));
		value.Set("range", Value.Callable((Context context, ArgumentsValue args) =>
		{
			long[] array = new long[3];
			bool[] array2 = new bool[3];
			if (args.Args.Count == 1)
			{
				array[1] = args.Args[0].Get<long>();
				array2[1] = true;
			}
			else
			{
				for (int i = 0; i < args.Args.Count; i++)
				{
					array[i] = args.Args[i].Get<long>();
					array2[i] = true;
				}
			}
			foreach (var kwarg in args.Kwargs)
			{
				string item = kwarg.Name;
				Value item2 = kwarg.Value;
				int num = item switch
				{
					"start" => 0, 
					"end" => 1, 
					"step" => 2, 
					_ => throw new JinjaException("Unknown argument " + item + " for function range"), 
				};
				if (array2[num])
				{
					throw new JinjaException("Duplicate argument " + item + " for function range");
				}
				array[num] = item2.Get<long>();
				array2[num] = true;
			}
			if (!array2[1])
			{
				throw new JinjaException("Missing required argument 'end' for function range");
			}
			long num2 = (array2[0] ? array[0] : 0);
			long num3 = array[1];
			long num4 = (array2[2] ? array[2] : 1);
			Value value4 = Value.FromArray();
			if (num4 > 0)
			{
				for (long num5 = num2; num5 < num3; num5 += num4)
				{
					value4.Add(new Value(num5));
				}
			}
			else
			{
				for (long num6 = num2; num6 > num3; num6 += num4)
				{
					value4.Add(new Value(num6));
				}
			}
			return value4;
		}));
		value.Set("strftime_now", Value.Callable((Context context, ArgumentsValue args) =>
		{
			args.ExpectArgs("strftime_now", (Min: 1, Max: 1));
			string text = args.Args[0].Get<string>();
			DateTimeOffset now = DateTimeOffset.Now;
			StringBuilder sb = new StringBuilder();
			int num = 0;
			while (num < text.Length)
			{
				char c = text[num++];
				if (c == '%')
				{
					c = text[num++];
					switch (c)
					{
					case '%':
						sb.Append(c);
						break;
					case 'n':
						sb.Append('\n');
						break;
					case 't':
						sb.Append('\t');
						break;
					case 'Y':
						sb.Append("yyyy");
						break;
					case 'y':
						sb.Append("yy");
						break;
					case 'm':
						sb.Append("MM");
						break;
					case 'b':
						sb.Append("MMM");
						break;
					case 'B':
						sb.Append("MMMM");
						break;
					case 'd':
						sb.Append("dd");
						break;
					case 'j':
						sb.Append("ddd");
						break;
					case 'a':
						sb.Append("ddd");
						break;
					case 'A':
						sb.Append("dddd");
						break;
					case 'H':
						sb.Append("HH");
						break;
					case 'I':
						sb.Append("hh");
						break;
					case 'M':
						sb.Append("mm");
						break;
					case 'S':
						sb.Append("ss");
						break;
					case 'f':
						sb.Append("ffffff");
						break;
					case 'p':
						sb.Append("tt");
						break;
					case 'z':
						sb.Append("zzz");
						break;
					case 'Z':
						sb.Append('K');
						break;
					case 'w':
						sb.Append((int)now.DayOfWeek);
						break;
					case 'V':
						sb.Append(GetIsoWeekNumber(now));
						break;
					case 'U':
						sb.Append(GetWeekOfYear(now, DayOfWeek.Sunday));
						break;
					case 'W':
						sb.Append(GetWeekOfYear(now, DayOfWeek.Monday));
						break;
					case 'c':
						sb.Append('G');
						break;
					case 'x':
						sb.Append('d');
						break;
					case 'X':
						sb.Append('T');
						break;
					default:
						AppendSafe(c);
						break;
					}
				}
				else
				{
					AppendSafe(c);
				}
			}
			return new Value(now.ToString(sb.ToString(), CultureInfo.InvariantCulture));
			void AppendSafe(char ch)
			{
				if ("FHKMdfghmstyz%:/\"'\\".Contains(ch))
				{
					sb.Append('\\');
				}
				sb.Append(ch);
			}
		}));
		return new Context(value);
		static Value CharTransformFunction(string name, Func<char, char> fn)
		{
			return SimpleFunction(name, new string[] { "text" }, (Context context, Value argsObj) =>
			{
				Value value4 = argsObj.Get("text");
				return value4.IsNull ? value4 : new Value(new string(new ReadOnlySpan<char>(value4.Get<string>().Select(fn).ToArray())));
			});
		}
		static Value DoJoin(Value items, string sep)
		{
			if (!items.IsArray)
			{
				throw new JinjaException("object is not iterable: " + items.Dump());
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < items.Count; i++)
			{
				if (i > 0)
				{
					stringBuilder.Append(sep);
				}
				stringBuilder.Append(items[i]);
			}
			return new Value(stringBuilder.ToString());
		}
		static int GetIsoWeekNumber(DateTimeOffset date)
		{
			return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(date.UtcDateTime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
		}
		static int GetWeekOfYear(DateTimeOffset date, DayOfWeek firstDayOfWeek)
		{
			return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(date.UtcDateTime, CalendarWeekRule.FirstFullWeek, firstDayOfWeek);
		}
		static Value MakeFilter(Value filter, Value extraArgs)
		{
			return SimpleFunction("", new string[] { "value" }, (Context context, Value argsObj) =>
			{
				Value item = argsObj.Get("value");
				ArgumentsValue argumentsValue = new ArgumentsValue
				{
					Args = { item }
				};
				for (int i = 0; i < extraArgs.Count; i++)
				{
					argumentsValue.Args.Add(extraArgs[i]);
				}
				return filter.Call(context, argumentsValue);
			});
		}
		static Value SelectOrReject(bool isSelect)
		{
			return Value.Callable((Context context, ArgumentsValue args) =>
			{
				args.ExpectArgs(isSelect ? "select" : "reject", (Min: 2, Max: int.MaxValue), (Min: 0, Max: 0));
				Value value4 = args.Args[0];
				if (value4.IsNull)
				{
					return Value.FromArray();
				}
				if (!value4.IsArray)
				{
					throw new JinjaException("object is not iterable: " + value4.Dump());
				}
				Value value5 = context.Get(args.Args[1].ToString() ?? "");
				if (value5.IsNull)
				{
					throw new JinjaException("Undefined filter: " + args.Args[1].Dump());
				}
				Value value6 = Value.FromArray();
				for (int i = 2; i < args.Args.Count; i++)
				{
					value6.Add(args.Args[i]);
				}
				Value value7 = MakeFilter(value5, value6);
				Value value8 = Value.FromArray();
				for (int j = 0; j < value4.Count; j++)
				{
					Value item = value4[j];
					if (value7.Call(context, new ArgumentsValue
					{
						Args = { item }
					}).ToBoolean() == isSelect)
					{
						value8.Add(item);
					}
				}
				return value8;
			});
		}
		static Value SelectOrRejectAttr(bool isSelect)
		{
			return Value.Callable((Context context, ArgumentsValue args) =>
			{
				args.ExpectArgs(isSelect ? "selectattr" : "rejectattr", (Min: 2, Max: int.MaxValue), (Min: 0, Max: 0));
				Value value4 = args.Args[0];
				if (value4.IsNull)
				{
					return Value.FromArray();
				}
				if (!value4.IsArray)
				{
					throw new JinjaException("object is not iterable: " + value4.Dump());
				}
				string value5 = args.Args[1].Get<string>();
				bool flag = args.Args.Count >= 3;
				Value value6 = null;
				ArgumentsValue argumentsValue = new ArgumentsValue
				{
					Args = { Value.Null }
				};
				if (flag)
				{
					value6 = context.Get(args.Args[2].ToString() ?? "");
					if (value6.IsNull)
					{
						throw new JinjaException("Undefined test: " + args.Args[2].Dump());
					}
					for (int i = 3; i < args.Args.Count; i++)
					{
						argumentsValue.Args.Add(args.Args[i]);
					}
					argumentsValue.Kwargs.AddRange(args.Kwargs);
				}
				Value value7 = Value.FromArray();
				for (int j = 0; j < value4.Count; j++)
				{
					Value value8 = value4[j];
					Value value9 = value8.Get(new Value(value5));
					if (flag)
					{
						argumentsValue.Args[0] = value9;
						if (value6.Call(context, argumentsValue).ToBoolean() == isSelect)
						{
							value7.Add(value8);
						}
					}
					else
					{
						value7.Add(value9);
					}
				}
				return value7;
			});
		}
	}

	internal static Context Make(Value values, Context? parent = null)
	{
		return new Context(values.IsNull ? Value.Null : values, parent ?? BuiltIns());
	}

	private static Value CreateValueFromJsonObject(JsonElement element)
	{
		Value value = Value.Object();
		foreach (JsonProperty item in element.EnumerateObject())
		{
			value.Set(item.Name, CreateValue(item.Value));
		}
		return value;
	}

	private static bool IsKeyValuePairType(Type? type)
	{
		if ((object)type != null && type.IsGenericType)
		{
			return type.GetGenericTypeDefinition() == typeof(KeyValuePair<, >);
		}
		return false;
	}

	private static bool TryGetIEnumerableType(object bindings, [NotNullWhen(true)] out Type? ienumerableArgument)
	{
		Type type = bindings.GetType().GetInterfaces().FirstOrDefault((Type itf) => itf.IsGenericType && itf.GetGenericTypeDefinition() == typeof(IEnumerable<>));
		if ((object)type != null)
		{
			ienumerableArgument = type.GetGenericArguments()[0];
			return true;
		}
		ienumerableArgument = null;
		return false;
	}

	private static bool IsKeyValueEnumerable(object bindings)
	{
		if (TryGetIEnumerableType(bindings, out Type ienumerableArgument))
		{
			return IsKeyValuePairType(ienumerableArgument);
		}
		return false;
	}

	private static Value CreateValueFromKeyValuePairEnumeration(IEnumerable enumerable)
	{
		Value value = Value.Object();
		foreach (object item in enumerable)
		{
			Type type = item.GetType();
			PropertyInfo property = type.GetProperty("Key");
			PropertyInfo? property2 = type.GetProperty("Value");
			object value2 = property.GetValue(item);
			object value3 = property2.GetValue(item);
			value.Set(CreateValue(value2), CreateValue(value3));
		}
		return value;
	}

	private static Value CreateValue(object? value)
	{
		if (value == null)
		{
			return Value.Null;
		}
		if (value is string value2)
		{
			return new Value(value2);
		}
		if (value is long value3)
		{
			return new Value(value3);
		}
		if (value is int num)
		{
			return new Value(num);
		}
		if (value is bool value4)
		{
			return new Value(value4);
		}
		if (value is double value5)
		{
			return new Value(value5);
		}
		if (value is float num2)
		{
			return new Value(num2);
		}
		if (value is JsonElement element)
		{
			if (element.ValueKind == JsonValueKind.Object)
			{
				return CreateValueFromJsonObject(element);
			}
			if (element.ValueKind == JsonValueKind.Array)
			{
				Value value6 = Value.FromArray();
				{
					foreach (JsonElement item in element.EnumerateArray())
					{
						value6.Add(CreateValue(item));
					}
					return value6;
				}
			}
			if (element.ValueKind == JsonValueKind.String)
			{
				string text = element.GetString();
				if (text != null)
				{
					return new Value(text);
				}
				return Value.Null;
			}
			if (element.ValueKind == JsonValueKind.Number)
			{
				if (element.TryGetInt64(out var value7))
				{
					return new Value(value7);
				}
				if (element.TryGetDouble(out var value8))
				{
					return new Value(value8);
				}
				return Value.Null;
			}
			if (element.ValueKind == JsonValueKind.True)
			{
				return new Value(value: true);
			}
			if (element.ValueKind == JsonValueKind.False)
			{
				return new Value(value: false);
			}
			return Value.Null;
		}
		if (IsKeyValueEnumerable(value))
		{
			return CreateValueFromKeyValuePairEnumeration((IEnumerable)value);
		}
		if (value is Array array)
		{
			if (IsKeyValuePairType(array.GetType().GetElementType()))
			{
				return CreateValueFromKeyValuePairEnumeration(array);
			}
			Value value9 = Value.FromArray();
			{
				foreach (object item2 in array)
				{
					value9.Add(CreateValue(item2));
				}
				return value9;
			}
		}
		if (TryGetIEnumerableType(value, out Type _))
		{
			Value value10 = Value.FromArray();
			{
				foreach (object item3 in (IEnumerable)value)
				{
					value10.Add(CreateValue(item3));
				}
				return value10;
			}
		}
		Value value11 = Value.Object();
		Type type = value.GetType();
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTuple<, >))
		{
			object value12 = type.GetField("Item1").GetValue(value);
			object value13 = type.GetField("Item2").GetValue(value);
			value11.Set(CreateValue(value12), CreateValue(value13));
		}
		else
		{
			PropertyInfo[] properties = type.GetProperties();
			foreach (PropertyInfo propertyInfo in properties)
			{
				value11.Set(propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? propertyInfo.Name, CreateValue(propertyInfo.GetValue(value)));
			}
		}
		return value11;
	}

	public static Context Make(object? bindings, Context? parent = null)
	{
		return Make((bindings == null) ? Value.Object() : CreateValue(bindings), parent);
	}

	public static Context Make(JsonElement bindings, Context? parent = null)
	{
		if (bindings.ValueKind != JsonValueKind.Object)
		{
			throw new JinjaException("Context bindings must be a JSON object");
		}
		return Make(CreateValueFromJsonObject(bindings), parent);
	}

	public static Context Make(IEnumerable<JsonElement> bindings, Context? parent = null)
	{
		Value value = Value.Object();
		foreach (JsonElement binding in bindings)
		{
			if (binding.ValueKind != JsonValueKind.Object)
			{
				throw new JinjaException("Context bindings must be a JSON object");
			}
			foreach (JsonProperty item in binding.EnumerateObject())
			{
				value.Set(item.Name, CreateValue(item.Value));
			}
		}
		return Make(value, parent);
	}

	private static Value SimpleFunction(string fnName, IReadOnlyList<string> paramsList, Func<Context, Value, Value> fn)
	{
		Dictionary<string, int> namedPositions = new Dictionary<string, int>(paramsList.Count);
		for (int i = 0; i < paramsList.Count; i++)
		{
			namedPositions[paramsList[i]] = i;
		}
		return Value.Callable((Context context, ArgumentsValue args) =>
		{
			Value value = Value.Object();
			bool[] array = new bool[paramsList.Count];
			for (int j = 0; j < args.Args.Count; j++)
			{
				Value value2 = args.Args[j];
				if (j >= paramsList.Count)
				{
					throw new JinjaException("Too many positional params for " + fnName);
				}
				value.Set(paramsList[j], value2);
				array[j] = true;
			}
			foreach (var (text, value3) in args.Kwargs)
			{
				if (!namedPositions.TryGetValue(text, out var value4))
				{
					throw new JinjaException("Unknown argument " + text + " for function " + fnName);
				}
				array[value4] = true;
				value.Set(text, value3);
			}
			return fn(context, value);
		});
	}
}
