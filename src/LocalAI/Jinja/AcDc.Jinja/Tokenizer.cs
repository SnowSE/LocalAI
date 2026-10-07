// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AcDc.Jinja.Expressions;
using AcDc.Jinja.TemplateTokens;

namespace AcDc.Jinja;

internal sealed partial class Tokenizer
{
	private readonly string _templateString;

	private readonly int _start;

	private readonly int _end;

	private int _it;

	private Location Location => new Location
	{
		Source = _templateString,
		Position = _it - _start
	};

	public Tokenizer(string templateString)
	{
		ArgumentNullException.ThrowIfNull(templateString, "templateString");
		_templateString = templateString;
		_start = (_it = 0);
		_end = templateString.Length;
	}

	private bool ConsumeSpaces(SpaceHandling spaceHandling = SpaceHandling.Strip)
	{
		if (spaceHandling == SpaceHandling.Strip)
		{
			while (_it < _end && char.IsWhiteSpace(_templateString[_it]))
			{
				_it++;
			}
		}
		return true;
	}

	private string? ParseString()
	{
		ConsumeSpaces();
		if (_it == _end)
		{
			return null;
		}
		if (_templateString[_it] == '"' || _templateString[_it] == '\'')
		{
			return doParse(_templateString[_it]);
		}
		return null;
		string? doParse(char quote)
		{
			if (_it == _end || _templateString[_it] != quote)
			{
				return null;
			}
			StringBuilder stringBuilder = new StringBuilder();
			bool flag = false;
			_it++;
			while (_it < _end)
			{
				char c = _templateString[_it];
				if (flag)
				{
					flag = false;
					switch (c)
					{
					case 'n':
						stringBuilder.Append('\n');
						break;
					case 'r':
						stringBuilder.Append('\r');
						break;
					case 't':
						stringBuilder.Append('\t');
						break;
					case 'b':
						stringBuilder.Append('\b');
						break;
					case 'f':
						stringBuilder.Append('\f');
						break;
					case '\\':
						stringBuilder.Append('\\');
						break;
					default:
						if (c == quote)
						{
							stringBuilder.Append(quote);
						}
						else
						{
							stringBuilder.Append(c);
						}
						break;
					}
				}
				else if (c == '\\')
				{
					flag = true;
				}
				else
				{
					if (c == quote)
					{
						_it++;
						return stringBuilder.ToString();
					}
					stringBuilder.Append(_templateString[_it]);
				}
				_it++;
			}
			return null;
		}
	}

	private Value? ParseNumber()
	{
		int it = _it;
		ConsumeSpaces();
		int it2 = _it;
		bool flag = false;
		bool flag2 = false;
		if (_it < _end && (_templateString[_it] == '+' || _templateString[_it] == '-'))
		{
			_it++;
		}
		while (_it < _end)
		{
			if (char.IsDigit(_templateString[_it]))
			{
				_it++;
				continue;
			}
			if (_templateString[_it] == '.')
			{
				if (flag)
				{
					throw new JinjaException("Multiple decimal points");
				}
				flag = true;
				_it++;
				continue;
			}
			if (_it == it2 || (_templateString[_it] != 'e' && _templateString[_it] != 'E'))
			{
				break;
			}
			if (flag2)
			{
				throw new JinjaException("Multiple exponents");
			}
			flag2 = true;
			_it++;
		}
		if (it2 == _it)
		{
			_it = it;
			return null;
		}
		string templateString = _templateString;
		int num = it2;
		string text = templateString.Substring(num, _it - num);
		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return new Value(result);
		}
		if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			return new Value(result2);
		}
		throw new JinjaException("Failed to parse number: " + text);
	}

	private Value? ParseConstant()
	{
		int it = _it;
		ConsumeSpaces();
		if (_it == _end)
		{
			return null;
		}
		if (_templateString[_it] == '"' || _templateString[_it] == '\'')
		{
			string text = ParseString();
			if (text != null)
			{
				// LocalAI change: adjacent string literals join, as in Python and Jinja
				// ("a" "b" is "ab"). Gemma 4's template relies on it.
				while (true)
				{
					int before = _it;
					ConsumeSpaces();
					if (_it < _end && (_templateString[_it] == '"' || _templateString[_it] == '\'') && ParseString() is { } next)
					{
						text += next;
						continue;
					}
					_it = before;
					break;
				}
				return new Value(text);
			}
		}
		string text2 = ConsumeToken(ConstantTokenRegex());
		if (!string.IsNullOrEmpty(text2))
		{
			switch (text2)
			{
			case "true":
			case "True":
				return new Value(value: true);
			case "false":
			case "False":
				return new Value(value: false);
			case "None":
				return Value.Null;
			default:
				throw new JinjaException("Unknown constant token: " + text2);
			}
		}
		Value value = ParseNumber();
		if ((object)value != null)
		{
			return value;
		}
		_it = it;
		return null;
	}

	private bool PeekSymbols(IEnumerable<string> symbols)
	{
		foreach (string symbol in symbols)
		{
			int length = symbol.Length;
			if (_it + length <= _end && _templateString.Substring(_it, length) == symbol)
			{
				return true;
			}
		}
		return false;
	}

	private List<string> ConsumeTokenGroups(Regex regex, SpaceHandling spaceHandling = SpaceHandling.Strip)
	{
		int it = _it;
		ConsumeSpaces(spaceHandling);
		Match match = regex.Match(_templateString, _it, _end - _it);
		if (match.Success && match.Index == _it)
		{
			_it += match.Length;
			List<string> list = new List<string>();
			for (int i = 1; i < match.Groups.Count; i++)
			{
				list.Add(match.Groups[i].Value);
			}
			return list;
		}
		_it = it;
		return new List<string>();
	}

	private string ConsumeToken(Regex regex, SpaceHandling spaceHandling = SpaceHandling.Strip)
	{
		int it = _it;
		ConsumeSpaces(spaceHandling);
		Match match = regex.Match(_templateString, _it, _end - _it);
		if (match.Success && match.Index == _it)
		{
			_it += match.Length;
			return match.Value;
		}
		_it = it;
		return "";
	}

	private string ConsumeToken(string token, SpaceHandling spaceHandling = SpaceHandling.Strip)
	{
		int it = _it;
		ConsumeSpaces(spaceHandling);
		int length = token.Length;
		if (_it + length <= _end && _templateString.Substring(_it, length) == token)
		{
			_it += length;
			return token;
		}
		_it = it;
		return "";
	}

	private Expression? ParseExpression(bool allowIfExpression = true)
	{
		Expression expression = ParseLogicalOr();
		if (_it == _end)
		{
			return expression;
		}
		if (!allowIfExpression)
		{
			return expression;
		}
		if (string.IsNullOrEmpty(ConsumeToken(IfTokenRegex())))
		{
			return expression;
		}
		Location location = Location;
		var (condition, elseExpression) = ParseIfExpression();
		return new IfExpr(location, condition, expression, elseExpression);
	}

	private (Expression Condition, Expression? ElseExpression) ParseIfExpression()
	{
		Expression? item = ParseLogicalOr() ?? throw new JinjaException("Expected condition expression");
		string value = ConsumeToken(ElseTokenRegex());
		Expression expression = null;
		if (!string.IsNullOrEmpty(value))
		{
			expression = ParseExpression(allowIfExpression: false);
			if (expression == null)
			{
				throw new JinjaException("Expected 'else' expression");
			}
		}
		return (Condition: item, ElseExpression: expression);
	}

	private Expression? ParseLogicalOr()
	{
		Expression expression = ParseLogicalAnd() ?? throw new JinjaException("Expected left side of 'logical or' expression");
		Location location = Location;
		while (!string.IsNullOrEmpty(ConsumeToken(OrTokenRegex())))
		{
			Expression right = ParseLogicalAnd() ?? throw new JinjaException("Expected right side of 'logical or' expression");
			expression = new BinaryOpExpr(location, BinaryOpExpr.Op.Or, expression, right);
		}
		return expression;
	}

	private Expression? ParseLogicalNot()
	{
		Location location = Location;
		Regex regex = NotTokenRegex();
		if (!string.IsNullOrEmpty(ConsumeToken(regex)))
		{
			Expression expression = ParseLogicalNot() ?? throw new JinjaException("Expected expression after 'not' keyword");
			return new UnaryOpExpr(location, UnaryOpExpr.Op.LogicalNot, expression);
		}
		return ParseLogicalCompare();
	}

	private Expression? ParseLogicalAnd()
	{
		Expression expression = ParseLogicalNot() ?? throw new JinjaException("Expected left side of 'logical and' expression");
		Location location = Location;
		while (!string.IsNullOrEmpty(ConsumeToken(AndTokenRegex())))
		{
			Expression right = ParseLogicalNot() ?? throw new JinjaException("Expected right side of 'logical and' expression");
			expression = new BinaryOpExpr(location, BinaryOpExpr.Op.And, expression, right);
		}
		return expression;
	}

	private Expression? ParseLogicalCompare()
	{
		Expression expression = ParseStringConcat() ?? throw new JinjaException("Expected left side of 'logical compare' expression");
		string text;
		while (!string.IsNullOrEmpty(text = ConsumeToken(CompareTokenRegex())))
		{
			Location location = Location;
			if (text == "is")
			{
				bool flag = !string.IsNullOrEmpty(ConsumeToken(NotTokenRegex()));
				VariableExpr right = ParseIdentifier() ?? throw new JinjaException("Expected identifier after 'is' keyword");
				return new BinaryOpExpr(expression.Location, flag ? BinaryOpExpr.Op.IsNot : BinaryOpExpr.Op.Is, expression, right);
			}
			Expression right2 = ParseStringConcat() ?? throw new JinjaException("Expected right side of 'logical compare' expression");
			BinaryOpExpr.Op op;
			switch (text)
			{
			case "==":
				op = BinaryOpExpr.Op.Eq;
				break;
			case "!=":
				op = BinaryOpExpr.Op.Ne;
				break;
			case "<":
				op = BinaryOpExpr.Op.Lt;
				break;
			case ">":
				op = BinaryOpExpr.Op.Gt;
				break;
			case "<=":
				op = BinaryOpExpr.Op.Le;
				break;
			case ">=":
				op = BinaryOpExpr.Op.Ge;
				break;
			case "in":
				op = BinaryOpExpr.Op.In;
				break;
			default:
				if (!(text.Substring(0, 3) == "not"))
				{
					throw new JinjaException("Unknown comparison operator: " + text);
				}
				op = BinaryOpExpr.Op.NotIn;
				break;
			}
			BinaryOpExpr.Op op2 = op;
			expression = new BinaryOpExpr(location, op2, expression, right2);
		}
		return expression;
	}

	private List<(string? Name, Expression? Expression)> ParseParameters()
	{
		ConsumeSpaces();
		if (string.IsNullOrEmpty(ConsumeToken("(")))
		{
			throw new JinjaException("Expected opening parenthesis in param list");
		}
		List<(string, Expression)> list = new List<(string, Expression)>();
		while (_it < _end)
		{
			if (!string.IsNullOrEmpty(ConsumeToken(")")))
			{
				return list;
			}
			Expression expression = ParseExpression() ?? throw new JinjaException("Expected expression in param list");
			if (expression is VariableExpr variableExpr)
			{
				if (!string.IsNullOrEmpty(ConsumeToken("=")))
				{
					Expression item = ParseExpression() ?? throw new JinjaException("Expected expression for named arg");
					list.Add((variableExpr.Name, item));
				}
				else
				{
					list.Add((variableExpr.Name, null));
				}
			}
			else
			{
				list.Add((null, expression));
			}
			if (string.IsNullOrEmpty(ConsumeToken(",")))
			{
				if (string.IsNullOrEmpty(ConsumeToken(")")))
				{
					throw new JinjaException("Expected closing parenthesis in param list");
				}
				return list;
			}
		}
		throw new JinjaException("Expected closing parenthesis in param list");
	}

	private ArgumentsExpression ParseCallArgs()
	{
		ConsumeSpaces();
		if (string.IsNullOrEmpty(ConsumeToken("(")))
		{
			throw new JinjaException("Expected opening parenthesis in call args");
		}
		ArgumentsExpression argumentsExpression = new ArgumentsExpression();
		while (_it < _end)
		{
			if (!string.IsNullOrEmpty(ConsumeToken(")")))
			{
				return argumentsExpression;
			}
			Expression expression = ParseExpression() ?? throw new JinjaException("Expected expression in call args");
			if (expression is VariableExpr variableExpr)
			{
				if (!string.IsNullOrEmpty(ConsumeToken("=")))
				{
					Expression item = ParseExpression() ?? throw new JinjaException("Expected expression for named arg");
					argumentsExpression.KwArgs.Add((variableExpr.Name, item));
				}
				else
				{
					argumentsExpression.Args.Add(expression);
				}
			}
			else
			{
				argumentsExpression.Args.Add(expression);
			}
			if (string.IsNullOrEmpty(ConsumeToken(",")))
			{
				if (string.IsNullOrEmpty(ConsumeToken(")")))
				{
					throw new JinjaException("Expected closing parenthesis in call args");
				}
				return argumentsExpression;
			}
		}
		throw new JinjaException("Expected closing parenthesis in call args");
	}

	private VariableExpr? ParseIdentifier()
	{
		Regex regex = IdentifierRegex();
		Location location = Location;
		string text = ConsumeToken(regex);
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		return new VariableExpr(location, text);
	}

	private Expression? ParseStringConcat()
	{
		Expression expression = ParseMathPow() ?? throw new JinjaException("Expected left side of 'string concat' expression");
		if (!string.IsNullOrEmpty(ConsumeToken(StringConcatTokenRegex())))
		{
			Expression right = ParseLogicalAnd() ?? throw new JinjaException("Expected right side of 'string concat' expression");
			expression = new BinaryOpExpr(Location, BinaryOpExpr.Op.StrConcat, expression, right);
		}
		return expression;
	}

	private Expression? ParseMathPow()
	{
		Expression expression = ParseMathPlusMinus() ?? throw new JinjaException("Expected left side of 'math pow' expression");
		while (!string.IsNullOrEmpty(ConsumeToken("**")))
		{
			Expression right = ParseMathPlusMinus() ?? throw new JinjaException("Expected right side of 'math pow' expression");
			expression = new BinaryOpExpr(Location, BinaryOpExpr.Op.MulMul, expression, right);
		}
		return expression;
	}

	private Expression? ParseMathPlusMinus()
	{
		Expression expression = ParseMathMulDiv() ?? throw new JinjaException("Expected left side of 'math plus/minus' expression");
		string text;
		while (!string.IsNullOrEmpty(text = ConsumeToken(PlusMinusTokenRegex())))
		{
			Expression right = ParseMathMulDiv() ?? throw new JinjaException("Expected right side of 'math plus/minus' expression");
			BinaryOpExpr.Op op = ((text == "+") ? BinaryOpExpr.Op.Add : BinaryOpExpr.Op.Sub);
			expression = new BinaryOpExpr(Location, op, expression, right);
		}
		return expression;
	}

	private Expression? ParseMathMulDiv()
	{
		Expression expression = ParseMathUnaryPlusMinus() ?? throw new JinjaException("Expected left side of 'math mul/div' expression");
		string text;
		while (!string.IsNullOrEmpty(text = ConsumeToken(MulDivTokenRegex())))
		{
			Expression right = ParseMathUnaryPlusMinus() ?? throw new JinjaException("Expected right side of 'math mul/div' expression");
			expression = new BinaryOpExpr(Location, text switch
			{
				"*" => BinaryOpExpr.Op.Mul, 
				"**" => BinaryOpExpr.Op.MulMul, 
				"/" => BinaryOpExpr.Op.Div, 
				"//" => BinaryOpExpr.Op.DivDiv, 
				_ => BinaryOpExpr.Op.Mod, 
			}, expression, right);
		}
		if (!string.IsNullOrEmpty(ConsumeToken("|")))
		{
			Expression expression2 = ParseMathMulDiv();
			if (expression2 is FilterExpr filterExpr)
			{
				filterExpr.Prepend(expression);
				return expression2;
			}
			if (expression2 == null)
			{
				throw new JinjaException("Expected expression after filter");
			}
			Location location = Location;
			int num = 2;
			List<Expression> list = new List<Expression>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<Expression> span = CollectionsMarshal.AsSpan(list);
			span[0] = expression;
			span[1] = expression2;
			return new FilterExpr(location, list);
		}
		return expression;
	}

	private CallExpr CallFunc(string name, ArgumentsExpression args)
	{
		return new CallExpr(Location, new VariableExpr(Location, name), args);
	}

	private Expression? ParseMathUnaryPlusMinus()
	{
		string text = ConsumeToken(UnaryPlusMinTokenRegex());
		Expression expression = ParseExpansion() ?? throw new JinjaException("Expected expr of 'unary plus/minus/expansion' expression");
		if (!string.IsNullOrEmpty(text))
		{
			UnaryOpExpr.Op op = ((!(text == "+")) ? UnaryOpExpr.Op.Minus : UnaryOpExpr.Op.Plus);
			return new UnaryOpExpr(Location, op, expression);
		}
		return expression;
	}

	private Expression? ParseExpansion()
	{
		string text = ConsumeToken(ExpansionTokenRegex());
		Expression expression = ParseValueExpression();
		if (string.IsNullOrEmpty(text))
		{
			return expression;
		}
		if (expression == null)
		{
			throw new JinjaException("Expected expr of 'expansion' expression");
		}
		UnaryOpExpr.Op op = ((text == "*") ? UnaryOpExpr.Op.Expansion : UnaryOpExpr.Op.ExpansionDict);
		return new UnaryOpExpr(Location, op, expression);
	}

	private Expression? ParseValueExpression()
	{
		Expression expression = ParseValue();
		while (_it < _end && ConsumeSpaces() && PeekSymbols((new string[3] { "[", ".", "(" })))
		{
			if (!string.IsNullOrEmpty(ConsumeToken("[")))
			{
				Location location = Location;
				Expression expression2 = null;
				Expression end = null;
				Expression step = null;
				bool flag = false;
				bool flag2 = false;
				if (!PeekSymbols(new string[] { ":" }))
				{
					expression2 = ParseExpression();
				}
				if (!string.IsNullOrEmpty(ConsumeToken(":")))
				{
					flag = true;
					if (!PeekSymbols((new string[2] { ":", "]" })))
					{
						end = ParseExpression();
					}
					if (!string.IsNullOrEmpty(ConsumeToken(":")))
					{
						flag2 = true;
						if (!PeekSymbols(new string[] { "]" }))
						{
							step = ParseExpression();
						}
					}
				}
				Expression index = ((flag | flag2) ? new SliceExpr(location, expression2, end, step) : expression2) ?? throw new JinjaException("Empty index in subscript");
				if (string.IsNullOrEmpty(ConsumeToken("]")))
				{
					throw new JinjaException("Expected closing bracket in subscript");
				}
				expression = new SubscriptExpr(expression.Location, expression, index);
			}
			else if (!string.IsNullOrEmpty(ConsumeToken(".")))
			{
				VariableExpr variableExpr = ParseIdentifier() ?? throw new JinjaException("Expected identifier in subscript");
				ConsumeSpaces();
				if (PeekSymbols(new string[] { "(" }))
				{
					ArgumentsExpression arguments = ParseCallArgs();
					expression = new MethodCallExpr(variableExpr.Location, expression, variableExpr, arguments);
				}
				else
				{
					LiteralExpr index2 = new LiteralExpr(variableExpr.Location, new Value(variableExpr.Name));
					expression = new SubscriptExpr(variableExpr.Location, expression, index2);
				}
			}
			else if (PeekSymbols(new string[] { "(" }))
			{
				Location location2 = Location;
				ArgumentsExpression arguments2 = ParseCallArgs();
				expression = new CallExpr(location2, expression, arguments2);
			}
			ConsumeSpaces();
		}
		return expression;
		Expression? ParseValue()
		{
			Location location3 = Location;
			Value value = ParseConstant();
			if ((object)value != null)
			{
				return new LiteralExpr(location3, value);
			}
			if (!string.IsNullOrEmpty(ConsumeToken(NullTokenRegex())))
			{
				return new LiteralExpr(location3, Value.Null);
			}
			VariableExpr variableExpr2 = ParseIdentifier();
			if (variableExpr2 != null)
			{
				return variableExpr2;
			}
			Expression expression3 = ParseBracedExpressionOrArray();
			if (expression3 != null)
			{
				return expression3;
			}
			ArrayExpr arrayExpr = ParseArray();
			if (arrayExpr != null)
			{
				return arrayExpr;
			}
			DictExpr dictExpr = ParseDictionary();
			if (dictExpr != null)
			{
				return dictExpr;
			}
			throw new JinjaException("Expected value expression");
		}
	}

	private Expression? ParseBracedExpressionOrArray()
	{
		if (string.IsNullOrEmpty(ConsumeToken("(")))
		{
			return null;
		}
		Expression expression = ParseExpression() ?? throw new JinjaException("Expected expression in braced expression");
		if (!string.IsNullOrEmpty(ConsumeToken(")")))
		{
			return expression;
		}
		List<Expression> list = new List<Expression> { expression };
		while (_it < _end)
		{
			if (string.IsNullOrEmpty(ConsumeToken(",")))
			{
				throw new JinjaException("Expected comma in tuple");
			}
			Expression item = ParseExpression() ?? throw new JinjaException("Expected expression in tuple");
			list.Add(item);
			if (!string.IsNullOrEmpty(ConsumeToken(")")))
			{
				return new ArrayExpr(Location, list);
			}
		}
		throw new JinjaException("Expected closing parenthesis");
	}

	private ArrayExpr? ParseArray()
	{
		if (string.IsNullOrEmpty(ConsumeToken("[")))
		{
			return null;
		}
		List<Expression> list = new List<Expression>();
		if (!string.IsNullOrEmpty(ConsumeToken("]")))
		{
			return new ArrayExpr(Location, list);
		}
		Expression item = ParseExpression() ?? throw new JinjaException("Expected first expression in array");
		list.Add(item);
		while (_it < _end)
		{
			if (!string.IsNullOrEmpty(ConsumeToken(",")))
			{
				Expression item2 = ParseExpression() ?? throw new JinjaException("Expected expression in array");
				list.Add(item2);
				continue;
			}
			if (!string.IsNullOrEmpty(ConsumeToken("]")))
			{
				return new ArrayExpr(Location, list);
			}
			throw new JinjaException("Expected comma or closing bracket in array");
		}
		throw new JinjaException("Expected closing bracket");
	}

	private DictExpr? ParseDictionary()
	{
		if (string.IsNullOrEmpty(ConsumeToken("{")))
		{
			return null;
		}
		List<(Expression Key, Expression Value)> elements = new List<(Expression, Expression)>();
		if (!string.IsNullOrEmpty(ConsumeToken("}")))
		{
			return new DictExpr(Location, elements);
		}
		ParseKeyValuePair();
		while (_it < _end)
		{
			if (!string.IsNullOrEmpty(ConsumeToken(",")))
			{
				ParseKeyValuePair();
				continue;
			}
			if (!string.IsNullOrEmpty(ConsumeToken("}")))
			{
				return new DictExpr(Location, elements);
			}
			throw new JinjaException("Expected comma or closing brace in dictionary");
		}
		throw new JinjaException("Expected closing brace");
		void ParseKeyValuePair()
		{
			Expression item = ParseExpression() ?? throw new JinjaException("Expected key in dictionary");
			if (string.IsNullOrEmpty(ConsumeToken(":")))
			{
				throw new JinjaException("Expected colon between key & value in dictionary");
			}
			Expression item2 = ParseExpression() ?? throw new JinjaException("Expected value in dictionary");
			elements.Add((item, item2));
		}
	}

	private static SpaceHandling ParsePreSpace(string s)
	{
		if (!(s == "-"))
		{
			return SpaceHandling.Keep;
		}
		return SpaceHandling.Strip;
	}

	private static SpaceHandling ParsePostSpace(string s)
	{
		if (!(s == "-"))
		{
			return SpaceHandling.Keep;
		}
		return SpaceHandling.Strip;
	}

	private List<string> ParseVarNames()
	{
		List<string> list = ConsumeTokenGroups(VarNamesRegex());
		if (list.Count == 0)
		{
			throw new JinjaException("Expected variable names");
		}
		List<string> list2 = new List<string>();
		string[] array = list[0].Split(',');
		foreach (string s in array)
		{
			list2.Add(StringUtils.Strip(s));
		}
		return list2;
	}

	public List<TemplateToken> Tokenize()
	{
		List<TemplateToken> list = new List<TemplateToken>();
		try
		{
			while (_it < _end)
			{
				Location location = Location;
				List<string> group = ConsumeTokenGroups(CommentTokenRegex(), SpaceHandling.Keep);
				if (group.Count > 0)
				{
					SpaceHandling preSpace = ParsePreSpace(group[0]);
					string comment = group[1];
					SpaceHandling postSpace = ParsePostSpace(group[2]);
					list.Add(new CommentTemplateToken(location, preSpace, postSpace, comment));
					continue;
				}
				group = ConsumeTokenGroups(ExprOpenRegex(), SpaceHandling.Keep);
				if (group.Count > 0)
				{
					SpaceHandling preSpace2 = ParsePreSpace(group[0]);
					Expression expression = ParseExpression();
					group = ConsumeTokenGroups(ExprCloseRegex());
					if (group.Count == 0)
					{
						throw new JinjaException("Expected closing expression tag");
					}
					SpaceHandling postSpace2 = ParsePostSpace(group[0]);
					list.Add(new ExpressionTemplateToken(location, preSpace2, postSpace2, expression));
					continue;
				}
				group = ConsumeTokenGroups(BlockOpenRegex(), SpaceHandling.Keep);
				if (group.Count > 0)
				{
					SpaceHandling preSpace3 = ParsePreSpace(group[0]);
					string text = ConsumeToken(BlockKeywordTokenRegex());
					if (string.IsNullOrEmpty(text))
					{
						throw new JinjaException("Expected block keyword");
					}
					switch (text)
					{
					case "if":
					{
						Expression condition = ParseExpression() ?? throw new JinjaException("Expected condition in if block");
						SpaceHandling postSpace14 = ParseBlockClose();
						list.Add(new IfTemplateToken(location, preSpace3, postSpace14, condition));
						break;
					}
					case "elif":
					{
						Expression condition2 = ParseExpression() ?? throw new JinjaException("Expected condition in elif block");
						SpaceHandling postSpace17 = ParseBlockClose();
						list.Add(new ElIfTemplateToken(location, preSpace3, postSpace17, condition2));
						break;
					}
					case "else":
					{
						SpaceHandling postSpace9 = ParseBlockClose();
						list.Add(new ElseTemplateToken(location, preSpace3, postSpace9));
						break;
					}
					case "endif":
					{
						SpaceHandling postSpace10 = ParseBlockClose();
						list.Add(new EndIfTemplateToken(location, preSpace3, postSpace10));
						break;
					}
					case "for":
					{
						List<string> variableNames = ParseVarNames();
						Regex regex = InRegex();
						if (string.IsNullOrEmpty(ConsumeToken(regex)))
						{
							throw new JinjaException("Expected 'in' keyword in for block");
						}
						Expression iterable = ParseExpression(allowIfExpression: false) ?? throw new JinjaException("Expected iterable in for block");
						Regex regex2 = IfRegex();
						Expression expression2 = null;
						if (!string.IsNullOrEmpty(ConsumeToken(regex2)))
						{
							expression2 = ParseExpression();
						}
						Regex regex3 = RecursiveRegex();
						bool recursive = !string.IsNullOrEmpty(ConsumeToken(regex3));
						SpaceHandling postSpace5 = ParseBlockClose();
						list.Add(new ForTemplateToken(location, preSpace3, postSpace5, variableNames, iterable, expression2 ?? new LiteralExpr(location, new Value(value: true)), recursive));
						break;
					}
					case "endfor":
					{
						SpaceHandling postSpace13 = ParseBlockClose();
						list.Add(new EndForTemplateToken(location, preSpace3, postSpace13));
						break;
					}
					case "generation":
					{
						SpaceHandling postSpace4 = ParseBlockClose();
						list.Add(new GenerationTemplateToken(location, preSpace3, postSpace4));
						break;
					}
					case "endgeneration":
					{
						SpaceHandling postSpace18 = ParseBlockClose();
						list.Add(new EndGenerationTemplateToken(location, preSpace3, postSpace18));
						break;
					}
					case "set":
					{
						Regex regex4 = NamespacedVariableRegex();
						string text2 = string.Empty;
						List<string> list2 = new List<string>();
						Expression expression3 = null;
						group = ConsumeTokenGroups(regex4);
						if (group.Count > 0)
						{
							text2 = group[0];
							list2.Add(group[1]);
							if (string.IsNullOrEmpty(ConsumeToken("=")))
							{
								throw new JinjaException("Expected equals sign in set");
							}
							expression3 = ParseExpression();
							if (expression3 == null)
							{
								throw new JinjaException("Expected value in set block");
							}
						}
						else
						{
							list2 = ParseVarNames();
							if (!string.IsNullOrEmpty(ConsumeToken("=")))
							{
								expression3 = ParseExpression();
								if (expression3 == null)
								{
									throw new JinjaException("Expected value in set block");
								}
							}
						}
						SpaceHandling postSpace6 = ParseBlockClose();
						list.Add(new SetTemplateToken(location, preSpace3, postSpace6, text2, list2, expression3));
						break;
					}
					case "endset":
					{
						SpaceHandling postSpace19 = ParseBlockClose();
						list.Add(new EndSetTemplateToken(location, preSpace3, postSpace19));
						break;
					}
					case "macro":
					{
						VariableExpr name = ParseIdentifier() ?? throw new JinjaException("Expected macro name in macro");
						List<(string, Expression)> parameters = ParseParameters();
						SpaceHandling postSpace16 = ParseBlockClose();
						list.Add(new MacroTemplateToken(location, preSpace3, postSpace16, name, parameters));
						break;
					}
					case "endmacro":
					{
						SpaceHandling postSpace15 = ParseBlockClose();
						list.Add(new EndMacroTemplateToken(location, preSpace3, postSpace15));
						break;
					}
					case "call":
					{
						Expression callee = ParseExpression() ?? throw new JinjaException("Expected expression in call");
						SpaceHandling postSpace12 = ParseBlockClose();
						list.Add(new CallTemplateToken(location, preSpace3, postSpace12, callee));
						break;
					}
					case "endcall":
					{
						SpaceHandling postSpace11 = ParseBlockClose();
						list.Add(new EndCallTemplateToken(location, preSpace3, postSpace11));
						break;
					}
					case "filter":
					{
						Expression filter = ParseExpression() ?? throw new JinjaException("Expected expression in filter");
						SpaceHandling postSpace8 = ParseBlockClose();
						list.Add(new FilterTemplateToken(location, preSpace3, postSpace8, filter));
						break;
					}
					case "endfilter":
					{
						SpaceHandling postSpace7 = ParseBlockClose();
						list.Add(new EndFilterTemplateToken(location, preSpace3, postSpace7));
						break;
					}
					case "break":
					case "continue":
					{
						SpaceHandling postSpace3 = ParseBlockClose();
						list.Add(new LoopControlTemplateToken(location, preSpace3, postSpace3, (!(text == "break")) ? LoopControlType.Continue : LoopControlType.Break));
						break;
					}
					default:
						throw new JinjaException("Unexpected block: " + text);
					}
					continue;
				}
				Match match = NonTextOpenRegex().Match(_templateString, _it, _end - _it);
				if (match.Success)
				{
					if (match.Index == _it)
					{
						if (match.Value != "{#")
						{
							throw new JinjaException("Internal error: Expected a comment");
						}
						throw new JinjaException("Missing end of comment tag");
					}
					string templateString = _templateString;
					int it = _it;
					string text3 = templateString.Substring(it, match.Index - it);
					_it = match.Index;
					list.Add(new TextTemplateToken(location, SpaceHandling.Keep, SpaceHandling.Keep, text3));
				}
				else
				{
					string templateString2 = _templateString;
					int it = _it;
					string text3 = templateString2.Substring(it, _end - it);
					_it = _end;
					list.Add(new TextTemplateToken(location, SpaceHandling.Keep, SpaceHandling.Keep, text3));
				}
				SpaceHandling ParseBlockClose()
				{
					group = ConsumeTokenGroups(BlockCloseRegex());
					if (group.Count == 0)
					{
						throw new JinjaException("Expected closing block tag");
					}
					return ParsePostSpace(group[0]);
				}
			}
			return list;
		}
		catch (Exception ex)
		{
			throw new JinjaException(ex.Message + LocationExtensions.ToString(_templateString, _it), ex);
		}
	}

	[GeneratedRegex("true\\b|True\\b|false\\b|False\\b|None\\b")]
	private static partial Regex ConstantTokenRegex();

	[GeneratedRegex("\\bif\\b")]
	private static partial Regex IfTokenRegex();

	[GeneratedRegex("else\\b")]
	private static partial Regex ElseTokenRegex();

	[GeneratedRegex("or\\b")]
	private static partial Regex OrTokenRegex();

	[GeneratedRegex("not\\b")]
	private static partial Regex NotTokenRegex();

	[GeneratedRegex("and\\b")]
	private static partial Regex AndTokenRegex();

	[GeneratedRegex("==|!=|<=?|>=?|in\\b|is\\b|not\\s+in\\b")]
	private static partial Regex CompareTokenRegex();

	[GeneratedRegex("((?!(?:not|is|and|or|del)\\b)[a-zA-Z_]\\w*)")]
	private static partial Regex IdentifierRegex();

	[GeneratedRegex("~(?!\\})")]
	private static partial Regex StringConcatTokenRegex();

	[GeneratedRegex("\\+|-(?![}%#]\\})")]
	private static partial Regex PlusMinusTokenRegex();

	[GeneratedRegex("\\*\\*?|//?|%(?!\\})")]
	private static partial Regex MulDivTokenRegex();

	[GeneratedRegex("\\*\\*?")]
	private static partial Regex ExpansionTokenRegex();

	[GeneratedRegex("null\\b")]
	private static partial Regex NullTokenRegex();

	[GeneratedRegex("((?:\\w+)(?:\\s*,\\s*(?:\\w+))*)\\s*")]
	private static partial Regex VarNamesRegex();

	[GeneratedRegex("\\{#([-~]?)([\\s\\S]*?)([-~]?)#\\}")]
	private static partial Regex CommentTokenRegex();

	[GeneratedRegex("\\{\\{([-~])?")]
	private static partial Regex ExprOpenRegex();

	[GeneratedRegex("^\\{%([-~])?\\s*")]
	private static partial Regex BlockOpenRegex();

	[GeneratedRegex("(if|else|elif|endif|for|endfor|generation|endgeneration|set|endset|block|endblock|macro|endmacro|filter|endfilter|break|continue|call|endcall)\\b")]
	private static partial Regex BlockKeywordTokenRegex();

	[GeneratedRegex("\\{\\{|\\{%|\\{#")]
	private static partial Regex NonTextOpenRegex();

	[GeneratedRegex("\\s*([-~])?\\}\\}")]
	private static partial Regex ExprCloseRegex();

	[GeneratedRegex("\\s*([-~])?%\\}")]
	private static partial Regex BlockCloseRegex();

	[GeneratedRegex("(\\w+)\\s*\\.\\s*(\\w+)")]
	private static partial Regex NamespacedVariableRegex();

	[GeneratedRegex("recursive\\b")]
	private static partial Regex RecursiveRegex();

	[GeneratedRegex("if\\b")]
	private static partial Regex IfRegex();

	[GeneratedRegex("in\\b")]
	private static partial Regex InRegex();

	[GeneratedRegex("\\+|-(?![}%#]\\})")]
	private static partial Regex UnaryPlusMinTokenRegex();
}
