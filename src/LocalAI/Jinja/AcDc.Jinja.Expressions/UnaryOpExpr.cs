// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class UnaryOpExpr : Expression
{
	public enum Op
	{
		Plus,
		Minus,
		LogicalNot,
		Expansion,
		ExpansionDict
	}

	public readonly Op Operator;

	public readonly Expression Expression;

	private static readonly Dictionary<Op, int> Precedences = new Dictionary<Op, int>
	{
		{
			Op.Plus,
			13
		},
		{
			Op.Minus,
			13
		},
		{
			Op.LogicalNot,
			2
		},
		{
			Op.Expansion,
			14
		},
		{
			Op.ExpansionDict,
			14
		}
	};

	public int Precedence => Precedences[Operator];

	public UnaryOpExpr(Location location, Op @operator, Expression expression)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(expression, "expression");
		Operator = @operator;
		Expression = expression;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value value = Expression.Evaluate(context);
		switch (Operator)
		{
		case Op.Plus:
			return value;
		case Op.Minus:
			return -value;
		case Op.LogicalNot:
			return new Value(!value.ToBoolean());
		case Op.Expansion:
		case Op.ExpansionDict:
			throw new JinjaException("Expansion operator is only supported in function calls and collections");
		default:
			throw new JinjaException("Unknown unary operator");
		}
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		switch (Operator)
		{
		case Op.Plus:
			stringBuilder.Append('+');
			break;
		case Op.Minus:
			stringBuilder.Append('-');
			break;
		case Op.LogicalNot:
			stringBuilder.Append("not ");
			break;
		case Op.Expansion:
			stringBuilder.Append('*');
			break;
		case Op.ExpansionDict:
			stringBuilder.Append("**");
			break;
		}
		stringBuilder.Append(Expression);
		return stringBuilder.ToString();
	}
}
