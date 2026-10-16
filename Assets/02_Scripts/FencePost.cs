using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 울타리 기둥 1개. 자기 자신이 어떤 기둥들과 연결돼 있는지만 들고 있습니다.
/// 실제 연결/해제 판단은 전부 FenceSystem 이 합니다.
/// </summary>
public class FencePost : MonoBehaviour
{
    /// <summary>이 기둥이 서 있는 그리드 칸 (x, z).</summary>
    public Vector2Int Cell { get; set; }

    /// <summary>
    /// 이 기둥에 연결된 울타리들.
    /// "연결 방향" 1개 = 이 리스트의 항목 1개 = 가로대 2개(상단·하단) 입니다.
    /// </summary>
    public readonly List<FenceLink> Links = new List<FenceLink>();

    /// <summary>기둥당 최대 연결 방향 수. 규칙상 2방향(= 가로대 4개)까지.</summary>
    public const int MaxDirections = 2;

    /// <summary>아직 연결할 자리가 남아 있는지.</summary>
    public bool HasFreeDirection => Links.Count < MaxDirections;

    /// <summary>이미 이 기둥과 연결돼 있는지 (같은 쌍을 두 번 잇지 않기 위해).</summary>
    public bool IsLinkedTo(FencePost other)
    {
        for (int i = 0; i < Links.Count; i++)
            if (Links[i].Other(this) == other) return true;
        return false;
    }

    /// <summary>발 밑(바닥) 기준 월드 위치. 가로대 높이 계산의 기준점입니다.</summary>
    public Vector3 BasePosition => transform.position;
}

/// <summary>
/// 기둥 두 개를 잇는 울타리 한 칸.
/// 가로대(상단·하단) 2개를 한 묶음으로 들고 있다가, 연결이 끊기면 같이 지웁니다.
/// </summary>
public class FenceLink
{
    public FencePost A;
    public FencePost B;
    public GameObject TopRail;
    public GameObject BottomRail;

    /// <summary>이 연결에서 반대쪽 기둥을 돌려줍니다.</summary>
    public FencePost Other(FencePost from) => from == A ? B : A;
}
