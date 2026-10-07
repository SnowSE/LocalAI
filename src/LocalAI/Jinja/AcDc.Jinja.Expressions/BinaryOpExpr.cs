// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class BinaryOpExpr : Expression
{
	public enum Op
	{
		StrConcat,
		Add,
		Sub,
		Mul,
		MulMul,
		Div,
		DivDiv,
		Mod,
		Eq,
		Ne,
		Lt,
		Gt,
		Le,
		Ge,
		And,
		Or,
		In,
		NotIn,
		Is,
		IsNot
	}

	private readonly Op _operator;

	private readonly Expression _left;

	private readonly Expression _right;

	private static readonly Dictionary<Op, int> Precedences = new Dictionary<Op, int>
	{
		{
			Op.MulMul,
			12
		},
		{
			Op.Mul,
			11
		},
		{
			Op.Div,
			11
		},
		{
			Op.DivDiv,
			11
		},
		{
			Op.Mod,
			11
		},
		{
			Op.Add,
			10
		},
		{
			Op.Sub,
			10
		},
		{
			Op.StrConcat,
			9
		},
		{
			Op.Eq,
			7
		},
		{
			Op.Ne,
			7
		},
		{
			Op.Lt,
			7
		},
		{
			Op.Gt,
			7
		},
		{
			Op.Le,
			7
		},
		{
			Op.Ge,
			7
		},
		{
			Op.In,
			6
		},
		{
			Op.NotIn,
			6
		},
		{
			Op.Is,
			6
		},
		{
			Op.IsNot,
			6
		},
		{
			Op.And,
			4
		},
		{
			Op.Or,
			3
		}
	};

	public int Precedence => Precedences[_operator];

	public BinaryOpExpr(Location location, Op @operator, Expression left, Expression right)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(left, "left");
		ArgumentNullException.ThrowIfNull(right, "right");
		_operator = @operator;
		_left = left;
		_right = right;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value l = _left.Evaluate(context);
		if (!l.IsCallable)
		{
			return DoEval(l);
		}
		return Value.Callable((Context callContext, ArgumentsValue args) =>
		{
			Value l2 = l.Call(callContext, args);
			return DoEval(l2);
		});
		Value DoEval(Value value)
		{
			VariableExpr t;
			if (_operator == Op.Is || _operator == Op.IsNot)
			{
				Expression right = _right;
				t = right as VariableExpr;
				if (t == null)
				{
					throw new JinjaException("Right operand of 'is' operator must be a variable");
				}
				bool flag = Eval();
				return new Value((_operator == Op.Is) ? flag : (!flag));
			}
			if (_operator == Op.And)
			{
				if (!value.ToBoolean())
				{
					return value;
				}
				return _right.Evaluate(context);
			}
			if (_operator == Op.Or)
			{
				if (value.ToBoolean())
				{
					return value;
				}
				return _right.Evaluate(context);
			}
			Value value2 = _right.Evaluate(context);
			return _operator switch
			{
				Op.StrConcat => new Value(value.ToString() + value2.ToString()), 
				Op.Add => value + value2, 
				Op.Sub => value - value2, 
				Op.Mul => value * value2, 
				Op.MulMul => Value.Pow(value, value2), 
				Op.Div => value / value2, 
				Op.DivDiv => Value.FloorDiv(value, value2), 
				Op.Mod => value % value2, 
				Op.Eq => new Value(value.Equals(value2)), 
				Op.Ne => new Value(!value.Equals(value2)), 
				Op.Lt => new Value(value < value2), 
				Op.Gt => new Value(value > value2), 
				Op.Le => new Value(value <= value2), 
				Op.Ge => new Value(value >= value2), 
				Op.In => new Value(In(value, value2)), 
				Op.NotIn => new Value(!In(value, value2)), 
				_ => throw new JinjaException("Unknown binary operator"), 
			};
			bool Eval()
			{
				string name = t.Name;
				return name switch
				{
					"none" => value.IsNull, 
					"boolean" => value.IsBoolean, 
					"integer" => value.IsInteger, 
					"float" => value.IsDouble, 
					"number" => value.IsNumber, 
					"string" => value.IsString, 
					"mapping" => value.IsObject, 
					"iterable" => value.IsIterable, 
					"sequence" => value.IsArray, 
					"defined" => !value.IsNull, 
					"true" => value.ToBoolean(), 
					"false" => !value.ToBoolean(), 
					_ => throw new JinjaException("Unknown type for 'is' operator " + name), 
				};
			}
		}
		static bool In(Value value, Value container)
		{
			if ((!container.IsArray && !container.IsObject) || !container.Contains(value))
			{
				if (value.IsString && container.IsString)
				{
					return container.ToString().Contains(value.ToString());
				}
				return false;
			}
			return true;
		}
	}

	private int GetPrecedence(Expression expr)
	{
		if (!(expr is BinaryOpExpr { Precedence: var precedence }))
		{
			if (!(expr is UnaryOpExpr { Precedence: var precedence2 }))
			{
				return int.MaxValue;
			}
			return precedence2;
		}
		return precedence;
	}

	private void WriteExpression(StringBuilder sb, Expression expr)
	{
		int num;
		if (expr is BinaryOpExpr binaryOpExpr)
		{
			num = binaryOpExpr.Precedence;
		}
		else
		{
			num = ((!(expr is UnaryOpExpr unaryOpExpr)) ? int.MaxValue : unaryOpExpr.Precedence);
		}
		bool flag = num < Precedence;
		if (flag)
		{
			sb.Append('(');
		}
		sb.Append(expr);
		if (flag)
		{
			sb.Append(')');
		}
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		WriteExpression(stringBuilder, _left);
		stringBuilder.Append(' ');
		switch (_operator)
		{
		case Op.StrConcat:
			stringBuilder.Append('~');
			break;
		case Op.Add:
			stringBuilder.Append('+');
			break;
		case Op.Sub:
			stringBuilder.Append('-');
			break;
		case Op.Mul:
			stringBuilder.Append('*');
			break;
		case Op.MulMul:
			stringBuilder.Append("**");
			break;
		case Op.Div:
			stringBuilder.Append('/');
			break;
		case Op.DivDiv:
			stringBuilder.Append("//");
			break;
		case Op.Mod:
			stringBuilder.Append('%');
			break;
		case Op.Eq:
			stringBuilder.Append("==");
			break;
		case Op.Ne:
			stringBuilder.Append("!=");
			break;
		case Op.Lt:
			stringBuilder.Append('<');
			break;
		case Op.Gt:
			stringBuilder.Append('>');
			break;
		case Op.Le:
			stringBuilder.Append("<=");
			break;
		case Op.Ge:
			stringBuilder.Append(">=");
			break;
		case Op.And:
			stringBuilder.Append("and");
			break;
		case Op.Or:
			stringBuilder.Append("or");
			break;
		case Op.In:
			stringBuilder.Append("in");
			break;
		case Op.NotIn:
			stringBuilder.Append("not in");
			break;
		case Op.Is:
			stringBuilder.Append("is");
			break;
		case Op.IsNot:
			stringBuilder.Append("is not");
			break;
		default:
			stringBuilder.Append(_operator);
			break;
		}
		stringBuilder.Append(' ');
		WriteExpression(stringBuilder, _right);
		return stringBuilder.ToString();
	}
}
