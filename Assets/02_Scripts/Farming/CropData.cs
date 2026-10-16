using UnityEngine;

/// <summary>
/// 작물 한 종류의 수치. 기획 4.2 표를 그대로 옮긴 것이고,
/// 밸런스는 전부 이 애셋에서 조정합니다 (기획 14장 규칙).
/// </summary>
[CreateAssetMenu(fileName = "Crop_", menuName = "LowPolyFarm/Crop Data")]
public class CropData : ScriptableObject
{
    [Header("표시")]
    public string displayName = "작물";

    [Tooltip("밭 위에 올라올 모델.")]
    public GameObject prefab;

    [Tooltip("모델이 1m 크기라 밭 한 칸에 올리려면 줄여야 합니다.")]
    public float modelScale = 0.35f;

    [Header("경제 (기획 4.2)")]
    [Tooltip("창고에 넣었을 때 받는 돈.")]
    public int sellPrice = 10;

    [Tooltip("상점 구매가. 0이면 상점에서 안 팝니다(시작 지급 작물).")]
    public int buyPrice = 0;

    [Header("성장")]
    [Tooltip("물을 준 뒤 다 자랄 때까지 걸리는 시간(초).")]
    public float growSeconds = 20f;

    [Tooltip("수확 배수 최소. 기획 4장 2~5배.")]
    [Range(1, 10)] public int minYield = 2;

    [Tooltip("수확 배수 최대.")]
    [Range(1, 10)] public int maxYield = 3;

    /// <summary>이 작물의 이번 수확 배수를 뽑습니다.</summary>
    public int RollYield() => Random.Range(minYield, maxYield + 1);
}
