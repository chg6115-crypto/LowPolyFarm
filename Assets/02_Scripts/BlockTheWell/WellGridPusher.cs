using UnityEngine;
using UnityEngine.InputSystem;
namespace LowPolyFarm.BlockTheWell {
/// <summary>
/// 캐릭터가 바위에 부딪히면 "어느 방향으로 한 칸 밀지"를 결정해 WellRock.TryPush를 호출한다.
/// 퍼즐 씬의 Player에만 붙이며(WellGame.Start에서 추가), 공용 PlayerMovement 코드는 바꾸지 않는다.
/// </summary>
[DefaultExecutionOrder(-100)]   // PlayerMovement보다 먼저 실행해 밀기 애니메이션 요청이 같은 프레임에 반영되게 함
public sealed class WellGridPusher : MonoBehaviour {
    PlayerMovement movement;
    WellRock pushedRock;        // 마지막으로 민 바위 (이동이 끝날 때까지 Push 애니메이션 유지용)

    void Awake() { movement = GetComponent<PlayerMovement>(); }

    void Update() {
        // 바위가 한 칸 이동하는 동안 캐릭터가 가까이(1.5m 이내) 붙어 있으면 Push 애니메이션을 계속 재생
        if (!movement.enabled || pushedRock == null || !pushedRock.Moving) return;
        var delta = transform.position - pushedRock.transform.position; delta.y = 0;
        if (delta.sqrMagnitude < 2.25f) movement.NotifyPushAnimation();
    }

    // CharacterController가 다른 콜라이더에 닿을 때마다 호출된다.
    void OnControllerColliderHit(ControllerColliderHit hit) {
        if (!movement.enabled || Keyboard.current == null) return;
        var rock = hit.collider.GetComponentInParent<WellRock>(); if (rock == null) return;   // 바위가 아니면 무시

        // 1) 현재 WASD 입력 방향 (New Input System)
        var k = Keyboard.current;
        float x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
        float z = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
        if (x == 0 && z == 0) return;                   // 그냥 닿아 있기만 하면 밀지 않음
        var input = new Vector3(x, 0, z);
        if (movement.cameraRelative && movement.cameraTransform != null) {
            // 카메라 기준 이동이므로 입력도 카메라가 보는 방향으로 변환
            var forward = Vector3.ProjectOnPlane(movement.cameraTransform.forward, Vector3.up).normalized;
            input = forward * z + Vector3.Cross(Vector3.up, forward) * x;
        }

        // 2) 부딪힌 면의 법선으로 밀 방향을 4방향 중 하나로 정한다 (윗면/아랫면 접촉은 제외)
        var normal = hit.normal; if (Mathf.Abs(normal.y) > .5f) return;
        var direction = Mathf.Abs(normal.x) > Mathf.Abs(normal.z)
            ? new Vector3(-Mathf.Sign(normal.x), 0, 0)
            : new Vector3(0, 0, -Mathf.Sign(normal.z));

        // 3) 입력 방향과 밀 방향이 거의 같을 때만 민다 (비스듬히 스치면 밀리지 않음)
        if (Vector3.Dot(input.normalized, direction) < .5f) return;

        // 4) 캐릭터가 바위 면의 중앙 쪽(옆으로 0.4m 이내)에서, 바위 뒤편에 있을 때만 인정
        var offset = transform.position - rock.transform.position;
        float across = direction.x != 0 ? Mathf.Abs(offset.z) : Mathf.Abs(offset.x);
        if (across > .4f || Vector3.Dot(offset, direction) >= 0) return;

        // 5) 한 칸 밀기 성공(또는 이미 밀리는 중)이면 Push 애니메이션 요청
        if (rock.TryPush(direction) || rock.Moving) { pushedRock = rock; movement.NotifyPushAnimation(); }
    }
}
}
