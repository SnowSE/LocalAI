// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class ArrayExpr : Expression
{
	private readonly IEnumerable<Expression> _elements;

	public ArrayExpr(Location location, IEnumerable<Expression> elements)
		: base(location)
	{
		_elements = elements;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value value = Value.FromArray();
		foreach (Expression element in _elements)
		{
			if (element == null)
			{
				throw new JinjaException("Array element is null");
			}
			value.Add(element.Evaluate(context));
		}
		return value;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append('[');
		foreach (Expression element in _elements)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder2);
			handler.AppendFormatted(element);
			handler.AppendLiteral(", ");
			stringBuilder2.Append(ref handler);
		}
		if (_elements.Any())
		{
			stringBuilder.Length -= 2;
		}
		stringBuilder.Append(']');
		return stringBuilder.ToString();
	}
}
