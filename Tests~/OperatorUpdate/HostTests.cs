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
echo waiting-race > "$4/phase.txt"
touch "$4/built"
while [ ! -f "$4/launch.allowed" ] && [ ! -f "$4/decision" ]; do sleep .02; done
if [ "$RACING_FAKE_HOLD_ALLOWED" = 1 ]; then
  while [ ! -f "$4/decision" ]; do sleep .02; done
fi
if [ -f "$4/decision" ] && [ "$(cat "$4/decision")" = cancel ]; then
  echo cancelled > "$4/phase.txt"; touch "$4/cancelled"; exit 130
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
bool playerExists=false;
using var client=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:8765")};
try
{
    using var host=new OperatorUpdateHost(client,()=>playerExists);
    var status=Node(await host.Status(true));Check(status["available"]!.GetValue<bool>(),"Unity closed: updater remains available");
    Check(!Node(await host.Status(false))["available"]!.GetValue<bool>(),"Viewer cannot enable update");
    Check(Node(await host.Status(false))["progress"]==null,"Build logs are restricted to operators");
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
    await Until(()=>File.Exists(Path.Combine(folder,"built")));
    await Task.Delay(300);
    Check(handler.Posts==0&&!handler.Maintenance&&!File.Exists(Path.Combine(folder,"decision")),"A completed build cannot interrupt a playing match");
    status=Node(await host.Status(true));
    Check(status["phase"]!.GetValue<string>()=="waiting-race"&&status["canCancel"]!.GetValue<bool>(),"Match-end waiting stays cancellable");
    handler.State="Goal";await Task.Delay(300);
    Check(handler.Posts==0&&!File.Exists(Path.Combine(folder,"launch.allowed")),"Goal celebration also waits for the whole match to reach results");
    handler.InRace=false;
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

    handler.Online=true;handler.InRace=true;
    result=Node(await host.Start("cancel-match-wait"));folder=result["logFolder"]!.GetValue<string>();
    await Until(()=>File.Exists(Path.Combine(folder,"built")));
    Check(Node(await host.Cancel(Path.GetFileName(folder)))["ok"]!.GetValue<bool>(),"The operator can cancel after build success while the race continues");
    await Until(()=>File.Exists(Path.Combine(folder,"cancelled")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    Check(handler.Posts==1&&!handler.Maintenance,"Cancelling match wait leaves the original game running normally");

    handler.Online=false;playerExists=true;
    result=Node(await host.Start("disconnected-race"));folder=result["logFolder"]!.GetValue<string>();
    await Until(()=>File.Exists(Path.Combine(folder,"built")));await Task.Delay(300);
    Check(!File.Exists(Path.Combine(folder,"launch.allowed")),"A disconnected API is not proof that a running Race has finished");
    playerExists=false;
    await Until(()=>File.Exists(Path.Combine(folder,"succeeded")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));

    handler.Online=true;handler.InRace=false;handler.RaceOnMaintenance=true;
    result=Node(await host.Start("new-match-race"));folder=result["logFolder"]!.GetValue<string>();
    await Until(()=>handler.InRace);await Task.Delay(250);
    Check(!File.Exists(Path.Combine(folder,"launch.allowed"))&&!handler.Maintenance,"A match starting after the status snapshot refuses the stale reservation");
    handler.InRace=false;
    await Until(()=>File.Exists(Path.Combine(folder,"succeeded")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));

    handler.Online=true;handler.InRace=false;handler.Maintenance=false;
    Environment.SetEnvironmentVariable("RACING_FAKE_HOLD_ALLOWED","1");
    result=Node(await host.Start("cancel-reservation"));folder=result["logFolder"]!.GetValue<string>();
    Environment.SetEnvironmentVariable("RACING_FAKE_HOLD_ALLOWED",null);
    await Until(()=>File.Exists(Path.Combine(folder,"launch.allowed")));
    Check(handler.Maintenance,"Safe results are reserved before committing launch");
    Check(Node(await host.Cancel(Path.GetFileName(folder)))["ok"]!.GetValue<bool>(),"Cancellation can win before the launch decision");
    await Until(()=>File.Exists(Path.Combine(folder,"cancelled")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    await Until(()=>!handler.Maintenance);
    Check(handler.Online,"Cancellation releases only updater-owned maintenance without closing the app");

    handler.Online=true;handler.InRace=false;handler.Editor=true;handler.Reject=true;
    result=Node(await host.Start("editor-reject"));folder=result["logFolder"]!.GetValue<string>();
    await Until(()=>File.Exists(Path.Combine(folder,"failed")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
    status=Node(await host.Status(true));Check(status["message"]!.GetValue<string>().Contains("Editor"),"A failed Editor release reports its cause and never terminates the Editor");
    handler.Online=false;handler.Reject=false;handler.Editor=false;

    handler.Online=true;handler.InRace=true;
    Environment.SetEnvironmentVariable("RACING_FAKE_WAIT","1");
    var firstHost=new OperatorUpdateHost(client,()=>playerExists);
    result=Node(await firstHost.Start("restart-relay"));folder=result["logFolder"]!.GetValue<string>();
    Environment.SetEnvironmentVariable("RACING_FAKE_WAIT",null);
    await Until(()=>File.Exists(Path.Combine(folder,"control-version")));firstHost.Dispose();
    using(var restarted=new OperatorUpdateHost(client,()=>playerExists))
    {
        status=Node(await restarted.Status(true));Check(status["busy"]!.GetValue<bool>()&&status["canCancel"]!.GetValue<bool>(),"Relay restart adopts the external build without stopping it");
        await File.WriteAllTextAsync(Path.Combine(folder,"finish-build"),"");
        await Until(()=>File.Exists(Path.Combine(folder,"built")));await Task.Delay(250);
        Check(!File.Exists(Path.Combine(folder,"launch.allowed")),"Restarted relay also waits for the current match");
        handler.InRace=false;
        await Until(()=>File.Exists(Path.Combine(folder,"succeeded")) && !Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate.lock")));
        Check(File.Exists(Path.Combine(folder,"released")),"Restarted relay completes the late handoff even with the browser closed");
    }
    // An in-game updater owns its own release. The relay must not compete with it.
    folder=Path.Combine(root,"Logs","OperatorUpdate","runtime-owned");Directory.CreateDirectory(folder);
    string runtimeLock=Path.Combine(root,"Logs","OperatorUpdate.lock");Directory.CreateDirectory(runtimeLock);
    await File.WriteAllTextAsync(Path.Combine(runtimeLock,"request.txt"),folder);
    await File.WriteAllTextAsync(Path.Combine(folder,"handoff.wait"),"runtime");
    await File.WriteAllTextAsync(Path.Combine(folder,"built"),"");
    handler.Online=true;handler.InRace=false;int beforeRuntime=handler.Posts;
    await host.Status(true);await Task.Delay(300);
    Check(!File.Exists(Path.Combine(folder,"launch.allowed")),"Relay does not compete with a live runtime-owned updater even at results");
    await File.WriteAllTextAsync(Path.Combine(folder,"decision"),"launch");
    await File.WriteAllTextAsync(Path.Combine(folder,"ready"),"");
    await File.WriteAllTextAsync(Path.Combine(folder,"released"),"runtime");
    handler.Online=false;await Task.Delay(300);
    Check(handler.Posts==beforeRuntime,"Runtime release cannot trigger a second bridge shutdown");
    await File.WriteAllTextAsync(Path.Combine(folder,"succeeded"),"");Directory.Delete(runtimeLock,true);

    folder=Path.Combine(root,"Logs","OperatorUpdate","runtime-exited");Directory.CreateDirectory(folder);Directory.CreateDirectory(runtimeLock);
    await File.WriteAllTextAsync(Path.Combine(runtimeLock,"request.txt"),folder);
    await File.WriteAllTextAsync(Path.Combine(folder,"handoff.wait"),"runtime");
    await File.WriteAllTextAsync(Path.Combine(folder,"built"),"");
    await host.Status(true);await Until(()=>File.Exists(Path.Combine(folder,"launch.allowed")));
    await File.WriteAllTextAsync(Path.Combine(folder,"decision"),"launch");await File.WriteAllTextAsync(Path.Combine(folder,"ready"),"");
    await Until(()=>File.Exists(Path.Combine(folder,"released")));
    Check(handler.Posts==beforeRuntime,"Relay can adopt a runtime-owned job after that game has exited");
    await File.WriteAllTextAsync(Path.Combine(folder,"succeeded"),"");Directory.Delete(runtimeLock,true);

    var restartedAfterCompletion=new OperatorUpdateHost(client,()=>false);
    var last=Node(await restartedAfterCompletion.Status(true));
    Check(last["logFolder"]!=null,"A restarted helper retains the last completed build logs");restartedAfterCompletion.Dispose();

    string telemetry=Path.Combine(root,"telemetry");Directory.CreateDirectory(telemetry);
    var now=DateTime.UtcNow;
    string buildLog=Path.Combine(telemetry,"build.log"),updateLog=Path.Combine(telemetry,"update.log");
    await File.WriteAllTextAsync(updateLog,"Unityでビルドしています。\n");
    await File.WriteAllTextAsync(buildLog,new string('x',80000)+"\nDisplayProgressbar: Compiling Scripts\nStart importing Assets/Track.fbx using Guid(abc)\nStarting -accessToken secret-test-value\n");
    await File.WriteAllTextAsync(Path.Combine(telemetry,"cache.txt"),"cold");
    File.SetLastWriteTimeUtc(buildLog,now.AddSeconds(-180));File.SetLastWriteTimeUtc(updateLog,now.AddSeconds(-180));
    var progress=OperatorBuildProgress.Read(telemetry,"building",true,now);
    Check(progress.completedSteps==3 && progress.totalSteps==6 && progress.stage=="アセットを読み込み" && progress.task=="Assets/Track.fbx","Log telemetry identifies the active asset and real completed stages");
    Check(progress.logQuiet && progress.quietSeconds>=180,"A quiet log is reported without claiming the process has failed");
    Check(progress.logBytes>80000 && progress.logLines.Length<=86 && !string.Join("",progress.logLines).Contains("secret-test-value"),"Large logs have bounded tails and command-line credentials are redacted");
    Check(OperatorBuildProgress.Read(telemetry,"cancelled",false,now).completedSteps==3,"Cancellation never presents a completed build bar");
    Check(OperatorBuildProgress.Read(telemetry,"completed",false,now).completedSteps==6,"Only successful completion fills the progress bar");
    Check(OperatorBuildProgress.Read(null,"idle",false,now).logLines.Length==0,"Missing logs are handled before the first request");

}
finally { Environment.SetEnvironmentVariable("RACING_FAKE_WAIT",null);Environment.SetEnvironmentVariable("RACING_FAKE_HOLD_ALLOWED",null);Directory.Delete(root,true); }
Console.WriteLine("OPERATOR_HOST_TESTS_PASS");
sealed class GameHandler : HttpMessageHandler
{
    public bool Online,Reject,Editor,Maintenance,RaceOnMaintenance;public int Posts;public JsonNode? Last;
    public string State="Title";
    public bool InRace {get=>State!="Title"&&State!="Result";set=>State=value?"Game":"Result";}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(!Online)throw new HttpRequestException("Unity is closed");
        if(request.Method==HttpMethod.Get)return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new {
            sessionId="fresh-session",state=State,isEditor=Editor,stateRevision=2,serverTime=1000,settingsRevision=9,
            settings=new{maintenanceAvailable=true,maintenanceActive=Maintenance,updateAvailable=true,updateBusy=false,updateStatus="Ready"}}))};
        var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token));
        if(body?["command"]?.GetValue<string>()=="maintenance")
        {
            if(RaceOnMaintenance){RaceOnMaintenance=false;InRace=true;return new HttpResponseMessage(HttpStatusCode.Conflict){Content=new StringContent("{\"ok\":false,\"reason\":\"state_changed\"}")};}
            Maintenance=body["args"]![0]!.GetValue<string>()=="true";
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"ok\":true}")};
        }
        Posts++;Last=body;
        if(request.Headers.GetValues("Origin").Single()!="http://127.0.0.1:8765")throw new Exception("Wrong Unity origin");
        if(!Reject) { Online=false;Maintenance=false; }
        return new HttpResponseMessage(Reject?HttpStatusCode.Conflict:HttpStatusCode.OK){Content=new StringContent(Reject?
            "{\"ok\":false,\"reason\":\"release_failed\"}":"{\"ok\":true}")};
    }
}

static class NativeProcess
{
    [System.Runtime.InteropServices.DllImport("libc")] public static extern int getpgrp();
}
