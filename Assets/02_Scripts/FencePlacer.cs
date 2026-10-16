using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// FenceSystem 을 마우스로 쓰기 위한 설치 도구입니다.
/// 건설 모드에서는 반투명 '고스트'로 어디에 세워질지, 어느 기둥과 이어질지 보여줍니다.
///
///   B        : 건설 모드 켜기/끄기
///   좌클릭   : 커서가 가리키는 타일에 기둥 설치 (자동 연결)
///   X / Del  : 커서가 가리키는 타일의 기둥 철거 (가로대도 같이 제거)
///
/// 카메라 회전이 우클릭이라 좌클릭과 겹치지 않습니다.
/// </summary>
public class FencePlacer : MonoBehaviour
{
    public FenceSystem fenceSystem;

    [Tooltip("커서 위치를 계산할 카메라. 비우면 Camera.main 을 씁니다.")]
    public Camera cam;

    [Tooltip("시작할 때 건설 모드로 둘지.")]
    public bool buildMode;

    [Header("설치 미리보기")]
    [Tooltip("세울 수 있는 자리의 고스트 색.")]
    public Color validColor = new Color(0.35f, 1f, 0.45f, 0.45f);
    [Tooltip("이미 기둥이 있어 세울 수 없는 자리의 고스트 색.")]
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.45f);
    [Tooltip("어느 기둥과 연결될지도 가로대 고스트로 보여줄지.")]
    public bool showConnectionPreview = true;

    [Tooltip("화면에 조작 안내를 띄울지.")]
    public bool showHint = true;

    // ---- 내부 상태 ----
    GameObject _ghostPost;
    readonly List<GameObject> _ghostRails = new List<GameObject>();
    Transform _ghostRoot;
    Material _ghostMat;

    Vector2Int _hoverCell;
    bool _hoverValid;
    bool _previewDirty = true;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (fenceSystem == null) fenceSystem = FindAnyObjectByType<FenceSystem>();
    }

    void OnDisable() => DestroyPreview();
    void OnDestroy() => DestroyPreview();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame)
        {
            buildMode = !buildMode;
            _previewDirty = true;
            if (!buildMode) HidePreview();
        }

        if (!buildMode || fenceSystem == null || cam == null) { _hoverValid = false; return; }

        _hoverValid = TryGetCursorPoint(out Vector3 point);
        if (!_hoverValid) { HidePreview(); return; }

        var cell = fenceSystem.WorldToCell(point);
        if (cell != _hoverCell) { _hoverCell = cell; _previewDirty = true; }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            // 설치하면 주변 기둥의 연결 여유가 바뀌므로 미리보기를 다시 만듭니다.
            if (fenceSystem.PlacePost(point) != null) _previewDirty = true;
        }

        if (kb != null && (kb.xKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame))
        {
            if (fenceSystem.RemovePostAt(point)) _previewDirty = true;
        }

        if (_previewDirty) { RefreshPreview(_hoverCell); _previewDirty = false; }
    }

    // ------------------------------------------------------------------
    // 미리보기
    // ------------------------------------------------------------------

    void RefreshPreview(Vector2Int cell)
    {
        EnsureGhostRoot();
        ClearGhostRails();

        Vector3 world = fenceSystem.CellToWorld(cell);
        bool occupied = fenceSystem.IsCellOccupied(cell);

        // 기둥 고스트
        if (_ghostPost == null) _ghostPost = CreateGhost(fenceSystem.postPrefab, "GhostPost");
        if (_ghostPost != null)
        {
            _ghostPost.SetActive(true);
            _ghostPost.transform.position = world;
        }
        SetGhostColor(occupied ? invalidColor : validColor);

        // 가로대 고스트: 실제 설치 때와 똑같은 판단/모양을 씁니다.
        if (!occupied && showConnectionPreview)
        {
            foreach (var target in fenceSystem.FindConnections(world))
            {
                AddGhostRail(world, target.BasePosition, fenceSystem.topRailHeight);
                AddGhostRail(world, target.BasePosition, fenceSystem.bottomRailHeight);
            }
        }
    }

    void AddGhostRail(Vector3 a, Vector3 b, float heightRatio)
    {
        var go = fenceSystem.BuildRailVisual(a, b, heightRatio, "GhostRail", _ghostRoot);
        if (go == null) return;
        StripToGhost(go);
        _ghostRails.Add(go);
    }

    /// <summary>프리팹을 복제해 콜라이더를 떼고 반투명 재질로 바꾼 '고스트'를 만듭니다.</summary>
    GameObject CreateGhost(GameObject prefab, string label)
    {
        if (prefab == null) return null;
        EnsureGhostRoot();

        var go = Instantiate(prefab);
        go.transform.SetParent(_ghostRoot, false);   // 프리팹의 로컬 회전 보존
        go.name = label;
        StripToGhost(go);
        return go;
    }

    /// <summary>콜라이더 제거 + 그림자 끄기 + 반투명 재질 적용.</summary>
    void StripToGhost(GameObject go)
    {
        // Destroy 는 프레임 끝에 처리되므로, 그 전까지 물리에 끼어들지 않도록 먼저 끕니다.
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) { c.enabled = false; SafeDestroy(c); }

        EnsureGhostMaterial();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    void EnsureGhostRoot()
    {
        if (_ghostRoot != null) return;
        var go = new GameObject("~FencePreview");
        go.transform.SetParent(transform, false);
        _ghostRoot = go.transform;
    }

    /// <summary>URP Unlit 을 반투명으로 설정한 미리보기 전용 재질.</summary>
    void EnsureGhostMaterial()
    {
        if (_ghostMat != null) return;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        _ghostMat = new Material(shader) { name = "FenceGhost (runtime)" };

        // URP 재질을 코드에서 투명으로 바꾸려면 아래 값들을 같이 맞춰줘야 합니다.
        _ghostMat.SetFloat("_Surface", 1f);                                   // 0 Opaque / 1 Transparent
        _ghostMat.SetFloat("_Blend", 0f);                                     // Alpha
        _ghostMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        _ghostMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _ghostMat.SetFloat("_ZWrite", 0f);
        _ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _ghostMat.DisableKeyword("_ALPHATEST_ON");
        _ghostMat.renderQueue = (int)RenderQueue.Transparent;
        SetGhostColor(validColor);
    }

    /// <summary>셰이더에 따라 색 프로퍼티 이름이 달라서 둘 다 시도합니다.</summary>
    void SetGhostColor(Color c)
    {
        if (_ghostMat == null) return;
        if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", c);
        if (_ghostMat.HasProperty("_Color")) _ghostMat.SetColor("_Color", c);
    }

    void ClearGhostRails()
    {
        foreach (var go in _ghostRails) SafeDestroy(go);
        _ghostRails.Clear();
    }

    void HidePreview()
    {
        if (_ghostPost != null) _ghostPost.SetActive(false);
        ClearGhostRails();
    }

    void DestroyPreview()
    {
        ClearGhostRails();
        SafeDestroy(_ghostPost);
        if (_ghostRoot != null) SafeDestroy(_ghostRoot.gameObject);
        SafeDestroy(_ghostMat);
        _ghostPost = null; _ghostRoot = null; _ghostMat = null;
    }

    /// <summary>플레이 중이면 Destroy, 에디터면 DestroyImmediate.</summary>
    static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    // ------------------------------------------------------------------
    // 커서 -> 월드
    // ------------------------------------------------------------------

    /// <summary>
    /// 마우스 광선을 바닥 평면(y = groundY)과 교차시켜 월드 좌표를 구합니다.
    /// 콜라이더가 없어도 동작하도록 평면으로 계산합니다.
    /// </summary>
    bool TryGetCursorPoint(out Vector3 point)
    {
        point = default;
        var mouse = Mouse.current;
        if (mouse == null) return false;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        var ground = new Plane(Vector3.up, new Vector3(0f, fenceSystem.groundY, 0f));
        if (!ground.Raycast(ray, out float dist)) return false;

        point = ray.GetPoint(dist);
        return true;
    }

    // ------------------------------------------------------------------
    // 씬 뷰 보조 표시 (게임 화면에는 위의 고스트가 보입니다)
    // ------------------------------------------------------------------

    void OnDrawGizmos()
    {
        if (!buildMode || !_hoverValid || fenceSystem == null) return;

        Vector3 c = fenceSystem.CellToWorld(_hoverCell);
        Gizmos.color = fenceSystem.IsCellOccupied(_hoverCell) ? Color.red : Color.green;
        Gizmos.DrawWireCube(c + Vector3.up * 0.02f,
                            new Vector3(fenceSystem.tileSize, 0.02f, fenceSystem.tileSize));
    }

    void OnGUI()
    {
        if (!showHint) return;
        const float w = 300f, h = 94f;
        GUILayout.BeginArea(new Rect(Screen.width - w - 12f, 12f, w, h), GUI.skin.box);
        GUILayout.Label(buildMode ? "<b>FENCE BUILD: ON</b>  (B to exit)" : "<b>FENCE BUILD: OFF</b>  (B to enter)",
                        new GUIStyle(GUI.skin.label) { richText = true });
        var small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        GUILayout.Label("Left click : place post", small);
        GUILayout.Label("X / Delete : remove post", small);
        if (buildMode) GUILayout.Label($"cell {_hoverCell.x}, {_hoverCell.y}", small);
        GUILayout.EndArea();
    }
}
