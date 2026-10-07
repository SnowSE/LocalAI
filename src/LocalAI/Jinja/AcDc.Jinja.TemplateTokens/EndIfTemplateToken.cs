// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.TemplateTokens;

internal sealed class EndIfTemplateToken : TemplateToken
{
	public EndIfTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace)
		: base(TemplateType.EndIf, location, preSpace, postSpace)
	{
	}

	public override string ToString()
	{
		return "endif";
	}
}
