// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;

namespace AcDc.Jinja;

internal abstract class TemplateToken
{
	public enum TemplateType
	{
		Text,
		Expression,
		If,
		Else,
		Elif,
		EndIf,
		For,
		EndFor,
		Generation,
		EndGeneration,
		Set,
		EndSet,
		Comment,
		Macro,
		EndMacro,
		Filter,
		EndFilter,
		Break,
		Continue,
		Call,
		EndCall
	}

	public readonly Location Location;

	public readonly SpaceHandling PreSpace;

	public readonly SpaceHandling PostSpace;

	public readonly TemplateType Type;

	protected TemplateToken(TemplateType type, Location location, SpaceHandling preSpace, SpaceHandling postSpace)
	{
		ArgumentNullException.ThrowIfNull(location, "location");
		Type = type;
		Location = location;
		PreSpace = preSpace;
		PostSpace = postSpace;
	}

	public static string TypeToString(TemplateType type)
	{
		return type switch
		{
			TemplateType.Text => "text", 
			TemplateType.Expression => "expression", 
			TemplateType.If => "if", 
			TemplateType.Else => "else", 
			TemplateType.Elif => "elif", 
			TemplateType.EndIf => "endif", 
			TemplateType.For => "for", 
			TemplateType.EndFor => "endfor", 
			TemplateType.Set => "set", 
			TemplateType.EndSet => "endset", 
			TemplateType.Comment => "comment", 
			TemplateType.Macro => "macro", 
			TemplateType.EndMacro => "endmacro", 
			TemplateType.Filter => "filter", 
			TemplateType.EndFilter => "endfilter", 
			TemplateType.Generation => "generation", 
			TemplateType.EndGeneration => "endgeneration", 
			TemplateType.Break => "break", 
			TemplateType.Continue => "continue", 
			TemplateType.Call => "call", 
			TemplateType.EndCall => "endcall", 
			_ => "Unknown", 
		};
	}
}
