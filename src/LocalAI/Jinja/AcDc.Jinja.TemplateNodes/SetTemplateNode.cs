// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.IO;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class SetTemplateNode : TemplateNode
{
	private readonly string _name;

	private readonly TemplateNode _templateValues;

	public SetTemplateNode(Location location, string name, TemplateNode templateValues)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(templateValues, "templateValues");
		_name = name;
		_templateValues = templateValues;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		context.Set(_name, _templateValues.Render(context));
	}

	public override string ToString()
	{
		return $"set {_name}={_templateValues}";
	}
}
