// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System.Collections.Generic;
using System.Text;

namespace AcDc.Jinja;

internal static class StringUtils
{
	public static string Strip(string s, string chars = "", bool left = true, bool right = true)
	{
		if (string.IsNullOrEmpty(chars))
		{
			chars = " \t\n\r";
		}
		int i = 0;
		int num = s.Length;
		if (left)
		{
			for (; i < num && chars.Contains(s[i]); i++)
			{
			}
		}
		if (right)
		{
			while (num > i && chars.Contains(s[num - 1]))
			{
				num--;
			}
		}
		int num2 = i;
		return s.Substring(num2, num - num2);
	}

	public static List<string> Split(string s, string sep)
	{
		List<string> list = new List<string>();
		int num = 0;
		while (num < s.Length)
		{
			int num2 = s.IndexOf(sep, num);
			if (num2 < 0)
			{
				list.Add(s.Substring(num));
				break;
			}
			int num3 = num;
			list.Add(s.Substring(num3, num2 - num3));
			num = num2 + sep.Length;
		}
		return list;
	}

	public static string Capitalize(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return s;
		}
		if (s.Length == 1)
		{
			return s.ToUpperInvariant();
		}
		return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
	}

	public static string HtmlEscape(string s)
	{
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		foreach (char c in s)
		{
			switch (c)
			{
			case '&':
				stringBuilder.Append("&amp;");
				break;
			case '<':
				stringBuilder.Append("&lt;");
				break;
			case '>':
				stringBuilder.Append("&gt;");
				break;
			case '"':
				stringBuilder.Append("&#34;");
				break;
			case '\'':
				stringBuilder.Append("&apos;");
				break;
			default:
				stringBuilder.Append(c);
				break;
			}
		}
		return stringBuilder.ToString();
	}
}
