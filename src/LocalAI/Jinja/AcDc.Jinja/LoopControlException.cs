// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja;

internal class LoopControlException : JinjaException
{
	public readonly LoopControlType ControlType;

	public LoopControlException(string message, LoopControlType controlType)
		: base(message)
	{
		ControlType = controlType;
	}

	public LoopControlException(LoopControlType controlType)
		: this(((controlType == LoopControlType.Continue) ? "continue" : "break") + " outside of a loop", controlType)
	{
	}
}
