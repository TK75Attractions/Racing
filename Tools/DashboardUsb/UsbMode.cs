using System.Text.Json;

internal static class UsbMode
{
    internal static string Requested(string[] args)
    {
        bool iphone=args.Contains("--iphone"),tethering=args.Contains("--tethering");
        if(iphone&&tethering)throw new ArgumentException("--iphone と --tethering は同時に指定できません。");
        return iphone?"iphone":tethering?"tethering":"wired";
    }
    internal static bool CanReuse(JsonElement info,string requested)
        => info.ValueKind==JsonValueKind.Object&&info.TryGetProperty("app",out var app)&&app.ValueKind==JsonValueKind.String&&app.GetString()=="racing-usb-dashboard"&&
            info.TryGetProperty("mode",out var mode)&&mode.ValueKind==JsonValueKind.String&&mode.GetString()==requested;
    internal static string Label(string? mode)=>mode switch{"wired"=>"Safari・有線LAN","iphone"=>"iPhone専用アプリ","tethering"=>"USBテザリング",_=>"旧USB方式または不明な方式"};
    internal static string Conflict(JsonElement info,string requested)
    {
        string? current=info.TryGetProperty("mode",out var mode)&&mode.ValueKind==JsonValueKind.String?mode.GetString():null;
        return $"補助アプリは「{Label(current)}」で起動中のため、「{Label(requested)}」を開始できません。\n"+
            "起動中の補助アプリのターミナルで Ctrl+C を押し、同じコマンドを実行し直してください。\n"+
            "RACEや通常のDashboard中継は終了しないでください。";
    }
}
