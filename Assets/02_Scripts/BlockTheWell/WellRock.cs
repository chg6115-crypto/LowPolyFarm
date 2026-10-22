using UnityEngine;
namespace LowPolyFarm.BlockTheWell {
/// <summary>
/// 밀 수 있는 바위. 소코반 규칙대로 상하좌우 "한 칸씩"만 이동한다.
/// 물리로 굴러가면 격자 중심에서 벗어나 퍼즐이 깨지므로, Rigidbody를 Kinematic으로 두고
/// 목적지 칸까지 일정 속도로 직접 옮긴다. (캐릭터는 격자에 묶이지 않고 자유롭게 움직인다)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class WellRock : MonoBehaviour {
    public Rigidbody Body { get; private set; }
    public bool Locked => false;                        // 예전 '우물에 들어가면 고정' 방식의 흔적. 지금은 항상 다시 밀 수 있다.
    public bool Moving { get; private set; }            // 한 칸 이동 중인지
    public Vector3 Destination { get; private set; }    // 이동 목표 칸(이동 중에는 이 칸을 예약한 것으로 취급)
    public float moveSpeed = 1f;                        // m/s. 한 칸(1m)을 약 1초에 밀어 밀기 애니메이션과 템포를 맞춤

    Vector3 initialPosition; Quaternion initialRotation; WellStage stage;

    void Awake() {
        Body = GetComponent<Rigidbody>();
        initialPosition = transform.position;            // R 재시작용 초기 위치 저장
        initialRotation = transform.rotation;
        Destination = initialPosition;
        stage = GetComponentInParent<WellStage>();
        Body.isKinematic = true;                         // 중력·충돌 반발로 움직이지 않게 함
        Body.useGravity = false;
    }

    /// <summary>
    /// direction(±X 또는 ±Z, 크기 1) 방향으로 한 칸 밀기를 시도한다.
    /// 다음 칸이 바닥이고, 벽이 아니고, 다른 바위가 없거나 예약되지 않았을 때만 성공.
    /// </summary>
    public bool TryPush(Vector3 direction) {
        if (Moving || stage == null) return false;       // 이동 중에는 추가 입력 무시
        var next = Destination + direction;

        // 1) 다음 칸에 바닥 타일이 있어야 한다 (맵 밖으로 못 나감)
        bool floor = false;
        foreach (Transform tile in stage.transform.Find("Tiles"))
            if (SameCell(tile.position, next)) { floor = true; break; }
        if (!floor) return false;

        // 2) 검정 고정 바위(벽)가 있으면 막힘
        foreach (Transform wall in stage.transform.Find("Walls"))
            if (SameCell(wall.position, next)) return false;

        // 3) 다른 바위가 그 칸에 있거나, 그 칸으로 이동 중(예약)이면 막힘 → 바위 두 개를 한 번에 밀 수 없음
        foreach (var rock in stage.rocks)
            if (rock != this && (SameCell(rock.Destination, next) || SameCell(rock.transform.position, next)))
                return false;

        Destination = next; Moving = true;
        return true;
    }

    /// <summary>두 위치가 같은 격자 칸인지 (칸 크기 1m, 높이는 무시).</summary>
    static bool SameCell(Vector3 a, Vector3 b) => Mathf.Abs(a.x - b.x) < .49f && Mathf.Abs(a.z - b.z) < .49f;

    void FixedUpdate() {
        if (!Moving) return;
        // Kinematic Rigidbody는 MovePosition으로 옮겨야 캐릭터와의 충돌이 자연스럽게 처리된다.
        var next = Vector3.MoveTowards(Body.position, Destination, moveSpeed * Time.fixedDeltaTime);
        Body.MovePosition(next);
        if ((next - Destination).sqrMagnitude < .000001f) Moving = false;   // 도착
    }

    /// <summary>스테이지 재시작 시 처음 위치로 복귀.</summary>
    public void ResetRock() {
        Moving = false; Destination = initialPosition;
        Body.isKinematic = true;
        Body.position = initialPosition; Body.rotation = initialRotation;
    }
}
}
