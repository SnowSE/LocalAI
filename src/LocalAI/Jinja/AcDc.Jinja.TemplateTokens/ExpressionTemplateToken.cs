// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;

namespace AcDc.Jinja.TemplateTokens;

internal sealed class ExpressionTemplateToken : TemplateToken
{
	public readonly Expression Expression;

	public ExpressionTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, Expression expression)
		: base(TemplateType.Expression, location, preSpace, postSpace)
	{
		ArgumentNullException.ThrowIfNull(expression, "expression");
		Expression = expression;
	}

	public override string? ToString()
	{
		return Expression.ToString();
	}
}
