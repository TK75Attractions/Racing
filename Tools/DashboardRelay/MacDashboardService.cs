using System.Xml.Linq;

// Install only the helper and its static page, never Unity or a public/Funnel listener.
internal static class MacDashboardService
{
    private const string Label = "com.racing.dashboard";
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
