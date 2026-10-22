using UnityEngine;
namespace LowPolyFarm.BlockTheWell {
/// <summary>
/// 우물 1개의 "막힘" 판정.
/// 바위가 우물 중심 근처(수평 허용 오차 이내)에 멈춰서 일정 시간 머물면 Blocked = true.
/// 캐릭터는 판정 대상이 아니므로 플레이어가 우물 위에 서 있어도 막힌 것으로 치지 않는다.
/// </summary>
public sealed class WellBlockZone : MonoBehaviour {
    public float horizontalTolerance = .22f;   // 우물 중심과 바위 중심 사이 허용 거리(XZ 평면, m)
    public float stableSeconds = .5f;          // 이 시간 이상 머물러야 막힌 것으로 인정 (스쳐 지나가는 경우 제외)
    public bool Blocked { get; private set; }

    WellStage stage;
    WellRock candidate;      // 지금 우물 위에 있는 바위 후보
    float stableTime;        // candidate가 연속으로 머문 시간

    void Awake() { stage = GetComponentInParent<WellStage>(); }

    /// <summary>바위가 이동을 마친 상태로 우물 중심 범위 안에 있는지.</summary>
    public bool Accepts(WellRock rock) {
        if (rock == null || rock.Moving) return false;           // 밀리는 중인 바위는 제외
        var delta = rock.transform.position - transform.position;
        return new Vector2(delta.x, delta.z).magnitude <= horizontalTolerance;  // 높이는 무시하고 평면 거리만 비교
    }

    void Update() {
        // 스테이지의 바위 중 우물 위에 있는 첫 번째 바위를 찾는다.
        WellRock next = null;
        foreach (var rock in stage.rocks) if (Accepts(rock)) { next = rock; break; }

        // 후보가 바뀌면(다른 바위가 오거나 빠져나가면) 시간을 처음부터 다시 잰다.
        if (next != candidate) { candidate = next; stableTime = 0; Blocked = false; }
        if (candidate == null) { Blocked = false; stableTime = 0; return; }

        // 같은 바위가 stableSeconds 이상 머물면 막힘. 바위를 다시 밀어내면 위에서 자동으로 해제된다.
        stableTime += Time.deltaTime;
        Blocked = stableTime >= stableSeconds;
    }

    /// <summary>스테이지 재시작 시 판정 상태 초기화.</summary>
    public void ResetZone() { Blocked = false; candidate = null; stableTime = 0; }
}
}
