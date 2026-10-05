using MystieHook.Services;

public static class Poller
{
    private static CancellationTokenSource? _cts;

    public static void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var ct = _cts.Token;
            while (!ct.IsCancellationRequested)
            {
                if (MystieHook.DmaMemory.IsAttached)
                    VmmService.RefreshModules(); // enqueues

                await Task.Delay(2000, ct);
            }
        });
    }

    public static void Stop() { try { _cts?.Cancel(); } catch { } _cts?.Dispose(); _cts = null; }
}
