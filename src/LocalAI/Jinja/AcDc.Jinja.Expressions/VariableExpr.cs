// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.Expressions;

internal sealed class VariableExpr : Expression
{
	public readonly string Name;

	public VariableExpr(Location location, string name)
		: base(location)
	{
		Name = name;
	}

	protected override Value DoEvaluate(Context context)
	{
		return context.Get(Name);
	}

	public override string ToString()
	{
		return Name;
	}
}
