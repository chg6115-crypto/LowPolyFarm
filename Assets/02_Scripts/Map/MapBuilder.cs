using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵 블록아웃을 파라미터로 생성합니다 (기획 3장 / 13.2단계).
///
///   - 4분면: 플레이어 1명당 한 구역 (창고 + 농기구, 우물 1개, 스폰 지점)
///   - 구역을 가르는 흙길 십자로
///   - 맵 중앙의 작은 연못
///   - 길가와 연못 주변의 풀숲 지대 (잠입 루트)
///
/// 기획 15장에서 "미정"인 맵 크기와 구역 넓이를 여기서 조정합니다.
/// 인스펙터에서 값을 바꾸고 우클릭 > Build Map 으로 다시 생성하세요.
/// </summary>
public class MapBuilder : MonoBehaviour
{
    [Header("크기 (m) — 기획 15장 미정 항목")]
    [Tooltip("구역 한 변. 플레이어 1명이 쓰는 농장 크기.")]
    public float quadrantSize = 14f;
    [Tooltip("구역을 가르는 십자 흙길의 폭.")]
    public float roadWidth = 8f;
    [Tooltip("중앙 연못 반지름.")]
    public float pondRadius = 2.5f;

    [Header("구역 내 배치 (구역 중심 기준 오프셋)")]
    [Tooltip("창고 위치. 바깥쪽 모서리 방향(+)입니다.")]
    public Vector2 warehouseOffset = new Vector2(5f, 5f);
    [Tooltip("우물 위치. 길 쪽(-)에 둬서 이웃이 바위로 막으러 올 수 있게 합니다.")]
    public Vector2 wellOffset = new Vector2(-4.5f, -4.5f);
    [Tooltip("플레이어 스폰 위치.")]
    public Vector2 spawnOffset = new Vector2(2.5f, 2.5f);

    [Header("프리팹")]
    public GameObject wellPrefab;
    public GameObject[] grassPrefabs;
    public GameObject[] bushPrefabs;
    public GameObject[] toolPrefabs;

    [Header("풀숲 (잠입 루트)")]
    [Tooltip("연못 주변에 심을 개수.")]
    public int pondGrassCount = 90;
    [Tooltip("길가에 심을 개수.")]
    public int roadGrassCount = 140;
    [Tooltip("같은 숫자면 항상 같은 배치가 나옵니다.")]
    public int randomSeed = 1234;

    [Header("머티리얼 (비우면 자동 생성)")]
    public Material groundMaterial;
    public Material roadMaterial;
    public Material warehouseMaterial;
    public Material pondMaterial;

    [Tooltip("풀·덤불에 덮어씌울 머티리얼. Pandazole 프리팹은 FBX 내장 Built-in 머티리얼을 " +
             "쓰고 있어 URP 에서 분홍색으로 나옵니다. 서드파티 원본을 건드리지 않으려고 " +
             "생성된 인스턴스에만 URP 머티리얼을 씌웁니다.")]
    public Material foliageMaterial;

    [Tooltip("농기구에 덮어씌울 머티리얼. 위와 같은 이유입니다.")]
    public Material toolMaterial;

    /// <summary>맵 전체 한 변 길이.</summary>
    public float MapSize => quadrantSize * 2f + roadWidth;

    /// <summary>4분면의 부호. (+,+), (-,+), (-,-), (+,-) 순서.</summary>
    static readonly Vector2[] Signs =
    {
        new Vector2( 1,  1),
        new Vector2(-1,  1),
        new Vector2(-1, -1),
        new Vector2( 1, -1),
    };

    const string ROOT = "Map";

    [ContextMenu("Build Map")]
    public void Build()
    {
        var root = transform.Find(ROOT);
        if (root != null) DestroyNow(root.gameObject);

        var mapRoot = new GameObject(ROOT).transform;
        mapRoot.SetParent(transform, false);

        BuildGround(mapRoot);
        BuildRoads(mapRoot);
        BuildPond(mapRoot);

        for (int i = 0; i < 4; i++) BuildQuadrant(mapRoot, i);

        ScatterGrass(mapRoot);

        Debug.Log($"[MapBuilder] 맵 생성 완료. 전체 {MapSize}x{MapSize}m, 구역 {quadrantSize}x{quadrantSize}m x4", this);
    }

    // ------------------------------------------------------------------

    void BuildGround(Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Ground";
        go.transform.SetParent(parent, false);
        // 윗면이 y=0 에 오도록 두께 1m 판을 깔고 절반을 내립니다.
        go.transform.localScale = new Vector3(MapSize, 1f, MapSize);
        go.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        Paint(go, groundMaterial, new Color(0.42f, 0.60f, 0.32f));   // 풀색
    }

    void BuildRoads(Transform parent)
    {
        // 흙길 십자. 바닥보다 아주 살짝 위에 깔아 z-fighting 을 피합니다.
        for (int axis = 0; axis < 2; axis++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = axis == 0 ? "Road_X" : "Road_Z";
            go.transform.SetParent(parent, false);
            go.transform.localScale = axis == 0
                ? new Vector3(MapSize, 0.02f, roadWidth)
                : new Vector3(roadWidth, 0.02f, MapSize);
            go.transform.localPosition = new Vector3(0f, 0.005f, 0f);
            DestroyNow(go.GetComponent<Collider>());   // 바닥 콜라이더로 충분
            Paint(go, roadMaterial, new Color(0.55f, 0.43f, 0.30f));
        }
    }

    void BuildPond(Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Pond";
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(pondRadius * 2f, 0.05f, pondRadius * 2f);
        go.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        DestroyNow(go.GetComponent<Collider>());
        Paint(go, pondMaterial, new Color(0.25f, 0.55f, 0.75f));
        // 물 뜨는 지점 판정은 이후 단계(4.1)에서 트리거로 추가합니다.
    }

    void BuildQuadrant(Transform parent, int index)
    {
        Vector2 s = Signs[index];
        float centre = roadWidth * 0.5f + quadrantSize * 0.5f;
        Vector3 origin = new Vector3(s.x * centre, 0f, s.y * centre);

        var qGo = new GameObject($"Quadrant_{index}");
        qGo.transform.SetParent(parent, false);
        qGo.transform.localPosition = origin;

        var q = qGo.AddComponent<FarmQuadrant>();
        q.index = index;
        q.size = quadrantSize;

        Vector3 Local(Vector2 off) => new Vector3(s.x * off.x, 0f, s.y * off.y);

        // 창고 (임시: 큐브 오두막) — 기획 12.3 "큐브 조합 오두막"
        var wh = BuildWarehouse(qGo.transform, Local(warehouseOffset), index);
        q.warehouse = wh;

        // 우물
        if (wellPrefab != null)
        {
            var well = Instantiate(wellPrefab);
            well.transform.SetParent(qGo.transform, false);     // 프리팹 회전 보존
            well.transform.localPosition = Local(wellOffset);
            well.name = "Well";
            q.well = well.transform;

            // 우물은 고정 장애물입니다. 바위와 달리 밀리면 안 되므로 Rigidbody 는 안 붙입니다.
            var mf = well.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;                              // 정적이라 convex 불필요
            }
        }

        // 스폰 지점
        var spawn = new GameObject("SpawnPoint");
        spawn.transform.SetParent(qGo.transform, false);
        spawn.transform.localPosition = Local(spawnOffset);
        // 맵 중앙을 바라보게
        spawn.transform.rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(-origin, Vector3.up).normalized, Vector3.up);
        q.spawnPoint = spawn.transform;

        // 농기구 — 창고 옆에 세워둡니다
        PlaceTools(qGo.transform, Local(warehouseOffset) + Local(new Vector2(-1.6f, 0f)));
    }

    Transform BuildWarehouse(Transform parent, Vector3 localPos, int index)
    {
        var root = new GameObject("Warehouse").transform;
        root.SetParent(parent, false);
        root.localPosition = localPos;

        // 몸통
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root, false);
        body.transform.localScale = new Vector3(3f, 2f, 3f);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Paint(body, warehouseMaterial, PlayerColor(index));

        // 지붕 (큐브를 45도 돌려서 삼각 지붕처럼)
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(root, false);
        roof.transform.localScale = new Vector3(2.4f, 2.4f, 3.2f);
        roof.transform.localPosition = new Vector3(0f, 2f, 0f);
        roof.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Paint(roof, null, new Color(0.45f, 0.28f, 0.22f));

        return root;
    }

    void PlaceTools(Transform parent, Vector3 localPos)
    {
        if (toolPrefabs == null || toolPrefabs.Length == 0) return;
        for (int i = 0; i < toolPrefabs.Length; i++)
        {
            if (toolPrefabs[i] == null) continue;
            var t = Instantiate(toolPrefabs[i]);
            t.transform.SetParent(parent, false);
            t.transform.localPosition = localPos + new Vector3(i * 0.35f, 0f, 0f);
            t.name = toolPrefabs[i].name;

            if (toolMaterial != null)
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int k = 0; k < mats.Length; k++) mats[k] = toolMaterial;
                    r.sharedMaterials = mats;
                }
        }
    }

    // ------------------------------------------------------------------

    void ScatterGrass(Transform parent)
    {
        if ((grassPrefabs == null || grassPrefabs.Length == 0) &&
            (bushPrefabs == null || bushPrefabs.Length == 0)) return;

        var holder = new GameObject("GrassZones").transform;
        holder.SetParent(parent, false);

        var rng = new System.Random(randomSeed);
        float half = MapSize * 0.5f;

        // 연못 주변 고리
        for (int i = 0; i < pondGrassCount; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = pondRadius + 0.4f + (float)rng.NextDouble() * (roadWidth * 0.5f - pondRadius - 0.2f);
            Place(holder, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), rng);
        }

        // 길가 (길 바깥쪽 가장자리를 따라)
        float edge = roadWidth * 0.5f;
        for (int i = 0; i < roadGrassCount; i++)
        {
            bool alongX = rng.Next(2) == 0;
            float along = ((float)rng.NextDouble() * 2f - 1f) * half;
            float side = (rng.Next(2) == 0 ? 1f : -1f) * (edge + (float)rng.NextDouble() * 1.6f);
            Vector3 p = alongX ? new Vector3(along, 0f, side) : new Vector3(side, 0f, along);
            if (Mathf.Abs(p.x) > half - 0.5f || Mathf.Abs(p.z) > half - 0.5f) continue;
            Place(holder, p, rng);
        }
    }

    void Place(Transform parent, Vector3 pos, System.Random rng)
    {
        var pool = new List<GameObject>();
        if (grassPrefabs != null) pool.AddRange(grassPrefabs);
        if (bushPrefabs != null && rng.Next(100) < 12) pool.AddRange(bushPrefabs);
        pool.RemoveAll(p => p == null);
        if (pool.Count == 0) return;

        var prefab = pool[rng.Next(pool.Count)];
        var go = Instantiate(prefab);
        go.transform.SetParent(parent, false);          // 프리팹 로컬 회전 보존
        go.transform.localPosition = pos;

        // 풀숲은 '지나다니는 잠입 루트'라서 막으면 안 됩니다 (기획 6.2).
        // 은신 판정용 트리거는 나중에 구역 단위로 따로 답니다.
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyNow(c);

        if (foliageMaterial != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = foliageMaterial;
                r.sharedMaterials = mats;
            }
        go.transform.Rotate(Vector3.up, (float)rng.NextDouble() * 360f, Space.World);
        float s = 0.8f + (float)rng.NextDouble() * 0.6f;
        go.transform.localScale *= s;
    }

    // ------------------------------------------------------------------

    static Color PlayerColor(int index) => index switch
    {
        0 => new Color(0.85f, 0.45f, 0.40f),
        1 => new Color(0.45f, 0.65f, 0.85f),
        2 => new Color(0.85f, 0.78f, 0.45f),
        _ => new Color(0.55f, 0.80f, 0.55f),
    };

    /// <summary>머티리얼이 지정돼 있으면 그걸 쓰고, 없으면 색만 입힌 URP 머티리얼을 만듭니다.</summary>
    static void Paint(GameObject go, Material shared, Color fallback)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;

        if (shared != null) { r.sharedMaterial = shared; return; }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(shader) { name = go.name + "_Mat" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", fallback);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        r.sharedMaterial = m;
    }

    static void DestroyNow(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
