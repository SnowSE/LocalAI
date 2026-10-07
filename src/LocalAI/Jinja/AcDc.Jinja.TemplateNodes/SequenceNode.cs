// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class SequenceNode : TemplateNode
{
	private readonly IEnumerable<TemplateNode> _children;

	public SequenceNode(Location location, IEnumerable<TemplateNode> children)
		: base(location)
	{
		_children = children;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		foreach (TemplateNode child in _children)
		{
			child.Render(writer, context);
		}
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (TemplateNode child in _children)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, stringBuilder2);
			handler.AppendFormatted(child);
			stringBuilder2.AppendLine(ref handler);
		}
		return stringBuilder.ToString();
	}
}
