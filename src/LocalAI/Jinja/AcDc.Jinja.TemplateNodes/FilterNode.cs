// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.IO;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class FilterNode : TemplateNode
{
	private readonly Expression _filter;

	private readonly TemplateNode _body;

	public FilterNode(Location location, Expression filter, TemplateNode body)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(filter, "filter");
		ArgumentNullException.ThrowIfNull(body, "body");
		_filter = filter;
		_body = body;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		Value value = _filter.Evaluate(context);
		if (!value.IsCallable)
		{
			throw new JinjaException("Filter must be a callable: " + value.Dump());
		}
		string value2 = _body.Render(context);
		ArgumentsValue argumentsValue = new ArgumentsValue();
		argumentsValue.Args.Add(new Value(value2));
		Value value3 = value.Call(context, argumentsValue);
		writer.Write(value3.ToString());
	}

	public override string? ToString()
	{
		return $"{_filter} | {_body}";
	}
}
