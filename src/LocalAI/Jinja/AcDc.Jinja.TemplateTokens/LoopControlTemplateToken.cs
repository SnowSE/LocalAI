// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.TemplateTokens;

internal sealed class LoopControlTemplateToken : TemplateToken
{
	public readonly LoopControlType ControlType;

	public LoopControlTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, LoopControlType controlType)
		: base(TemplateType.Break, location, preSpace, postSpace)
	{
		ControlType = controlType;
	}

	public override string ToString()
	{
		return ControlType.ToString();
	}
}
