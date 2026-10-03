using System;
using UnityEngine;

/// <summary>アイテムボックスから出るアイテムの種類です。</summary>
public enum RaceItemType
{
    None,
    Shield,
    Rocket,
    Oil,
    Confuse
}

/// <summary>順位に応じてアイテムボックスの中身を抽選します。Unityの状態に依存しない純粋な抽選表です。</summary>
[Serializable]
public sealed class RaceItemTable
{
    [Serializable]
    public struct Entry
    {
        public RaceItemType item;
        [Tooltip("1位の車が引いたときの重み。0でその順位では出ません。")]
        [Min(0f)] public float leaderWeight;
        [Tooltip("2位の車が引いたときの重み。")]
        [Min(0f)] public float trailerWeight;
    }

    [SerializeField] private Entry[] entries = CreateDefaultEntries();

    public static Entry[] CreateDefaultEntries()
    {
        // 逆転用のロケットとコンフューズは後方の車にだけ出します。
        return new[]
        {
            new Entry { item = RaceItemType.Shield, leaderWeight = 50f, trailerWeight = 15f },
            new Entry { item = RaceItemType.Oil, leaderWeight = 50f, trailerWeight = 20f },
            new Entry { item = RaceItemType.Rocket, leaderWeight = 0f, trailerWeight = 35f },
            new Entry { item = RaceItemType.Confuse, leaderWeight = 0f, trailerWeight = 30f }
        };
    }

    /// <summary>
    /// 順位（1始まり）と、妨害できる相手がいるかどうかから1つ選びます。
    /// random01 は [0, 1) の乱数です。候補がなければ None を返します。
    /// </summary>
    public RaceItemType Roll(int racePosition, bool hasOpponent, float random01)
    {
        Entry[] source = entries != null && entries.Length > 0 ? entries : CreateDefaultEntries();
        bool isLeader = racePosition <= 1;
        float total = 0f;
        foreach (Entry entry in source) total += GetWeight(entry, isLeader, hasOpponent);
        if (total <= 0f) return RaceItemType.None;

        float pick = Mathf.Clamp01(random01) * total;
        RaceItemType last = RaceItemType.None;
        foreach (Entry entry in source)
        {
            float weight = GetWeight(entry, isLeader, hasOpponent);
            if (weight <= 0f) continue;
            last = entry.item;
            if (pick < weight) return entry.item;
            pick -= weight;
        }

        return last;
    }

    private static float GetWeight(Entry entry, bool isLeader, bool hasOpponent)
    {
        if (entry.item == RaceItemType.None) return 0f;
        // 相手がゴール済みなどで妨害できないときは、相手に作用するアイテムを候補から外します。
        if (!hasOpponent && entry.item == RaceItemType.Confuse) return 0f;
        return Mathf.Max(0f, isLeader ? entry.leaderWeight : entry.trailerWeight);
    }
}
