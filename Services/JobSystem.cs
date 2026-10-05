using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace MystieHook.Services
{
    public static class JobSystem
    {
        private static readonly Channel<Func<CancellationToken, Task>> _ch =
            Channel.CreateUnbounded<Func<CancellationToken, Task>>(
                new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        private static CancellationTokenSource? _cts;
        private static Task[]? _workers;

        public static void Start(int workers = 3)
        {
            _cts = new CancellationTokenSource();
            _workers = Enumerable.Range(0, workers)
                .Select(_ => Task.Run(() => Loop(_cts.Token)))
                .ToArray();
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { Task.WaitAll(_workers ?? Array.Empty<Task>(), 1500); } catch { }
            _cts?.Dispose();
            _cts = null; _workers = null;
        }

        public static ValueTask Enqueue(Func<CancellationToken, Task> job)
            => _ch.Writer.WriteAsync(job);

        // Convenience overloads
        public static void Schedule(Func<CancellationToken, Task> job)
            => _ = Enqueue(job);

        public static void Schedule(Func<Task> job)
            => _ = Enqueue(async _ => await job().ConfigureAwait(false));

        public static void Schedule(Action<CancellationToken> job)
            => _ = Enqueue(ct => { job(ct); return Task.CompletedTask; });

        public static void Schedule(Action job)
            => _ = Enqueue(_ => { job(); return Task.CompletedTask; });

        private static async Task Loop(CancellationToken ct)
        {
            while (await _ch.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_ch.Reader.TryRead(out var job))
                {
                    try { await job(ct).ConfigureAwait(false); }
                    catch { /* log if needed */ }
                }
            }
        }
    }
}
