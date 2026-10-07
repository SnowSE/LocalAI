// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.Text;
using AcDc.Jinja.Expressions;

namespace AcDc.Jinja;

internal sealed class ArgumentsExpression
{
	public readonly List<Expression> Args;

	public readonly List<(string Name, Expression Expression)> KwArgs;

	public ArgumentsExpression()
	{
		Args = new List<Expression>();
		KwArgs = new List<(string, Expression)>();
	}

	public ArgumentsValue Evaluate(Context context)
	{
		ArgumentsValue result = new ArgumentsValue();
		foreach (Expression arg in Args)
		{
			if (arg is UnaryOpExpr unaryOpExpr)
			{
				if (unaryOpExpr.Operator == UnaryOpExpr.Op.Expansion)
				{
					Value value = unaryOpExpr.Expression.Evaluate(context);
					if (!value.IsArray)
					{
						throw new JinjaException("Expansion operator only supported on arrays");
					}
					value.ForEach((Value item2) =>
					{
						result.Args.Add(item2);
					});
					continue;
				}
				if (unaryOpExpr.Operator == UnaryOpExpr.Op.ExpansionDict)
				{
					Value dict = unaryOpExpr.Expression.Evaluate(context);
					if (!dict.IsObject)
					{
						throw new JinjaException("ExpansionDict operator only supported on objects");
					}
					dict.ForEach((Value key) =>
					{
						result.Kwargs.Add((key.Get<string>(), dict.Get(key)));
					});
					continue;
				}
			}
			result.Args.Add(arg.Evaluate(context));
		}
		foreach (var (item, expression) in KwArgs)
		{
			result.Kwargs.Add((item, expression.Evaluate(context)));
		}
		return result;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("Args: [");
		for (int i = 0; i < Args.Count; i++)
		{
			stringBuilder.Append(Args[i]);
			if (i < Args.Count - 1)
			{
				stringBuilder.Append(", ");
			}
		}
		stringBuilder.Append("], KwArgs: {");
		for (int j = 0; j < KwArgs.Count; j++)
		{
			stringBuilder.Append(KwArgs[j].Name);
			stringBuilder.Append(": ");
			stringBuilder.Append(KwArgs[j].Expression);
			if (j < KwArgs.Count - 1)
			{
				stringBuilder.Append(", ");
			}
		}
		return stringBuilder.ToString();
	}
}
