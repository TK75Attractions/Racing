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
    private readonly CancellationTokenSource lifetime = new();
    private string? handoffFolder;
    private bool Busy => worker != null && !worker.HasExited;
    private string Read(string name)
    {
        try { return requestFolder != null ? File.ReadAllText(Path.Combine(requestFolder,name)).Trim().TrimStart('\uFEFF') : ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ""; }
    }
    private string Progress => Read("status.txt") is { Length: > 0 } text ? text : "更新を開始しています。";
    private void ObserveRequest(string? project)
    {
        if (project == null) return;
        string file=Path.Combine(project,"Logs","OperatorUpdate.lock","request.txt");
        if (File.Exists(file))
        {
            string folder;
            try { folder=File.ReadAllText(file).Trim().TrimStart('\uFEFF'); }
            catch(IOException) { return; }
            if (folder.Length==0) return;
            string allowed=Path.Combine(project,"Logs","OperatorUpdate")+Path.DirectorySeparatorChar;
            if (Path.GetFullPath(folder).StartsWith(allowed,StringComparison.Ordinal)) requestFolder=folder;
        }
        if(requestFolder != null && File.Exists(Path.Combine(requestFolder,"handoff.wait"))) EnsureHandoff(requestFolder);
    }
    private bool IsBusy(string? project) => Busy || project != null && Directory.Exists(Path.Combine(project,"Logs","OperatorUpdate.lock"));
    private void EnsureHandoff(string folder)
    {
        if (handoffFolder == folder) return;
        handoffFolder=folder;
        CancellationToken token=lifetime.Token;
        _ = Task.Run(async () => {
            try
            {
                bool seenLock=false;
                string? project=FindProject();
                while (!File.Exists(Path.Combine(folder,"ready")))
                {
                    if (new[]{"failed","cancelled","succeeded"}.Any(name=>File.Exists(Path.Combine(folder,name)))) return;
                    bool locked=project != null && Directory.Exists(Path.Combine(project,"Logs","OperatorUpdate.lock"));
                    if (seenLock && !locked) return;
                    seenLock |= locked;
                    await Task.Delay(200,token);
                }
                if (new[]{"failed","cancelled","succeeded"}.Any(name=>File.Exists(Path.Combine(folder,name)))) return;
                if (File.Exists(Path.Combine(folder,"released")) ||
                    File.ReadAllText(Path.Combine(folder,"decision")).Trim() != "launch") return;
                var game=await Game();
                if (game != null)
                {
                    // New players/editors can release devices gracefully. Older Race players
                    // are stopped by the launcher's narrowly matched process fallback.
                    JsonNode? answer=null;
                    try { answer=await SendSetting(game,Guid.NewGuid().ToString("N"),"releaseForUpdate",Path.GetFileName(folder)); }
                    catch (Exception error) when(error is HttpRequestException or OperationCanceledException or IOException) { }
                    if (game["isEditor"]?.GetValue<bool>() == true && answer?["ok"]?.GetValue<bool>() == false)
                        throw new IOException("Unity EditorのPlayを停止できません。Playを停止してから再実行してください。");
                }
                await File.WriteAllTextAsync(Path.Combine(folder,"released"),"ready",token);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { }
            catch(Exception) when(token.IsCancellationRequested) { }
            catch(Exception error) { try { await File.WriteAllTextAsync(Path.Combine(folder,"handoff.failed"),"起動の引き継ぎに失敗しました："+error.Message); } catch(IOException) { } }
        });
    }
    public async Task<object> Status(bool canControl)
    {
        await gate.WaitAsync();
        try
        {
            string? project=FindProject(); ObserveRequest(project);
            bool busy=IsBusy(project);
            string phase=Read("phase.txt");
            if(phase.Length==0) phase=Read("cancelled").Length>0 ? "cancelled" : busy ? "building" : "idle";
            string decision=Read("decision");
            if (decision=="cancel" && busy) phase="cancelling";
            bool canCancel=canControl && busy && Read("control-version")=="2" && requestFolder != null && !File.Exists(Path.Combine(requestFolder,"decision"));
            bool hasProgress=requestFolder != null && File.Exists(Path.Combine(requestFolder,"status.txt"));
            return new { available=canControl && project != null && !busy,busy,canControl,canCancel,
                requestId=requestFolder != null ? Path.GetFileName(requestFolder) : null,phase,
                logFolder=requestFolder,message=decision=="cancel" && busy ? "更新をキャンセルしています。既存のRaceは終了しません。" : hasProgress ? Progress :
                    project != null ? "最新mainを別フォルダーでビルドし、成功後に既存のRaceを入れ替えます。" : "更新元プロジェクトが見つかりません。RACING_PROJECT_PATHを設定してください。" };
        }
        finally { gate.Release(); }
    }
    public async Task<object> Start(string id)
    {
        await gate.WaitAsync();
        try
        {
            if(results.TryGetValue(id,out var cached)) return cached;
            string project=FindProject() ?? throw new IOException("更新元プロジェクトが見つかりません。RACING_PROJECT_PATHを設定してください。");
            object result;
            if(IsBusy(project)) result=new {ok=false,message="別の更新が実行中です。進行状況を確認してください。"};
            else
            {
                // The always-on relay owns the build even if an old Race is running.
                // Never hand long builds to an old player's 120-second preflight timer.
                requestFolder=Path.Combine(project,"Logs","OperatorUpdate",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(requestFolder);
                bool windows=OperatingSystem.IsWindows();
                string script=windows ? "update-and-run.ps1" : "update-and-run.sh";
                string runner=Path.Combine(requestFolder,script);
                File.Copy(Path.Combine(project,"Tools","OperatorUpdate",script),runner);
                File.WriteAllText(Path.Combine(requestFolder,"handoff.wait"),"relay");
                bool detached=OperatingSystem.IsMacOS();
                var start=new ProcessStartInfo(windows ? "powershell.exe" : detached ? "/usr/bin/perl" : "/bin/bash") { WorkingDirectory=project,UseShellExecute=false,CreateNoWindow=true };
                // launchd otherwise kills a service's whole process group on restart.
                if(detached) foreach(var arg in new[]{"-MPOSIX","-e","POSIX::setsid() >= 0 or die $!; exec '/bin/bash', @ARGV or die $!;"}) start.ArgumentList.Add(arg);
                if(windows) foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File"}) start.ArgumentList.Add(arg);
                foreach(var arg in new[]{runner,project,Environment.GetEnvironmentVariable("RACING_UNITY_PATH")??"","0",requestFolder}) start.ArgumentList.Add(arg);
                worker?.Dispose(); worker=Process.Start(start) ?? throw new IOException("更新プログラムを起動できませんでした。");
                EnsureHandoff(requestFolder);
                result=new { ok=true,message="ビルドを開始しました。既存のRaceは完了までそのまま使用できます。",logFolder=requestFolder,requestId=Path.GetFileName(requestFolder) };
            }
            if(results.Count>=256) results.Remove(results.Keys.First());
            results[id]=result; return result;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            var result=new {ok=false,message="更新を開始できませんでした："+error.Message};results[id]=result;return result;
        }
        finally { gate.Release(); }
    }
    public async Task<object> Cancel(string requestId)
    {
        await gate.WaitAsync();
        try
        {
            string? project=FindProject(); ObserveRequest(project);
            if(requestFolder==null || Path.GetFileName(requestFolder)!=requestId)
                return new {ok=false,message="更新要求が変わりました。最新の進行状況を確認してください。"};
            if(Read("decision")=="cancel") return new {ok=true,message="キャンセルを受け付けました。停止処理を待っています。"};
            if(!IsBusy(project) || Read("control-version")!="2") return new {ok=false,message="この更新はキャンセルできません。進行状況を確認してください。"};
            try
            {
                // Exactly one of cancellation and application replacement may win.
                using var file=new FileStream(Path.Combine(requestFolder,"decision"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                using var writer=new StreamWriter(file); writer.Write("cancel\n");
            }
            catch(IOException) { return new {ok=false,message="アプリの切り替えが始まったため、キャンセルできません。"}; }
            return new {ok=true,message="キャンセルを受け付けました。既存のRaceは終了しません。"};
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { return new {ok=false,message="キャンセル要求を保存できません："+error.Message}; }
        finally { gate.Release(); }
    }
    public void Dispose() { lifetime.Cancel();worker?.Dispose();gate.Dispose();lifetime.Dispose(); }
}
