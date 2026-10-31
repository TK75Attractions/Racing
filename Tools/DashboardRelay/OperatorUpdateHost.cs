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
    private readonly Func<bool> playerRunning;
    public OperatorUpdateHost(HttpClient client, Func<bool>? playerRunning = null)
    { unity = client; this.playerRunning = playerRunning ?? (() => Process.GetProcessesByName("Race").Any()); }

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
        // Keep the last build's logs accessible after a helper restart, too.
        if (requestFolder == null && Directory.Exists(Path.Combine(project,"Logs","OperatorUpdate")))
            requestFolder=Directory.EnumerateDirectories(Path.Combine(project,"Logs","OperatorUpdate"))
                .Where(folder=>File.Exists(Path.Combine(folder,"status.txt")))
                .OrderByDescending(folder=>Directory.GetCreationTimeUtc(folder)).FirstOrDefault();
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
                // Runtime-triggered jobs have their own in-game poller. Do not race it
                // to close the same bridge; adopt only if its game has already exited.
                bool runtimeOwner=File.ReadAllText(Path.Combine(folder,"handoff.wait")).Trim()=="runtime";
                if (runtimeOwner)
                    while (!Finished(folder))
                    {
                        if (File.Exists(Path.Combine(folder,"released"))) return;
                        if (await Game() == null && !playerRunning()) break;
                        await Task.Delay(200,token);
                    }
                bool seenLock=false;
                string? project=FindProject();
                while (!File.Exists(Path.Combine(folder,"built")) && !File.Exists(Path.Combine(folder,"ready")))
                {
                    if (Finished(folder)) return;
                    bool locked=project != null && Directory.Exists(Path.Combine(project,"Logs","OperatorUpdate.lock"));
                    if (seenLock && !locked) return;
                    seenLock |= locked;
                    await Task.Delay(200,token);
                }
                // Keep cancellation open until the current match ends. Reserving the safe
                // title/result screen prevents a new match racing the launch decision.
                while (!Finished(folder) && !File.Exists(Path.Combine(folder,"launch.allowed")))
                {
                    var current=await Game();
                    if (current == null ? !playerRunning() : await ReserveFinishedMatch(folder,current))
                        await File.WriteAllTextAsync(Path.Combine(folder,"launch.allowed"),"safe",token);
                    else await Task.Delay(200,token);
                }
                while (!Finished(folder) && !File.Exists(Path.Combine(folder,"ready"))) await Task.Delay(100,token);
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
                    if (answer?["ok"]?.GetValue<bool>() != true &&
                        (game["isEditor"]?.GetValue<bool>() == true || !await ReserveFinishedMatch(folder,game)))
                        throw new IOException(game["isEditor"]?.GetValue<bool>() == true ? "Unity EditorのPlayを停止できません。試合終了後にPlayを停止してから再実行してください。" : "試合終了後の機器解放を確認できません。既存のRaceは終了していません。");
                }
                await File.WriteAllTextAsync(Path.Combine(folder,"released"),"ready",token);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { }
            catch(Exception) when(token.IsCancellationRequested) { }
            catch(Exception error) { try { await File.WriteAllTextAsync(Path.Combine(folder,"handoff.failed"),"起動の引き継ぎに失敗しました："+error.Message); } catch(IOException) { } }
            finally
            {
                if (!token.IsCancellationRequested && !File.Exists(Path.Combine(folder,"succeeded")) &&
                    (Finished(folder) || File.Exists(Path.Combine(folder,"handoff.failed")))) await RestoreReservedMatch(folder);
            }
        });
    }
    private static bool Finished(string folder) => new[]{"failed","cancelled","succeeded"}.Any(name=>File.Exists(Path.Combine(folder,name)));
    private async Task<bool> ReserveFinishedMatch(string folder, JsonObject game)
    {
        string? state=game["state"]?.GetValue<string>();
        if (state != "Title" && state != "Result") return false;
        if (game["settings"]?["maintenanceActive"]?.GetValue<bool>() == true) return true;
        if (game["settings"]?["maintenanceAvailable"]?.GetValue<bool>() != true) return false;
        try
        {
            var answer=await SendSetting(game,Guid.NewGuid().ToString("N"),"maintenance","true");
            if (answer?["ok"]?.GetValue<bool>() != true) return false; // Includes a match starting after the snapshot.
            await File.WriteAllTextAsync(Path.Combine(folder,"maintenance.owner"),game["sessionId"]!.GetValue<string>());
            return true;
        }
        catch (Exception error) when(error is HttpRequestException or OperationCanceledException or IOException) { return false; }
    }
    private async Task RestoreReservedMatch(string folder)
    {
        try
        {
            string owner=Path.Combine(folder,"maintenance.owner");
            if (!File.Exists(owner)) return;
            var game=await Game();
            if (game?["sessionId"]?.GetValue<string>() == await File.ReadAllTextAsync(owner) &&
                game?["settings"]?["maintenanceActive"]?.GetValue<bool>() == true)
                await SendSetting(game!,Guid.NewGuid().ToString("N"),"maintenance","false");
        }
        catch (Exception error) when(error is HttpRequestException or OperationCanceledException or IOException) { }
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
                logFolder=requestFolder,progress=canControl ? OperatorBuildProgress.Read(requestFolder,phase,busy) : null,message=decision=="cancel" && busy ? "更新をキャンセルしています。既存のRaceは終了しません。" : hasProgress ? Progress :
                    project != null ? "最新mainを別フォルダーでビルドし、成功後、試合終了を待って既存のRaceを入れ替えます。" : "更新元プロジェクトが見つかりません。RACING_PROJECT_PATHを設定してください。" };
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
