using System.Xml.Linq;

// Install only the helper and its static page, never Unity or a public/Funnel listener.
internal static class MacDashboardService
{
    private const string Label = "com.racing.dashboard";
    // Update code/assets while preserving the installed route, operator policy, and push keys.
    public static async Task UpdateInstalled()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        string library=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library");
        string service=Path.Combine(library,"Application Support","RacingDashboard");
        string plist=Path.Combine(library,"LaunchAgents",Label+".plist");
        if(!File.Exists(plist))throw new IOException("常駐設定がありません。先にInstall Dashboard Serviceを実行してください。");
        var config=XDocument.Load(plist);
        if(!config.Descendants("string").Any(x=>x.Value==Path.Combine(service,"DashboardRelay.dll")))throw new IOException("既存設定が別のアプリを使用しています。");
        if(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)==service)throw new IOException("更新元プロジェクトから実行してください。");
        var uid=await QuickStart.Run("/usr/bin/id",CancellationToken.None,"-u");
        if(uid.Exit!=0||!uint.TryParse(uid.Output.Trim(),out uint userId))throw new IOException("ログインユーザーを確認できません。");
        string scope="gui/"+userId;
        string staging=Path.Combine(Path.GetTempPath(),"racing-dashboard-update-"+Guid.NewGuid().ToString("N"));
        var files=Directory.EnumerateFiles(AppContext.BaseDirectory,"*.dll").Select(Path.GetFileName).Cast<string>().ToList();
        files.AddRange(new[]{"DashboardRelay.deps.json","DashboardRelay.runtimeconfig.json"});
        files.AddRange(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,"Dashboard")).Select(file=>Path.Combine("Dashboard",Path.GetFileName(file))));
        var previous=new List<string>();bool stopped=false;
        try
        {
            foreach(string file in files)
            {
                string next=Path.Combine(staging,"next",file);Directory.CreateDirectory(Path.GetDirectoryName(next)!);
                File.Copy(Path.Combine(AppContext.BaseDirectory,file),next);
                string old=Path.Combine(service,file);
                if(File.Exists(old)){string backup=Path.Combine(staging,"previous",file);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(old,backup);previous.Add(file);}
            }
            var stop=await QuickStart.Run("/bin/launchctl",CancellationToken.None,"bootout",scope+"/"+Label);
            if(stop.Exit!=0)throw new IOException("常駐Dashboardを停止できませんでした。更新は適用していません。");
            stopped=true;
            foreach(string file in files){string target=Path.Combine(service,file);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(staging,"next",file),target,true);}
            var start=await BootstrapAfterStop(scope,plist);
            if(start.Exit!=0)throw new IOException("更新後のDashboardを起動できませんでした："+start.Error.Trim());
            stopped=false;Console.WriteLine("DASHBOARD_SERVICE_UPDATE_PASS: installed route, operator policy, and notification subscriptions preserved.");
        }
        catch
        {
            if(stopped){foreach(string file in previous)File.Copy(Path.Combine(staging,"previous",file),Path.Combine(service,file),true);await BootstrapAfterStop(scope,plist);}
            throw;
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    private static async Task<(int Exit,string Output,string Error)> BootstrapAfterStop(string scope,string plist)
    {
        // bootout can return before launchd has completely retired the previous registration.
        (int Exit,string Output,string Error) result=(1,"","");
        for(int attempt=0;attempt<5;attempt++)
        {
            await Task.Delay(300*(attempt+1));
            result=await QuickStart.Run("/bin/launchctl",CancellationToken.None,"bootstrap",scope,plist);
            if(result.Exit==0)break;
        }
        return result;
    }
    public static async Task Configure(bool install)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("この常駐設定はmacOS用です。");
        var uid = await QuickStart.Run("/usr/bin/id", CancellationToken.None, "-u");
        if (uid.Exit != 0 || !uint.TryParse(uid.Output.Trim(), out uint userId)) throw new IOException("ログインユーザーを確認できませんでした。");
        string scope = "gui/" + userId;
        string library = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library");
        string service = Path.Combine(library, "Application Support", "RacingDashboard");
        string agents = Path.Combine(library, "LaunchAgents");
        string plist = Path.Combine(agents, Label + ".plist");
        string logs = Path.Combine(library, "Logs", "RacingDashboard");
        if (File.Exists(plist))
        {
            var previous = XDocument.Load(plist);
            string executable = Path.Combine(service, "DashboardRelay.dll");
            if (!previous.Descendants("string").Any(value => value.Value == executable))
                throw new IOException("同名の起動設定が別のアプリを使用しています。既存設定は変更しません。");
        }
        string? dotnet = null;
        QuickStart.Settings? connection = null;
        if (install)
        {
            string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            dotnet = new[] { host, "/opt/homebrew/bin/dotnet", "/usr/local/share/dotnet/dotnet", "/usr/local/bin/dotnet" }
                .FirstOrDefault(path => !string.IsNullOrEmpty(path) && File.Exists(path));
            if (dotnet == null || !File.Exists(Path.Combine(AppContext.BaseDirectory, "Dashboard", "index.html")))
                throw new IOException(".NETと管理画面を確認できません。プロジェクトから常駐設定を実行してください。");
            // Resolve the private route here: the macOS app CLI may not read preferences from launchd.
            connection = await QuickStart.Configure(CancellationToken.None);
            if (connection.Origin == null) throw new IOException(connection.Notice ?? "Tailscaleの接続設定を確認できませんでした。");
        }
        await QuickStart.Run("/bin/launchctl", CancellationToken.None, "bootout", scope + "/" + Label);
        if (!install)
        {
            if (File.Exists(plist)) File.Delete(plist);
            Console.WriteLine("Dashboardの常駐を解除しました。ログとアプリのコピーはLibrary内に残しています。");
            return;
        }
        Directory.CreateDirectory(service); Directory.CreateDirectory(agents); Directory.CreateDirectory(logs);
        foreach (string name in new[] { "DashboardRelay.dll", "DashboardRelay.deps.json", "DashboardRelay.runtimeconfig.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(service, name), true);
        // Web Push's managed encryption/protocol libraries must accompany the relay.
        foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            File.Copy(source, Path.Combine(service, Path.GetFileName(source)), true);
        string assets = Path.Combine(service, "Dashboard"); Directory.CreateDirectory(assets);
        foreach (string source in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Dashboard")))
            File.Copy(source, Path.Combine(assets, Path.GetFileName(source)), true);
        var environment = new XElement("dict", new XElement("key", "DOTNET_CLI_TELEMETRY_OPTOUT"), new XElement("string", "1"),
            new XElement("key", "RACING_DASHBOARD_ORIGIN"), new XElement("string", connection!.Origin),
            new XElement("key", "RACING_DASHBOARD_OPERATORS"), new XElement("string", string.Join(",", connection.Operators)));
        string? sourceAssets = DashboardAssets.FindSourceDirectory();
        if (sourceAssets != null) environment.Add(new XElement("key", "RACING_DASHBOARD_ASSETS"), new XElement("string", sourceAssets));
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                new XElement("key", "Label"), new XElement("string", Label),
                new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", dotnet),
                    new XElement("string", Path.Combine(service, "DashboardRelay.dll")), new XElement("string", "--service"), new XElement("string", "--no-browser")),
                new XElement("key", "WorkingDirectory"), new XElement("string", service),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "KeepAlive"), new XElement("true"),
                new XElement("key", "ThrottleInterval"), new XElement("integer", "10"),
                new XElement("key", "EnvironmentVariables"), environment,
                new XElement("key", "StandardOutPath"), new XElement("string", Path.Combine(logs, "relay.log")),
                new XElement("key", "StandardErrorPath"), new XElement("string", Path.Combine(logs, "relay-error.log")))));
        document.Save(plist);
        var result = await QuickStart.Run("/bin/launchctl", CancellationToken.None, "bootstrap", scope, plist);
        if (result.Exit != 0) throw new IOException("Dashboardの常駐起動に失敗しました: " + result.Error.Trim());
        Console.WriteLine("Dashboardの常駐を設定しました。ログイン時に自動起動し、Unity停止中も管理画面を開けます。");
        Console.WriteLine("ログ: " + logs);
    }
}
