// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Globalization;
using System.Text;

namespace AcDc.Jinja.Expressions;

internal sealed class LiteralExpr : Expression
{
	private readonly Value _value;

	public LiteralExpr(Location location, Value value)
		: base(location)
	{
		_value = value;
	}

	protected override Value DoEvaluate(Context context)
	{
		return _value;
	}

	public override string ToString()
	{
		if (_value.IsString)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append('"');
			string text = _value.ToString();
			foreach (char c in text)
			{
				switch (c)
				{
				case '\0':
					stringBuilder.Append("\\0");
					continue;
				case '\a':
					stringBuilder.Append("\\a");
					continue;
				case '\b':
					stringBuilder.Append("\\b");
					continue;
				case '\f':
					stringBuilder.Append("\\f");
					continue;
				case '\n':
					stringBuilder.Append("\\n");
					continue;
				case '\r':
					stringBuilder.Append("\\n");
					continue;
				case '\t':
					stringBuilder.Append("\\t");
					continue;
				case '\v':
					stringBuilder.Append("\\v");
					continue;
				case '\\':
					stringBuilder.Append("\\\\");
					continue;
				case '"':
					stringBuilder.Append("\\\"");
					continue;
				}
				if (char.GetUnicodeCategory(c) != UnicodeCategory.Control)
				{
					stringBuilder.Append(c);
					continue;
				}
				stringBuilder.Append("\\u");
				ushort num = c;
				stringBuilder.Append(num.ToString("x4"));
			}
			stringBuilder.Append('"');
			return stringBuilder.ToString();
		}
		return _value.ToString();
	}
}
