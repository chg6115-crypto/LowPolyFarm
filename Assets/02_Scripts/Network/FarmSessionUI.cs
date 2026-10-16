using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 1단계용 임시 접속 UI 입니다. 나중에 제대로 된 로비 UI 가 생기면 지웁니다.
/// (기획 13.1 "세션 생성·참가 코드 UI(임시)")
/// </summary>
[RequireComponent(typeof(FarmSession))]
public class FarmSessionUI : MonoBehaviour
{
    FarmSession _session;
    string _codeInput = "";

    void Awake() => _session = GetComponent<FarmSession>();

    void OnGUI()
    {
        const float w = 320f;
        GUILayout.BeginArea(new Rect(12f, 12f, w, 320f), GUI.skin.box);

        var nm = NetworkManager.Singleton;
        bool connected = nm != null && nm.IsListening;

        GUILayout.Label("<b>LowPolyFarm</b>",
                        new GUIStyle(GUI.skin.label) { richText = true });

        if (!connected)
        {
            GUILayout.Space(4f);
            GUILayout.Label("로컬 테스트 (클라우드 불필요)",
                            new GUIStyle(GUI.skin.label) { fontSize = 11 });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host")) _session.HostLocal();
            if (GUILayout.Button("Client")) _session.JoinLocal();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("온라인 (Unity Cloud 연결 필요)",
                            new GUIStyle(GUI.skin.label) { fontSize = 11 });

            GUI.enabled = !_session.Busy;
            if (GUILayout.Button("방 만들기")) _session.HostOnline();

            GUILayout.BeginHorizontal();
            _codeInput = GUILayout.TextField(_codeInput, 10);
            if (GUILayout.Button("참가", GUILayout.Width(60f))) _session.JoinOnline(_codeInput);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }
        else
        {
            GUILayout.Space(4f);
            GUILayout.Label(nm.IsHost ? "역할: 호스트" : "역할: 클라이언트");

            var flow = GameFlow.Instance;
            int count = nm.ConnectedClientsIds.Count;
            int need = flow != null ? flow.MinPlayers : 2;

            GUILayout.Label($"접속 인원: {count} / 4   (시작 최소 {need}명)");

            string code = _session.JoinCode;
            if (!string.IsNullOrEmpty(code))
            {
                GUILayout.Label("참가 코드");
                GUILayout.TextField(code);   // 드래그해서 복사할 수 있게 TextField
            }

            GUILayout.Space(6f);

            if (flow != null && flow.Phase.Value == GamePhase.Lobby)
            {
                if (nm.IsHost)
                {
                    // 인원이 모자라면 눌리지 않게만 하고 버튼 자체는 보여줍니다.
                    GUI.enabled = flow.CanStartGame;
                    if (GUILayout.Button("게임 시작", GUILayout.Height(30f)))
                        flow.StartGame();
                    GUI.enabled = true;

                    if (!flow.CanStartGame)
                        GUILayout.Label($"{need - count}명 더 필요합니다.",
                                        new GUIStyle(GUI.skin.label) { fontSize = 11 });
                }
                else
                {
                    GUILayout.Label("호스트가 시작하기를 기다리는 중...",
                                    new GUIStyle(GUI.skin.label) { fontSize = 11 });
                }
            }
            else if (flow != null)
            {
                GUILayout.Label("<b>게임 진행 중</b>",
                                new GUIStyle(GUI.skin.label) { richText = true });
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("나가기")) _session.Leave();
        }

        GUILayout.Space(8f);
        GUILayout.Label(_session.Status, new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true });

        GUILayout.EndArea();
    }
}
