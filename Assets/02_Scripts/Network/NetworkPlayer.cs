using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 스폰된 플레이어 1명. "이게 내 캐릭터인가"를 판단해서
/// 입력/카메라를 로컬 플레이어에게만 붙여줍니다.
///
/// 이동 자체는 NetworkTransform(AuthorityMode = Owner)이 동기화하고,
/// 애니메이션은 NetworkAnimator 가 동기화합니다. 이 스크립트는 그 둘을 건드리지 않습니다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : NetworkBehaviour
{
    [Header("내 캐릭터일 때만 켤 컴포넌트")]
    [Tooltip("PlayerMovement, CharacterMotionDebug 처럼 입력을 받는 것들을 넣습니다. " +
             "남의 캐릭터에서 켜져 있으면 내 키 입력으로 남이 움직여 버립니다.")]
    [SerializeField] MonoBehaviour[] localOnlyComponents;

    [Header("머리 위 이름표 (없으면 비워두세요)")]
    [SerializeField] GameObject nameTagRoot;

    /// <summary>배정받은 본진 번호. 서버가 정하고 모두가 읽습니다. 미배정이면 -1.</summary>
    public readonly NetworkVariable<int> QuadrantIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    PlayerMovement _movement;

    /// <summary>
    /// 서버가 플레이어를 옮길 때 쓰는 RPC 입니다.
    /// NetworkTransform 이 Owner 권한이라 서버가 트랜스폼을 직접 바꿔도
    /// 소유자 쪽 값에 덮어써집니다. 그래서 소유자에게 시켜야 합니다.
    /// </summary>
    [Rpc(SendTo.Owner)]
    public void TeleportRpc(Vector3 position, Quaternion rotation)
    {
        // CharacterController 가 켜진 채로는 순간이동이 씹힙니다.
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        // 보간이 켜져 있으면 맵을 가로질러 미끄러지듯 보이므로 즉시 반영시킵니다.
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt != null) nt.Teleport(position, rotation, transform.localScale);

        if (cc != null) cc.enabled = true;
    }

    public override void OnNetworkSpawn()
    {
        _movement = GetComponent<PlayerMovement>();

        bool mine = IsOwner;

        // 남의 캐릭터에서는 입력 스크립트를 꺼 둡니다.
        foreach (var c in localOnlyComponents)
            if (c != null) c.enabled = mine;

        // 내 이름표는 내 화면에 안 보이게 (나중에 이름표를 붙이면 쓰입니다)
        if (nameTagRoot != null) nameTagRoot.SetActive(!mine);

        if (mine) BindLocalCamera();

        gameObject.name = mine
            ? $"Player_{OwnerClientId} (me)"
            : $"Player_{OwnerClientId}";
    }

    /// <summary>씬에 있는 추적 카메라를 내 캐릭터에 붙입니다.</summary>
    void BindLocalCamera()
    {
        var cam = FindAnyObjectByType<OrbitFollowCamera>();
        if (cam != null)
        {
            cam.target = transform;
            // 카메라 기준 이동이라 PlayerMovement 에도 같은 카메라를 알려줘야 합니다.
            if (_movement != null) _movement.cameraTransform = cam.transform;
        }
        else if (Camera.main != null && _movement != null)
        {
            _movement.cameraTransform = Camera.main.transform;
        }

        // 울타리 설치 도구도 내 카메라를 쓰도록
        var placer = FindAnyObjectByType<FencePlacer>();
        if (placer != null && Camera.main != null) placer.cam = Camera.main;
    }
}
