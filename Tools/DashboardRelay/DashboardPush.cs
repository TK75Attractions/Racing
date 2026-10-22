using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

/// <summary>Per-device subscriptions and VAPID keys stay on the game PC, outside the repository.</summary>
internal sealed class DashboardPush : IDisposable
{
    internal sealed record Subscriber(string Owner,string Endpoint,Dictionary<string,string> Keys);
    private sealed record State(string PublicKey,string PrivateKey,List<Subscriber> Subscribers);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly object sync=new();
    private readonly string folder;
    private readonly string? origin;
    private State? state;
    private readonly HttpClient transport;
    private PushServiceClient? sender;
    private readonly Dictionary<string,string> delivery=new();
    public bool Ready=>state!=null&&origin!=null;
    public string PublicKey=>state?.PublicKey??"";
    public int Count { get {lock(sync)return state?.Subscribers.Count??0;} }
    public DashboardPush(string folder,string? origin,HttpClient? transport=null)
    {
        this.folder=folder;this.origin=origin;
        this.transport=transport??new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,MaxConnectionsPerServer=4}){Timeout=TimeSpan.FromSeconds(8)};
        if(origin==null||!Uri.TryCreate(origin,UriKind.Absolute,out var site)||site.Scheme!="https")return;
        try
        {
            Directory.CreateDirectory(folder);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            string path=Path.Combine(folder,"subscriptions.json");
            if(File.Exists(path))
            {
                if(new FileInfo(path).Length>262144)throw new IOException("Push state too large");
                state=JsonSerializer.Deserialize<State>(File.ReadAllText(path),Json)??throw new IOException("Invalid push state");
                if(state.Subscribers==null||state.PublicKey==null||state.PrivateKey==null||state.Subscribers.Count>32||!ValidKeys(state.PublicKey,state.PrivateKey))throw new IOException("Invalid push state");
                state.Subscribers.RemoveAll(s=>s==null||string.IsNullOrEmpty(s.Owner)||s.Endpoint==null||s.Keys==null||!ValidSubscription(s.Endpoint,s.Keys));
            }
            else
            {
                using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var p=key.ExportParameters(true);
                state=new State(Base64(new byte[]{4}.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray()),Base64(p.D!),new());Save();
            }
            sender=new PushServiceClient(this.transport){DefaultAuthentication=new VapidAuthentication(state.PublicKey,state.PrivateKey){Subject=origin},
                DefaultAuthenticationScheme=VapidAuthenticationScheme.Vapid,DefaultTimeToLive=120,AutoRetryAfter=false};
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or CryptographicException or ArgumentException)
        {state=null;Console.WriteLine("Push notification storage is unavailable ("+error.GetType().Name+").");}
    }
    public object Status(string owner,bool canControl)=>new{enabled=Ready&&canControl,publicKey=Ready&&canControl?PublicKey:"",
        subscriptions=OwnCount(owner),lastDelivery=LastDelivery(owner)};
    private int OwnCount(string owner){lock(sync)return state?.Subscribers.Count(x=>x.Owner==owner)??0;}
    private string LastDelivery(string owner){lock(sync)return delivery.GetValueOrDefault(owner,"");}
    public bool Subscribe(string owner,string endpoint,Dictionary<string,string> keys)
    {
        if(!Ready||!ValidSubscription(endpoint,keys)||owner.Length>256)return false;
        lock(sync)
        {
            var previous=state!.Subscribers.Find(x=>x.Endpoint==endpoint);
            if(previous!=null&&previous.Owner!=owner)return false;
            if(previous==null&&(state.Subscribers.Count>=32||OwnCount(owner)>=4))return false;
            if(previous!=null)state.Subscribers.Remove(previous);
            state.Subscribers.Add(new Subscriber(owner,endpoint,new(keys)));Save();return true;
        }
    }
    public bool Remove(string owner,string endpoint)
    {
        if(!Ready)return false;
        lock(sync){var previous=state!.Subscribers.Find(x=>x.Endpoint==endpoint);if(previous!=null&&previous.Owner!=owner)return false;
            state.Subscribers.RemoveAll(x=>x.Endpoint==endpoint&&x.Owner==owner);Save();return true;}
    }
    public async Task Send(string body,CancellationToken cancellation,string? onlyOwner=null)
    {
        Subscriber[] subscribers;lock(sync)subscribers=state?.Subscribers.Where(x=>onlyOwner==null||x.Owner==onlyOwner).ToArray()??[];
        await Task.WhenAll(subscribers.Select(async subscriber=>
        {
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var message=new PushMessage(JsonSerializer.Serialize(new{title="Racing 機器・接続の異常",body,tag="racing-hardware",url="/",time=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},Json)){TimeToLive=120,Urgency=PushMessageUrgency.High};
                await sender!.RequestPushMessageDeliveryAsync(new PushSubscription{Endpoint=subscriber.Endpoint,Keys=subscriber.Keys},message,timeout.Token);
                lock(sync)delivery[subscriber.Owner]="sent";
            }
            catch(PushServiceClientException error) when(error.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {Remove(subscriber.Owner,subscriber.Endpoint);lock(sync)delivery[subscriber.Owner]="expired";}
            catch(Exception error) when(error is HttpRequestException or OperationCanceledException or PushServiceClientException or ArgumentException or CryptographicException)
            {lock(sync)delivery[subscriber.Owner]="failed";}
        }));
    }
    public async Task Monitor(HttpClient unity,CancellationToken stop,Func<string,bool> authorized)
    {
        var monitor=new HardwareAlertMonitor();var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!stop.IsCancellationRequested)
        {
            try
            {
                // Revoked operators must not keep receiving previously registered subscriptions.
                lock(sync)if(state!=null&&state.Subscribers.RemoveAll(x=>!authorized(x.Owner))>0)Save();
                if(Count>0)
                {
                    JsonElement? snapshot=null;
                    try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop);timeout.CancelAfter(3_000);
                        using var response=await unity.GetAsync("/api/status",timeout.Token);response.EnsureSuccessStatusCode();
                        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));snapshot=doc.RootElement.Clone();}
                    catch(Exception e) when(e is HttpRequestException or OperationCanceledException or JsonException){}
                    string? incident=monitor.Evaluate(snapshot,clock.Elapsed.TotalSeconds);
                    if(incident!=null)await Send(incident,stop);
                }
                await Task.Delay(2000,stop);
            }
            catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){Console.WriteLine("Push monitor could not update local storage.");await Task.Delay(2000,stop);}
        }
    }
    public static bool ValidSubscription(string endpoint,IDictionary<string,string> keys)
    {
        if(endpoint.Length>2048||!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Fragment.Length>0)return false;
        string host=uri.IdnHost;
        if(!(host=="web.push.apple.com"||host.EndsWith(".push.apple.com",StringComparison.OrdinalIgnoreCase)||host=="fcm.googleapis.com"||host=="updates.push.services.mozilla.com"||host.EndsWith(".notify.windows.com",StringComparison.OrdinalIgnoreCase)))return false;
        try
        {
            if(!keys.TryGetValue("p256dh",out string? p)||!keys.TryGetValue("auth",out string? a))return false;
            byte[] pub=Unbase64(p);if(pub.Length!=65||pub[0]!=4||Unbase64(a).Length!=16)return false;
            using var check=ECDiffieHellman.Create(new ECParameters{Curve=ECCurve.NamedCurves.nistP256,Q=new ECPoint{X=pub[1..33],Y=pub[33..65]}});return true;
        }catch(Exception e)when(e is FormatException or CryptographicException or ArgumentException){return false;}
    }
    private static bool ValidKeys(string pub,string secret)
    {try{byte[] p=Unbase64(pub),d=Unbase64(secret);if(p.Length!=65||p[0]!=4||d.Length!=32)return false;using var key=ECDsa.Create(new ECParameters{Curve=ECCurve.NamedCurves.nistP256,D=d,Q=new ECPoint{X=p[1..33],Y=p[33..65]}});return true;}catch(Exception e)when(e is FormatException or CryptographicException or ArgumentException){return false;}}
    internal static string Base64(byte[] data)=>Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
    internal static byte[] Unbase64(string data)=>Convert.FromBase64String(data.Replace('-','+').Replace('_','/').PadRight((data.Length+3)/4*4,'='));
    private void Save()
    {
        string file=Path.Combine(folder,"subscriptions.json"),temp=file+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.None))
        {
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            JsonSerializer.Serialize(stream,state,Json);stream.Flush(true);
        }
        File.Move(temp,file,true);
    }
    public void Dispose(){sender?.DefaultAuthentication?.Dispose();transport.Dispose();}
}
