// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.Linq;
using AcDc.Jinja.Expressions;

namespace AcDc.Jinja.TemplateTokens;

internal sealed class MacroTemplateToken : TemplateToken
{
	public readonly VariableExpr Name;

	public readonly IReadOnlyList<(string Name, Expression Expression)> Parameters;

	public MacroTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, VariableExpr name, IReadOnlyList<(string Name, Expression Expression)> parameters)
		: base(TemplateType.Macro, location, preSpace, postSpace)
	{
		Name = name;
		Parameters = parameters;
	}

	public override string ToString()
	{
		return $"macro {Name}({string.Join(", ", Parameters.Select(((string Name, Expression Expression) p) => $"{p.Name}={p.Expression}"))})";
	}
}
