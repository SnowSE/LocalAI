// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class SetNode : TemplateNode
{
	private readonly string _namespace;

	private readonly IReadOnlyCollection<string> _variableNames;

	private readonly Expression _valueExpression;

	public SetNode(Location location, string @namespace, IReadOnlyCollection<string> variableNames, Expression valueExpression)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(valueExpression, "valueExpression");
		_namespace = @namespace;
		_variableNames = variableNames;
		_valueExpression = valueExpression;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		if (!string.IsNullOrEmpty(_namespace))
		{
			if (_variableNames.Count != 1)
			{
				throw new JinjaException("Namespaced set only supports a single variable name");
			}
			string key = _variableNames.Single();
			Value value = context.Get(_namespace);
			if (!value.IsObject)
			{
				throw new JinjaException("Namespace '" + _namespace + "' is not an object");
			}
			value.Set(key, _valueExpression.Evaluate(context));
		}
		else
		{
			Value item = _valueExpression.Evaluate(context);
			context.DestructuringAssign(_variableNames, item);
		}
	}

	public override string ToString()
	{
		return $"{_namespace}([{string.Join(", ", _variableNames)}], = {_valueExpression})";
	}
}
