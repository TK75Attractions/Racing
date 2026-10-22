using System;
using System.Collections.Generic;

[Flags]
public enum DashboardInputButtons { None = 0, Accelerate = 1, Brake = 2, Left = 4, Right = 8, Reset = 16, Ready = 32 }

/// <summary>Bounded operator lease; monotonic sequences prevent delayed presses from undoing releases.</summary>
public sealed class DashboardInputLease
{
    public const double TimeoutSeconds = 1.0;
    public const DashboardInputButtons Movement = DashboardInputButtons.Accelerate | DashboardInputButtons.Brake | DashboardInputButtons.Left | DashboardInputButtons.Right;
    private readonly Dictionary<string, long> sequences = new Dictionary<string, long>();
    private readonly Queue<string> owners = new Queue<string>();
    private string owner;
    private double expiresAt;
    private DashboardInputButtons held, pending;
    public long Generation { get; private set; }
    public bool IsActive(double now) => owner != null && now < expiresAt;
    public string Owner(double now) => IsActive(now) ? owner : "";
    public DashboardInputButtons Held(double now) => IsActive(now) ? held : DashboardInputButtons.None;

    public bool TryApply(string controller, long sequence, string command, DashboardInputButtons buttons, double now, out string reason)
    {
        reason = "invalid_input";
        if (string.IsNullOrEmpty(controller) || controller.Length > 80 || sequence <= 0 || sequence > 9007199254740991L || double.IsNaN(now) || double.IsInfinity(now)) return false;
        if (command != "begin" && command != "hold" && command != "pulse" && command != "release") return false;
        if ((command == "begin" || command == "hold") && (buttons & ~Movement) != 0 || command == "release" && buttons != DashboardInputButtons.None ||
            command == "pulse" && buttons != DashboardInputButtons.Reset && buttons != DashboardInputButtons.Ready) return false;
        if (IsActive(now) && owner != controller) { reason = "input_busy"; return false; }
        if (sequences.TryGetValue(controller, out long previous) && sequence <= previous) { reason = "input_outdated"; return false; }
        if (!sequences.ContainsKey(controller))
        {
            owners.Enqueue(controller);
            if (owners.Count > 32) sequences.Remove(owners.Dequeue());
        }
        sequences[controller] = sequence;
        if (command == "release") { Clear(); reason = ""; return true; }
        if (command != "begin" && !IsActive(now)) { reason = "input_expired"; return false; }
        if (command == "begin") { held = pending = DashboardInputButtons.None; Generation++; }
        owner = controller; expiresAt = now + TimeoutSeconds;
        if (command == "begin" || command == "hold") held = buttons;
        else pending |= buttons;
        reason = ""; return true;
    }

    // Called once per game update, never by status readers or physics consumers.
    public DashboardInputButtons Sample(double now)
    {
        if (!IsActive(now)) { Clear(); return DashboardInputButtons.None; }
        DashboardInputButtons sample = held | pending;
        pending = DashboardInputButtons.None;
        return sample;
    }
    public void Clear() { if (owner != null) Generation++; owner = null; expiresAt = 0; held = pending = DashboardInputButtons.None; }
}
