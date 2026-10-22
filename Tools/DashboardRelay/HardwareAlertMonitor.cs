using System.Text.Json;

/// <summary>Stable incidents, recovery hysteresis and batching prevent repeating sensor alarms.</summary>
internal sealed class HardwareAlertMonitor
{
    private sealed class Incident(double now) { public double First=now, Last=now; public bool Sent; }
    private readonly Dictionary<string,Incident> incidents=new();
    private double lastNotification=double.NegativeInfinity;
    private bool wasActive;
    private string session="";
    public string? Evaluate(JsonElement? snapshot,double now)
    {
        var faults=new Dictionary<string,(string text,double delay)>();
        if(snapshot is not JsonElement root)
        {
            if(wasActive)faults["unity-offline"]=("レース進行中にUnityの応答が途絶えました。ゲームPCを確認してください。",8);
        }
        else
        {
            string id=Text(root,"sessionId");
            if(id!=session){incidents.Clear();session=id;}
            wasActive=Text(root,"state") is "Game" or "Countdown" or "Tutorial";
            var settings=Object(root,"settings");var hw=Object(root,"hardware");var serial=Object(root,"serial");
            string mode=Text(settings,"inputMode");
            bool required=mode is "serial" or "mixed" && !Flag(settings,"bothPlayersAutodrive");
            if(required)
            {
                if(!Flag(serial,"portOpen"))faults["usb"]=("ESP32のUSB接続がありません。ケーブル・給電・ポート設定を確認してください。",8);
                else if(!Flag(hw,"sessionOpen")||!Flag(hw,"recent"))faults["session"]=("ESP32の新しい応答を確認できません。通信・装置の再起動を確認してください。",8);
                else
                {
                    bool oneOnly=mode=="mixed"||Text(hw,"inputMode")=="P1_STEERING";
                    if(Text(hw,"state")=="FATAL")faults["fatal"]=("ESP32が重大な異常を報告しています。機器診断を開いてください。",2);
                    if(hw.TryGetProperty("imus",out var imus)&&imus.ValueKind==JsonValueKind.Array)
                    {
                        int i=0;foreach(var imu in imus.EnumerateArray())
                        {if(i<2&&(!oneOnly||i==0)&&Text(imu,"status")=="Fault")faults["imu"+i]=($"IMU {i+1} に異常があります。電源・I2Cの分岐配線とアドレスを機器診断で確認してください。",6);i++;}
                    }
                    if(!oneOnly&&Text(hw,"ads")=="Fault")faults["ads"]=("ペダル用ADS1115に異常があります。I2C配線と給電を確認してください。",6);
                    if(Text(hw,"state")=="READY")
                    {
                        if(Flag(hw,"invalidTelemetry"))faults["invalid-input"]=("ESP32の入力データが不正です。受信形式・値・通信状態を確認してください。",6);
                        else if(!Flag(hw,"telemetryEnabled"))faults["stream"]=("ESP32の入力配信が停止しています。TEL配信設定を確認してください。",15);
                        else if(!Flag(hw,"inputFresh"))faults["input-stale"]=("走行入力の更新が止まっています。ESP32とセンサーの状態を確認してください。",8);
                    }
                    if(Text(hw,"state")!="CALIBRATING")
                    {
                        if(!Flag(hw,"calibrated"))faults["calibration"]=("センサーの校正が未完了です。待機中に機器診断で確認してください。",30);
                        if(!Flag(hw,"angleLocked"))faults["angle"]=("ハンドル中央の基準が未設定です。機器診断で確認してください。",30);
                    }
                }
            }
        }
        foreach(var key in incidents.Keys.ToArray())if(!faults.ContainsKey(key)&&now-incidents[key].Last>=10)incidents.Remove(key);
        var ready=new List<string>();
        foreach(var (key,fault) in faults)
        {
            if(!incidents.TryGetValue(key,out var incident))incidents[key]=incident=new Incident(now);
            incident.Last=now;
            if(!incident.Sent&&now-incident.First>=fault.delay)ready.Add(key);
        }
        if(ready.Count==0||now-lastNotification<60)return null;
        foreach(string key in ready)incidents[key].Sent=true;
        lastNotification=now;
        return string.Join("\n",ready.Take(4).Select(key=>faults[key].text));
    }
    internal static JsonElement Object(JsonElement root,string key)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Object?value:default;
    internal static string Text(JsonElement root,string key)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
    internal static bool Flag(JsonElement root,string key)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.True;
}
