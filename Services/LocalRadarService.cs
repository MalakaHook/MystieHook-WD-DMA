using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;

namespace MystieHook.Services;

// Started only by the publisher worker; never blocks UI/DMA callbacks.
internal sealed class LocalRadarService : IDisposable
{
    private readonly Process _process;
    private static LocalRadarService _active;
    public string PublisherToken {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _instance=Guid.NewGuid().ToString("N");
    private LocalRadarService()
    {
        var start=new ProcessStartInfo("node") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            WorkingDirectory=Path.Combine(AppContext.BaseDirectory,"WebRadar"),RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(start.WorkingDirectory,"server.mjs"));
        start.Environment["PUBLISHER_KEY"]=PublisherToken;
        start.Environment["RADAR_INSTANCE"]=_instance;
        start.Environment["MAP_TILE_ROOT"]=LocalMaps.Root;
        _process=Process.Start(start) ?? throw new InvalidOperationException("Local radar could not start.");
        _process.OutputDataReceived+=(_,_)=>{};
        _process.ErrorDataReceived+=(_,_)=>{};
        _process.BeginOutputReadLine();_process.BeginErrorReadLine();
    }
    public static async Task<LocalRadarService> Start(CancellationToken token)
    {
        var service=new LocalRadarService();
        Interlocked.Exchange(ref _active,service)?.Dispose();
        try
        {
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromMilliseconds(400)};
            for(int attempt=0;attempt<30;attempt++)
            {
                token.ThrowIfCancellationRequested();
                if(service._process.HasExited)throw new InvalidOperationException("Local radar exited.");
                try
                {
                    var text=await client.GetStringAsync("http://127.0.0.1:3080/healthz",token);
                    using var result=JsonDocument.Parse(text);
                    if(result.RootElement.TryGetProperty("instance",out var instance)&&instance.GetString()==service._instance)return service;
                }
                catch(HttpRequestException) { }
                catch(TaskCanceledException) when(!token.IsCancellationRequested) { }
                await Task.Delay(100,token);
            }
            throw new TimeoutException("Local radar did not become ready.");
        }
        catch {service.Dispose();throw;}
    }
    public static void StopAll()=>Interlocked.Exchange(ref _active,null)?.Dispose();
    private int _disposed;
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        Interlocked.CompareExchange(ref _active,null,this);
        try {if(!_process.HasExited)_process.Kill(entireProcessTree:true);}catch { }
        _process.Dispose();
    }
}