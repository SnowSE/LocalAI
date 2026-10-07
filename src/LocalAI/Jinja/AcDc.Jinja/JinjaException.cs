// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;

namespace AcDc.Jinja;

internal class JinjaException : Exception
{
	internal Location? Location;

	public string? LocationString => Location?.ToString();

	public JinjaException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public JinjaException(string message)
		: base(message)
	{
	}
}
