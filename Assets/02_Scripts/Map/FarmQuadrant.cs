using UnityEngine;

/// <summary>
/// 플레이어 1명이 쓰는 4분면 하나. 이후 단계(농사·서리·개 AI)에서
/// "여기가 누구 땅인가"를 물어볼 때 쓰는 기준점입니다.
/// </summary>
public class FarmQuadrant : MonoBehaviour
{
    [Tooltip("0~3. 접속 순서대로 플레이어에게 배정됩니다.")]
    public int index;

    [Tooltip("이 구역을 쓰는 플레이어의 ClientId. 비어 있으면 -1.")]
    public long ownerClientId = -1;

    [Header("구역 내 주요 지점")]
    public Transform spawnPoint;
    public Transform warehouse;
    public Transform well;

    [Header("범위 (가로세로 m)")]
    public float size = 14f;

    /// <summary>이 지점이 내 구역 안인지. 서리/침입 판정에 씁니다.</summary>
    public bool Contains(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        float half = size * 0.5f;
        return Mathf.Abs(local.x) <= half && Mathf.Abs(local.z) <= half;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.05f,
                            new Vector3(size, 0.1f, size));
    }
}
