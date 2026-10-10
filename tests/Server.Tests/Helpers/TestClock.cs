namespace Stallions.Server.Tests.Helpers;

/// <summary>A TimeProvider whose time only moves when the test says so.</summary>
public class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    public DateTime UtcNow => Now.UtcDateTime;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
