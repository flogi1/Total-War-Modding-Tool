namespace ParadoxModTool.Settings;

/// <summary>Sichtbare Folge der kleinen Werkzeugleiste aus der Nutzerreihenfolge und der Sichtbarkeit je Eintrag.</summary>
public static class ToolbarLayout
{
    /// <summary>
    /// Ausgeblendete Einträge fallen weg; danach fallen Trennlinien am Anfang, am Ende und direkt hintereinander weg, sodass zwischen
    /// zwei sichtbaren Einträgen höchstens eine Trennlinie steht.
    /// </summary>
    public static IReadOnlyList<string> VisibleSequence(IReadOnlyList<string> order, Func<string, bool> isVisible)
    {
        var result = new List<string>();
        foreach (var id in order)
        {
            if (ToolbarCatalog.IsSeparator(id))
            {
                if (result.Count > 0 && !ToolbarCatalog.IsSeparator(result[^1]))
                {
                    result.Add(id);
                }
            }
            else if (isVisible(id))
            {
                result.Add(id);
            }
        }

        while (result.Count > 0 && ToolbarCatalog.IsSeparator(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
