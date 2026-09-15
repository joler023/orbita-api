namespace Orbita.IntegrationTests.TestSupport;

/// <summary>Polls until a condition holds or the timeout elapses — for asserting on background worker side effects.</summary>
public static class Eventually
{
    public static async Task AssertAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(interval);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }
}
