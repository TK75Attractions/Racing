using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editor palette of reusable course prop prefabs.</summary>
[CreateAssetMenu(fileName = "CoursePlacementCatalog", menuName = "Racing/Course Placement Catalog")]
public sealed class CourseLightPlacementCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;

        public Entry() { }
        public Entry(string displayName, GameObject prefab)
        {
            this.displayName = displayName;
            this.prefab = prefab;
        }

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? (prefab != null ? prefab.name : "未設定")
            : displayName;
        public GameObject Prefab => prefab;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    public IReadOnlyList<Entry> Entries => entries;

    public bool AddIfMissing(GameObject prefab, string displayName)
    {
        if (prefab == null || entries.Exists(entry => entry != null && entry.Prefab == prefab))
            return false;

        entries.Add(new Entry(displayName, prefab));
        return true;
    }
}
