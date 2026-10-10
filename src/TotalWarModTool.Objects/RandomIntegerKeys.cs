using System.Globalization;

namespace TotalWarModTool.Objects;

/// <summary>Allocates new values for the integer key fields of copied rows (spec 5.8, "Festlegungen aus Plan 4").</summary>
public static class RandomIntegerKeys
{
    public const long Min = 1_000_000_000;
    public const long Max = int.MaxValue;
    public const int MaxDraws = 100;

    /// <summary>
    /// Draws uniformly in [Min, Max]. A value that taken(table, field, value) reports, or that this allocator handed out before (for any table and field),
    /// is drawn again. Throws InvalidOperationException after MaxDraws draws for one call.
    /// </summary>
    public static NewIntegerKey Create(Random random, Func<string, string, string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(taken);
        var handedOut = new HashSet<long>();
        return (table, field) =>
        {
            for (var draw = 0; draw < MaxDraws; draw++)
            {
                var value = random.NextInt64(Min, Max + 1);
                if (handedOut.Contains(value) || taken(table, field, value.ToString(CultureInfo.InvariantCulture)))
                {
                    continue;
                }

                handedOut.Add(value);
                return value.ToString(CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException("No free integer key for " + table + "." + field + " found after " + MaxDraws + " draws.");
        };
    }
}
