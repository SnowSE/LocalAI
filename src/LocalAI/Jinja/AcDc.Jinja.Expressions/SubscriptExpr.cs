// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class SubscriptExpr : Expression
{
	private readonly Expression _base;

	private readonly Expression _index;

	public SubscriptExpr(Location location, Expression value, Expression index)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(value, "value");
		ArgumentNullException.ThrowIfNull(index, "index");
		_base = value;
		_index = index;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value value = _base.Evaluate(context);
		int len;
		if (_index is SliceExpr sliceExpr)
		{
			len = value.Count;
			long num = ((sliceExpr.Step == null) ? 1 : sliceExpr.Step.Evaluate(context).Get<long>());
			if (num == 0L)
			{
				throw new JinjaException("Slice step cannot be zero");
			}
			long num2 = ((sliceExpr.Start == null) ? ((num < 0) ? (len - 1) : 0) : Clamp(Wrap(sliceExpr.Start.Evaluate(context).Get<long>()), num));
			long num3 = ((sliceExpr.End == null) ? ((num < 0) ? (-1) : len) : Clamp(Wrap(sliceExpr.End.Evaluate(context).Get<long>()), num));
			if (value.IsString)
			{
				string text = value.Get<string>();
				StringBuilder stringBuilder = new StringBuilder();
				if (num2 < num3 && num == 1)
				{
					stringBuilder.Append(text, (int)num2, (int)(num3 - num2));
				}
				else
				{
					for (long num4 = num2; (num > 0) ? (num4 < num3) : (num4 > num3); num4 += num)
					{
						stringBuilder.Append(text[(int)num4]);
					}
				}
				return new Value(stringBuilder.ToString());
			}
			if (value.IsArray)
			{
				Value value2 = Value.FromArray();
				for (long num5 = num2; (num > 0) ? (num5 < num3) : (num5 > num3); num5 += num)
				{
					value2.Add(value[(int)num5]);
				}
				return value2;
			}
			throw new JinjaException(value.IsNull ? "Cannot subscript null" : "Subscripting only supported on arrays and strings");
		}
		Value value3 = _index.Evaluate(context);
		if (value.IsNull)
		{
			if (_base is VariableExpr variableExpr)
			{
				throw new JinjaException("'" + variableExpr.Name + "' is " + (context.Contains(variableExpr.Name) ? "null" : "not defined"));
			}
			throw new JinjaException("Trying to access property '" + value3.Dump() + "' on null!");
		}
		return value.Get(value3);
		// LocalAI change: clamp out-of-range bounds the way Python does, instead of throwing.
		// Chat templates rely on it, e.g. Qwen's message.content[:tool_start_length] on short messages.
		long Clamp(long i, long step)
		{
			return (step > 0) ? Math.Clamp(i, 0, len) : Math.Clamp(i, -1, len - 1);
		}
		long Wrap(long i)
		{
			if (i >= 0)
			{
				return i;
			}
			return i + len;
		}
	}

	public override string ToString()
	{
		return $"{_base}[{_index}]";
	}
}
