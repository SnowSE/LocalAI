// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class FilterExpr : Expression
{
	private readonly IList<Expression> _parts;

	public FilterExpr(Location location, IList<Expression> parts)
		: base(location)
	{
		_parts = parts;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value value = Value.Null;
		bool flag = true;
		foreach (Expression part in _parts)
		{
			if (part == null)
			{
				throw new JinjaException("FilterExpr.part is null");
			}
			if (flag)
			{
				flag = false;
				value = part.Evaluate(context);
			}
			else if (part is CallExpr callExpr)
			{
				Value value2 = callExpr.Object.Evaluate(context);
				ArgumentsValue argumentsValue = callExpr.Arguments.Evaluate(context);
				argumentsValue.Args.Insert(0, value);
				value = value2.Call(context, argumentsValue);
			}
			else
			{
				Value value3 = part.Evaluate(context);
				ArgumentsValue argumentsValue2 = new ArgumentsValue();
				argumentsValue2.Args.Insert(0, value);
				value = value3.Call(context, argumentsValue2);
			}
		}
		return value;
	}

	public void Prepend(Expression expression)
	{
		_parts.Insert(0, expression);
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (Expression part in _parts)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(" | ");
			}
			stringBuilder.Append(part);
		}
		return stringBuilder.ToString();
	}
}
