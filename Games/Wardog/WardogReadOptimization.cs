using System.Diagnostics;

namespace MystieHook.Games.Wardog;

// Enabled in the promoted portable profile and optimized test profile.
// The ordinary local-test build can still retain its baseline for comparison.
public static class WardogReadOptimization
{
#if WARDOG_READ_OPTIMIZATION
    public const bool Enabled = true;
#else
    public const bool Enabled = false;
#endif
    public const int BackgroundRequests = 64, BackgroundBytes = 16 * 1024;
    public static bool Active => Enabled;
#if WARDOG_LITE_BATCHING
    public const bool LiteBatching = true;
#else
    public const bool LiteBatching = false;
#endif

    // Stagger initial acquisition only. Existing start-to-start 20 Hz pacing and
    // original source timestamps remain in charge of every subsequent cycle.
    public static void StaggerBackground(int offsetMilliseconds, CancellationToken token)
    {
        if (!Active) return;
        token.ThrowIfCancellationRequested();
        int phase = (int)((Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency)) % 50);
        int wait = (offsetMilliseconds - phase + 50) % 50;
        if (wait != 0) token.WaitHandle.WaitOne(wait);
        token.ThrowIfCancellationRequested();
    }
}
