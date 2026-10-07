// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja;

internal class Location
{
	public required string? Source { get; init; }

	public required int Position { get; init; }

	public override string ToString()
	{
		return LocationExtensions.ToString(Source, Position);
	}
}
