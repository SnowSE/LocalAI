// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.IO;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class LoopControlNode : TemplateNode
{
	private readonly LoopControlType _controlType;

	public LoopControlNode(Location location, LoopControlType controlType)
		: base(location)
	{
		_controlType = controlType;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		throw new LoopControlException(_controlType);
	}

	public override string ToString()
	{
		return $"{_controlType}";
	}
}
