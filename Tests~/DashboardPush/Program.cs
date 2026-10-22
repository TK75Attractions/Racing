using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

int tests=0;
void Require(bool condition,string message){if(!condition)throw new Exception(message);tests++;}
JsonElement State(Action<JsonObject>? change=null){var data=JsonNode.Parse("""
{"sessionId":"one","state":"Game","settings":{"inputMode":"serial","steeringMode":"Normal"},"serial":{"portOpen":true},"hardware":{"sessionOpen":true,"recent":true,"state":"READY","inputMode":"ALL","ads":"Ok","imus":[{"status":"Ok"},{"status":"Ok"}],"inputFresh":true,"telemetryEnabled":true,"calibrated":true,"angleLocked":true}}
""")!.AsObject();change?.Invoke(data);return JsonSerializer.SerializeToElement(data);}
var healthy=State();var fault=State(s=>s["hardware"]!["imus"]![0]!["status"]="Fault");
var monitor=new HardwareAlertMonitor();Require(monitor.Evaluate(healthy,0)==null,"healthy");
Require(monitor.Evaluate(fault,1)==null&&monitor.Evaluate(fault,6)==null,"debounce short glitches");
Require(monitor.Evaluate(fault,8)?.Contains("IMU 1")==true,"stable IMU incident");
Require(monitor.Evaluate(fault,120)==null,"no repeated alert while fault persists");
monitor.Evaluate(healthy,121);monitor.Evaluate(healthy,132);monitor.Evaluate(fault,133);
Require(monitor.Evaluate(fault,140)?.Contains("IMU 1")==true,"recovered incident can recur");
monitor=new();var mixed=State(s=>{s["settings"]!["inputMode"]="mixed";s["hardware"]!["ads"]="Fault";s["hardware"]!["imus"]![1]!["status"]="Fault";});
monitor.Evaluate(mixed,0);Require(monitor.Evaluate(mixed,120)==null,"unused P2/pedals in wheel-only mode");
monitor=new();var keyboard=State(s=>{s["settings"]!["inputMode"]="keyboard";s["serial"]!["portOpen"]=false;});monitor.Evaluate(keyboard,0);Require(monitor.Evaluate(keyboard,120)==null,"keyboard has no USB requirement");
monitor=new();var automatic=State(s=>{s["settings"]!["bothPlayersAutodrive"]=true;s["serial"]!["portOpen"]=false;});monitor.Evaluate(automatic,0);Require(monitor.Evaluate(automatic,120)==null,"automatic race has no device requirement");
monitor=new();monitor.Evaluate(healthy,0);monitor.Evaluate(null,1);Require(monitor.Evaluate(null,10)?.Contains("Unity")==true,"active race disappearance");
monitor=new();monitor.Evaluate(State(s=>s["state"]="Title"),0);monitor.Evaluate(null,1);Require(monitor.Evaluate(null,100)==null,"stopping idle Unity is not a race incident");
monitor=new();var calibration=State(s=>s["hardware"]!["calibrated"]=false);monitor.Evaluate(calibration,0);Require(monitor.Evaluate(calibration,20)==null,"startup calibration grace");Require(monitor.Evaluate(calibration,31)?.Contains("校正")==true,"unfinished calibration");

string folder=Path.Combine(Path.GetTempPath(),"racing-push-test-"+Guid.NewGuid().ToString("N"));
using var receiver=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);var receiverParams=receiver.ExportParameters(false);
byte[] receiverPublic=[4,..receiverParams.Q.X!,..receiverParams.Q.Y!];byte[] auth=RandomNumberGenerator.GetBytes(16);
var keys=new Dictionary<string,string>{{"p256dh",DashboardPush.Base64(receiverPublic)},{"auth",DashboardPush.Base64(auth)}};
const string endpoint="https://web.push.apple.com/QAtest";
try
{
 var handler=new Capture();using var http=new HttpClient(handler);string publicKey;
 using(var push=new DashboardPush(folder,"https://race.demo.ts.net",http))
 {
  Require(push.Ready,"store ready");publicKey=push.PublicKey;
  foreach(string bad in new[]{"http://127.0.0.1/private","https://localhost/","https://web.push.apple.com.evil.test/path","https://web.push.apple.com:8765/private","https://web.push.apple.com@evil.test/a"})
   Require(!push.Subscribe("alice",bad,keys),"reject arbitrary outbound URL");
  Require(!push.Subscribe("alice",endpoint,new(){{"p256dh","bad"},{"auth","bad"}}),"reject invalid browser keys");
  Require(push.Subscribe("alice",endpoint,keys)&&push.Count==1,"valid browser subscription");
  Require(!push.Subscribe("bob",endpoint,keys)&&!push.Remove("bob",endpoint),"per-user ownership");
  string status=JsonSerializer.Serialize(push.Status("alice",true));Require(!status.Contains("privateKey")&&!status.Contains("QAtest"),"no secrets in status");
  await push.Send("センサー異常の検証",CancellationToken.None);
  Require(handler.Encoding=="aes128gcm"&&handler.Authorization.StartsWith("vapid ",StringComparison.OrdinalIgnoreCase),"Safari-compatible encryption and VAPID");
  byte[] wire=handler.Payload;byte[] salt=wire[..16];int size=BinaryPrimitives.ReadInt32BigEndian(wire.AsSpan(16,4));int length=wire[20];byte[] serverPublic=wire[21..(21+length)];
  using var server=ECDiffieHellman.Create(new ECParameters{Curve=ECCurve.NamedCurves.nistP256,Q=new ECPoint{X=serverPublic[1..33],Y=serverPublic[33..65]}});
  byte[] shared=receiver.DeriveRawSecretAgreement(server.PublicKey);
  byte[] info=[..Encoding.ASCII.GetBytes("WebPush: info\0"),..receiverPublic,..serverPublic];
  byte[] ikm=HKDF.DeriveKey(HashAlgorithmName.SHA256,shared,32,auth,info);
  byte[] cek=HKDF.DeriveKey(HashAlgorithmName.SHA256,ikm,16,salt,Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
  byte[] nonce=HKDF.DeriveKey(HashAlgorithmName.SHA256,ikm,12,salt,Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
  byte[] encrypted=wire[(21+length)..];byte[] plain=new byte[encrypted.Length-16];using(var aes=new AesGcm(cek,16))aes.Decrypt(nonce,encrypted.AsSpan(0,plain.Length),encrypted.AsSpan(plain.Length),plain);
  int end=plain.Length-1;while(end>=0&&plain[end]==0)end--;Require(end>=0&&plain[end]==2,"final encrypted record");
  using var decoded=JsonDocument.Parse(plain.AsMemory(0,end));Require(decoded.RootElement.GetProperty("body").GetString()=="センサー異常の検証","independent recipient decrypts the real generated request");
  string token=handler.Authorization.Split("t=")[1].Split(',')[0];var jwt=token.Split('.');byte[] pub=DashboardPush.Unbase64(publicKey);
  using var signing=ECDsa.Create(new ECParameters{Curve=ECCurve.NamedCurves.nistP256,Q=new ECPoint{X=pub[1..33],Y=pub[33..65]}});
  Require(signing.VerifyData(Encoding.ASCII.GetBytes(jwt[0]+"."+jwt[1]),DashboardPush.Unbase64(jwt[2]),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation),"VAPID signature verifies independently");
  using var jwtData=JsonDocument.Parse(DashboardPush.Unbase64(jwt[1]));Require(jwtData.RootElement.GetProperty("aud").GetString()=="https://web.push.apple.com","VAPID audience is provider origin");
  Require(OperatingSystem.IsWindows()||(File.GetUnixFileMode(Path.Combine(folder,"subscriptions.json"))&(UnixFileMode.GroupRead|UnixFileMode.OtherRead))==0,"private storage permissions");
 }
 using(var push=new DashboardPush(folder,"https://race.demo.ts.net",new HttpClient(handler)))
 {
  Require(push.PublicKey==publicKey&&push.Count==1,"restart retains keys and subscriptions");handler.Code=HttpStatusCode.Gone;
  await push.Send("expired",CancellationToken.None);Require(push.Count==0,"410 automatically prunes stale subscription");
 }
 Console.WriteLine($"PUSH_TESTS_PASS: {tests} checks; real aes128gcm delivery request decrypted, VAPID signature verified, persistence, ownership, endpoint validation, incident debounce.");
}
finally{Directory.Delete(folder,true);}
internal sealed class Capture:HttpMessageHandler
{
 public string Encoding="",Authorization="";public byte[] Payload=[];public HttpStatusCode Code=HttpStatusCode.Created;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken stop)
 {Encoding=string.Join(",",request.Content!.Headers.ContentEncoding);Authorization=request.Headers.Authorization?.ToString()??"";Payload=await request.Content.ReadAsByteArrayAsync(stop);return new HttpResponseMessage(Code){RequestMessage=request};}
}
