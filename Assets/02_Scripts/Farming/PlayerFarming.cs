using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>들고 있는 도구 (기획 11장 1~4 전환).</summary>
public enum FarmTool : byte { Hoe = 0, WateringCan = 1 }

/// <summary>
/// 플레이어의 농사 행동 (기획 4장 / 11장).
///
///   1 / 2      : 괭이 / 물뿌리개 전환
///   좌클릭     : 들고 있는 도구 사용 (괭이질 / 물주기)
///   E          : 상황에 맞는 행동 (우물·연못에서 물 긷기, 작물 묻기, 수확)
///
/// 입력은 소유자만 받고, 실제 판정은 전부 서버가 합니다 (기획 10장).
/// 실패하면 왜 안 됐는지 화면에 띄웁니다.
/// </summary>
public class PlayerFarming : NetworkBehaviour
{
    [Header("조작 범위")]
    [Tooltip("캐릭터 앞 이 거리의 칸을 대상으로 삼습니다.")]
    [SerializeField] float reach = 0.9f;
    [Tooltip("우물·연못에서 물을 뜰 수 있는 거리.")]
    [SerializeField] float waterSourceRange = 2.2f;

    [Header("소지")]
    [Tooltip("손으로 들 수 있는 작물 개수. 기획 6.3 최대 2개.")]
    [SerializeField] int carryCapacity = 2;
    [Tooltip("물을 한 번 길으면 줄 수 있는 칸 수. 기획 4.1 기본 3칸.")]
    [SerializeField] int waterCapacity = 3;

    [Header("시작 지급")]
    [SerializeField] int startingCrops = 2;
    [SerializeField] byte startingCropId = 0;

    [Header("손에 드는 도구 모델 (Veresen 프리팹)")]
    [SerializeField] GameObject hoeModel;
    [SerializeField] GameObject wateringCanModel;
    [Tooltip("도구를 붙일 본 이름.")]
    [SerializeField] string handBone = "RightHand";
    [SerializeField] Vector3 toolLocalPos = new Vector3(0f, 0.02f, 0.02f);
    [SerializeField] Vector3 toolLocalEuler = new Vector3(0f, 0f, -75f);
    [SerializeField] float toolScale = 0.35f;

    [Header("조준 표시")]
    [SerializeField] Color aimOkColor = new Color(0.4f, 1f, 0.5f, 0.5f);
    [SerializeField] Color aimBadColor = new Color(1f, 0.4f, 0.4f, 0.5f);

    // ---- 동기화 상태 ----
    public readonly NetworkVariable<int> Water = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    readonly NetworkList<byte> _carried = new NetworkList<byte>();

    public FarmTool Tool { get; private set; } = FarmTool.Hoe;
    public int CarriedCount => _carried.Count;

    GameObject _heldTool;
    GameObject _aimMarker;
    string _message = "";
    float _messageUntil;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            for (int i = 0; i < startingCrops; i++) _carried.Add(startingCropId);

        if (IsOwner) BuildAimMarker();
        EquipTool(Tool);
    }

    public override void OnNetworkDespawn()
    {
        if (_aimMarker != null) Destroy(_aimMarker);
        if (_heldTool != null) Destroy(_heldTool);
    }

    // ------------------------------------------------------------------
    // 입력 (소유자만)
    // ------------------------------------------------------------------

    void Update()
    {
        if (!IsOwner) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        // 울타리 건설 모드에서는 도구 사용을 막습니다 (기획 11장 참고).
        var placer = FindAnyObjectByType<FencePlacer>();
        bool building = placer != null && placer.buildMode;

        if (kb.digit1Key.wasPressedThisFrame) { Tool = FarmTool.Hoe; EquipTool(Tool); }
        if (kb.digit2Key.wasPressedThisFrame) { Tool = FarmTool.WateringCan; EquipTool(Tool); }

        UpdateAimMarker();

        if (kb.eKey.wasPressedThisFrame) InteractRpc(TargetCell());

        var mouse = Mouse.current;
        if (!building && mouse != null && mouse.leftButton.wasPressedThisFrame)
            UseToolRpc(TargetCell(), (byte)Tool);
    }

    /// <summary>캐릭터가 보고 있는 앞쪽 칸.</summary>
    Vector2Int TargetCell()
    {
        var grid = FarmGrid.Instance;
        if (grid == null) return Vector2Int.zero;
        return grid.WorldToCell(transform.position + transform.forward * reach);
    }

    // ------------------------------------------------------------------
    // 서버 처리
    // ------------------------------------------------------------------

    [Rpc(SendTo.Server)]
    void UseToolRpc(Vector2Int cell, byte tool)
    {
        var grid = FarmGrid.Instance;
        if (grid == null) { ResultRpc((byte)FarmResult.NoTile); return; }

        FarmResult r;
        switch ((FarmTool)tool)
        {
            case FarmTool.Hoe:
                r = grid.ServerTill(cell);
                break;

            case FarmTool.WateringCan:
                if (Water.Value <= 0) { ResultRpc((byte)FarmResult.NoWater); return; }
                r = grid.ServerWater(cell);
                if (r == FarmResult.Ok) Water.Value--;
                break;

            default:
                return;
        }
        ResultRpc((byte)r);
    }

    [Rpc(SendTo.Server)]
    void InteractRpc(Vector2Int cell)
    {
        var grid = FarmGrid.Instance;
        if (grid == null) return;

        // 1) 우물이나 연못 근처면 물을 긷습니다
        if (NearWaterSource())
        {
            Water.Value = waterCapacity;
            ResultRpc((byte)FarmResult.Ok);
            return;
        }

        if (!grid.TryGetTile(cell, out var tile)) { ResultRpc((byte)FarmResult.NoTile); return; }

        // 2) 다 자랐으면 수확
        if (tile.State == FarmTile.Grown)
        {
            if (_carried.Count >= carryCapacity) { ResultRpc((byte)FarmResult.HandsFull); return; }
            var r = grid.ServerHarvestOne(cell, out byte cropId);
            if (r == FarmResult.Ok) _carried.Add(cropId);
            ResultRpc((byte)r);
            return;
        }

        // 3) 갈린 땅 + 작물 보유 -> 묻기
        if (tile.State == FarmTile.Tilled)
        {
            if (_carried.Count == 0) { ResultRpc((byte)FarmResult.NoCrop); return; }
            byte crop = _carried[_carried.Count - 1];
            var r = grid.ServerPlant(cell, crop);
            if (r == FarmResult.Ok) _carried.RemoveAt(_carried.Count - 1);
            ResultRpc((byte)r);
            return;
        }

        ResultRpc((byte)FarmResult.NotGrown);
    }

    /// <summary>서버가 결과를 본인에게만 돌려줍니다.</summary>
    [Rpc(SendTo.Owner)]
    void ResultRpc(byte result)
    {
        _message = Describe((FarmResult)result);
        _messageUntil = Time.time + 2f;
    }

    static string Describe(FarmResult r) => r switch
    {
        FarmResult.Ok => "",
        FarmResult.NotFarmable => "여기는 밭을 만들 수 없습니다 (내 구역 안에서만)",
        FarmResult.AlreadyTilled => "이미 갈아둔 땅입니다",
        FarmResult.NoTile => "먼저 괭이질로 땅을 갈아야 합니다",
        FarmResult.NotTilled => "이미 뭔가 심겨 있습니다",
        FarmResult.AlreadyWatered => "이미 물을 줬습니다",
        FarmResult.NotPlanted => "물을 줄 작물이 없습니다",
        FarmResult.NotGrown => "아직 다 자라지 않았습니다",
        FarmResult.NoWater => "물이 없습니다. 우물에서 E 로 길어오세요",
        FarmResult.HandsFull => "손이 가득 찼습니다",
        FarmResult.NoCrop => "심을 작물이 없습니다",
        _ => r.ToString(),
    };

    /// <summary>우물이나 중앙 연못 근처인지 (기획 4.1).</summary>
    bool NearWaterSource()
    {
        foreach (var q in FindObjectsByType<FarmQuadrant>(FindObjectsInactive.Include))
        {
            if (q.well == null) continue;
            if (Vector3.Distance(transform.position, q.well.position) <= waterSourceRange)
                return true;
        }

        var pond = GameObject.Find("Pond");
        if (pond != null)
        {
            Vector3 d = transform.position - pond.transform.position;
            d.y = 0f;
            if (d.magnitude <= pond.transform.localScale.x * 0.5f + waterSourceRange) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // 손에 든 도구 / 조준 표시
    // ------------------------------------------------------------------

    void EquipTool(FarmTool tool)
    {
        if (_heldTool != null) Destroy(_heldTool);

        var prefab = tool == FarmTool.Hoe ? hoeModel : wateringCanModel;
        if (prefab == null) return;

        Transform hand = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == handBone) { hand = t; break; }
        if (hand == null) return;

        _heldTool = Instantiate(prefab);
        _heldTool.transform.SetParent(hand, false);         // 프리팹 로컬 회전 보존
        _heldTool.transform.localPosition = toolLocalPos;
        _heldTool.transform.localRotation = Quaternion.Euler(toolLocalEuler);
        _heldTool.transform.localScale *= toolScale;
        foreach (var c in _heldTool.GetComponentsInChildren<Collider>(true)) Destroy(c);
    }

    void BuildAimMarker()
    {
        _aimMarker = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _aimMarker.name = "~AimMarker";
        Destroy(_aimMarker.GetComponent<Collider>());
        _aimMarker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(shader);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        _aimMarker.GetComponent<Renderer>().sharedMaterial = m;
    }

    void UpdateAimMarker()
    {
        if (_aimMarker == null) return;
        var grid = FarmGrid.Instance;
        if (grid == null) { _aimMarker.SetActive(false); return; }

        var cell = TargetCell();
        _aimMarker.SetActive(true);
        _aimMarker.transform.position = grid.CellToWorld(cell) + Vector3.up * 0.16f;
        _aimMarker.transform.localScale = Vector3.one * grid.tileSize * 0.92f;

        bool ok = grid.IsFarmable(cell);
        var r = _aimMarker.GetComponent<Renderer>();
        if (r.sharedMaterial.HasProperty("_BaseColor"))
            r.sharedMaterial.SetColor("_BaseColor", ok ? aimOkColor : aimBadColor);
    }

    // ------------------------------------------------------------------
    // 로컬 HUD
    // ------------------------------------------------------------------

    void OnGUI()
    {
        if (!IsOwner) return;

        const float w = 260f, h = 108f;
        GUILayout.BeginArea(new Rect(12f, Screen.height - h - 12f, w, h), GUI.skin.box);

        GUILayout.Label($"<b>도구</b>  {(Tool == FarmTool.Hoe ? "[1] 괭이" : "[2] 물뿌리개")}",
                        new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.Label($"물  {Water.Value} / {waterCapacity}");
        GUILayout.Label($"작물  {_carried.Count} / {carryCapacity}");
        GUILayout.Label($"조준 칸  {TargetCell().x}, {TargetCell().y}",
                        new GUIStyle(GUI.skin.label) { fontSize = 10 });
        GUILayout.Label("좌클릭 도구 / E 상호작용",
                        new GUIStyle(GUI.skin.label) { fontSize = 10 });
        GUILayout.EndArea();

        // 실패 사유를 화면 가운데 아래에 잠깐 띄웁니다
        if (Time.time < _messageUntil && !string.IsNullOrEmpty(_message))
        {
            var style = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = 15, richText = true };
            GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 30f),
                      "<color=#ffd24a>" + _message + "</color>", style);
        }
    }
}
