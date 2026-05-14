using UnityEngine;
using System.Collections.Generic;

public class LapManager : MonoBehaviour
{
    [System.Serializable]
    public class CarTimeData
    {
        public string carName;
        public int lapCount;
        public float currentLapTime = 0;
        public float bestLapTime = float.MaxValue;
    }

    //Rigidbodyがキー
    private Dictionary<Rigidbody, CarTimeData> carDataMap = new Dictionary<Rigidbody, CarTimeData>();


    // Update is called once per frame
    void Update()
    {
        foreach (var data in carDataMap.Values)
        {
            data.currentLapTime += Time.deltaTime;            
        }
    }
    public void OnCarPassGoal(Rigidbody rb)
    {
        //初めて
        if (!carDataMap.ContainsKey(rb))
        {
            carDataMap[rb] = new CarTimeData { carName = rb.name };
            Debug.Log($"{rb.name} joined");
            return;
        }
        //二回目以降
        CarTimeData data = carDataMap[rb];
        data.lapCount++;

        if (data.currentLapTime < data.bestLapTime)
        {
            data.bestLapTime = data.currentLapTime;

        }
        Debug.Log($"{data.carName} : Lap {data.lapCount} | Time: {data.currentLapTime:F2}s | Best: {data.bestLapTime:F2}s");

        data.currentLapTime = 0;
    }
}
