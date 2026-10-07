// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class DictExpr : Expression
{
	private readonly IEnumerable<(Expression Key, Expression Value)> _elements;

	public DictExpr(Location location, IEnumerable<(Expression Key, Expression Value)> elements)
		: base(location)
	{
		foreach (var element in elements)
		{
			ArgumentNullException.ThrowIfNull(element.Key, "e.Key");
			ArgumentNullException.ThrowIfNull(element.Value, "e.Value");
		}
		_elements = elements;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value value = Value.Object();
		foreach (var element in _elements)
		{
			value.Set(element.Key.Evaluate(context), element.Value.Evaluate(context));
		}
		return value;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append('{');
		foreach (var element in _elements)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendFormatted(element.Key);
			handler.AppendLiteral(" : ");
			handler.AppendFormatted(element.Value);
			handler.AppendLiteral(", ");
			stringBuilder2.Append(ref handler);
		}
		if (_elements.Any())
		{
			stringBuilder.Length -= 2;
		}
		stringBuilder.Append('}');
		return stringBuilder.ToString();
	}
}
