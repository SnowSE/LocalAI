// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja;

internal readonly struct Options
{
	public required bool TrimBlocks { get; init; }

	public required bool LStripBlocks { get; init; }

	public required bool KeepTrailingNewline { get; init; }

	public static Options Default => new Options
	{
		TrimBlocks = false,
		LStripBlocks = false,
		KeepTrailingNewline = false
	};
}
