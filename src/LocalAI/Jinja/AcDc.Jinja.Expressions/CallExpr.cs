// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class CallExpr : Expression
{
	public readonly Expression Object;

	public readonly ArgumentsExpression Arguments;

	public CallExpr(Location location, Expression @object, ArgumentsExpression arguments)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(@object, "@object");
		Object = @object;
		Arguments = arguments;
	}

	protected override Value DoEvaluate(Context context)
	{
		Value value = Object.Evaluate(context);
		if (!value.IsCallable)
		{
			throw new JinjaException("Object is not callable: " + value.Dump(2));
		}
		ArgumentsValue args = Arguments.Evaluate(context);
		return value.Call(context, args);
	}

	public override string ToString()
	{
		StringBuilder stringBuilder2;
		StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
		handler.AppendFormatted(Object);
		handler.AppendLiteral("(");
		handler.AppendFormatted(Arguments);
		handler.AppendLiteral(")");
		stringBuilder2.Append(ref handler);
		return stringBuilder.ToString();
	}
}
