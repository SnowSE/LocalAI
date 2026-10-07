// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.TemplateTokens;

internal sealed class CallTemplateToken : TemplateToken
{
	public readonly Expression Callee;

	public CallTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, Expression callee)
		: base(TemplateType.Call, location, preSpace, postSpace)
	{
		Callee = callee;
	}

	public override string? ToString()
	{
		return Callee.ToString();
	}
}
