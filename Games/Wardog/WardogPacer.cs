using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MystieHook.Games.Wardog;

/// <summary>Cancelable deadline pacing on the dedicated DMA reader thread; no busy wait.</summary>
internal sealed class WardogPacer : IDisposable
{
    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    private readonly TimerWaitHandle _timer;
    private readonly WaitHandle[] _waits;
    private readonly CancellationToken _token;
    private long _deadline;
    private int _rate;

    public WardogPacer(CancellationToken token)
    {
        _token = token;
        // Windows high-resolution waitable timer, supported since Windows 10 1803.
        // https://learn.microsoft.com/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw
        var handle = CreateWaitableTimerExW(0, 0, 0x2, 0x00100002); // SYNCHRONIZE | TIMER_MODIFY_STATE
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateWaitableTimerExW(0, 0, 0, 0x00100002);
        }
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Could not create the Wardog refresh timer");
        }
        _timer = new TimerWaitHandle(handle);
        _waits = [_timer, token.WaitHandle];
    }

    public void WaitNext(long sampleStarted, int targetHz)
    {
        _token.ThrowIfCancellationRequested();
        targetHz = Math.Clamp(targetHz, 1, 120);
        long period = (long)Math.Round((double)Stopwatch.Frequency / targetHz);
        _deadline = _deadline == 0 || _rate != targetHz ? sampleStarted + period : _deadline + period;
        _rate = targetHz;
        long now = Stopwatch.GetTimestamp();
        if (_deadline <= now)
        {
            // A slow read missed the deadline. Do not accumulate catch-up cycles
            // or force another millisecond of sleep on top of an overrun.
            _deadline = now;
            return;
        }
        long due = -Math.Max(1, (long)Math.Ceiling((_deadline - now) * (10000000.0 / Stopwatch.Frequency)));
        if (!SetWaitableTimerEx(_timer.SafeWaitHandle, ref due, 0, 0, 0, 0, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not schedule the Wardog refresh timer");
        WaitHandle.WaitAny(_waits);
        _token.ThrowIfCancellationRequested();
    }

    public void Reset() => _deadline = 0;
    public void Dispose() => _timer.Dispose();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long dueTime, int period,
        nint callback, nint argument, nint wakeContext, uint tolerableDelay);
}
