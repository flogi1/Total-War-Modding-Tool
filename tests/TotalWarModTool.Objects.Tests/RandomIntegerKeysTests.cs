namespace TotalWarModTool.Objects.Tests;

public class RandomIntegerKeysTests
{
    [Fact]
    public void Values_lie_in_the_high_range()
    {
        var allocator = RandomIntegerKeys.Create(new Random(42), (_, _, _) => false);

        for (var i = 0; i < 1000; i++)
        {
            var value = long.Parse(allocator("units_tables", "key"), System.Globalization.CultureInfo.InvariantCulture);

            Assert.InRange(value, RandomIntegerKeys.Min, RandomIntegerKeys.Max);
        }
    }

    [Fact]
    public void A_taken_value_is_drawn_again()
    {
        var first = RandomIntegerKeys.Create(new Random(42), (_, _, _) => false)("units_tables", "key");
        var asked = new List<(string Table, string Field, string Value)>();
        var allocator = RandomIntegerKeys.Create(new Random(42), (table, field, value) =>
        {
            asked.Add((table, field, value));
            return asked.Count == 1;
        });

        var result = allocator("units_tables", "key");

        Assert.NotEqual(first, result);
        Assert.Equal(2, asked.Count);
        Assert.Equal(first, asked[0].Value);
        Assert.Equal(("units_tables", "key", result), asked[1]);
    }

    [Fact]
    public void A_value_is_never_handed_out_twice()
    {
        var allocator = RandomIntegerKeys.Create(new Random(42), (_, _, _) => false);

        var values = Enumerable.Range(0, 1000).Select(i => allocator(i % 2 == 0 ? "a_tables" : "b_tables", "key")).ToList();

        Assert.Equal(1000, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void It_gives_up_after_max_draws()
    {
        var draws = 0;
        var allocator = RandomIntegerKeys.Create(new Random(42), (_, _, _) =>
        {
            draws++;
            return true;
        });

        Assert.Throws<InvalidOperationException>(() => allocator("units_tables", "key"));
        Assert.Equal(RandomIntegerKeys.MaxDraws, draws);
    }
}
