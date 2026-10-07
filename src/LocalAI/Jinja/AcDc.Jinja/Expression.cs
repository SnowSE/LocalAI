// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;

namespace AcDc.Jinja;

internal abstract class Expression
{
	public readonly Location Location;

	protected Expression(Location location)
	{
		ArgumentNullException.ThrowIfNull(location, "location");
		Location = location;
	}

	protected abstract Value DoEvaluate(Context context);

	public Value Evaluate(Context context)
	{
		try
		{
			return DoEvaluate(context);
		}
		catch (JinjaException ex)
		{
			ex.Location = Location;
			throw;
		}
		catch (Exception innerException)
		{
			throw new Exception($"Error evaluating expression at {Location}", innerException);
		}
	}
}
