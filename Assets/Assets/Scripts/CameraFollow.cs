using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField, Min(0.01f)]
    private float followSpeed = 5f;

    private Transform target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target == null)
            return;

        // Immediately position the camera when a target is assigned.
        // This prevents the camera travelling across the map
        // when the local player first spawns.
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 targetPosition = GetTargetPosition();

        // Exponential smoothing provides consistent behaviour
        // across different frame rates.
        float smoothingFactor =
            1f - Mathf.Exp(-followSpeed * Time.deltaTime);

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothingFactor
        );
    }

    private Vector3 GetTargetPosition()
    {
        return new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );
    }

    private void SnapToTarget()
    {
        transform.position = GetTargetPosition();
    }
}
