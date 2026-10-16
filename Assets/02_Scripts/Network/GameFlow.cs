using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public enum GamePhase : byte
{
    /// <summary>접속만 한 상태. 다들 연못가에 모여 있습니다.</summary>
    Lobby,
    /// <summary>본진을 배정받고 게임이 돌아가는 중.</summary>
    Playing,
}

/// <summary>
/// 대기 → 게임 시작 흐름을 관리합니다.
///
/// 게임 시작을 누르면 서버가 **본진(4분면)을 무작위로 섞어서** 배정하고
/// 각 플레이어를 자기 스폰 지점으로 보냅니다.
/// 기획 10장에 따라 배정은 전부 서버가 결정합니다.
/// </summary>
public class GameFlow : NetworkBehaviour
{
    public static GameFlow Instance { get; private set; }

    [Tooltip("게임을 시작할 수 있는 최소 인원. 기획 2~4인이지만 2명부터 시작 가능합니다.")]
    [SerializeField] int minPlayers = 2;

    [Tooltip("비워두면 씬에서 FarmQuadrant 를 전부 찾습니다.")]
    [SerializeField] List<FarmQuadrant> quadrants = new List<FarmQuadrant>();

    [Header("대기 중 위치")]
    [Tooltip("대기 중에는 중앙 연못 주변에 둥글게 세웁니다.")]
    [SerializeField] float lobbyRadius = 4.2f;

    /// <summary>모두가 읽는 현재 단계.</summary>
    public readonly NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(
        GamePhase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int MinPlayers => minPlayers;
    public int PlayerCount =>
        NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ConnectedClientsIds.Count : 0;

    /// <summary>호스트가 지금 시작 버튼을 누를 수 있는지.</summary>
    public bool CanStartGame =>
        IsServer && Phase.Value == GamePhase.Lobby && PlayerCount >= minPlayers;

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (quadrants.Count == 0)
        {
            quadrants.AddRange(FindObjectsByType<FarmQuadrant>(FindObjectsInactive.Include));
            quadrants.Sort((a, b) => a.index.CompareTo(b.index));
        }

        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // 대기
    // ------------------------------------------------------------------

    void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        if (Phase.Value == GamePhase.Lobby) PlaceInLobby(clientId);
        else AssignFreeQuadrant(clientId);     // 진행 중 난입
    }

    void OnClientDisconnected(ulong clientId)
    {
        foreach (var q in quadrants)
            if (q != null && q.ownerClientId == (long)clientId) q.ownerClientId = -1;
    }

    /// <summary>대기 중인 플레이어를 연못 주변에 둥글게 세웁니다.</summary>
    void PlaceInLobby(ulong clientId)
    {
        var ids = NetworkManager.ConnectedClientsIds.ToList();
        int slot = Mathf.Max(0, ids.IndexOf(clientId));
        int slots = Mathf.Max(quadrants.Count, 1);          // 보통 4자리
        float angle = slot / (float)slots * Mathf.PI * 2f;

        Vector3 pos = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * lobbyRadius;
        Quaternion rot = Quaternion.LookRotation(-pos.normalized, Vector3.up);   // 연못을 바라보게
        Teleport(clientId, pos, rot);
    }

    // ------------------------------------------------------------------
    // 시작
    // ------------------------------------------------------------------

    /// <summary>호스트 전용. 본진을 무작위로 배정하고 게임을 시작합니다.</summary>
    public void StartGame()
    {
        if (!CanStartGame) return;

        // 본진 랜덤 배정: 구역 순서를 섞어서 접속 순서와 무관하게 만듭니다.
        var order = Enumerable.Range(0, quadrants.Count).ToList();
        Shuffle(order);

        foreach (var q in quadrants) if (q != null) q.ownerClientId = -1;

        var ids = NetworkManager.ConnectedClientsIds.ToList();
        for (int i = 0; i < ids.Count && i < order.Count; i++)
        {
            var q = quadrants[order[i]];
            if (q == null) continue;
            q.ownerClientId = (long)ids[i];
            SendToQuadrant(ids[i], q);
        }

        Phase.Value = GamePhase.Playing;
        Debug.Log($"[GameFlow] 게임 시작. {ids.Count}명, 본진 배정 = " +
                  string.Join(", ", ids.Select((id, i) => $"client{id}->Q{order[i]}")));
    }

    /// <summary>게임 중에 들어온 플레이어에게 빈 본진을 줍니다.</summary>
    void AssignFreeQuadrant(ulong clientId)
    {
        var free = quadrants.FirstOrDefault(q => q != null && q.ownerClientId < 0);
        if (free == null) { PlaceInLobby(clientId); return; }
        free.ownerClientId = (long)clientId;
        SendToQuadrant(clientId, free);
    }

    void SendToQuadrant(ulong clientId, FarmQuadrant q)
    {
        var np = GetPlayer(clientId);
        if (np != null) np.QuadrantIndex.Value = q.index;

        Transform sp = q.spawnPoint != null ? q.spawnPoint : q.transform;
        Teleport(clientId, sp.position, sp.rotation);
    }

    void Teleport(ulong clientId, Vector3 pos, Quaternion rot)
    {
        var np = GetPlayer(clientId);
        if (np != null) np.TeleportRpc(pos, rot);
    }

    NetworkPlayer GetPlayer(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var c)) return null;
        return c.PlayerObject != null ? c.PlayerObject.GetComponent<NetworkPlayer>() : null;
    }

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
