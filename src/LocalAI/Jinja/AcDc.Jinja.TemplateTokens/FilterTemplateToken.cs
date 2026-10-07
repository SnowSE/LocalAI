// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.TemplateTokens;

internal sealed class FilterTemplateToken : TemplateToken
{
	public readonly Expression Filter;

	public FilterTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, Expression filter)
		: base(TemplateType.Filter, location, preSpace, postSpace)
	{
		Filter = filter;
	}

	public override string? ToString()
	{
		return Filter.ToString();
	}
}
