namespace TotalWarModTool.Settings;

/// <summary>
/// Pure helpers for id lists ordered by the user (tile bar, small toolbar). Arrows, drag and drop and normalizing on load all use
/// the same methods.
/// </summary>
public static class ReorderList
{
    /// <summary>
    /// Moves <paramref name="id"/> next to <paramref name="targetId"/>: an item that stood behind the target lands before it, an item
    /// that stood before the target lands behind it. Returns false and leaves the list unchanged for the same id or a missing id.
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

    /// <summary>Swaps two entries; returns false for equal or invalid indexes.</summary>
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
    /// Known ids from <paramref name="order"/> in their order without duplicates (the first occurrence counts), then the missing
    /// catalog ids in catalog order. Ids that are neither in the catalog nor satisfy <paramref name="isExtra"/> (separators) are
    /// dropped. <see langword="null"/> yields a copy of the catalog.
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
