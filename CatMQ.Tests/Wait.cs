namespace CatMQ.Tests
{
    /// <summary>
    /// Polling helper so the tests wait for a condition rather than sleeping for a guessed amount of time.
    /// </summary>
    internal static class Wait
    {
        public static bool Until(Func<bool> condition, int timeoutSeconds = 10)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                Thread.Sleep(20);
            }
            return condition();
        }
    }
}
