#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class Startup
{
    static Startup()    
    {
        EditorPrefs.SetInt("showCounts_sportcarcgbr10", EditorPrefs.GetInt("showCounts_sportcarcgbr10") + 1);

        if (EditorPrefs.GetInt("showCounts_sportcarcgbr10") == 1)       
        {
            int randomLink = Random.Range(0, 2);

            if(randomLink == 1)            
                Application.OpenURL("https://assetstore.unity.com/publishers/23606");
            else
                Application.OpenURL("https://assetstore.unity.com/packages/slug/358482");
        }
    }     
}
#endif
