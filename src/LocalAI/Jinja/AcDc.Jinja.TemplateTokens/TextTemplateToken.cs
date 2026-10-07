// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Text;

namespace AcDc.Jinja.TemplateTokens;

internal sealed class TextTemplateToken : TemplateToken
{
	public readonly string Text;

	public TextTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, string text)
		: base(TemplateType.Text, location, preSpace, postSpace)
	{
		Text = text;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(Text);
		return stringBuilder.ToString();
	}
}
