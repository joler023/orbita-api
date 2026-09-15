namespace Orbita.UnitTests.TestSupport;

/// <summary>
/// <see cref="FixedTimeProvider"/> that can be moved forward.
///
/// Anything with an expiry — the 24h service window (ORB-B07), a signed media URL
/// (ORB-B06), a refresh or reset token — can only be tested honestly by letting time
/// pass, and a frozen clock cannot do that. Both of those stories shipped with the gap
/// written down rather than closed; this is the piece they were missing.
/// </summary>
internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}
