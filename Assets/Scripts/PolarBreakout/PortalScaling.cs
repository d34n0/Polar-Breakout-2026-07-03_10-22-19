using UnityEngine;

public class PortalScaling : MonoBehaviour
{
    [SerializeField] private float growDuration = 2.0f; // Time in seconds to reach full size
    private Vector3 targetScale;
    private float elapsedTime = 0f;

    void Start()
    {
        // Save the intended final size, then shrink the system to zero
        targetScale = transform.localScale;
        transform.localScale = Vector3.zero;
    }

    void Update()
    {
        if (elapsedTime < growDuration)
        {
            elapsedTime += Time.deltaTime;
            // Smoothly transition the entire system scale from 0 to 100%
            transform.localScale = Vector3.Lerp(Vector3.zero, targetScale, elapsedTime / growDuration);
        }
    }
}