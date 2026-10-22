using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person camera that orbits a target with the mouse.
/// Hold the right mouse button to look around, scroll to zoom.
/// PlayerMovement reads this transform, so WASD follows wherever the camera points.
/// </summary>
public class OrbitFollowCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    [Tooltip("Height above the target's pivot that the camera orbits around.")]
    public float pivotHeight = 0.7f;
    public Vector3 pivotOffset;
    public bool deriveYawFromPlacement = true;

    [Header("Orbit")]
    public float yaw;
    public float pitch = 15f;
    public float minPitch = -20f;
    public float maxPitch = 70f;

    [Header("Mouse")]
    [Tooltip("Degrees per pixel of mouse movement.")]
    public float sensitivity = 0.15f;
    [Tooltip("Off = always look; on = only while the right mouse button is held.")]
    public bool requireRightMouseButton = true;
    public bool invertY;

    [Header("Zoom")]
    public float distance = 3.2f;
    public float minDistance = 1.2f;
    public float maxDistance = 8f;
    public float zoomSpeed = 0.3f;

    [Header("Smoothing")]
    [Tooltip("0 = snap, higher = softer. Applies to position only, never to the mouse.")]
    public float followSmoothTime = 0.08f;

    Vector3 _velocity;

    public void RestoreView(float initialYaw, float initialPitch, float initialDistance)
    {
        yaw = initialYaw;
        pitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        distance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
        _velocity = Vector3.zero;
        if (target == null) return;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = PivotPoint() - transform.forward * distance;
    }

    void Start()
    {
        // keep whatever angle the camera was placed at in the scene
        if (target != null && deriveYawFromPlacement)
        {
            Vector3 flat = transform.position - PivotPoint();
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
                yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + 180f;
        }
    }

    Vector3 PivotPoint() => target.position + Vector3.up * pivotHeight + pivotOffset;

    void LateUpdate()
    {
        if (target == null) return;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (!requireRightMouseButton || mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * sensitivity;
                pitch += (invertY ? d.y : -d.y) * sensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - scroll * zoomSpeed * 0.01f, minDistance, maxDistance);
        }

        Vector3 pivot = PivotPoint();
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 wanted = pivot - rot * Vector3.forward * distance;

        transform.position = followSmoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, wanted, ref _velocity, followSmoothTime)
            : wanted;
        transform.rotation = rot;
    }
}
