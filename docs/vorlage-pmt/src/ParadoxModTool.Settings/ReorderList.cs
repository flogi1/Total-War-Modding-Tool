namespace ParadoxModTool.Settings;

/// <summary>
/// Reine Helfer für vom Nutzer geordnete Kennungslisten (Kachelleiste, kleine Werkzeugleiste). Dieselben Methoden bedienen Pfeile,
/// Drag-and-drop und das Normalisieren beim Laden einer Sitzung.
/// </summary>
public static class ReorderList
{
    /// <summary>
    /// Verschiebt <paramref name="id"/> neben <paramref name="targetId"/>: stand es dahinter, landet es davor; stand es davor, landet es
    /// dahinter. <see langword="false"/> und unveränderte Liste bei gleichem Ziel oder wenn eine der Kennungen fehlt.
    /// </summary>
    public static bool MoveNextTo(IList<string> order, string id, string targetId)
    {
        var from = order.IndexOf(id);
        var to = order.IndexOf(targetId);
        if (from < 0 || to < 0 || from == to)
        {
            return false;
        }

        order.RemoveAt(from);
        order.Insert(order.IndexOf(targetId) + (from < to ? 1 : 0), id);
        return true;
    }

    /// <summary>Vertauscht zwei Einträge; <see langword="false"/> bei gleichem oder ungültigem Index.</summary>
    public static bool Swap(IList<string> order, int i, int j)
    {
        if (i == j || i < 0 || j < 0 || i >= order.Count || j >= order.Count)
        {
            return false;
        }

        (order[i], order[j]) = (order[j], order[i]);
        return true;
    }

    /// <summary>
    /// Bekannte Kennungen aus <paramref name="order"/> in ihrer Reihenfolge ohne Duplikate (das erste Vorkommen zählt), danach fehlende
    /// Katalog-Kennungen in Katalogreihenfolge. Kennungen, die weder im Katalog stehen noch <paramref name="isExtra"/> erfüllen
    /// (Trennlinien), fallen weg. <see langword="null"/> ergibt eine Kopie des Katalogs.
    /// </summary>
    public static List<string> Complete(IEnumerable<string>? order, IReadOnlyList<string> catalog, Func<string, bool>? isExtra = null)
    {
        var known = new HashSet<string>(catalog, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var id in order ?? Enumerable.Empty<string>())
        {
            if ((known.Contains(id) || isExtra?.Invoke(id) == true) && seen.Add(id))
            {
                result.Add(id);
            }
        }

        result.AddRange(catalog.Where(seen.Add));
        return result;
    }
}
