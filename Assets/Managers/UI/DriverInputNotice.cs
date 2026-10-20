/// <summary>Describe the input actually in use, rather than treating every wait as a missing cable.</summary>
public static class DriverInputNotice
{
    public static string Connection(IDriveInputSource source)
    {
        switch (DriveInputStatus.Of(source))
        {
            case DriveConnectionState.Ready: return "";
            case DriveConnectionState.KeyboardMissing: return "キーボードの接続を確認してください";
            case DriveConnectionState.SerialDisconnected: return "ESP32の接続を確認してください";
            case DriveConnectionState.SerialPreparing: return "ESP32の準備を待っています";
            case DriveConnectionState.TelemetryWaiting: return "ハンドル入力を待っています";
            case DriveConnectionState.TelemetryStale: return "ハンドルの通信を待っています";
            case DriveConnectionState.TelemetryDisabled: return "ESP32の入力送信が停止中です";
            case DriveConnectionState.ProtocolMismatch: return "ESP32の通信形式を確認してください";
            default: return "接続を確認してください";
        }
    }

    public static string Release(IDriveInputSource source)
    {
        if (source is P1SteeringTestInputSource test)
        {
            if (test.Mode == P1SteeringTestMode.ConstantPedal)
                return test.ResumePending ? "常時ペダルを準備中" : "S / F8で常時ペダルを再開";
            if (test.Mode == P1SteeringTestMode.KeyboardPedal) return "W/Sを離して再開";
            source = test.NormalSource;
        }
        if (source is KeyboardDriveInputSource) return "キーを離して再開";
        if (source is PlayerAutodrive) return ""; // It supplies its own neutral sample automatically.
        return "ペダルを離して再開";
    }

    public static string Pedal(IDriveInputSource source)
    {
        if (source is P1SteeringTestInputSource test && test.Mode == P1SteeringTestMode.ConstantPedal)
        {
            if (test.ConstantPedalPaused) return "常時ペダル停止中：F8で再開";
            if (test.ConstantPedal <= 0f) return "常時ペダル 0%：F8で調整";
        }
        return "";
    }
}
