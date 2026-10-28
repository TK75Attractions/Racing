using System;
using System.Collections.Generic;

public enum DriverNoticePriority { Success, Guidance, Safety, Danger, Critical }

/// <summary>One scheduler per HUD. Conditions are refreshed; events are consumed once.
/// Adapted from raymee/night-ennichi-visuals; independent of Unity's frame clock.</summary>
public sealed class DriverNoticeQueue
{
    public const float MinimumDisplaySeconds = .8f;
    public sealed class Notice
    {
        public string Key, Text;
        public DriverNoticePriority Priority;
        public float Duration, Expires, LastSeen, ShownAt;
        public bool Condition;
    }
    private readonly List<Notice> pending = new List<Notice>();
    private readonly Dictionary<string, float> lastShown = new Dictionary<string, float>();
    public Notice Current { get; private set; }

    public void Submit(string key, string text, DriverNoticePriority priority, float now,
        float duration = 1.5f, bool condition = false)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return;
        Notice existing = Current != null && Current.Key == key ? Current : null;
        if (existing == null)
            foreach (Notice notice in pending) if (notice.Key == key) { existing = notice; break; }
        if (existing != null)
        {
            // Updating the same source must never leave a duplicate, stale card in the queue.
            existing.Text = text; existing.Priority = priority; existing.LastSeen = now;
            existing.Condition = condition; existing.Duration = Math.Max(MinimumDisplaySeconds, duration);
            return;
        }
        if (!condition && lastShown.TryGetValue(text, out float last) && now - last < 3f) return;
        pending.Add(new Notice { Key = key, Text = text, Priority = priority, LastSeen = now,
            Duration = Math.Max(MinimumDisplaySeconds, duration), Expires = now + 4f, Condition = condition });
    }

    public Notice Tick(float now)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
            if (pending[i].Condition ? now - pending[i].LastSeen > .1f : now > pending[i].Expires)
                pending.RemoveAt(i);
        Notice best = null;
        foreach (Notice notice in pending)
            if (best == null || notice.Priority > best.Priority) best = notice;
        if (Current != null)
        {
            bool held = now - Current.ShownAt < MinimumDisplaySeconds;
            bool alive = Current.Condition ? now - Current.LastSeen < MinimumDisplaySeconds
                : now - Current.ShownAt < Current.Duration;
            bool interrupt = best != null && best.Priority >= DriverNoticePriority.Safety && best.Priority > Current.Priority;
            if (!interrupt && (held || alive)) return Current;
            Current = null;
        }
        if (best != null)
        {
            pending.Remove(best); Current = best; best.ShownAt = now; lastShown[best.Text] = now;
        }
        return Current;
    }

    public void Cancel(string key)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
            if (pending[i].Key == key) pending.RemoveAt(i);
        if (Current != null && Current.Key == key) Current = null;
    }

    public void Release(string key)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
            if (pending[i].Key == key && pending[i].Condition) pending.RemoveAt(i);
        // Never tell the player to release a pedal or wait for connection after it is resolved.
        if (Current != null && Current.Key == key && Current.Condition && Current.Priority == DriverNoticePriority.Critical)
            Current = null;
    }

    public void Clear() { pending.Clear(); lastShown.Clear(); Current = null; }
}

/// <summary>Mailbox only; presentation timing belongs to the HUD queue.</summary>
public sealed class DriverNoticeSignal
{
    public string Text { get; private set; } = "";
    private DriverNoticePriority priority;
    private float duration;
    public void Publish(string text, DriverNoticePriority kind, float seconds)
    { Text = text; priority = kind; duration = seconds; }
    public void Clear() { Text = ""; }
    public void Drain(DriverNoticeQueue queue, string key, float now)
    {
        if (Text.Length == 0) return;
        queue.Submit(key, Text, priority, now, duration); Clear();
    }
}
