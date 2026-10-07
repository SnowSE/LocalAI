// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.TemplateTokens;

internal sealed class EndForTemplateToken : TemplateToken
{
	public EndForTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace)
		: base(TemplateType.EndFor, location, preSpace, postSpace)
	{
	}

	public override string ToString()
	{
		return "endfor";
	}
}
