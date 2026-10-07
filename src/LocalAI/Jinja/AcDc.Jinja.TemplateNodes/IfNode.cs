// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class IfNode : TemplateNode
{
	private readonly IEnumerable<(Expression? Expression, TemplateNode TemplateNode)> _cascade;

	public IfNode(Location location, IEnumerable<(Expression? Expression, TemplateNode TemplateNode)> cascade)
		: base(location)
	{
		_cascade = cascade;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		foreach (var item in _cascade)
		{
			bool flag = true;
			if (item.Expression != null)
			{
				flag = item.Expression.Evaluate(context).ToBoolean();
			}
			if (flag)
			{
				if (item.TemplateNode == null)
				{
					throw new JinjaException($"If branch at {Location} has no template node.");
				}
				item.TemplateNode.Render(writer, context);
				break;
			}
		}
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (var item in _cascade)
		{
			stringBuilder.AppendLine((item.Expression == null) ? $"else {item.TemplateNode}" : $"if {item.Expression} {item.TemplateNode}");
		}
		return stringBuilder.ToString();
	}
}
