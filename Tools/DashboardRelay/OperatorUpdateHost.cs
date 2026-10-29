using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

// The relay remains available with Unity closed. It owns the offline update process.
internal sealed class OperatorUpdateHost : IDisposable
{
    private readonly HttpClient unity;
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, object> results = new();
    private Process? worker;
    private string? requestFolder;
    public OperatorUpdateHost(HttpClient client) => unity = client;

    public static string? FindProject()
    {
        string saved = Path.Combine(AppContext.BaseDirectory, "operator-project.txt");
        foreach (string? start in new[] { Environment.GetEnvironmentVariable("RACING_PROJECT_PATH"),
            File.Exists(saved) ? File.ReadAllText(saved).Trim() : null,
            Environment.GetEnvironmentVariable("RACING_DASHBOARD_ASSETS"), Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(start) || !Path.IsPathFullyQualified(start)) continue;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if ((Directory.Exists(Path.Combine(dir.FullName,".git")) || File.Exists(Path.Combine(dir.FullName,".git"))) &&
                    File.Exists(Path.Combine(dir.FullName,"Tools","OperatorUpdate","update-and-run.sh"))) return dir.FullName;
        }
        return null;
    }
    private async Task<JsonObject?> Game()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { return JsonNode.Parse(await unity.GetStringAsync("/api/status",timeout.Token)) as JsonObject; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException) { return null; }
    }
    private async Task<JsonNode?> SendSetting(JsonObject game, string id, string command, params string[] args)
    {
        var body=new JsonObject { ["id"]=id,["command"]=command,["args"]=new JsonArray(args.Select(arg=>(JsonNode?)JsonValue.Create(arg)).ToArray()) };
        foreach(string field in new[]{"stateRevision","settingsRevision"})body[field]=game[field]?.DeepClone();
        body["targetSession"]=game["sessionId"]?.DeepClone();body["expectedState"]=game["state"]?.DeepClone();body["observedAt"]=game["serverTime"]?.DeepClone();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/action/settings");
        request.Headers.Add("Origin","http://127.0.0.1:8765");
        request.Content=new StringContent(body.ToJsonString(),System.Text.Encoding.UTF8,"application/json");
        using var response=await unity.SendAsync(request,timeout.Token);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
    }
    private bool Busy => worker != null && !worker.HasExited;
    private string Progress => requestFolder != null && File.Exists(Path.Combine(requestFolder,"status.txt"))
        ? File.ReadAllText(Path.Combine(requestFolder,"status.txt")).Trim().TrimStart('\uFEFF') : "更新を開始しています。";

    public async Task<object> Status(bool canControl)
    {
        await gate.WaitAsync();
        try
        {
            string? project = FindProject();
            string? lockFile = project == null ? null : Path.Combine(project,"Logs","OperatorUpdate.lock","request.txt");
            if (lockFile != null && File.Exists(lockFile)) requestFolder=File.ReadAllText(lockFile).Trim().TrimStart('\uFEFF');
            if (Busy || (project != null && Directory.Exists(Path.Combine(project,"Logs","OperatorUpdate.lock"))))
                return new { available=false,busy=true,message=Progress,logFolder=requestFolder,canControl };
            worker?.Dispose();worker=null;
            var game = await Game();
            if (game != null)
            {
                var settings = game["settings"];
                bool canPause=settings?["maintenanceAvailable"]?.GetValue<bool>() == true && project != null;
                bool needsPause=game["state"]?.GetValue<string>() is not ("Title" or "Result") && settings?["maintenanceActive"]?.GetValue<bool>() != true;
                return new { available=canControl && (settings?["updateAvailable"]?.GetValue<bool>() == true || canPause),
                    busy=settings?["updateBusy"]?.GetValue<bool>() == true,
                    message=requestFolder != null && File.Exists(Path.Combine(requestFolder,"failed")) ? Progress : needsPause && canPause ? "更新時にメンテナンスへ切り替え、最新mainをビルド・起動します。" : settings?["updateStatus"]?.GetValue<string>() ?? "ゲームを更新機能のあるバージョンで起動してください。", logFolder=requestFolder, canControl };
            }
            bool found = FindProject() != null;
            return new { available=canControl && found, busy=false, canControl, logFolder=requestFolder,
                message=found ? (requestFolder != null ? Progress : "Unity停止中でも最新mainをビルドして起動できます。") : "更新元プロジェクトが見つかりません。RACING_PROJECT_PATHを設定してください。" };
        }
        finally { gate.Release(); }
    }
    public async Task<object> Start(string id)
    {
        await gate.WaitAsync();
        try
        {
            if (results.TryGetValue(id,out var cached)) return cached;
            object result;
            string? projectPath=FindProject();
            if (Busy || (projectPath != null && Directory.Exists(Path.Combine(projectPath,"Logs","OperatorUpdate.lock")))) result = new { ok=false, message="別の更新が実行中です。進行状況を確認してください。" };
            else
            {
                var game = await Game();
                if (game != null)
                {
                    // Older running players require maintenance before accepting an update.
                    // The explicit operator update action authorizes this pause even during a race.
                    bool needsPause=game["state"]?.GetValue<string>() is not ("Title" or "Result") && game["settings"]?["maintenanceActive"]?.GetValue<bool>() != true;
                    if(needsPause)
                    {
                        var pause=await SendSetting(game,Guid.NewGuid().ToString("N"),"maintenance","true");
                        if(pause?["ok"]?.GetValue<bool>() != true)throw new IOException("メンテナンスへ切り替えられません。最新状態を確認して再実行してください。");
                        game=await Game() ?? throw new IOException("ゲームが停止しました。更新状態を確認してください。");
                    }
                    // Fetch the current anchor rather than using the browser's stale settings revision.
                    var answer=await SendSetting(game,id,"updateAndRun");
                    bool ok=answer?["ok"]?.GetValue<bool>() == true;
                    string? reason=answer?["reason"]?.GetValue<string>();
                    string message=reason is "state_changed" or "settings_changed" ? "ゲーム状態が変わりました。タイトル・結果・メンテナンス画面で実行してください。" :
                        !ok && !string.IsNullOrEmpty(reason) && reason != "invalid_setting" ? reason :
                        answer?["settings"]?["updateStatus"]?.GetValue<string>() ?? "更新を開始できませんでした。";
                    result = new { ok,message };
                }
                else
                {
                    string project = FindProject() ?? throw new IOException("更新元プロジェクトが見つかりません。RACING_PROJECT_PATHを設定してください。");
                    requestFolder=Path.Combine(project,"Logs","OperatorUpdate",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(requestFolder);
                    bool windows=OperatingSystem.IsWindows();
                    string script=windows?"update-and-run.ps1":"update-and-run.sh";
                    string runner=Path.Combine(requestFolder,script);
                    File.Copy(Path.Combine(project,"Tools","OperatorUpdate",script),runner);
                    var start=new ProcessStartInfo(windows?"powershell.exe":"/bin/bash") { WorkingDirectory=project,UseShellExecute=false,CreateNoWindow=true };
                    if(windows) foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File"})start.ArgumentList.Add(arg);
                    foreach(var arg in new[]{runner,project,Environment.GetEnvironmentVariable("RACING_UNITY_PATH")??"","0",requestFolder})start.ArgumentList.Add(arg);
                    worker?.Dispose();worker=Process.Start(start) ?? throw new IOException("更新プログラムを起動できませんでした。");
                    result=new { ok=true,message="更新を開始しました。",logFolder=requestFolder };
                }
            }
            if(results.Count>=256)results.Remove(results.Keys.First());
            results[id]=result;return result;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception or JsonException)
        {
            // A timed-out request may already have started; cache it so retries cannot start a second job.
            var result=new { ok=false,message="更新開始を確認できませんでした："+error.Message };
            results[id]=result;return result;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { worker?.Dispose();gate.Dispose(); }
}
