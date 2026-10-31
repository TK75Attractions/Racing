using System.Text.Json;
static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
var root=Path.Combine(Path.GetTempPath(),"racing-stats-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var clock=new DateTime(2026,11,1,12,0,0);
void Book(string day,bool starts,params object[] entries){string dir=starts?Path.Combine(root,"Plays"):root;Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,day+".json"),JsonSerializer.Serialize(new{version=1,day,entries}));}
object Rank(string id,int player,int ms,string board="standard",int play=1)=>new{raceId=id,boardKey=board,playerNumber=player,milliseconds=ms,playNumber=play,finishedAt="12:00"};
object Start(string id,int player,string board="standard",int laps=2,string preset="Standard",bool accelerated=false)=>new{raceId=id,boardKey=board,playerNumber=player,goalLaps=laps,preset,highSpeed=accelerated,reversed=false,startedAt="12:00:00"};
try{
 var stats=new PlayStatistics(root);var empty=stats.Read(clock);
 Check(empty.todayPlays==0&&empty.boards.Length==0&&empty.days.Length==1,"Empty history shows zero with an empty ranking");
 Book("2026-10-31",true,Start("overnight",1),Start("overnight",2));
 Book("2026-11-01",true,Start("unfinished",1),Start("new",1,"quick",1,"Quick"),Start("new",1,"quick",1,"Quick"));
 Book("2026-11-01",false,Rank("overnight",1,90000),Rank("overnight",2,90000),Rank("legacy",1,100000),Rank("legacy",2,110000),Rank("legacy",1,100000),Rank("new",1,45000,"quick",3));
 var result=stats.Read(clock);
 Check(result.todayPlays==4&&result.days.Single(x=>x.day=="2026-10-31").plays==2,"Midnight finishes stay on their start day and legacy finishes fill missing starts");
 Check(result.days[0].starts==2&&result.days[0].legacyFinishes==2&&result.todayRanked==5,"Unfinished plays count while duplicates never add another person");
 Check(result.boards.Length==2&&result.boards.Single(b=>b.id=="standard").rows.Select(r=>r.rank).SequenceEqual(new[]{1,1,3,4}),"Leaderboards stay separate and equal milliseconds share competition rank");
 Check(result.boards.Single(b=>b.id=="quick").label.Contains("1周")&&result.boards.Single(b=>b.id=="quick").rows[0].milliseconds==45000,"Known rules label the one-lap quick board");
 Check(stats.Read(clock.AddDays(1)).todayPlays==0&&stats.Read(clock.AddDays(1)).boards.Length==0,"Today rolls over without restarting the service");
 string path=Path.Combine(root,"2026-11-01.json");File.Copy(path,path+".bak");File.WriteAllText(path,"broken");
 result=stats.Read(clock);Check(result.todayRanked==5&&result.warnings.Any(),"Corrupt main history uses its valid backup");
 Check(File.ReadAllText(path)=="broken","Statistics never overwrite original history");
 File.WriteAllText(path+".bak","broken too");result=stats.Read(clock);
 Check(result.warnings.Length>0&&result.todayPlays==2,"Unreadable rankings still retain known starts and show a warning");
 Book("2026-10-30",false,new{raceId="invalid",boardKey="standard",playerNumber="not-a-number",milliseconds=1,playNumber=1});
 Check(stats.Read(clock).warnings.Any(w=>w.Contains("2026-10-30")),"Wrong JSON types do not crash the statistics endpoint");
 Book("2026-11-01",true,Start("fast",1,"fast",2,"NoTutorial",true),Start("normal",1,"normal",2,"NoTutorial",false));
 Book("2026-11-01",false,Rank("fast",1,40000,"fast"),Rank("normal",1,80000,"normal"));
 var tuned=stats.Read(clock);
 Check(tuned.boards.Length==2&&tuned.boards.Single(b=>b.highSpeed).label.Contains("練習なし")&&tuned.boards.Single(b=>b.highSpeed).label.Contains("高速化"),"High-speed and no-tutorial board metadata remains separate from normal driving");
 Console.WriteLine("PLAY_STATISTICS_TESTS_PASS");
}finally{Directory.Delete(root,true);}
