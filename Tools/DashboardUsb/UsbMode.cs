using System.Text.Json;

internal static class UsbMode
{
    internal static string Requested(string[] args)
    {
        if(args.Contains("--tethering"))throw new ArgumentException("USB接続はiPhone / iPad専用アプリを使用してください。");
        return "iphone";
    }
    internal static bool CanReuse(JsonElement info,string requested)
        => info.ValueKind==JsonValueKind.Object&&info.TryGetProperty("app",out var app)&&app.ValueKind==JsonValueKind.String&&app.GetString()=="racing-usb-dashboard"&&
            info.TryGetProperty("mode",out var mode)&&mode.ValueKind==JsonValueKind.String&&mode.GetString()==requested&&
            (requested!="iphone"||info.TryGetProperty("protocol",out var protocol)&&protocol.ValueKind==JsonValueKind.String&&protocol.GetString()=="racing-usb-v2");
    internal static string Label(string? mode)=>mode=="iphone"?"iPhone / iPad専用アプリ":"旧方式または不明な方式";
    internal static string Conflict(JsonElement info,string requested)
    {
        string? current=info.TryGetProperty("mode",out var mode)&&mode.ValueKind==JsonValueKind.String?mode.GetString():null;
        return $"補助アプリは「{Label(current)}」で起動中です。方式またはバージョンが異なるため「{Label(requested)}」を開始できません。\n"+
            "起動中の補助アプリのターミナルで Ctrl+C を押し、同じコマンドを実行し直してください。\n"+
            "RACEや通常のDashboard中継は終了しないでください。";
    }
}
