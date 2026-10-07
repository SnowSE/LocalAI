// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Text;

namespace AcDc.Jinja;

internal static class LocationExtensions
{
	private const int MaxPrefixLength = 80;

	private const int MaxSuffixLength = 40;

	private const string Ellipsis = "...";

	public static string ToString(string? source, int position)
	{
		if (source == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		int num = position;
		while (num > 0 && position - num < 80 && source[num - 1] != '\n')
		{
			num--;
		}
		if (num > 0 && source[num - 1] != '\n')
		{
			stringBuilder.Append("...");
		}
		stringBuilder.Append(source, num, position - num);
		int i;
		for (i = position; i < source.Length && i - position < 40 && source[i] != '\n'; i++)
		{
		}
		if (i < source.Length && source[i] != '\n')
		{
			stringBuilder.Append("...");
		}
		stringBuilder.Append(source, position, i - position);
		stringBuilder.AppendLine();
		if (num > 1)
		{
			stringBuilder.Append(' ', num - 1);
		}
		stringBuilder.Append('^');
		stringBuilder.AppendLine();
		return stringBuilder.ToString();
	}
}
