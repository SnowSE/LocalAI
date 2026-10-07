// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AcDc.Jinja.TemplateNodes;
using AcDc.Jinja.TemplateTokens;

namespace AcDc.Jinja;

internal sealed partial class TemplateNodeParser
{
	private readonly string _templateString;

	private readonly Options _options;

	private readonly IReadOnlyList<TemplateToken> _tokens;

	private int _it;

	public TemplateNodeParser(string templateString, Options options, IReadOnlyList<TemplateToken> tokens)
	{
		ArgumentNullException.ThrowIfNull(templateString, "templateString");
		_templateString = templateString;
		_it = 0;
		_options = options;
		_tokens = tokens;
	}

	private JinjaException Unexpected(TemplateToken token)
	{
		return new JinjaException("Unexpected " + TemplateToken.TypeToString(token.Type) + LocationExtensions.ToString(_templateString, token.Location.Position));
	}

	private JinjaException Unterminated(TemplateToken token)
	{
		return new JinjaException("Unterminated " + TemplateToken.TypeToString(token.Type) + LocationExtensions.ToString(_templateString, token.Location.Position));
	}

	public TemplateNode ParseTemplate(bool fully = false)
	{
		List<TemplateNode> list = new List<TemplateNode>();
		int count = _tokens.Count;
		while (_it < count)
		{
			int it = _it;
			TemplateToken templateToken = _tokens[_it++];
			if (templateToken is IfTemplateToken ifTemplateToken)
			{
				List<(Expression, TemplateNode)> list2 = new List<(Expression, TemplateNode)> { (ifTemplateToken.Condition, ParseTemplate()) };
				while (_it < count && _tokens[_it].Type == TemplateToken.TemplateType.Elif)
				{
					ElIfTemplateToken elIfTemplateToken = (ElIfTemplateToken)_tokens[_it++];
					list2.Add((elIfTemplateToken.Condition, ParseTemplate()));
				}
				if (_it < count && _tokens[_it].Type == TemplateToken.TemplateType.Else)
				{
					_it++;
					list2.Add((null, ParseTemplate()));
				}
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndIf)
				{
					throw Unterminated(_tokens[it]);
				}
				list.Add(new IfNode(templateToken.Location, list2));
			}
			else if (templateToken is ForTemplateToken forTemplateToken)
			{
				TemplateNode body = ParseTemplate();
				TemplateNode elseBody = null;
				if (_it < count && _tokens[_it].Type == TemplateToken.TemplateType.Else)
				{
					_it++;
					elseBody = ParseTemplate();
				}
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndFor)
				{
					throw Unterminated(_tokens[it]);
				}
				list.Add(new ForNode(templateToken.Location, forTemplateToken.VariableNames, forTemplateToken.Iterable, forTemplateToken.Condition, body, forTemplateToken.Recursive, elseBody));
			}
			else if (templateToken is GenerationTemplateToken)
			{
				TemplateNode item = ParseTemplate();
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndGeneration)
				{
					throw Unterminated(_tokens[it]);
				}
				list.Add(item);
			}
			else if (templateToken is TextTemplateToken textTemplateToken)
			{
				SpaceHandling spaceHandling = ((_it - 1 > 0) ? _tokens[_it - 2].PostSpace : SpaceHandling.Keep);
				SpaceHandling num = ((_it < count) ? _tokens[_it].PreSpace : SpaceHandling.Keep);
				string text = textTemplateToken.Text;
				if (num == SpaceHandling.Strip)
				{
					text = TrailingSpaceRegex().Replace(text, "");
				}
				else if (_options.LStripBlocks && _it < count)
				{
					int num2 = text.Length;
					while (num2 > 0 && (text[num2 - 1] == ' ' || text[num2 - 1] == '\t'))
					{
						num2--;
					}
					if ((num2 == 0 && _it - 1 == 0) || (num2 > 0 && text[num2 - 1] == '\n'))
					{
						text = text.Substring(0, num2);
					}
				}
				if (spaceHandling == SpaceHandling.Strip)
				{
					text = LeadingSpaceRegex().Replace(text, "");
				}
				else if (_options.TrimBlocks && _it - 1 > 0 && !(_tokens[_it - 2] is ExpressionTemplateToken) && !string.IsNullOrEmpty(text) && text[0] == '\n')
				{
					text = text.Substring(1);
				}
				if (_it == count && !_options.KeepTrailingNewline)
				{
					int length = text.Length;
					if (length > 0 && text[length - 1] == '\n')
					{
						length--;
						if (length > 0 && text[length - 1] == '\r')
						{
							length--;
						}
						text = text.Substring(0, length);
					}
				}
				list.Add(new TextNode(templateToken.Location, text));
			}
			else if (templateToken is ExpressionTemplateToken expressionTemplateToken)
			{
				list.Add(new ExpressionNode(templateToken.Location, expressionTemplateToken.Expression));
			}
			else if (templateToken is SetTemplateToken setTemplateToken)
			{
				if (setTemplateToken.Value != null)
				{
					list.Add(new SetNode(templateToken.Location, setTemplateToken.Namespace, setTemplateToken.VariableNames, setTemplateToken.Value));
					continue;
				}
				TemplateNode templateValues = ParseTemplate();
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndSet)
				{
					throw Unterminated(_tokens[it]);
				}
				if (!string.IsNullOrEmpty(setTemplateToken.Namespace))
				{
					throw new JinjaException("Namespaced set not supported in set with template value");
				}
				if (setTemplateToken.VariableNames.Count != 1)
				{
					throw new JinjaException("Structural assignment not supported in set with template value");
				}
				string name = setTemplateToken.VariableNames.Single();
				list.Add(new SetTemplateNode(templateToken.Location, name, templateValues));
			}
			else if (templateToken is MacroTemplateToken macroTemplateToken)
			{
				TemplateNode body2 = ParseTemplate();
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndMacro)
				{
					throw Unterminated(_tokens[it]);
				}
				list.Add(new MacroNode(templateToken.Location, macroTemplateToken.Name, macroTemplateToken.Parameters.ToList(), body2));
			}
			else if (templateToken is CallTemplateToken callTemplateToken)
			{
				TemplateNode body3 = ParseTemplate();
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndCall)
				{
					throw Unterminated(_tokens[it]);
				}
				list.Add(new CallNode(templateToken.Location, callTemplateToken.Callee, body3));
			}
			else if (templateToken is FilterTemplateToken filterTemplateToken)
			{
				TemplateNode body4 = ParseTemplate();
				if (_it == count || _tokens[_it++].Type != TemplateToken.TemplateType.EndFilter)
				{
					throw Unterminated(_tokens[it]);
				}
				list.Add(new FilterNode(templateToken.Location, filterTemplateToken.Filter, body4));
			}
			else
			{
				if (templateToken is CommentTemplateToken)
				{
					continue;
				}
				if (!(templateToken is LoopControlTemplateToken loopControlTemplateToken))
				{
					if (templateToken is EndForTemplateToken || templateToken is EndSetTemplateToken || templateToken is EndMacroTemplateToken || templateToken is EndCallTemplateToken || templateToken is EndFilterTemplateToken || templateToken is EndIfTemplateToken || templateToken is ElseTemplateToken || templateToken is EndGenerationTemplateToken || templateToken is ElIfTemplateToken)
					{
						_it--;
						break;
					}
					throw Unexpected(_tokens[_it - 1]);
				}
				list.Add(new LoopControlNode(templateToken.Location, loopControlTemplateToken.ControlType));
			}
		}
		if (fully && _it != count)
		{
			throw Unexpected(_tokens[_it]);
		}
		if (list.Count == 0)
		{
			return new TextNode(new Location
			{
				Source = _templateString,
				Position = 0
			}, string.Empty);
		}
		if (list.Count == 1)
		{
			return list[0];
		}
		return new SequenceNode(list[0].Location, list);
	}

	[GeneratedRegex("\\s+$")]
	private static partial Regex TrailingSpaceRegex();

	[GeneratedRegex("^\\s+")]
	private static partial Regex LeadingSpaceRegex();
}
