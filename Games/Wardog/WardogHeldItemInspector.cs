using VmmSharpEx;

namespace MystieHook.Games.Wardog;

internal sealed class WardogHeldItemInspector
{
    private readonly object _sync=new();
    private Task _worker=Task.CompletedTask;
    private CancellationTokenSource _cancel;
    private bool _requested;
    private string _report="",_label="",_status="Hold a weapon, enter its in-game name, then capture.";
    public string Report=>Volatile.Read(ref _report);
    public string Status=>Volatile.Read(ref _status);
    public void Request(string label)
    {
        lock(_sync)
        {
            if(!_worker.IsCompleted || _requested)return;
            _label=label;_requested=true;
            Volatile.Write(ref _report,"");Volatile.Write(ref _status,"Capture requested...");
        }
    }
    public void Clear()
    {
        lock(_sync)
        {
            _requested=false;_cancel?.Cancel();
            Volatile.Write(ref _report,"");Volatile.Write(ref _status,"Capture cleared.");
        }
    }
    public void EnsureStarted(Vmm connection,uint pid,ulong moduleBase,WardogDmaGate gate,
        Func<WardogReader.Frame> source,CancellationToken attachmentToken)
    {
        lock(_sync)
        {
            if(!_requested || !_worker.IsCompleted || attachmentToken.IsCancellationRequested)return;
            _requested=false;string label=_label;
            _cancel?.Dispose();_cancel=CancellationTokenSource.CreateLinkedTokenSource(attachmentToken);
            _cancel.CancelAfter(TimeSpan.FromSeconds(2));var token=_cancel.Token;
            _worker=Task.Run(()=>
            {
                bool Attached()=>DmaMemory.IsAttached && DmaMemory.Pid==pid && DmaMemory.Base==moduleBase
                    && ReferenceEquals(DmaMemory.Vmm,connection);
                try
                {
                    using var dma=new WardogDmaReader(connection,pid,token,gate,WardogDmaPriority.Vehicle);
                    int calls=0,bytes=0;
                    bool Read(ulong address,Span<byte> output)
                    {
                        token.ThrowIfCancellationRequested();
                        if(++calls>256 || output.Length>4096 || (bytes+=output.Length)>65536)
                            throw new InvalidOperationException();
                        return Attached() && dma.Read(address,output);
                    }
                    var reader=new WardogReader(moduleBase,Read);
                    var sample=reader.InspectHeldItem(source());
                    lock(_sync)
                    {
                        if(token.IsCancellationRequested)return;
                        bool valid=Attached() && WardogReader.HeldItemInspectionMatches(sample,source());
                        Volatile.Write(ref _report,valid?$"User label: {label}\nCaptured UTC: {DateTime.UtcNow:O}\n{sample.Report}":"");
                        Volatile.Write(ref _status,valid?"Capture complete (saved snapshot). Copy before capturing again.":"Capture rejected: unreadable data or changed/stale local context. Retry in-game.");
                    }
                }
                catch
                {
                    lock(_sync)
                    {
                        if(token.IsCancellationRequested && _status=="Capture cleared.")return;
                        Volatile.Write(ref _report,"");Volatile.Write(ref _status,"Capture cancelled or read budget/read failure. Retry while holding the weapon.");
                    }
                }
            });
        }
    }
    public void StopAndWait(){Clear();_worker.GetAwaiter().GetResult();Clear();}
}
