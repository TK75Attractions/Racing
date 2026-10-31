using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

static JsonNode Node(object value) => JsonSerializer.SerializeToNode(value)!;
static void Check(bool value,string description) { if(!value)throw new Exception(description);Console.WriteLine("PASS "+description); }
static async Task Until(Func<bool> done)
{
    for(int i=0;i<250;i++) { if(done()) return; await Task.Delay(20); }
    throw new Exception("Timed out waiting for owned fake worker");
}
var root=Path.Combine(Path.GetTempPath(),"operator host "+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,".git"));Directory.CreateDirectory(Path.Combine(root,"Tools","OperatorUpdate"));
string script=Path.Combine(root,"Tools","OperatorUpdate","update-and-run.sh");
await File.WriteAllTextAsync(script,"""
#!/bin/bash
mkdir -p "$4" "$1/Logs/OperatorUpdate.lock"
printf '%s' "$4" > "$1/Logs/OperatorUpdate.lock/request.txt"
printf '%s' "$3" > "$4/parent.txt"
ps -p $$ -o pgid= > "$4/group.txt"
trap 'rm -rf "$1/Logs/OperatorUpdate.lock"' EXIT
printf '2' > "$4/control-version"
echo building > "$4/phase.txt"
echo fake-building > "$4/status.txt"
if [ "$RACING_FAKE_WAIT" = 1 ]; then
  while [ ! -f "$4/finish-build" ] && [ ! -f "$4/decision" ]; do sleep .05; done
fi
if [ -f "$4/decision" ] && [ "$(cat "$4/decision")" = cancel ]; then
  echo cancelled > "$4/phase.txt"; echo fake-cancelled > "$4/status.txt"; touch "$4/cancelled"; exit 130
fi
(set -o noclobber; echo launch > "$4/decision") || exit 9
echo switching > "$4/phase.txt"
touch "$4/ready"
while [ ! -f "$4/released" ]; do
  if [ -f "$4/handoff.failed" ]; then cat "$4/handoff.failed" > "$4/status.txt"; echo failed > "$4/phase.txt";touch "$4/failed";exit 1;fi
  sleep .05
done
echo completed > "$4/phase.txt"
echo offline-built > "$4/status.txt"
touch "$4/succeeded"
""");
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
    await Until(()=>File.Exists(Path.Combine(folder,"succeeded")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    Check(File.ReadAllText(Path.Combine(folder,"parent.txt"))=="0","Relay never waits for or delegates ownership to the running game");
    if(OperatingSystem.IsMacOS())Check(int.Parse(File.ReadAllText(Path.Combine(folder,"group.txt")).Trim())!=NativeProcess.getpgrp(),
        "macOS update worker runs outside the relay's launchd process group");
    Check(Node(await host.Start("offline-1")).ToJsonString()==result.ToJsonString(),"Duplicate ID returns the original result");
    Check(Directory.GetDirectories(Path.Combine(root,"Logs","OperatorUpdate")).Length==1,"Retry does not spawn another updater");
    status=Node(await host.Status(true));Check(status["message"]!.GetValue<string>()=="offline-built","Progress survives worker completion");

    handler.Online=true;handler.InRace=true;
    Environment.SetEnvironmentVariable("RACING_FAKE_WAIT","1");
    result=Node(await host.Start("online-build"));Environment.SetEnvironmentVariable("RACING_FAKE_WAIT",null);
    folder=result["logFolder"]!.GetValue<string>();await Until(()=>File.Exists(Path.Combine(folder,"control-version")));
    status=Node(await host.Status(true));
    Check(status["busy"]!.GetValue<bool>()&&status["canCancel"]!.GetValue<bool>(),"Running build reports progress and cancellation independently of Unity");
    Check(handler.Posts==0,"The existing Race is neither paused nor closed during the build");
    var stale=Node(await host.Cancel("different-request"));Check(!stale["ok"]!.GetValue<bool>(),"A stale cancel cannot affect a different build");
    await File.WriteAllTextAsync(Path.Combine(folder,"finish-build"),"");
    await Until(()=>File.Exists(Path.Combine(folder,"succeeded")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    Check(handler.Posts==1&&handler.Last?["command"]?.GetValue<string>()=="releaseForUpdate"&&handler.Last?["settingsRevision"]?.GetValue<int>()==9,
        "Only the verified completed build releases the current game using a fresh anchor");
    Check(handler.Last?["targetSession"]?.GetValue<string>()=="fresh-session","Handoff keeps game-session guards");
    await host.Start("online-build");Check(handler.Posts==1,"Online retry is idempotent");
    Check(!Node(await host.Cancel(Path.GetFileName(folder)))["ok"]!.GetValue<bool>(),"A committed launch cannot be cancelled");

    Environment.SetEnvironmentVariable("RACING_FAKE_WAIT","1");
    result=Node(await host.Start("cancel-build"));Environment.SetEnvironmentVariable("RACING_FAKE_WAIT",null);
    folder=result["logFolder"]!.GetValue<string>();await Until(()=>File.Exists(Path.Combine(folder,"control-version")));
    Check(Node(await host.Cancel(Path.GetFileName(folder)))["ok"]!.GetValue<bool>(),"Cancellation is accepted for the exact active request");
    await Until(()=>File.Exists(Path.Combine(folder,"cancelled")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    status=Node(await host.Status(true));
    Check(status["phase"]!.GetValue<string>()=="cancelled"&&!status["busy"]!.GetValue<bool>(),"Cancelled jobs release busy state and retain their real result");
    Check(handler.Posts==1&&!File.Exists(Path.Combine(folder,"ready")),"Cancellation never triggers game shutdown or launch handoff");
    Check(Node(await host.Cancel(Path.GetFileName(folder)))["ok"]!.GetValue<bool>(),"Repeated cancellation is idempotent");

    handler.Online=true;handler.Editor=true;handler.Reject=true;
    result=Node(await host.Start("editor-reject"));folder=result["logFolder"]!.GetValue<string>();
    await Until(()=>File.Exists(Path.Combine(folder,"failed")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    status=Node(await host.Status(true));Check(status["message"]!.GetValue<string>().Contains("Editor"),"A failed Editor release reports its cause and never terminates the Editor");
    handler.Online=false;handler.Reject=false;handler.Editor=false;

    Environment.SetEnvironmentVariable("RACING_FAKE_WAIT","1");
    var firstHost=new OperatorUpdateHost(client);
    result=Node(await firstHost.Start("restart-relay"));folder=result["logFolder"]!.GetValue<string>();
    Environment.SetEnvironmentVariable("RACING_FAKE_WAIT",null);
    await Until(()=>File.Exists(Path.Combine(folder,"control-version")));firstHost.Dispose();
    using(var restarted=new OperatorUpdateHost(client))
    {
        status=Node(await restarted.Status(true));Check(status["busy"]!.GetValue<bool>()&&status["canCancel"]!.GetValue<bool>(),"Relay restart adopts the external build without stopping it");
        await File.WriteAllTextAsync(Path.Combine(folder,"finish-build"),"");
        await Until(()=>File.Exists(Path.Combine(folder,"succeeded")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
        Check(File.Exists(Path.Combine(folder,"released")),"Restarted relay completes the late handoff even with the browser closed");
    }
}
finally { Environment.SetEnvironmentVariable("RACING_FAKE_WAIT",null);Directory.Delete(root,true); }
Console.WriteLine("OPERATOR_HOST_TESTS_PASS");
sealed class GameHandler : HttpMessageHandler
{
    public bool Online,Reject,InRace,Editor;public int Posts;public JsonNode? Last;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(!Online)throw new HttpRequestException("Unity is closed");
        if(request.Method==HttpMethod.Get)return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new {
            sessionId="fresh-session",state=InRace?"Game":"Title",isEditor=Editor,stateRevision=2,serverTime=1000,settingsRevision=9,
            settings=new{updateAvailable=true,updateBusy=false,updateStatus="Ready"}}))};
        Posts++;Last=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token));
        if(request.Headers.GetValues("Origin").Single()!="http://127.0.0.1:8765")throw new Exception("Wrong Unity origin");
        if(!Reject) Online=false;
        return new HttpResponseMessage(Reject?HttpStatusCode.Conflict:HttpStatusCode.OK){Content=new StringContent(Reject?
            "{\"ok\":false,\"reason\":\"release_failed\"}":"{\"ok\":true}")};
    }
}

static class NativeProcess
{
    [System.Runtime.InteropServices.DllImport("libc")] public static extern int getpgrp();
}
