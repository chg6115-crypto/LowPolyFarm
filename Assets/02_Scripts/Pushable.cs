using UnityEngine;

/// <summary>
/// Marks a Rigidbody the player can shove by walking into it.
/// Rotation is frozen on Awake so props slide instead of tipping over.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Pushable : MonoBehaviour
{
    [Tooltip("Fraction of the player's speed this object moves at. Below 1 it feels heavy.")]
    [Range(0.1f, 1f)]
    public float speedFactor = 0.9f;

    [Tooltip("Keep rotation locked so the object never topples.")]
    public bool lockRotation = true;

    Rigidbody _rb;
    public Rigidbody Body => _rb != null ? _rb : (_rb = GetComponent<Rigidbody>());

    void Awake()
    {
        if (lockRotation)
            Body.constraints |= RigidbodyConstraints.FreezeRotation;
    }
}
