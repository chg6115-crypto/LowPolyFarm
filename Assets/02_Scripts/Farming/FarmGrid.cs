using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 농사 동작의 결과. 실패해도 왜 실패했는지 플레이어에게 보여주려고 코드로 돌려줍니다.
/// (아무 반응이 없으면 버그인지 조작 실수인지 구분이 안 됩니다.)
/// </summary>
public enum FarmResult : byte
{
    Ok,
    NotFarmable,     // 길·연못 등 4분면 밖
    AlreadyTilled,   // 이미 갈아둔 땅
    NoTile,          // 밭이 아님 (먼저 괭이질 필요)
    NotTilled,       // 이미 뭔가 심겨 있음
    AlreadyWatered,
    NotPlanted,      // 물 줄 작물이 없음
    NotGrown,
    NoWater,         // 물뿌리개가 비었음
    HandsFull,       // 손에 더 못 듦
    NoCrop,          // 심을 작물이 없음
}

/// <summary>밭 한 칸의 상태. NetworkList 로 동기화되므로 unmanaged 구조체여야 합니다.</summary>
public struct FarmTile : INetworkSerializable, System.IEquatable<FarmTile>
{
    public const byte Tilled = 1;   // 갈아만 둔 땅
    public const byte Planted = 2;  // 작물을 묻은 땅
    public const byte Grown = 3;    // 다 자라서 올라온 땅

    public int X, Z;
    public byte State;
    public byte CropId;
    public byte Watered;     // bool 은 NetworkList 에서 다루기 번거로워 byte 로 둡니다
    public byte Remaining;   // 다 자란 뒤 아직 안 거둔 개수
    public float PlantedAt;  // 물을 준 시각(서버 시간)

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref X);
        s.SerializeValue(ref Z);
        s.SerializeValue(ref State);
        s.SerializeValue(ref CropId);
        s.SerializeValue(ref Watered);
        s.SerializeValue(ref Remaining);
        s.SerializeValue(ref PlantedAt);
    }

    public bool Equals(FarmTile o) =>
        X == o.X && Z == o.Z && State == o.State && CropId == o.CropId &&
        Watered == o.Watered && Remaining == o.Remaining && PlantedAt.Equals(o.PlantedAt);
}

/// <summary>
/// 밭 전체를 관리합니다 (기획 4장).
///
///   괭이질 → 갈린 땅
///   갈린 땅에 작물을 놓으면 묻힘 (씨앗 없음, 농산물 자체를 심습니다)
///   물을 주면 그때부터 성장 타이머가 돌고, 다 자라면 2~5배로 올라옵니다
///
/// 칸 상태는 전부 **서버 권한**입니다 (기획 10장).
/// 클라이언트는 요청만 보내고, 실제 판정과 변경은 서버가 합니다.
/// </summary>
public class FarmGrid : NetworkBehaviour
{
    public static FarmGrid Instance { get; private set; }

    [Header("그리드")]
    [Tooltip("한 칸 크기. 울타리(FenceSystem)와 같은 1m 를 씁니다.")]
    public float tileSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;
    public float groundY = 0f;

    [Header("작물 (id = 배열 순서)")]
    public CropData[] crops;

    [Header("밭 비주얼")]
    [Tooltip("갈린 땅에 깔 프리팹. Soil_Base 를 씁니다.")]
    public GameObject tilledPrefab;
    [Tooltip("마른 흙 색.")]
    public Color dryColor = new Color(0.62f, 0.47f, 0.33f);
    [Tooltip("물 준 흙 색. 기획 4장 '젖은 흙'.")]
    public Color wetColor = new Color(0.33f, 0.24f, 0.16f);

    readonly NetworkList<FarmTile> _tiles = new NetworkList<FarmTile>();
    readonly Dictionary<Vector2Int, GameObject> _visuals = new Dictionary<Vector2Int, GameObject>();
    MaterialPropertyBlock _mpb;

    // ------------------------------------------------------------------

    void Awake() => _mpb = new MaterialPropertyBlock();

    public override void OnNetworkSpawn()
    {
        Instance = this;
        _tiles.OnListChanged += OnTilesChanged;
        RefreshAllVisuals();
    }

    public override void OnNetworkDespawn()
    {
        _tiles.OnListChanged -= OnTilesChanged;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!IsServer) return;
        TickGrowth();
    }

    // ------------------------------------------------------------------
    // 좌표
    // ------------------------------------------------------------------

    public Vector2Int WorldToCell(Vector3 w) => new Vector2Int(
        Mathf.RoundToInt((w.x - gridOrigin.x) / tileSize),
        Mathf.RoundToInt((w.z - gridOrigin.z) / tileSize));

    public Vector3 CellToWorld(Vector2Int c) => new Vector3(
        gridOrigin.x + c.x * tileSize, groundY, gridOrigin.z + c.y * tileSize);

    public CropData GetCrop(int id) =>
        (crops != null && id >= 0 && id < crops.Length) ? crops[id] : null;

    int IndexOf(Vector2Int cell)
    {
        for (int i = 0; i < _tiles.Count; i++)
            if (_tiles[i].X == cell.x && _tiles[i].Z == cell.y) return i;
        return -1;
    }

    public bool TryGetTile(Vector2Int cell, out FarmTile tile)
    {
        int i = IndexOf(cell);
        if (i < 0) { tile = default; return false; }
        tile = _tiles[i];
        return true;
    }

    /// <summary>밭은 4분면 안에만 만들 수 있습니다. 길과 연못은 비워둡니다.</summary>
    public bool IsFarmable(Vector2Int cell)
    {
        Vector3 w = CellToWorld(cell);
        foreach (var q in FindObjectsByType<FarmQuadrant>(FindObjectsInactive.Include))
            if (q.Contains(w)) return true;
        return false;
    }

    // ------------------------------------------------------------------
    // 서버 동작 (전부 서버에서만 호출됩니다)
    // ------------------------------------------------------------------

    /// <summary>괭이질. 빈 땅을 갈아 밭으로 만듭니다.</summary>
    public FarmResult ServerTill(Vector2Int cell)
    {
        if (!IsFarmable(cell)) return FarmResult.NotFarmable;
        if (IndexOf(cell) >= 0) return FarmResult.AlreadyTilled;

        _tiles.Add(new FarmTile { X = cell.x, Z = cell.y, State = FarmTile.Tilled });
        return FarmResult.Ok;
    }

    /// <summary>갈린 땅에 작물을 묻습니다. 씨앗이 아니라 농산물 자체입니다.</summary>
    public FarmResult ServerPlant(Vector2Int cell, byte cropId)
    {
        int i = IndexOf(cell);
        if (i < 0) return FarmResult.NoTile;
        var t = _tiles[i];
        if (t.State != FarmTile.Tilled) return FarmResult.NotTilled;
        if (GetCrop(cropId) == null) return FarmResult.NoCrop;

        t.State = FarmTile.Planted;
        t.CropId = cropId;
        t.Watered = 0;
        _tiles[i] = t;
        return FarmResult.Ok;
    }

    /// <summary>물주기. 이때부터 성장 타이머가 돕니다.</summary>
    public FarmResult ServerWater(Vector2Int cell)
    {
        int i = IndexOf(cell);
        if (i < 0) return FarmResult.NoTile;
        var t = _tiles[i];
        if (t.State != FarmTile.Planted) return FarmResult.NotPlanted;
        if (t.Watered != 0) return FarmResult.AlreadyWatered;

        t.Watered = 1;
        t.PlantedAt = Time.time;
        _tiles[i] = t;
        return FarmResult.Ok;
    }

    /// <summary>다 자란 칸에서 1개 거둡니다. 다 거두면 갈린 땅으로 돌아갑니다.</summary>
    public FarmResult ServerHarvestOne(Vector2Int cell, out byte cropId)
    {
        cropId = 0;
        int i = IndexOf(cell);
        if (i < 0) return FarmResult.NoTile;
        var t = _tiles[i];
        if (t.State != FarmTile.Grown || t.Remaining == 0) return FarmResult.NotGrown;

        cropId = t.CropId;
        t.Remaining--;
        if (t.Remaining == 0)
        {
            t.State = FarmTile.Tilled;     // 땅은 그대로 남습니다
            t.Watered = 0;
            t.CropId = 0;
        }
        _tiles[i] = t;
        return FarmResult.Ok;
    }

    /// <summary>물 준 칸의 성장 시간을 확인합니다.</summary>
    void TickGrowth()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            if (t.State != FarmTile.Planted || t.Watered == 0) continue;

            var crop = GetCrop(t.CropId);
            if (crop == null) continue;
            if (Time.time - t.PlantedAt < crop.growSeconds) continue;

            t.State = FarmTile.Grown;
            t.Remaining = (byte)crop.RollYield();        // 2~5배
            _tiles[i] = t;
        }
    }

    /// <summary>해당 칸이 다 자라기까지 남은 비율 0~1. UI 표시용.</summary>
    public float GrowProgress(FarmTile t)
    {
        if (t.State != FarmTile.Planted || t.Watered == 0) return 0f;
        var crop = GetCrop(t.CropId);
        if (crop == null || crop.growSeconds <= 0f) return 1f;
        return Mathf.Clamp01((Time.time - t.PlantedAt) / crop.growSeconds);
    }

    // ------------------------------------------------------------------
    // 비주얼 (모든 클라이언트가 각자 그립니다)
    // ------------------------------------------------------------------

    void OnTilesChanged(NetworkListEvent<FarmTile> e) => RefreshAllVisuals();

    void RefreshAllVisuals()
    {
        var seen = new HashSet<Vector2Int>();

        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            var cell = new Vector2Int(t.X, t.Z);
            seen.Add(cell);

            if (!_visuals.TryGetValue(cell, out var go) || go == null)
            {
                go = CreateTileVisual(cell);
                _visuals[cell] = go;
            }
            UpdateTileVisual(go, t);
        }

        // 사라진 칸 정리
        var gone = new List<Vector2Int>();
        foreach (var kv in _visuals) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var c in gone)
        {
            if (_visuals[c] != null) Destroy(_visuals[c]);
            _visuals.Remove(c);
        }
    }

    GameObject CreateTileVisual(Vector2Int cell)
    {
        var root = new GameObject($"Tile_{cell.x}_{cell.y}");
        root.transform.SetParent(transform, false);
        root.transform.position = CellToWorld(cell);

        if (tilledPrefab != null)
        {
            var soil = Instantiate(tilledPrefab);
            soil.transform.SetParent(root.transform, false);   // 프리팹 회전 보존
            soil.name = "Soil";
            foreach (var c in soil.GetComponentsInChildren<Collider>(true)) Destroy(c);
        }
        return root;
    }

    void UpdateTileVisual(GameObject root, FarmTile t)
    {
        // 흙 색: 물을 주면 진해집니다 (기획 4장)
        var soil = root.transform.Find("Soil");
        if (soil != null)
        {
            var r = soil.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", t.Watered != 0 ? wetColor : dryColor);
                r.SetPropertyBlock(_mpb);
            }
        }

        // 자란 작물
        var cropRoot = root.transform.Find("Crop");
        bool wantCrop = t.State == FarmTile.Grown && t.Remaining > 0;

        if (!wantCrop)
        {
            if (cropRoot != null) Destroy(cropRoot.gameObject);
            return;
        }

        var data = GetCrop(t.CropId);
        if (data == null || data.prefab == null) return;

        // 개수가 바뀌면 다시 만듭니다
        if (cropRoot != null && cropRoot.childCount == t.Remaining) return;
        if (cropRoot != null) Destroy(cropRoot.gameObject);

        var holder = new GameObject("Crop").transform;
        holder.SetParent(root.transform, false);

        // 수확 개수만큼 칸 안에 흩어 놓습니다
        for (int i = 0; i < t.Remaining; i++)
        {
            var c = Instantiate(data.prefab);
            c.transform.SetParent(holder, false);
            float a = (i / (float)Mathf.Max(1, t.Remaining)) * Mathf.PI * 2f;
            float r = t.Remaining == 1 ? 0f : tileSize * 0.22f;
            c.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.13f, Mathf.Sin(a) * r);
            c.transform.localRotation = c.transform.localRotation * Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
            c.transform.localScale *= data.modelScale;
            foreach (var col in c.GetComponentsInChildren<Collider>(true)) Destroy(col);
        }
    }
}
