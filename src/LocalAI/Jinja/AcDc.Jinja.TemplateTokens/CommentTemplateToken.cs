// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

namespace AcDc.Jinja.TemplateTokens;

internal sealed class CommentTemplateToken : TemplateToken
{
	public readonly string Comment;

	public CommentTemplateToken(Location location, SpaceHandling preSpace, SpaceHandling postSpace, string comment)
		: base(TemplateType.Comment, location, preSpace, postSpace)
	{
		Comment = comment;
	}

	public override string ToString()
	{
		return "{# " + Comment + " #}";
	}
}
