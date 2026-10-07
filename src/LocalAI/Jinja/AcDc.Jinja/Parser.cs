// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AcDc.Jinja;

internal sealed partial class Parser
{
	public static string NormalizeNewlines(string s)
	{
		return NewlinesRegex().Replace(s, "\n");
	}

	public static TemplateNode Parse(string templateStr, Options options)
	{
		List<TemplateToken> tokens = new Tokenizer(NormalizeNewlines(templateStr)).Tokenize();
		return new TemplateNodeParser(templateStr, options, tokens).ParseTemplate(fully: true);
	}

	[GeneratedRegex("\r\n")]
	private static partial Regex NewlinesRegex();
}
