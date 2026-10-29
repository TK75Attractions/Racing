using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

static JsonNode Node(object value) => JsonSerializer.SerializeToNode(value)!;
static void Check(bool value,string description) { if(!value)throw new Exception(description);Console.WriteLine("PASS "+description); }
var root=Path.Combine(Path.GetTempPath(),"operator host "+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,".git"));Directory.CreateDirectory(Path.Combine(root,"Tools","OperatorUpdate"));
string script=Path.Combine(root,"Tools","OperatorUpdate","update-and-run.sh");
await File.WriteAllTextAsync(script,"#!/bin/bash\nmkdir -p \"$4\"\nprintf '%s' \"$3\" > \"$4/parent.txt\"\necho offline-built > \"$4/status.txt\"\nsleep .2\ntouch \"$4/succeeded\"\n");
Environment.SetEnvironmentVariable("RACING_PROJECT_PATH",root);
var handler=new GameHandler();
using var client=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:8765")};
try
{
    using var host=new OperatorUpdateHost(client);
    var status=Node(await host.Status(true));Check(status["available"]!.GetValue<bool>(),"Unity closed: updater remains available");
    Check(!Node(await host.Status(false))["available"]!.GetValue<bool>(),"Viewer cannot enable update");
    var result=Node(await host.Start("offline-1"));Check(result["ok"]!.GetValue<bool>(),"Starts without Unity");
    string folder=result["logFolder"]!.GetValue<string>();
    for(int i=0;i<100&&!File.Exists(Path.Combine(folder,"succeeded"));i++)await Task.Delay(20);
    Check(File.ReadAllText(Path.Combine(folder,"parent.txt"))=="0","Offline runner never waits for process 0");
    Check(Node(await host.Start("offline-1")).ToJsonString()==result.ToJsonString(),"Duplicate ID returns the original result");
    Check(Directory.GetDirectories(Path.Combine(root,"Logs","OperatorUpdate")).Length==1,"Retry does not spawn another updater");
    status=Node(await host.Status(true));Check(status["message"]!.GetValue<string>()=="offline-built","Progress survives worker completion");
    handler.Online=true;
    status=Node(await host.Status(true));Check(status["available"]!.GetValue<bool>(),"Reconnect uses current game availability");
    result=Node(await host.Start("online-1"));Check(result["ok"]!.GetValue<bool>(),"Online request accepted");
    Check(handler.Posts==1&&handler.Last?["settingsRevision"]?.GetValue<int>()==9&&handler.Last?["targetSession"]?.GetValue<string>()=="fresh-session","Forwarded update uses a fresh game anchor");
    await host.Start("online-1");Check(handler.Posts==1,"Online retry is idempotent");
    handler.InRace=true;
    result=Node(await host.Start("during-race"));Check(result["ok"]!.GetValue<bool>()&&handler.Pauses==1,"Race update enters maintenance before requesting restart");
    handler.InRace=false;
    handler.Reject=true;
    result=Node(await host.Start("rejected"));Check(result["message"]!.GetValue<string>()=="タイトル画面で実行してください。","Failure shows the updater's exact reason");
    var lockPath=Path.Combine(root,"Logs","OperatorUpdate.lock");Directory.CreateDirectory(lockPath);File.WriteAllText(Path.Combine(lockPath,"request.txt"),folder);
    status=Node(await host.Status(true));Check(status["busy"]!.GetValue<bool>()&&!status["available"]!.GetValue<bool>(),"Standalone/Unity update lock also disables the relay button");
}
finally { Directory.Delete(root,true); }
Console.WriteLine("OPERATOR_HOST_TESTS_PASS");
sealed class GameHandler : HttpMessageHandler
{
    public bool Online,Reject,InRace;public int Posts,Pauses;public JsonNode? Last;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(!Online)throw new HttpRequestException("Unity is closed");
        if(request.Method==HttpMethod.Get&&InRace)return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"sessionId\":\"fresh-session\",\"state\":\"Game\",\"stateRevision\":2,\"serverTime\":1000,\"settingsRevision\":9,\"settings\":{\"updateAvailable\":false,\"maintenanceAvailable\":true,\"maintenanceActive\":false}}")};
        if(request.Method==HttpMethod.Get)return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""
        {"sessionId":"fresh-session","state":"Title","stateRevision":2,"serverTime":1000,"settingsRevision":9,"settings":{"updateAvailable":true,"updateBusy":false,"updateStatus":"Ready"}}
        """)};
        Posts++;Last=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token));
        if(Last?["command"]?.GetValue<string>()=="maintenance"){Pauses++;InRace=false;}
        if(request.Headers.GetValues("Origin").Single()!="http://127.0.0.1:8765")throw new Exception("Wrong Unity origin");
        return new HttpResponseMessage(Reject?HttpStatusCode.Conflict:HttpStatusCode.OK){Content=new StringContent(Reject?
            "{\"ok\":false,\"settings\":{\"updateStatus\":\"タイトル画面で実行してください。\"}}":"{\"ok\":true,\"settings\":{\"updateStatus\":\"Checking\"}}")};
    }
}
