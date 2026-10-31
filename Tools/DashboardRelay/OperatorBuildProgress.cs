using System.Text;
using System.Text.RegularExpressions;

// Bounded log reads keep polling cheap even when Unity emits very large shader logs.
internal static class OperatorBuildProgress
{
    internal sealed record Snapshot(int completedSteps, int totalSteps, string stage, string task,
        double elapsedSeconds, double quietSeconds, bool logQuiet, string cache,
        long logBytes, string[] logLines);
    public static Snapshot Read(string? folder, string phase, bool busy, DateTime? clock = null)
    {
        DateTime now=clock ?? DateTime.UtcNow;
        if(folder==null || !Directory.Exists(folder)) return new(0,6,"待機中","",0,0,false,"unknown",0,Array.Empty<string>());
        var update=Tail(Path.Combine(folder,"update.log"),16);
        var build=Tail(Path.Combine(folder,"build.log"),70);
        int done=phase switch { "preparing"=>1,"native" or "cache"=>2,"building"=>3,
            "waiting-race"=>4,"switching" or "launching"=>5,"completed"=>6,_=>0 };
        if(phase is "failed" or "cancelled" or "cancelling")
            done=File.Exists(Path.Combine(folder,"ready"))?5:File.Exists(Path.Combine(folder,"built"))?4:build.Bytes>0?3:0;
        string stage=phase switch { "fetching"=>"mainを取得", "preparing"=>"ビルド元を準備", "native"=>"音声プラグインをビルド",
            "cache"=>"Unityキャッシュを準備", "building"=>"Unityを起動・読み込み", "waiting-race"=>"試合終了待ち",
            "switching"=>"旧Raceを終了", "launching"=>"新しいRaceを起動", "completed"=>"完了", "failed"=>"失敗",
            "cancelled"=>"キャンセル済み", "cancelling"=>"キャンセル処理中",_=>"待機中" };
        string task="";
        if(phase=="building")
            foreach(string line in build.Lines)
            {
                if(line.Contains("DisplayProgressbar:")) {stage="Unityビルド";task=line[(line.IndexOf("DisplayProgressbar:")+19)..].Trim();}
                else if(line.Contains("Start importing "))
                {
                    stage="アセットを読み込み";
                    task=line[(line.IndexOf("Start importing ")+16)..].Split(" using Guid",StringSplitOptions.None)[0];
                }
                else if(line.Contains("Compiling shader",StringComparison.OrdinalIgnoreCase)) {stage="シェーダーをコンパイル";task=line;}
                else if(line.Contains("ScriptCompilationBuildProgram") || line.Contains("Compiling Scripts")) {stage="スクリプトをコンパイル";task=line;}
                else if(line.Contains("OPERATOR_BUILD_PASS:")) {stage="ビルド出力を確認";task=line;}
                else if(line.Contains("Building Player") || line.Contains("BuildPlayer: ")) {stage="Playerをビルド";task=line;}
            }
        DateTime activity=update.Modified>build.Modified?update.Modified:build.Modified;
        double quiet=activity==DateTime.MinValue?0:Math.Max(0,(now-activity).TotalSeconds);
        string cache="unknown";
        try {cache=File.ReadAllText(Path.Combine(folder,"cache.txt")).Trim().TrimStart('\uFEFF');} catch(IOException) {} catch(UnauthorizedAccessException) {}
        var logs=update.Lines.Select(line=>"[更新] "+line).Concat(build.Lines.Select(line=>"[Unity] "+line)).ToArray();
        return new(done,6,stage,task,Math.Max(0,(now-Directory.GetCreationTimeUtc(folder)).TotalSeconds),quiet,
            busy && phase=="building" && quiet>=120,cache,update.Bytes+build.Bytes,logs);
    }
    private sealed record LogTail(string[] Lines,long Bytes,DateTime Modified);
    private static LogTail Tail(string path,int limit)
    {
        try
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            long size=stream.Length,start=Math.Max(0,size-64*1024);
            stream.Seek(start,SeekOrigin.Begin);
            var buffer=new byte[(int)(size-start)];
            int count=stream.ReadAtLeast(buffer,buffer.Length,throwOnEndOfStream:false);
            string text=Encoding.UTF8.GetString(buffer,0,count);
            if(start>0) {int newline=text.IndexOf('\n');text=newline>=0?text[(newline+1)..]:"";}
            var lines=text.Replace('\r','\n').Split('\n').Where(line=>!string.IsNullOrWhiteSpace(line)).TakeLast(limit)
                .Select(line=>Clean(line)).ToArray();
            return new(lines,size,File.GetLastWriteTimeUtc(path));
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {return new(Array.Empty<string>(),0,DateTime.MinValue);}
    }
    private static string Clean(string line)
    {
        line=Regex.Replace(line,@"\x1B\[[0-?]*[ -/]*[@-~]","");
        line=Regex.Replace(line,@"(?i)(-accessToken\s+|Authorization:\s*Bearer\s+)\S+","$1[redacted]");
        return line.Length>900?line[..900]+"…":line;
    }
}
