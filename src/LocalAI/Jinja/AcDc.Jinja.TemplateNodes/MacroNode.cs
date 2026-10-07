// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AcDc.Jinja.Expressions;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class MacroNode : TemplateNode
{
	private readonly VariableExpr _macroName;

	private readonly IReadOnlyList<(string Name, Expression Expression)> _parameters;

	private readonly TemplateNode _body;

	private readonly Dictionary<string, int> _namedParameterPositions;

	public MacroNode(Location location, VariableExpr name, IReadOnlyList<(string Name, Expression)> parameters, TemplateNode body)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(name, "name");
		ArgumentNullException.ThrowIfNull(body, "body");
		_macroName = name;
		_parameters = parameters;
		_body = body;
		_namedParameterPositions = new Dictionary<string, int>();
		for (int i = 0; i < parameters.Count; i++)
		{
			string item = parameters[i].Name;
			if (!string.IsNullOrEmpty(item))
			{
				_namedParameterPositions[item] = i;
			}
		}
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		Value value = Value.Callable((Context callContext, ArgumentsValue args) =>
		{
			Context context2 = Context.Make(Value.Object(), context);
			if (callContext.Contains("caller"))
			{
				context2.Set("caller", callContext.Get("caller"));
			}
			bool[] array = new bool[_parameters.Count];
			for (int i = 0; i < args.Args.Count; i++)
			{
				Value value2 = args.Args[i];
				if (i >= _parameters.Count)
				{
					throw new JinjaException("Too many positional arguments for macro " + _macroName.Name);
				}
				array[i] = true;
				string item = _parameters[i].Name;
				context2.Set(item, value2);
			}
			foreach (var (text, value3) in args.Kwargs)
			{
				if (!_namedParameterPositions.TryGetValue(text, out var value4))
				{
					throw new JinjaException("Unknown parameter name for macro '" + _macroName.Name + "': " + text);
				}
				context2.Set(text, value3);
				array[value4] = true;
			}
			for (int j = 0; j < _parameters.Count; j++)
			{
				if (!array[j] && _parameters[j].Expression != null)
				{
					Value value5 = _parameters[j].Expression.Evaluate(callContext);
					context2.Set(_parameters[j].Name, value5);
				}
			}
			return new Value(_body.Render(context2));
		});
		context.Set(_macroName.Name, value);
	}

	public override string ToString()
	{
		StringBuilder stringBuilder2;
		StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 2, stringBuilder2);
		handler.AppendLiteral("macro ");
		handler.AppendFormatted(_macroName);
		handler.AppendLiteral("(");
		handler.AppendFormatted(string.Join(", ", _parameters.Select(((string Name, Expression Expression) p) => p.Name)));
		handler.AppendLiteral(")");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.Append(_body);
		stringBuilder.AppendLine();
		return stringBuilder.ToString();
	}
}
