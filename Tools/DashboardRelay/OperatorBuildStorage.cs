using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

// Only generated operator outputs are candidates. Logs and the reusable Library stay intact.
internal static class OperatorBuildStorage
{
    internal sealed record Report(string[] deleted,string[] kept,string[] warnings);
    private static readonly Regex RequestName=new(@"^\d{8}-\d{6}-[A-Za-z0-9]+$",RegexOptions.CultureInvariant);
    private static bool Link(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    private static bool Within(string path,string parent)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent)+Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    private static string[]? RunningPlayers()
    {
        var paths=new List<string>();
        foreach(var process in Process.GetProcessesByName("Race"))using(process)
        {
            try { if(!process.HasExited)paths.Add(process.MainModule?.FileName ?? throw new IOException("Raceの起動元を取得できません。")); }
            catch(InvalidOperationException) { }
            catch { return null; } // Do not guess when any running app cannot be identified.
        }
        return paths.ToArray();
    }
    private static bool LiveEditor(string source)
    {
        string file=Path.Combine(source,"Library","EditorInstance.json");
        if(!File.Exists(file))return false;
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(file));
            using var process=Process.GetProcessById(json.RootElement.GetProperty("process_id").GetInt32());
            return !process.HasExited && process.ProcessName.Contains("Unity",StringComparison.OrdinalIgnoreCase);
        }
        catch(ArgumentException) { return false; } // PID no longer exists.
        catch { return true; }
    }
    public static async Task<Report> Prune(string project,Func<string[]?>? running=null,Func<string,bool>? editor=null)
    {
        var deleted=new List<string>();var kept=new List<string>();var warnings=new List<string>();
        string logs=Path.Combine(project,"Logs"),outputs=Path.Combine(project,"Builds","Operator"),sources=Path.Combine(project,"Builds","OperatorSource");
        Directory.CreateDirectory(logs);
        FileStream gate;
        try {gate=new FileStream(Path.Combine(logs,"OperatorPrune.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException) {return new([],[],["別のビルド整理が実行中です。"]);}
        using(gate)
        {
            string[]? players=(running??RunningPlayers)();
            if(players==null)return new([],[],["起動中のRaceの保存先を確認できないため、削除を見送りました。"]);
            string active="";string request=Path.Combine(logs,"OperatorUpdate.lock","request.txt");
            if(File.Exists(request))active=Path.GetFileName(File.ReadAllText(request).Trim().TrimStart('\uFEFF'));
            bool Finished(string name)=>new[]{"succeeded","failed","cancelled"}.Any(marker=>File.Exists(Path.Combine(logs,"OperatorUpdate",name,marker)));
            bool PlayerIn(string folder)=>players.Any(path=>Within(path,folder));
            bool RootSafe(string path)=>Directory.Exists(path)&&!Link(Path.Combine(project,"Builds"))&&!Link(path);
            if(RootSafe(outputs))
            {
                var candidates=Directory.EnumerateDirectories(outputs).Where(path=>!Link(path)).Where(path=>
                    File.Exists(Path.Combine(path,"Race.app","Contents","MacOS","Race")) || File.Exists(Path.Combine(path,"Race.exe")) ||
                    RequestName.IsMatch(Path.GetFileName(path))&&Finished(Path.GetFileName(path))).ToArray();
                // Retain the newest two completed apps for rollback, in addition to every live app.
                var latest=candidates.Where(path=>!RequestName.IsMatch(Path.GetFileName(path)) || File.Exists(Path.Combine(logs,"OperatorUpdate",Path.GetFileName(path),"succeeded")))
                    .Where(path=>File.Exists(Path.Combine(path,"Race.app","Contents","MacOS","Race"))||File.Exists(Path.Combine(path,"Race.exe")))
                    .OrderByDescending(Directory.GetCreationTimeUtc).Take(2).ToHashSet();
                foreach(string path in candidates)
                {
                    string name=Path.GetFileName(path);
                    if(name==active || PlayerIn(path) || latest.Contains(path) || RequestName.IsMatch(name)&&!Finished(name)) {kept.Add(path);continue;}
                    try {Directory.Delete(path,true);deleted.Add(path);} catch(Exception error) when(error is IOException or UnauthorizedAccessException) {warnings.Add(path+": "+error.Message);}
                }
            }
            if(RootSafe(sources))foreach(string path in Directory.EnumerateDirectories(sources).ToArray())
            {
                string name=Path.GetFileName(path);
                if(Link(path)||!RequestName.IsMatch(name)||!Finished(name)||name==active||PlayerIn(path)||(editor??LiveEditor)(path)) {kept.Add(path);continue;}
                try
                {
                    var start=new ProcessStartInfo("git"){UseShellExecute=false,WorkingDirectory=project,RedirectStandardError=true,RedirectStandardOutput=true};
                    foreach(string arg in new[]{"worktree","remove","--force",path})start.ArgumentList.Add(arg);
                    using var process=Process.Start(start)!;
                    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();await stdout;string error=await stderr;
                    if(process.ExitCode!=0 && Directory.Exists(path)) {warnings.Add(path+": "+error.Trim());continue;}
                    deleted.Add(path);
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) {warnings.Add(path+": "+error.Message);}
            }
        }
        return new(deleted.ToArray(),kept.ToArray(),warnings.ToArray());
    }
}
