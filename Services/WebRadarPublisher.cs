using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MystieHook.Games.Wardog;

namespace MystieHook.Services;

public sealed record WebRadarContact(double X, double Y, string Name, string Team = "Unknown")
{
    public string Id { get; init; }
    public double? FacingYaw { get; init; }
}
public sealed record WebRadarPacket(int Map, WebRadarContact Local, WebRadarContact[] Players,
    WebRadarContact[] Vehicles, double AgeMs, double VehicleAgeMs, long SentAt)
{
    public WebRadarTiming Timing { get; init; }
}
public sealed record WebRadarTiming(string Nonce, double ElapsedMs);
public sealed record WebRadarStatus(string Message, string Url = "", bool Active = false);

// Owns network I/O. Reads completed snapshots only, with a single in-flight upload.
public sealed class WebRadarPublisher
{
    private readonly object _sync = new();
    private CancellationTokenSource _cancel;
    private Task _worker = Task.CompletedTask;
    private WebRadarStatus _status = new("Not sharing");
    private string _passwordChange;
    public WebRadarStatus Status => Volatile.Read(ref _status);
    public bool Running { get { lock (_sync) return !_worker.IsCompleted; } }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Session(string Id, string Token, string Url, string TimingNonce);
    private sealed record UploadResult(string Status, string TimingNonce, int? Players, int? Vehicles, long Uploads);
    private static bool ValidNonce(string nonce) => nonce is {Length:32} && nonce.All(Uri.IsHexDigit);
    public static bool ValidSessionId(string id) => id is {Length:7}
        ? id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')
        : id is {Length:32} && id.All(Uri.IsHexDigit);
    public static string UploadMessage(string status) => status switch
    {
        "live" => "Sharing live radar",
        "source_stale" or "source_missing" => "Waiting for fresh desktop game positions.",
        "transport_stale" => "Radar upload exceeded the 1 second web display limit. Check network latency.",
        "timing_invalid" => "Synchronizing radar timing; waiting for the next upload.",
        _ => "Radar server update required. Upload the latest radar server files."
    };

    public static bool ValidPassword(string password) => password != null && (password.Length == 0 || password.Length is >=8 and <=128);
    public static bool ValidEndpoint(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.Port == 3080 && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    public void Start(string endpoint, string password, Func<(WardogReader.Frame Players, WardogReader.VehicleFrame Vehicles, int Map)> source, Func<WardogReader.CameraFrame> cameraSource=null)
    {
        if (!ValidEndpoint(endpoint))
        { Volatile.Write(ref _status, new("The radar server address must be http://127.0.0.1:3080.")); return; }
        if (!ValidPassword(password))
        { Volatile.Write(ref _status, new(PasswordMessage(password))); return; }
        lock (_sync)
        {
            if (!_worker.IsCompleted) return;
            _cancel?.Dispose();_cancel = new();_passwordChange = null;
            var token = _cancel.Token;
            Volatile.Write(ref _status,new("Starting private session…"));
            _worker = Task.Run(()=>Run(new Uri(endpoint), password, source, token, cameraSource));
        }
    }
    public void Stop() { lock (_sync) _cancel?.Cancel(); }
    public void ChangePassword(string password)
    {
        if (!ValidPassword(password)) { Volatile.Write(ref _status,Status with {Message=PasswordMessage(password)});return; }
        if (Status.Active) Interlocked.Exchange(ref _passwordChange,password);
    }
    private static string PasswordMessage(string password) => password is {Length: >128}
        ? "Password is too long. Maximum 128 characters."
        : "Minimum 8 characters. Leave empty for link-only access.";
    public static WebRadarPacket Snapshot(WardogReader.Frame frame, WardogReader.VehicleFrame vehicles, int map, Func<ulong,ulong,string> identity=null, WardogReader.CameraFrame camera=null)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!frame.IsFresh || !frame.LocalPosition.Valid()) return new(Math.Clamp(map,-1,2),null,[],[],250,250,now);
        static WebRadarContact Point(WardogReader.Vec p,string name,string team="Unknown") => new(p.X,p.Y,name.Length>64?name[..64]:name,team);
        double age=Math.Max(0,Stopwatch.GetElapsedTime(frame.SampleStarted).TotalMilliseconds);
        bool vehicleFresh=vehicles.IsFresh && vehicles.World==frame.WorldAddress;
        double vehicleAge=vehicleFresh?Math.Max(0,Stopwatch.GetElapsedTime(vehicles.SampleStarted).TotalMilliseconds):250;
        double? heading=camera is {IsFresh:true} && camera.World==frame.WorldAddress && camera.Controller==frame.LocalController
            && double.IsFinite(camera.Camera.Rotation.Y) ? (camera.Camera.Rotation.Y%360+360)%360 : null;
        return new(Math.Clamp(map,-1,2),Point(frame.LocalPosition,"You") with {FacingYaw=heading},
            frame.WebRadarPlayers.Where(p=>p.Position.Valid()).Take(1024).Select(p=>Point(p.Position,p.Name??"Player",p.TeamRelation.ToString()) with {Id=identity?.Invoke(p.Address,p.SourceState),FacingYaw=p.FacingYaw is >=0 and <360 ? p.FacingYaw : null}).ToArray(),
            vehicleFresh?vehicles.Vehicles.Where(v=>v.Position.Valid()).Take(512).Select(v=>Point(v.Position,v.DisplayLabel,v.TeamRelation.ToString()) with {Id=identity?.Invoke(v.Address,v.ObjectNameId??0)}).ToArray():[],
            age,vehicleAge,now);
    }
    private async Task Run(Uri origin,string password,
        Func<(WardogReader.Frame Players,WardogReader.VehicleFrame Vehicles,int Map)> source,CancellationToken token,Func<WardogReader.CameraFrame> cameraSource)
    {
        using var client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseProxy=false}) {BaseAddress=origin,Timeout=TimeSpan.FromSeconds(3)};
        Session session=null;string failure=null; LocalRadarService localServer=null;
        byte[] identityKey=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        async Task<HttpResponseMessage> Send(HttpMethod method,string route,string bearer,object body,CancellationToken cancel)
        {
            using var request=new HttpRequestMessage(method,route);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
            if(body!=null)request.Content=JsonContent.Create(body,options:Json);
            return await client.SendAsync(request,cancel).ConfigureAwait(false);
        }
        try
        {
            localServer=await LocalRadarService.Start(token);
            using(var response=await Send(HttpMethod.Post,"/api/sessions",localServer.PublisherToken,new {password,timingVersion=1},token))
            {
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException();
                session=await response.Content.ReadFromJsonAsync<Session>(Json,token);
                if(session==null || !ValidSessionId(session.Id) || session.Token.Length!=64
                    || !session.Token.All(Uri.IsHexDigit))throw new InvalidOperationException();
            }
            if(!ValidNonce(session.TimingNonce))
            { failure=UploadMessage(null);return; }
            string timingNonce=session.TimingNonce;
            long timingReceived=Stopwatch.GetTimestamp();
            string url=new Uri(origin,session.Id).AbsoluteUri;
            string route="/api/sessions/"+session.Id;
            Volatile.Write(ref _status,new("Sharing live radar",url,true));
            int failures=0;
            while(!token.IsCancellationRequested)
            {
                string change=Interlocked.Exchange(ref _passwordChange,null);
                if(change!=null)
                {
                    using var response=await Send(HttpMethod.Post,route+"/password",session.Token,new {password=change},token);
                    Volatile.Write(ref _status,new(response.IsSuccessStatusCode?"Password updated; viewers must unlock again.":"Password update failed; previous password remains.",url,true));
                }
                var input=source();
                double elapsed=Stopwatch.GetElapsedTime(timingReceived).TotalMilliseconds;
                string Identity(ulong actor,ulong identity) => Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(identityKey,
                    System.Text.Encoding.ASCII.GetBytes($"{input.Players.WorldAddress:X}:{input.Players.LocalController:X}:{actor:X}:{identity:X}")))[..32].ToLowerInvariant();
                var packet=Snapshot(input.Players,input.Vehicles,input.Map,Identity,cameraSource?.Invoke()) with {Timing=new(timingNonce,elapsed)};
                try
                {
                    using var response=await Send(HttpMethod.Post,route+"/frame",session.Token,packet,token);
                    if(response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized)throw new InvalidOperationException();
                    if(!response.IsSuccessStatusCode)throw new HttpRequestException();
                    var result=await response.Content.ReadFromJsonAsync<UploadResult>(Json,token);
                    if(result==null||!ValidNonce(result.TimingNonce))
                    { failure=UploadMessage(null);return; }
                    timingNonce=result.TimingNonce;timingReceived=Stopwatch.GetTimestamp();
                    string counts=result.Players.HasValue ? $" Server last received: {result.Players} players, {result.Vehicles ?? 0} vehicles (upload {result.Uploads})." : " Server has not received a fresh game snapshot yet.";
                    Volatile.Write(ref _status,new(UploadMessage(result.Status)+counts,url,true));
                    failures=0;
                }
                catch(HttpRequestException)
                {
                    failures++;Volatile.Write(ref _status,new("Connection interrupted; retrying…",url,true));
                    if(failures>=5)throw;
                }
                await Task.Delay(failures>0?1000:100,token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch { failure="Web radar stopped. Check that Node.js 22+ is installed and port 3080 is free; then start again."; }
        finally
        {
            if(session!=null)
            {
                try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(2));using var response=await Send(HttpMethod.Delete,"/api/sessions/"+session.Id,session.Token,null,cleanup.Token);}catch{ }
            }
            localServer?.Dispose();
            Interlocked.Exchange(ref _passwordChange,null);
            Volatile.Write(ref _status,new(failure??"Not sharing"));
        }
    }
}
