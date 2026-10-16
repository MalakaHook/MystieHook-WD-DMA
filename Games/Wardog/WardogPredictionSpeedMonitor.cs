using System.Diagnostics;
using VmmSharpEx;
namespace MystieHook.Games.Wardog;

internal sealed class WardogPredictionSpeedMonitor
{
    private sealed class TransferBudgetException:Exception;
    private readonly object _sync=new();private Task _worker=Task.CompletedTask;private CancellationTokenSource _cancel;
    private volatile bool _enabled,_fault;
    private int _refreshVersion;
    private WardogReader.PredictionSpeedSample _sample=WardogReader.PredictionSpeedSample.Empty();
    public WardogReader.PredictionSpeedSample Current=>Volatile.Read(ref _sample);
    public void Refresh()
    {
        lock(_sync){Interlocked.Increment(ref _refreshVersion);
            Volatile.Write(ref _sample,WardogReader.PredictionSpeedSample.Empty("Refreshing weapon bullet-speed cache"));}
    }
    public bool Enabled
    {
        get=>_enabled;
        set{lock(_sync){if(value==_enabled)return;if(value)_fault=false;_enabled=value;
            if(!value){_cancel?.Cancel();Volatile.Write(ref _sample,WardogReader.PredictionSpeedSample.Empty("Bullet-speed sampling disabled"));}}}
    }
    public void EnsureStarted(Vmm connection,uint pid,ulong moduleBase,WardogDmaGate gate,Func<WardogReader.Frame> source,CancellationToken attachment)
    {
        lock(_sync)
        {
            if(!Enabled||_fault||!_worker.IsCompleted||attachment.IsCancellationRequested)return;
            _cancel?.Dispose();_cancel=CancellationTokenSource.CreateLinkedTokenSource(attachment);var token=_cancel.Token;
            _worker=Task.Run(()=>
            {
                bool Attached()=>DmaMemory.IsAttached&&DmaMemory.Pid==pid&&DmaMemory.Base==moduleBase&&ReferenceEquals(DmaMemory.Vmm,connection);
                try
                {
                    using var dma=new WardogDmaReader(connection,pid,token,gate,WardogDmaPriority.Vehicle,WardogReadStream.PredictionSpeed);
                    int calls=0,bytes=0;
                    void Budget(int count,int length)
                    {if((calls+=count)>256||(bytes+=length)>65536)throw new TransferBudgetException();}
                    bool Read(ulong address,Span<byte> output)
                    {
                        token.ThrowIfCancellationRequested();if(output.Length>4096)throw new TransferBudgetException();
                        Budget(1,output.Length);return Attached()&&dma.Read(address,output);
                    }
                    void Verify(IReadOnlyCollection<WardogReadBatch.Request> requests)
                    {
                        token.ThrowIfCancellationRequested();
                        if(requests.Any(r=>r.Bytes.Length>4096))throw new TransferBudgetException();
                        Budget(requests.Count,requests.Sum(r=>r.Bytes.Length));if(Attached())dma.Scatter(requests);
                    }
                    // Retain addresses only. BeginFrame clears all previous success
                    // flags and freshly scatters the bounded known ranges; changed
                    // links use direct reads and unused old ranges leave at EndFrame.
                    var batch=new WardogReadBatch(Read,Verify,()=>dma.LastReadStarted);
                    var reader=new WardogReader(moduleBase,batch.Read,verifyScatter:Verify,readSampleStarted:batch.SampleStarted);
                    var cache=new WardogReader.PredictionSpeedCache();int cacheVersion=Volatile.Read(ref _refreshVersion);
                    WardogReadOptimization.StaggerBackground(25,token);
                    while(Enabled&&!token.IsCancellationRequested&&Attached())
                    {
                        long cycleStarted=Stopwatch.GetTimestamp();calls=bytes=0;
                        int version=Volatile.Read(ref _refreshVersion);
                        if(version!=cacheVersion){cache.Clear();cacheVersion=version;}
                        var context=source();var sample=WardogReader.PredictionSpeedSample.Empty("Player context unavailable or expired for bullet-speed sampling");
                        if(context.IsFresh&&Stopwatch.GetElapsedTime(context.SampleStarted).TotalMilliseconds>=0)
                        {
                            try
                            {
                                // The timestamp precedes prefetch: batching never
                                // renews the age of bytes read earlier in this cycle.
                                batch.BeginFrame();
                                sample=reader.CapturePredictionSpeed(context,captureStarted:cycleStarted,cache:cache);
                                if(sample.CachedForWeapon)
                                {
                                    batch.ForgetRange(sample.InitialAddress,4);
                                    batch.ForgetRange(sample.ModifierAddress,4);
                                }
                            }
                            catch(TransferBudgetException){sample=WardogReader.PredictionSpeedSample.Empty("Bullet-speed transfer budget reached; sample discarded");}
                            finally{batch.EndFrame();}
                        }
                        // A delayed/unreadable background identity check need not
                        // discard a stored numerical value. Player scans still
                        // freshly prove all held-weapon guards before every use.
                        if(!sample.HasUsableSpeed&&cache.Contains(Current)&&Current.Matches(source()))sample=Current;
                        if(!Attached()||!sample.Matches(source()))sample=WardogReader.PredictionSpeedSample.Empty(sample.Status);
                        lock(_sync)if(Enabled&&!token.IsCancellationRequested&&version==Volatile.Read(ref _refreshVersion))Volatile.Write(ref _sample,sample);
                        int wait=Math.Max(0,(int)Math.Ceiling(50-Stopwatch.GetElapsedTime(cycleStarted).TotalMilliseconds));
                        if(wait>0)token.WaitHandle.WaitOne(wait);
                    }
                }
                catch(OperationCanceledException){}
                catch{_fault=true;Volatile.Write(ref _sample,WardogReader.PredictionSpeedSample.Empty("Bullet-speed reader stopped; toggle prediction off/on to retry"));}
                finally{if(!Enabled||attachment.IsCancellationRequested||!Attached())Volatile.Write(ref _sample,WardogReader.PredictionSpeedSample.Empty());}
            });
        }
    }
    public void StopAndWait(){Enabled=false;_worker.GetAwaiter().GetResult();Volatile.Write(ref _sample,WardogReader.PredictionSpeedSample.Empty());}
}
