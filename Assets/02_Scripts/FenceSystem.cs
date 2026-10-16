using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 울타리 설치 / 자동 연결 / 철거를 담당합니다.
///
/// 규칙
///  - 기둥은 ground tile 1칸당 1개만 설치
///  - 연결 1방향당 가로대 2개(상단·하단)
///  - 기둥당 최대 2방향 = 가로대 최대 4개
///  - 최대 연결 거리 3m
///  - 수동 연결 수정 없음. 기둥을 철거하면 거기 붙은 가로대도 같이 사라짐
///
/// 자동 연결 순서
///  1. 새 기둥 주변 maxLinkDistance 안의 기둥을 찾는다
///  2. 이미 2방향이 찬 기둥은 후보에서 뺀다
///  3. 가장 가까운 후보 하나와 연결한다
///  4. 예외: 새 기둥이 두 울타리 끝 사이에 (거의 일직선으로) 놓이면 양쪽 모두 연결한다
///  5. 후보가 없으면 기둥만 세운다
/// </summary>
public class FenceSystem : MonoBehaviour
{
    [Header("프리팹")]
    [Tooltip("기둥 프리팹. 피벗이 바닥에 있고 identity 회전에서 서 있어야 합니다.")]
    public GameObject postPrefab;
    [Tooltip("가로대 프리팹. identity 회전에서 길이 방향이 월드 +Z 여야 합니다.")]
    public GameObject railPrefab;

    [Header("그리드 (ground tile)")]
    [Tooltip("타일 한 칸의 크기(m). Soil_Base 가 1x1 이라 기본 1.")]
    public float tileSize = 1f;
    [Tooltip("(0,0) 칸의 월드 중심. 타일 배치에 맞춰 조정하세요.")]
    public Vector3 gridOrigin = Vector3.zero;
    [Tooltip("기둥이 서는 바닥 높이.")]
    public float groundY = 0f;

    [Header("연결 규칙")]
    [Tooltip("이 거리 안의 기둥만 연결 후보가 됩니다.")]
    public float maxLinkDistance = 3f;

    [Tooltip("새 기둥을 사이에 두고 두 후보가 이루는 각이 이 값 이상이면 '사이에 끼웠다'고 보고 양쪽 다 연결합니다. 180도에 가까울수록 일직선.")]
    [Range(90f, 180f)]
    public float straightAngleThreshold = 120f;

    [Tooltip("끄면 항상 가장 가까운 기둥 하나에만 연결합니다.")]
    public bool connectBothWhenBetween = true;

    [Header("가로대 높이 (기둥 높이 대비 비율)")]
    // 기는 자세 최고점이 약 0.59m 라서, 아래 가로대를 그보다 위로 올려야
    // Ctrl 로 기어서 통과할 수 있습니다. (기둥 높이 0.99m 기준 아랫면 ≈ 0.64m)
    [Range(0f, 1f)] public float topRailHeight = 0.93f;
    [Range(0f, 1f)] public float bottomRailHeight = 0.68f;

    // ---- 런타임 상태 ----

    /// <summary>칸 -> 그 칸에 서 있는 기둥. 한 칸에 하나만 허용하기 위해 씁니다.</summary>
    readonly Dictionary<Vector2Int, FencePost> _posts = new Dictionary<Vector2Int, FencePost>();

    /// <summary>프리팹에서 한 번만 재는 값들 (모델을 바꿔도 코드를 안 고치려고 측정해서 씁니다).</summary>
    float _postHeight = 1f;
    float _railNativeLength = 1f;
    Vector3 _railCenterOffset;   // 프리팹 피벗 기준, 가로대 메시 중심이 얼마나 치우쳐 있는지
    bool _measured;

    public IEnumerable<FencePost> AllPosts => _posts.Values;

    void Awake() => Measure();

    // ------------------------------------------------------------------
    // 그리드 좌표 변환
    // ------------------------------------------------------------------

    /// <summary>월드 좌표 -> 타일 칸 번호.</summary>
    public Vector2Int WorldToCell(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt((world.x - gridOrigin.x) / tileSize),
            Mathf.RoundToInt((world.z - gridOrigin.z) / tileSize));
    }

    /// <summary>타일 칸 번호 -> 그 칸 중심의 월드 좌표(바닥 높이).</summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            gridOrigin.x + cell.x * tileSize,
            groundY,
            gridOrigin.z + cell.y * tileSize);
    }

    public bool IsCellOccupied(Vector2Int cell) => _posts.ContainsKey(cell);

    public FencePost GetPostAt(Vector2Int cell) =>
        _posts.TryGetValue(cell, out var p) ? p : null;

    // ------------------------------------------------------------------
    // 설치
    // ------------------------------------------------------------------

    /// <summary>
    /// 월드 좌표를 타일 칸으로 스냅해서 기둥을 세우고, 규칙에 맞게 자동 연결합니다.
    /// 이미 기둥이 있는 칸이면 아무것도 안 하고 null 을 돌려줍니다.
    /// </summary>
    public FencePost PlacePost(Vector3 worldPosition)
    {
        Measure();

        var cell = WorldToCell(worldPosition);
        if (IsCellOccupied(cell)) return null;
        if (postPrefab == null) { Debug.LogError("[FenceSystem] postPrefab 이 비어 있습니다.", this); return null; }

        // 기둥도 프리팹에 구워진 로컬 회전을 그대로 써야 합니다.
        // Quaternion.identity 로 만들면 모델이 누워버립니다.
        var go = Instantiate(postPrefab);
        go.transform.SetParent(transform, false);      // 로컬 TRS 보존
        go.transform.position = CellToWorld(cell);     // 회전은 건드리지 않고 위치만
        go.name = $"FencePost_{cell.x}_{cell.y}";

        var post = go.GetComponent<FencePost>();
        if (post == null) post = go.AddComponent<FencePost>();
        post.Cell = cell;

        _posts.Add(cell, post);

        AutoConnect(post);
        return post;
    }

    // ------------------------------------------------------------------
    // 철거
    // ------------------------------------------------------------------

    /// <summary>해당 칸의 기둥과, 거기 붙어 있던 가로대를 전부 제거합니다.</summary>
    public bool RemovePostAt(Vector3 worldPosition) => RemovePost(GetPostAt(WorldToCell(worldPosition)));

    public bool RemovePost(FencePost post)
    {
        if (post == null) return false;

        // 리스트를 돌면서 지우면 꼬이니 복사본으로 순회합니다.
        var links = post.Links.ToArray();
        foreach (var link in links) BreakLink(link);

        _posts.Remove(post.Cell);
        SafeDestroy(post.gameObject);
        return true;
    }

    // ------------------------------------------------------------------
    // 자동 연결 판단
    // ------------------------------------------------------------------

    void AutoConnect(FencePost newPost)
    {
        var targets = FindConnections(newPost.BasePosition, newPost);
        foreach (var t in targets) CreateLink(newPost, t);
    }

    /// <summary>
    /// 이 위치에 기둥을 세우면 '어느 기둥과 연결될지'를 돌려줍니다 (0~2개).
    /// 실제 설치와 설치 미리보기가 똑같이 이 함수를 쓰기 때문에
    /// 미리보기에 보이는 대로 연결됩니다.
    /// </summary>
    public List<FencePost> FindConnections(Vector3 worldPosition, FencePost exclude = null)
    {
        var picked = new List<FencePost>();

        // 1~2. 범위 안에서 '아직 연결 여유가 있는' 기둥만 모은다
        var candidates = new List<FencePost>();
        float maxSqr = maxLinkDistance * maxLinkDistance;

        foreach (var other in _posts.Values)
        {
            if (other == null || other == exclude) continue;
            if (!other.HasFreeDirection) continue;                 // 이미 2방향이 찬 기둥은 제외
            if (exclude != null && exclude.IsLinkedTo(other)) continue;

            float sqr = (other.BasePosition - worldPosition).sqrMagnitude;
            if (sqr > maxSqr || sqr < 0.0001f) continue;
            candidates.Add(other);
        }
        if (candidates.Count == 0) return picked;                   // 4. 연결할 게 없으면 기둥만

        // 가까운 순으로 정렬
        candidates.Sort((p, q) =>
            (p.BasePosition - worldPosition).sqrMagnitude
            .CompareTo((q.BasePosition - worldPosition).sqrMagnitude));

        // 3. 가장 가까운 기둥 하나
        picked.Add(candidates[0]);

        // 4(예외). 새 기둥이 두 울타리 끝 '사이'에 놓였는지 본다.
        //          첫 번째와 거의 반대 방향에 있는 후보가 있으면 그쪽도 연결.
        if (connectBothWhenBetween)
        {
            Vector3 toFirst = Flat(candidates[0].BasePosition - worldPosition);
            for (int i = 1; i < candidates.Count; i++)
            {
                Vector3 toOther = Flat(candidates[i].BasePosition - worldPosition);
                if (Vector3.Angle(toFirst, toOther) >= straightAngleThreshold)
                {
                    picked.Add(candidates[i]);                       // 가까운 순이라 먼저 걸리는 게 최선
                    break;
                }
            }
        }
        return picked;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ------------------------------------------------------------------
    // 연결 만들기 / 끊기
    // ------------------------------------------------------------------

    void CreateLink(FencePost a, FencePost b)
    {
        if (a == null || b == null || a == b) return;
        if (!a.HasFreeDirection || !b.HasFreeDirection) return;
        if (a.IsLinkedTo(b)) return;

        string tag = $"{a.Cell.x}_{a.Cell.y}__{b.Cell.x}_{b.Cell.y}";
        var link = new FenceLink { A = a, B = b };
        link.TopRail = BuildRailVisual(a.BasePosition, b.BasePosition, topRailHeight, "TopRail_" + tag);
        link.BottomRail = BuildRailVisual(a.BasePosition, b.BasePosition, bottomRailHeight, "BottomRail_" + tag);

        a.Links.Add(link);
        b.Links.Add(link);
    }

    void BreakLink(FenceLink link)
    {
        if (link == null) return;
        link.A?.Links.Remove(link);
        link.B?.Links.Remove(link);
        SafeDestroy(link.TopRail);
        SafeDestroy(link.BottomRail);
    }

    /// <summary>
    /// 두 지점 사이에 가로대 하나를 만들어 돌려줍니다.
    /// 가로대 프리팹은 그대로 두고 빈 부모를 씌워서 위치/회전/길이를 조절합니다.
    /// (프리팹 자체에 회전이 구워져 있어도 안전하게 다루려고 이렇게 합니다.)
    /// 설치 미리보기(FencePlacer)도 같은 함수를 써서 모양이 어긋나지 않습니다.
    /// </summary>
    public GameObject BuildRailVisual(Vector3 pa, Vector3 pb, float heightRatio, string label, Transform parent = null)
    {
        Measure();
        if (railPrefab == null) { Debug.LogError("[FenceSystem] railPrefab 이 비어 있습니다.", this); return null; }

        Vector3 dir = Flat(pb - pa);
        float distance = dir.magnitude;
        if (distance < 0.0001f) return null;
        dir /= distance;

        var holder = new GameObject(label);
        holder.transform.SetParent(parent != null ? parent : transform, false);

        // 프리팹의 로컬 회전(모델에 구워진 자세)을 반드시 그대로 둬야 합니다.
        // SetParent(..., false) 가 로컬 TRS 를 보존해 줍니다.
        var visual = Instantiate(railPrefab);
        visual.transform.SetParent(holder.transform, false);

        // 길이 방향(부모 기준 +Z)만 늘려서 두 기둥 사이를 꽉 채웁니다. 굵기는 그대로.
        float stretch = distance / _railNativeLength;
        holder.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        holder.transform.localScale = new Vector3(1f, 1f, stretch);

        // 메시 중심이 피벗에서 Y로 조금 떠 있어서, 원하는 높이에 '보이는 중심'이 오도록 빼줍니다.
        float y = groundY + _postHeight * heightRatio - _railCenterOffset.y;
        holder.transform.position = new Vector3((pa.x + pb.x) * 0.5f, y, (pa.z + pb.z) * 0.5f);

        return holder;
    }

    // ------------------------------------------------------------------
    // 프리팹 실측 (하드코딩 대신)
    // ------------------------------------------------------------------

    void Measure()
    {
        if (_measured) return;
        _measured = true;

        if (postPrefab != null)
        {
            var r = postPrefab.GetComponentInChildren<Renderer>();
            if (r != null) _postHeight = r.bounds.size.y;
        }

        if (railPrefab != null)
        {
            var r = railPrefab.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                // 프리팹 에셋의 bounds 는 identity 기준이라 그대로 로컬 값으로 쓸 수 있습니다.
                _railNativeLength = Mathf.Max(0.0001f, r.bounds.size.z);
                _railCenterOffset = r.bounds.center;
            }
        }
    }

    /// <summary>플레이 중이면 Destroy, 에디터면 DestroyImmediate.</summary>
    static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    // ------------------------------------------------------------------
    // 씬 뷰에서 연결 상태 확인용
    // ------------------------------------------------------------------

    void OnDrawGizmosSelected()
    {
        foreach (var post in _posts.Values)
        {
            if (post == null) continue;

            // 초록 = 아직 연결 가능, 빨강 = 2방향이 꽉 참
            Gizmos.color = post.HasFreeDirection ? Color.green : Color.red;
            Gizmos.DrawWireSphere(post.BasePosition + Vector3.up * 0.05f, 0.12f);

            // 연결선 (한 쌍이 두 번 그려지는 건 신경 쓰지 않아도 됩니다)
            Gizmos.color = Color.yellow;
            foreach (var link in post.Links)
            {
                var other = link.Other(post);
                if (other != null)
                    Gizmos.DrawLine(post.BasePosition + Vector3.up * 0.05f,
                                    other.BasePosition + Vector3.up * 0.05f);
            }
        }
    }
}
