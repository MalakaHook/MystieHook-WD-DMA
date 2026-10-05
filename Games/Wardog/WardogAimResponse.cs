namespace MystieHook.Games.Wardog;

public enum WardogAimCurve { Exponential, Linear, EaseIn, EaseOut, Smoothstep, Smootherstep }

// Shared by the response worker and UI preview. No input, rendering or DMA operations.
public static class WardogAimCurves
{
    public static double Progress(WardogAimCurve curve, double time)
    {
        double t = Math.Clamp(time, 0, 1);
        return curve switch
        {
            WardogAimCurve.Exponential => 1 - Math.Exp(-3 * t),
            WardogAimCurve.Linear => t,
            WardogAimCurve.EaseIn => t * t,
            WardogAimCurve.EaseOut => t * (2 - t),
            WardogAimCurve.Smoothstep => t * t * (3 - 2 * t),
            WardogAimCurve.Smootherstep => t * t * t * (t * (6 * t - 15) + 10),
            _ => 0
        };
    }

    public static double Gain(WardogAimProfile profile, double elapsed, double seconds, double multiplier)
    {
        if (seconds <= 0) return 0;
        // Preserve the existing response exactly, including its asymptotic tail.
        if (profile.Curve == WardogAimCurve.Exponential) return profile.ResponseGain(seconds, multiplier);
        double duration = profile.DurationMs / 1000.0 / multiplier;
        double before = Progress(profile.Curve, elapsed / duration);
        double after = Progress(profile.Curve, (elapsed + seconds) / duration);
        // A fraction of remaining error makes subdivided time steps compose to the same curve.
        return before >= 1 ? 1 : Math.Clamp((after - before) / (1 - before), 0, 1);
    }
}

public sealed class WardogAimResponse
{
    private double _elapsed;
    private ulong _target;
    private WardogAimProfile _profile;
    public void Reset() { _elapsed = 0; _target = 0; _profile = null; }
    public double Advance(WardogAimProfile profile, ulong target, double seconds, double multiplier)
    {
        if (target != _target || profile != _profile) _elapsed = 0;
        _target = target;
        _profile = profile;
        double gain = WardogAimCurves.Gain(profile, _elapsed, seconds, multiplier);
        _elapsed = Math.Min(60, _elapsed + Math.Max(0, seconds));
        return gain;
    }
}
