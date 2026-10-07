// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja;

internal static class ValueUtils
{
	public static bool In(Value value, Value container)
	{
		bool found = false;
		container.ForEach((Value item) =>
		{
			if (!found && item.Equals(value))
			{
				found = true;
			}
		});
		return found;
	}
}
