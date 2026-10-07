// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;

namespace AcDc.Jinja.TemplateTokens;

internal sealed class SetTemplateToken : TemplateToken
{
	public readonly string Namespace;

	public readonly IReadOnlyCollection<string> VariableNames;

	public readonly Expression Value;

	public SetTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, string @namespace, IReadOnlyCollection<string> variableNames, Expression value)
		: base(TemplateType.Set, location, preSpace, postSpace)
	{
		Namespace = @namespace;
		VariableNames = variableNames;
		Value = value;
	}

	public override string ToString()
	{
		return $"{Namespace}({string.Join(", ", VariableNames)} = {Value})";
	}
}
