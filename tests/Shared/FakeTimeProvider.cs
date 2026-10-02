// Root namespace on purpose: resolves unqualified from every TotalWarModTool.<Module>.Tests namespace.
namespace TotalWarModTool;

/// <summary>A <see cref="TimeProvider"/> whose clock only moves when a test sets <see cref="Now"/>. Local time is UTC.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
