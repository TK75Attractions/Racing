using System.Globalization;
using System.Text.Json;

// Read-only aggregation of Unity's local journals. Available while Race is closed.
internal sealed class PlayStatistics
{
    internal sealed record Day(string day,int plays,int starts,int legacyFinishes,int rankedFinishes);
    internal sealed record Row(int rank,int playNumber,int playerNumber,int milliseconds,string finishedAt);
    internal sealed record Board(string id,string label,int goalLaps,bool reversed,string preset,Row[] rows);
    internal sealed record Snapshot(string day,string timezone,int todayPlays,int todayRanked,Day[] days,Board[] boards,string[] warnings);
    private sealed record Entry(string day,string raceId,int player,string board,int milliseconds,int play,string time,int laps,bool reversed,string preset)
    { public string Key=>raceId+":"+player; }
    private readonly string directory;
    public PlayStatistics(string directory) => this.directory=directory;
    public static string DefaultDirectory()
    {
        string? configured=Environment.GetEnvironmentVariable("RACING_STATISTICS_DIRECTORY");
        if(!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)) return configured;
        if(OperatingSystem.IsMacOS()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","DefaultCompany","Race","DailyRankings");
        if(OperatingSystem.IsWindows()) return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"..","LocalLow","DefaultCompany","Race","DailyRankings"));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".config","unity3d","DefaultCompany","Race","DailyRankings");
    }
    public Snapshot Read(DateTime? localClock=null)
    {
        string today=(localClock??DateTime.Now).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        var warnings=new List<string>();
        var ranks=ReadBooks(directory,false,warnings);
        var starts=ReadBooks(Path.Combine(directory,"Plays"),true,warnings);
        var uniqueStarts=starts.OrderBy(e=>e.day,StringComparer.Ordinal).DistinctBy(e=>e.Key).ToArray();
        var startedKeys=uniqueStarts.Select(e=>e.Key).ToHashSet(StringComparer.Ordinal);
        var uniqueRanks=ranks.OrderBy(e=>e.day,StringComparer.Ordinal).DistinctBy(e=>e.Key).ToArray();
        var legacy=uniqueRanks.Where(e=>!startedKeys.Contains(e.Key)).ToArray();
        var days=uniqueStarts.Select(e=>e.day).Concat(uniqueRanks.Select(e=>e.day)).Append(today).Distinct()
            .OrderByDescending(day=>day,StringComparer.Ordinal).Select(day=> {
                int counted=uniqueStarts.Count(e=>e.day==day),fallback=legacy.Count(e=>e.day==day);
                return new Day(day,counted+fallback,counted,fallback,uniqueRanks.Count(e=>e.day==day));
            }).ToArray();
        var metadata=uniqueStarts.GroupBy(e=>e.board).ToDictionary(group=>group.Key,group=>group.Last(),StringComparer.Ordinal);
        string[] keys=uniqueRanks.Where(e=>e.day==today).Select(e=>e.board).Concat(uniqueStarts.Where(e=>e.day==today).Select(e=>e.board)).Distinct().OrderBy(key=>key,StringComparer.Ordinal).ToArray();
        var boards=keys.Select((key,index)=> {
            metadata.TryGetValue(key,out var meta);
            string label=meta==null ? keys.Length==1?"保存済みコース":"保存済みコース "+(index+1) :
                (meta.preset=="Quick"?"クイック":"通常")+"・"+meta.laps+"周・"+(meta.reversed?"逆走":"正走");
            var ordered=uniqueRanks.Where(e=>e.day==today && e.board==key).OrderBy(e=>e.milliseconds).ThenBy(e=>e.play).ThenBy(e=>e.player).ThenBy(e=>e.raceId,StringComparer.Ordinal).ToArray();
            int previous=-1,rank=0;
            var rows=ordered.Select((entry,i)=>{if(previous!=entry.milliseconds)rank=i+1;previous=entry.milliseconds;return new Row(rank,entry.play,entry.player,entry.milliseconds,entry.time);}).ToArray();
            return new Board(key,label,meta?.laps??0,meta?.reversed??false,meta?.preset??"",rows);
        }).ToArray();
        var current=days.First(day=>day.day==today);
        return new(today,TimeZoneInfo.Local.Id,current.plays,current.rankedFinishes,days,boards,warnings.ToArray());
    }
    private static List<Entry> ReadBooks(string folder,bool starts,List<string> warnings)
    {
        var result=new List<Entry>();
        if(!Directory.Exists(folder)) return result;
        try
        {
            var days=Directory.EnumerateFiles(folder,"*.json*").Select(Path.GetFileName)
                .Select(name=>name!.EndsWith(".json.bak",StringComparison.Ordinal)?name[..^9]:name.EndsWith(".json",StringComparison.Ordinal)?name[..^5]:"")
                .Where(day=>DateOnly.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)).Distinct().OrderBy(day=>day,StringComparer.Ordinal);
            foreach(string day in days)
            {
                string file=Path.Combine(folder,day+".json");
                try { result.AddRange(ReadBook(File.Exists(file)?file:file+".bak",day,starts)); }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    try { result.AddRange(ReadBook(file+".bak",day,starts));warnings.Add(day+" の"+(starts?"プレイ人数":"ランキング")+"はバックアップから表示しています。"); }
                    catch(Exception backup) when(backup is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                    { warnings.Add(day+" の"+(starts?"プレイ人数":"ランキング")+"を読み込めません。元の履歴は保持しています。"); }
                }
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { warnings.Add((starts?"プレイ人数":"ランキング")+"の履歴フォルダーを読み込めません。"); }
        return result;
    }
    private static Entry[] ReadBook(string path,string day,bool starts)
    {
        if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("History too large.");
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        var book=document.RootElement;
        if(Int(book,"version")!=1 || Text(book,"day")!=day || !book.TryGetProperty("entries",out var entries) || entries.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Invalid history schema.");
        var result=new List<Entry>();
        foreach(var entry in entries.EnumerateArray())
        {
            string race=Text(entry,"raceId"),board=Text(entry,"boardKey");int player=Int(entry,"playerNumber"),ms=Int(entry,"milliseconds"),play=Int(entry,"playNumber"),laps=Int(entry,"goalLaps");
            if(race.Length==0 || board.Length==0 || player<1 || player>2 || (starts?laps<1:ms<1||ms>=86400000||play<1))throw new InvalidDataException("Invalid entry.");
            result.Add(new(day,race,player,board,ms,play,Text(entry,starts?"startedAt":"finishedAt"),laps,
                entry.TryGetProperty("reversed",out var reverse)&&reverse.ValueKind==JsonValueKind.True,Text(entry,"preset")));
        }
        return result.ToArray();
    }
    private static string Text(JsonElement value,string name)=>value.ValueKind==JsonValueKind.Object && value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString()??"":"";
    private static int Int(JsonElement value,string name)=>value.ValueKind==JsonValueKind.Object && value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.Number&&field.TryGetInt32(out int number)?number:0;
}
