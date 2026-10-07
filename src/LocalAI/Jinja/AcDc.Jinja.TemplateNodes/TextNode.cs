// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.IO;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class TextNode : TemplateNode
{
	private readonly string _text;

	public TextNode(Location location, string text)
		: base(location)
	{
		_text = text;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		writer.Write(_text);
	}

	public override string ToString()
	{
		return _text;
	}
}
