// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;

namespace AcDc.Jinja.TemplateTokens;

internal sealed class IfTemplateToken : TemplateToken
{
	public readonly Expression Condition;

	public IfTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, Expression condition)
		: base(TemplateType.If, location, preSpace, postSpace)
	{
		ArgumentNullException.ThrowIfNull(condition, "condition");
		Condition = condition;
	}

	public override string ToString()
	{
		return $"if {Condition}";
	}
}
