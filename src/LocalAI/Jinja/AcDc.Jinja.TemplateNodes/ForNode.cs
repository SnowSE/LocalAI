// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AcDc.Jinja.TemplateNodes;

internal sealed class ForNode : TemplateNode
{
	private readonly IReadOnlyCollection<string> _variableNames;

	private readonly Expression _iterable;

	private readonly Expression? _condition;

	private readonly TemplateNode _body;

	private readonly bool _recursive;

	private readonly TemplateNode? _elseBody;

	public ForNode(Location location, IReadOnlyCollection<string> variableNames, Expression iterable, Expression? condition, TemplateNode body, bool recursive, TemplateNode? elseBody)
		: base(location)
	{
		ArgumentNullException.ThrowIfNull(iterable, "iterable");
		ArgumentNullException.ThrowIfNull(body, "body");
		_variableNames = variableNames;
		_iterable = iterable;
		_condition = condition;
		_body = body;
		_recursive = recursive;
		_elseBody = elseBody;
	}

	private protected override void DoRender(StringWriter writer, Context context)
	{
		Value iterableValue = _iterable.Evaluate(context);
		Visit(iterableValue);
		void Visit(Value iter)
		{
			Value filteredItems = Value.FromArray();
			if (!iter.IsNull)
			{
				if (!iterableValue.IsIterable)
				{
					throw new JinjaException("For loop must be iterable: " + iterableValue.Dump());
				}
				iterableValue.ForEach((Value item2) =>
				{
					context.DestructuringAssign(_variableNames, item2);
					if (_condition == null || _condition.Evaluate(context).ToBoolean())
					{
						filteredItems.Add(item2);
					}
				});
			}
			if (filteredItems.Count == 0)
			{
				_elseBody?.Render(writer, context);
			}
			else
			{
				Value value = (_recursive ? Value.Callable(loopFunction) : Value.Object());
				value.Set("length", filteredItems.Count);
				int cycleIndex = 0;
				value.Set("cycle", (Context _, ArgumentsValue args) =>
				{
					if (args.Args.Count == 0 || args.Kwargs.Count > 0)
					{
						throw new JinjaException("cycle() expects at least 1 positional argument and no named arg");
					}
					Value result = args.Args[cycleIndex];
					cycleIndex = (cycleIndex + 1) % args.Args.Count;
					return result;
				});
				Context context2 = Context.Make(Value.Object(), context);
				context2.Set("loop", value);
				for (int num = 0; num < filteredItems.Count; num++)
				{
					Value item = filteredItems[num];
					context2.DestructuringAssign(_variableNames, item);
					value.Set("index", (long)num + 1L);
					value.Set("index0", num);
					value.Set("revindex", filteredItems.Count - num);
					value.Set("revindex0", filteredItems.Count - num - 1);
					value.Set("length", filteredItems.Count);
					value.Set("first", num == 0);
					value.Set("last", num == filteredItems.Count - 1);
					value.Set("previtem", (num > 0) ? filteredItems[num - 1] : Value.Null);
					value.Set("nextitem", (num < filteredItems.Count - 1) ? filteredItems[num + 1] : Value.Null);
					try
					{
						_body.Render(writer, context2);
					}
					catch (LoopControlException ex)
					{
						if (ex.ControlType == LoopControlType.Break)
						{
							break;
						}
						_ = ex.ControlType;
						_ = 1;
					}
				}
			}
		}
		Value loopFunction(Context _, ArgumentsValue args)
		{
			if (args.Args.Count != 1 || args.Kwargs.Count > 0 || !args.Args[0].IsArray)
			{
				throw new JinjaException("loop() expects exactly one positional iterable argument");
			}
			Value iter = args.Args[0];
			Visit(iter);
			return Value.Null;
		}
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("for ");
		stringBuilder.Append(string.Join(", ", _variableNames));
		stringBuilder.Append(" in ");
		stringBuilder.Append(_iterable);
		if (_condition == null)
		{
			stringBuilder.Append(" if ");
			stringBuilder.Append(_condition);
		}
		stringBuilder.AppendLine();
		stringBuilder.Append(_body);
		if (_recursive)
		{
			stringBuilder.Append(" recursive");
		}
		if (_elseBody != null)
		{
			stringBuilder.Append(" else ");
			stringBuilder.Append(_elseBody);
		}
		return stringBuilder.ToString();
	}
}
