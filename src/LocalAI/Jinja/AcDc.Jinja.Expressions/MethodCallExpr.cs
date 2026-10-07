// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class MethodCallExpr : Expression
{
	private readonly Expression _object;

	private readonly VariableExpr _method;

	private readonly ArgumentsExpression _arguments;

	public MethodCallExpr(Location location, Expression @object, VariableExpr method, ArgumentsExpression arguments)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(@object, "@object");
		ArgumentNullException.ThrowIfNull(method, "method");
		ArgumentNullException.ThrowIfNull(arguments, "arguments");
		_object = @object;
		_method = method;
		_arguments = arguments;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value obj = _object.Evaluate(context);
		ArgumentsValue argumentsValue = _arguments.Evaluate(context);
		if (obj.IsNull)
		{
			throw new JinjaException("Trying to call method '" + _method.Name + "' on null");
		}
		if (obj.IsArray)
		{
			switch (_method.Name)
			{
			case "append":
				argumentsValue.ExpectArgs("append method", (Min: 1, Max: 1));
				obj.Add(argumentsValue.Args[0]);
				return Value.Null;
			case "pop":
				argumentsValue.ExpectArgs("pop method", (Min: 0, Max: 1));
				return obj.Pop((argumentsValue.Args.Count == 0) ? Value.Null : argumentsValue.Args[0]);
			case "insert":
			{
				argumentsValue.ExpectArgs("insert method", (Min: 2, Max: 2));
				long num = argumentsValue.Args[0].Get<long>();
				if (num < 0 || num > obj.Count)
				{
					throw new JinjaException("Index out of range for insert method");
				}
				obj.Insert((int)num, argumentsValue.Args[1]);
				return Value.Null;
			}
			}
		}
		else if (obj.IsObject)
		{
			switch (_method.Name)
			{
			case "items":
			{
				argumentsValue.ExpectArgs("items method", (Min: 0, Max: 0));
				Value result = Value.FromArray();
				obj.ForEach((Value key) =>
				{
					result.Add(Value.FromArray((new Value[2]
					{
						key,
						obj.Get(key)
					})));
				});
				return result;
			}
			case "pop":
				argumentsValue.ExpectArgs("pop method", (Min: 1, Max: 1));
				return obj.Pop(argumentsValue.Args[0]);
			case "keys":
			{
				argumentsValue.ExpectArgs("keys method", (Min: 0, Max: 0));
				Value value2 = Value.FromArray();
				{
					foreach (Value key in obj.Keys)
					{
						value2.Add(new Value(key));
					}
					return value2;
				}
			}
			case "get":
			{
				argumentsValue.ExpectArgs("get method", (Min: 1, Max: 2));
				Value value = argumentsValue.Args[0];
				if (!obj.Contains(value))
				{
					if (argumentsValue.Args.Count != 1)
					{
						return argumentsValue.Args[1];
					}
					return Value.Null;
				}
				return obj.Get(value);
			}
			}
			Value value3 = new Value(_method.Name);
			if (obj.Contains(value3))
			{
				Value value4 = obj.Get(value3);
				if (!value4.IsCallable)
				{
					throw new JinjaException("Property '" + _method.Name + "' is not callable");
				}
				return value4.Call(context, argumentsValue);
			}
		}
		else if (obj.IsString)
		{
			string text = obj.Get<string>();
			switch (_method.Name)
			{
			case "strip":
			{
				argumentsValue.ExpectArgs("strip method", (Min: 0, Max: 1));
				string chars3 = ((argumentsValue.Args.Count == 0) ? "" : argumentsValue.Args[0].Get<string>());
				return new Value(StringUtils.Strip(text, chars3));
			}
			case "lstrip":
			{
				argumentsValue.ExpectArgs("lstrip method", (Min: 0, Max: 1));
				string chars2 = ((argumentsValue.Args.Count == 0) ? "" : argumentsValue.Args[0].Get<string>());
				return new Value(StringUtils.Strip(text, chars2, left: true, right: false));
			}
			case "rstrip":
			{
				argumentsValue.ExpectArgs("rstrip method", (Min: 0, Max: 1));
				string chars = ((argumentsValue.Args.Count == 0) ? "" : argumentsValue.Args[0].Get<string>());
				return new Value(StringUtils.Strip(text, chars, left: false));
			}
			case "split":
			{
				argumentsValue.ExpectArgs("split method", (Min: 1, Max: 1));
				string sep = argumentsValue.Args[0].Get<string>();
				List<string> list = StringUtils.Split(text, sep);
				Value value6 = Value.FromArray();
				{
					foreach (string item in list)
					{
						value6.Add(new Value(item));
					}
					return value6;
				}
			}
			case "capitalize":
				argumentsValue.ExpectArgs("capitalize method", (Min: 0, Max: 0));
				return new Value(StringUtils.Capitalize(text));
			case "upper":
				argumentsValue.ExpectArgs("upper method", (Min: 0, Max: 0));
				return new Value(text.ToUpperInvariant());
			case "lower":
				argumentsValue.ExpectArgs("lower method", (Min: 0, Max: 0));
				return new Value(text.ToLowerInvariant());
			case "endswith":
			{
				argumentsValue.ExpectArgs("endswith method", (Min: 1, Max: 1));
				string value8 = argumentsValue.Args[0].Get<string>();
				return new Value(text.EndsWith(value8, StringComparison.InvariantCulture));
			}
			case "startswith":
			{
				argumentsValue.ExpectArgs("startswith method", (Min: 1, Max: 1));
				string value7 = argumentsValue.Args[0].Get<string>();
				return new Value(text.StartsWith(value7, StringComparison.InvariantCulture));
			}
			case "title":
			{
				argumentsValue.ExpectArgs("title method", (Min: 0, Max: 0));
				StringBuilder stringBuilder2 = new StringBuilder(text);
				for (int num5 = 0; num5 < stringBuilder2.Length; num5++)
				{
					stringBuilder2[num5] = ((num5 == 0 || char.IsWhiteSpace(stringBuilder2[num5 - 1])) ? char.ToUpperInvariant(stringBuilder2[num5]) : char.ToLowerInvariant(stringBuilder2[num5]));
				}
				return new Value(stringBuilder2.ToString());
			}
			case "replace":
			{
				argumentsValue.ExpectArgs("replace method", (Min: 2, Max: 3));
				string text2 = argumentsValue.Args[0].Get<string>();
				string value5 = argumentsValue.Args[1].Get<string>();
				long num2 = ((argumentsValue.Args.Count == 3) ? argumentsValue.Args[2].Get<long>() : text.Length);
				int num3 = 0;
				StringBuilder stringBuilder = new StringBuilder();
				int num4;
				while ((num4 = text.IndexOf(text2, num3)) >= 0 && num2-- > 0)
				{
					stringBuilder.Append(text, num3, num4 - num3);
					stringBuilder.Append(value5);
					num3 = num4 + text2.Length;
				}
				stringBuilder.Append(text, num3, text.Length - num3);
				return new Value(stringBuilder.ToString());
			}
			}
		}
		throw new JinjaException("Unknown method " + _method.Name);
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(_object);
		stringBuilder.Append('.');
		stringBuilder.Append(_method);
		stringBuilder.Append('(');
		stringBuilder.Append(_arguments);
		stringBuilder.Append(')');
		return stringBuilder.ToString();
	}
}
