// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.Text;

namespace AcDc.Jinja.TemplateTokens;

internal sealed class ForTemplateToken : TemplateToken
{
	public readonly IReadOnlyCollection<string> VariableNames;

	public readonly Expression Iterable;

	public readonly Expression Condition;

	public readonly bool Recursive;

	public ForTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, IReadOnlyCollection<string> variableNames, Expression iterable, Expression condition, bool recursive)
		: base(TemplateType.For, location, preSpace, postSpace)
	{
		VariableNames = variableNames;
		Iterable = iterable;
		Condition = condition;
		Recursive = recursive;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("for ");
		stringBuilder.Append(string.Join(", ", VariableNames));
		stringBuilder.Append(" in ");
		stringBuilder.Append(Iterable);
		if (Condition == null)
		{
			stringBuilder.Append(" if ");
			stringBuilder.Append(Condition);
		}
		stringBuilder.AppendLine();
		if (Recursive)
		{
			stringBuilder.Append(" recursive");
		}
		return stringBuilder.ToString();
	}
}
