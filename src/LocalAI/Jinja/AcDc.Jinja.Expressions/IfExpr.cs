// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class IfExpr : Expression
{
	private readonly Expression _condition;

	private readonly Expression _thenExpression;

	private readonly Expression? _elseExpression;

	public IfExpr(Location location, Expression condition, Expression thenExpression, Expression? elseExpression)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(condition, "condition");
		ArgumentNullException.ThrowIfNull(thenExpression, "thenExpression");
		_condition = condition;
		_thenExpression = thenExpression;
		_elseExpression = elseExpression;
	}

	protected override Value DoEvaluate(Context context)
	{
		if (_condition.Evaluate(context).ToBoolean())
		{
			return _thenExpression.Evaluate(context);
		}
		if (_elseExpression != null)
		{
			return _elseExpression.Evaluate(context);
		}
		return Value.Null;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("if ");
		stringBuilder.Append(_condition);
		stringBuilder.Append(" then ");
		stringBuilder.Append(_thenExpression);
		if (_elseExpression != null)
		{
			stringBuilder.Append(" else ");
			stringBuilder.Append(_elseExpression);
		}
		return stringBuilder.ToString();
	}
}
