using UnityEngine;

public class GoalSensor : MonoBehaviour
{
    public int lapCount = 0;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void OnTriggerEnter(Collider other)
    {
        if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"))
        {
            lapCount++;
            Debug.Log($"Lap {lapCount} passed");
        }
    }
}
