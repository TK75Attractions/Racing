using System.Diagnostics;
static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
string root=Path.Combine(Path.GetTempPath(),"operator storage "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string App(string name,int age,bool? success=true)
{
    string path=Path.Combine(root,"Builds","Operator",name);Directory.CreateDirectory(Path.Combine(path,"Race.app","Contents","MacOS"));File.WriteAllText(Path.Combine(path,"Race.app","Contents","MacOS","Race"),"test build");
    Directory.SetCreationTimeUtc(path,DateTime.UtcNow.AddDays(-age));
    if(success!=null){string log=Path.Combine(root,"Logs","OperatorUpdate",name);Directory.CreateDirectory(log);File.WriteAllText(Path.Combine(log,success.Value?"succeeded":"failed"),"");}
    return path;
}
try
{
    string old=App("20260101-100000-old",5),live=App("20260102-100000-live",4),previous=App("20260103-100000-previous",3),latest=App("20260104-100000-latest",2),failed=App("20260105-100000-failed",1,false);
    string external=Path.Combine(root,"external");Directory.CreateDirectory(external);File.WriteAllText(Path.Combine(external,"keep"),"keep");
    string manual=App("ManualPreview",6,null);Directory.CreateSymbolicLink(Path.Combine(manual,"linked"),external);string unknown=Path.Combine(root,"Builds","Operator","user-notes");Directory.CreateDirectory(unknown);File.WriteAllText(Path.Combine(unknown,"keep"),"keep");
    string cache=Path.Combine(root,"Builds","OperatorCache","Library");Directory.CreateDirectory(cache);File.WriteAllText(Path.Combine(cache,"keep"),"keep");
    var report=await OperatorBuildStorage.Prune(root,()=>[Path.Combine(live,"Race.app","Contents","MacOS","Race")]);
    Check(!Directory.Exists(old)&&!Directory.Exists(failed)&&!Directory.Exists(manual),"Delete old generated apps and failed outputs");
    Check(Directory.Exists(latest)&&Directory.Exists(previous)&&Directory.Exists(live),"Keep latest two successful outputs and every running app");
    Check(File.Exists(Path.Combine(cache,"keep"))&&File.Exists(Path.Combine(unknown,"keep")),"Never delete the reusable Library or unrelated files");
    string protectedOld=App("20260106-100000-protected",8);string lockDir=Path.Combine(root,"Logs","OperatorUpdate.lock");Directory.CreateDirectory(lockDir);File.WriteAllText(Path.Combine(lockDir,"request.txt"),Path.Combine(root,"Logs","OperatorUpdate",Path.GetFileName(protectedOld)));
    await OperatorBuildStorage.Prune(root,()=>[]);Check(Directory.Exists(protectedOld),"Keep the active request even if an old terminal marker exists");Directory.Delete(lockDir,true);
    string unreadable=App("20260107-100000-unreadable",9);report=await OperatorBuildStorage.Prune(root,()=>null);
    Check(Directory.Exists(unreadable)&&report.warnings.Length==1,"Unidentified running processes prevent deletion");
    Check(File.Exists(Path.Combine(external,"keep")),"Internal bundle links never delete external targets");
    Directory.CreateSymbolicLink(Path.Combine(root,"Builds","Operator","linked"),external);await OperatorBuildStorage.Prune(root,()=>[]);
    Check(File.Exists(Path.Combine(external,"keep")),"Linked output folders cannot delete external files");
    string source=Path.Combine(root,"Builds","OperatorSource","20260108-100000-source"),busy=Path.Combine(root,"Builds","OperatorSource","20260109-100000-editor");
    var init=Process.Start(new ProcessStartInfo("git"){ArgumentList={"init",root},RedirectStandardOutput=true,RedirectStandardError=true});await init!.WaitForExitAsync();
    async Task Git(params string[] args){var start=new ProcessStartInfo("git"){WorkingDirectory=root,RedirectStandardError=true,RedirectStandardOutput=true};foreach(var arg in args)start.ArgumentList.Add(arg);using var p=Process.Start(start)!;await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception(await p.StandardError.ReadToEndAsync());}
    await Git("-c","user.name=QA","-c","user.email=qa@example.com","commit","--allow-empty","-m","initial");
    foreach(string path in new[]{source,busy}){await Git("worktree","add","--detach",path);string log=Path.Combine(root,"Logs","OperatorUpdate",Path.GetFileName(path));Directory.CreateDirectory(log);File.WriteAllText(Path.Combine(log,"succeeded"),"");}
    report=await OperatorBuildStorage.Prune(root,()=>[],path=>path==busy);
    Check(!Directory.Exists(source)&&Directory.Exists(busy),"Remove finished worktrees, keep projects open in an Editor");
    Check(Directory.Exists(Path.Combine(root,"Logs","OperatorUpdate",Path.GetFileName(latest))),"Retain diagnostic logs after cleanup");
}
finally {Directory.Delete(root,true);}
Console.WriteLine("OPERATOR_STORAGE_TESTS_PASS");
