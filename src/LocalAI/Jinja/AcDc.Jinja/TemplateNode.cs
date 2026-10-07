// Vendored from AcDc.Jinja 1.0.0 (MIT, Vincent Van Den Berghe), a C# port of google/minja.
// See LICENSE and README.md in this folder for what was changed.
#nullable disable warnings

using System;
using System.IO;

namespace AcDc.Jinja;

internal abstract class TemplateNode
{
	internal readonly Location Location;

	private protected TemplateNode(Location location)
	{
		ArgumentNullException.ThrowIfNull(location, "location");
		Location = location;
	}

	private protected abstract void DoRender(StringWriter writer, Context context);

	public string Render(Context context)
	{
		using StringWriter stringWriter = new StringWriter();
		Render(stringWriter, context);
		return stringWriter.ToString();
	}

	internal void Render(StringWriter writer, Context context)
	{
		try
		{
			DoRender(writer, context);
		}
		catch (LoopControlException ex) when (Location.Source != null)
		{
			throw new LoopControlException(ex.Message + Environment.NewLine + Location, ex.ControlType);
		}
		catch (JinjaException ex2)
		{
			ex2.Location = Location;
			throw;
		}
		catch (Exception ex3) when (Location.Source != null)
		{
			throw new JinjaException(ex3.Message + Environment.NewLine + Location, ex3);
		}
	}
}
