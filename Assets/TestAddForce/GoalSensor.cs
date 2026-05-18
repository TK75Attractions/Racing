using UnityEngine;

public class GoalSensor : MonoBehaviour
{
    [SerializeField] private LapManager lapManager;

    
    public int lapCount = 0;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void OnTriggerEnter(Collider other)
    {
        
        lapCount++;
        Debug.Log($"Lap {lapCount} passed");
        lapManager.OnCarPassGoal(other.attachedRigidbody);
    }
}
