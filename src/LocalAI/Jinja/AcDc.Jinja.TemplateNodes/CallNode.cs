// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.IO;
using AcDc.Jinja.Expressions;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class CallNode : TemplateNode
{
	private readonly Expression _expression;

	private readonly TemplateNode _body;

	public CallNode(Location location, Expression expression, TemplateNode body)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(expression, "expression");
		ArgumentNullException.ThrowIfNull(body, "body");
		_expression = expression;
		_body = body;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		Value value = Value.Callable((Context _, ArgumentsValue argumentsValue) => new Value(_body.Render(context)));
		context.Set("caller", value);
		CallExpr obj = (_expression as CallExpr) ?? throw new JinjaException("Invalid call block syntax - expected function call");
		Value value2 = obj.Object.Evaluate(context);
		if (!value2.IsCallable)
		{
			throw new JinjaException("Call target must be callable: " + value2.Dump());
		}
		ArgumentsValue args = obj.Arguments.Evaluate(context);
		Value value3 = value2.Call(context, args);
		writer.Write(value3.ToString());
	}

	public override string ToString()
	{
		return $"{_expression} {{{_body}}}";
	}
}
