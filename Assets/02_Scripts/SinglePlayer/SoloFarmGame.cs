using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LowPolyFarm.SinglePlayer
{
    [DefaultExecutionOrder(-100)]
    public sealed class SoloFarmGame : MonoBehaviour
    {
        public SoloFarmSettings settings;
        public SoloFarmState State { get; private set; }
        readonly System.Random random = new System.Random();
        readonly Dictionary<Vector2Int, GameObject> tiles = new Dictionary<Vector2Int, GameObject>();
        readonly List<Material> materials = new List<Material>();
        readonly Dictionary<Color, Material> palette = new Dictionary<Color, Material>();
        [Header("Objects placed in Single scene")]
        [SerializeField] Transform player;
        [SerializeField] Transform marker;
        [SerializeField] Transform boundary;
        [SerializeField] Transform warehousePoint;
        [SerializeField] Transform wellPoint;
        PlayerMovement movement;
        CharacterMotionDebug motionDebug;
        Animator playerAnimator;
        [SerializeField] GameObject heldHoe;
        [SerializeField] Camera view;
        OrbitFollowCamera orbitCamera;
        Vector3 warehouse => warehousePoint.position;
        Vector3 well => wellPoint.position;
        Vector2Int target;
        bool hasTarget, shop, paused, confirmReset, dirty, saveBlocked;
        int tool;
        float saveClock;
        string message = "창고 앞에서 E로 시작 작물 3개를 꺼내세요 (손에는 2개씩).";
        string SavePath => Path.Combine(Application.persistentDataPath, "LowPolyFarm_SinglePlayer_v1.json");

        void Start()
        {
            if (settings == null) { Debug.LogError("SoloFarmSettings is missing."); enabled = false; return; }
            if (player == null || view == null || marker == null || boundary == null || boundary.childCount != 4 || warehousePoint == null || wellPoint == null || settings.soilPrefab == null) {
                Debug.LogError("Single scene references are missing. Place and connect the scene objects in the Inspector."); enabled = false; return;
            }
            movement = player.GetComponent<PlayerMovement>();
            motionDebug = player.GetComponent<CharacterMotionDebug>();
            playerAnimator = player.GetComponent<Animator>();
            movement.cameraTransform = view.transform;
            orbitCamera = view.GetComponent<OrbitFollowCamera>();
            if (orbitCamera == null) { Debug.LogError("Single camera requires OrbitFollowCamera."); enabled = false; return; }
            orbitCamera.target = player;
            State = SoloFarmState.New(settings);
            Load();
            RefreshBoundary();
            RefreshTiles();
        }
        GameObject Shape(string name, PrimitiveType type, Vector3 pos, Vector3 size, Color color, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(transform);
            go.transform.position = pos; go.transform.localScale = size;
            if (!palette.TryGetValue(color, out var mat)) {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color = color;
                materials.Add(mat); palette.Add(color, mat);
            }
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) { var c = go.GetComponent<Collider>(); c.enabled = false; Destroy(c); }
            return go;
        }
        GameObject PlaceAsset(GameObject prefab, Vector3 position, Transform parent)
        {
            var go = Instantiate(prefab, parent, false);
            go.transform.position = position; // Keep the prefab's authored rotation, scale and materials.
            return go;
        }
        static void DisableColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
        }
        void RefreshBoundary()
        {
            float edge = State.radius + .5f, length = edge * 2;
            for (int i = 0; i < 4; i++) {
                bool x = i < 2; float side = i % 2 == 0 ? edge : -edge;
                var bar = boundary.GetChild(i);
                bar.localPosition = new Vector3(x?side:0,.03f,x?0:side);
                bar.localScale = new Vector3(x?.06f:length,.04f,x?length:.06f);
            }
        }
        void Update()
        {
            if (State == null) return;
            var k = Keyboard.current; var m = Mouse.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) { if (shop) shop = false; else paused = !paused; }
            movement.enabled = !shop && !paused;
            orbitCamera.enabled = !shop && !paused;
            if (motionDebug != null) motionDebug.enabled = !shop && !paused;
            if (playerAnimator != null) playerAnimator.speed = paused || shop ? 0 : 1;
            if (heldHoe != null) heldHoe.SetActive(tool == 0);
            if (paused) return;
            State.Tick(Time.deltaTime,settings,random);
            saveClock += Time.deltaTime;
            if (saveClock > 30) { Save(); saveClock = 0; }
            if (k != null && !shop) {
                if (k.digit1Key.wasPressedThisFrame) tool = 0;
                if (k.digit2Key.wasPressedThisFrame) tool = 1;
                if (k.digit3Key.wasPressedThisFrame) tool = 2;
                if (k.f5Key.wasPressedThisFrame) Save();
            }
            hasTarget = false;
            if (m != null && !shop && m.position.ReadValue().y < Screen.height-170) {
                var ray = view.ScreenPointToRay(m.position.ReadValue());
                if (new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float enter)) {
                    var hit = ray.GetPoint(enter); target = new Vector2Int(Mathf.RoundToInt(hit.x),Mathf.RoundToInt(hit.z));
                    hasTarget = Mathf.Abs(target.x) <= State.radius && Mathf.Abs(target.y) <= State.radius && Near(new Vector3(target.x,0,target.y));
                }
            }
            marker.gameObject.SetActive(hasTarget);
            if (hasTarget) marker.position = new Vector3(target.x,.06f,target.y);
            if (k != null && !shop && k.eKey.wasPressedThisFrame) Interact();
            if (m != null && !shop && hasTarget && m.leftButton.wasPressedThisFrame) UseTool();
            RefreshTiles();
        }
        bool Near(Vector3 pos) => Vector2.Distance(new Vector2(player.position.x,player.position.z),new Vector2(pos.x,pos.z)) <= settings.reach;
        void Interact()
        {
            if (Near(warehouse)) { shop = true; return; }
            if (Near(well)) { State.water = settings.waterCapacity; message = "물을 채웠습니다."; dirty = true; return; }
            if (!hasTarget) { message = "가까운 밭에 마우스를 올리거나 창고·우물에 다가가세요."; return; }
            var t = State.At(target.x,target.y);
            bool ok = t != null && t.remaining > 0 ? State.Harvest(t,settings) : tool == 2 ? State.Water(t) : State.Plant(t);
            message = ok ? "작업 완료" : "밭 상태, 손에 든 작물, 물의 양을 확인하세요."; dirty |= ok;
        }
        void UseTool()
        {
            var t = State.At(target.x,target.y);
            bool ok = tool == 0 ? State.Till(target.x,target.y) : tool == 2 ? State.Water(t) : t != null && t.remaining > 0 ? State.Harvest(t,settings) : State.Plant(t);
            message = ok ? "작업 완료" : "이 작업을 할 수 없습니다. 도구와 밭 상태를 확인하세요."; dirty |= ok;
        }
        void RefreshTiles()
        {
            foreach (var t in State.tiles) {
                var cell = new Vector2Int(t.x,t.z);
                string signature = $"{t.crop}:{t.watered}:{t.remaining}";
                if (tiles.TryGetValue(cell,out var old) && old.name == signature) continue;
                if (old != null) Destroy(old);
                var root = new GameObject(signature); root.transform.SetParent(transform); tiles[cell] = root;
                var soil = PlaceAsset(settings.soilPrefab,new Vector3(t.x,.015f,t.z),root.transform);
                DisableColliders(soil);
                if (t.watered) foreach (var r in soil.GetComponentsInChildren<Renderer>()) {
                    var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
                    var mat = r.sharedMaterial;
                    if (mat != null && mat.HasProperty("_BaseColor")) block.SetColor("_BaseColor",mat.GetColor("_BaseColor") * new Color(.6f,.6f,.6f,1));
                    r.SetPropertyBlock(block);
                }
                if (t.crop < 0) continue;
                int count = Mathf.Max(1,t.remaining);
                for (int i = 0; i < count; i++) {
                    float angle = i*Mathf.PI*2/count;
                    Vector3 pos = new Vector3(t.x+Mathf.Cos(angle)*.22f,.16f,t.z+Mathf.Sin(angle)*.22f);
                    var crop = settings.crops[t.crop];
                    GameObject visual;
                    if (t.remaining > 0 && crop.prefab != null) {
                        visual = Instantiate(crop.prefab,pos,Quaternion.identity,root.transform);
                        foreach (var c in visual.GetComponentsInChildren<Collider>()) { c.enabled = false; Destroy(c); }
                        var rs = visual.GetComponentsInChildren<Renderer>();
                        if (rs.Length > 0) { var bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds); float extent = Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z)); if (extent > .001f) visual.transform.localScale *= .35f/extent; }
                    } else {
                        visual = Shape("Crop",PrimitiveType.Sphere,pos,Vector3.one*(t.remaining>0?.3f:.12f),t.remaining>0?crop.color:Color.green); visual.transform.SetParent(root.transform);
                    }
                }
            }
        }
        void OnGUI()
        {
            if (State == null) return;
            GUILayout.BeginArea(new Rect(12,12,Mathf.Min(600,Screen.width-24),155),GUI.skin.box);
            GUILayout.Label($"자유 농장   돈 {State.money}   물 {State.water}/{settings.waterCapacity}   밭 {State.radius*2+1} × {State.radius*2+1}");
            string held = string.Join(", ",State.hands.ConvertAll(i=>settings.crops[i].name));
            GUILayout.Label($"손 {State.hands.Count}/{settings.handCapacity}: {held}   |   도구: {new[]{"괭이","작물","물뿌리개"}[tool]}");
            GUILayout.Label("WASD 이동 · Shift 달리기 · 우클릭 회전 · 휠 줌 · Esc 일시정지");
            GUILayout.Label("1 괭이 / 2 작물 / 3 물 · 좌클릭 사용 · E 심기/수확/물/창고 · F5 저장");
            GUILayout.Label(message);
            if (hasTarget) { var t = State.At(target.x,target.y); GUILayout.Label(t==null?"빈 땅: 괭이로 갈기":t.crop<0?"갈린 땅: 작물을 묻으세요":t.remaining>0?$"수확 가능 {t.remaining}개":!t.watered?"물이 필요합니다":$"성장 중 {Mathf.Clamp01(t.elapsed/settings.crops[t.crop].seconds):P0}"); }
            GUILayout.EndArea();
            if (paused) { GUI.Box(new Rect(Screen.width/2-160,Screen.height/2-45,320,90),"일시정지 — Esc로 계속"); return; }
            if (!shop) return;
            GUILayout.BeginArea(new Rect(Screen.width/2-190,180,380,400),GUI.skin.box);
            GUILayout.Label("창고 · 판매와 투자");
            if (GUILayout.Button($"시작 당근 꺼내기 (보관 {State.reserve}개)")) {
                if (State.reserve>0 && State.hands.Count<settings.handCapacity) { State.reserve--; State.hands.Add(0); dirty = true; }
            }
            if (GUILayout.Button("손에 든 작물 전부 판매")) { dirty |= State.Sell(settings); }
            for (int i = 1; i < settings.crops.Length; i++) if (GUILayout.Button($"{settings.crops[i].name} 구매 — {settings.crops[i].buy}")) { bool ok = State.Buy(i,settings); dirty |= ok; message = ok?"구매했습니다.":"돈이나 손의 빈자리가 부족합니다."; }
            if (GUILayout.Button($"밭 한 줄씩 확장 — {settings.expansionPrice} (최대 {settings.maximumRadius*2+1}칸)")) { if (State.Expand(settings)) { dirty=true; RefreshBoundary(); } else message="확장 비용이 부족하거나 최대 크기입니다."; }
            // Selling all starter produce must not make a sandbox permanently unplayable.
            if (State.money < settings.crops[1].buy && State.reserve==0 && State.hands.Count==0 && !State.tiles.Exists(t=>t.crop>=0) && GUILayout.Button("재시작용 당근 1개 받기")) { State.hands.Add(0); dirty=true; }
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("새 농장 시작…")) confirmReset = !confirmReset;
            if (confirmReset && GUILayout.Button("현재 싱글 농장을 초기화합니다 — 확인")) ResetFarm();
            if (GUILayout.Button("닫기 (Esc)")) { shop=false; confirmReset=false; Save(); }
            GUILayout.EndArea();
        }
        void ResetFarm()
        {
            State = SoloFarmState.New(settings); foreach (var go in tiles.Values) Destroy(go); tiles.Clear();
            saveBlocked=false; dirty=true; confirmReset=false; RefreshBoundary(); Save();
        }
        void Load()
        {
            if (!File.Exists(SavePath)) return;
            try { var saved = JsonUtility.FromJson<SoloFarmState>(File.ReadAllText(SavePath)); if (saved == null || !saved.Valid(settings)) throw new InvalidDataException("Invalid farm save"); State=saved; message="저장된 농장을 불러왔습니다."; }
            catch (Exception e) { saveBlocked=true; message="저장 읽기 실패. 원본을 보존했습니다. 새 농장 확인 후 저장할 수 있습니다."; Debug.LogWarning(e.Message); }
        }
        public void Save()
        {
            if (State == null || saveBlocked) return;
            try {
                string temp = SavePath+".tmp"; File.WriteAllText(temp,JsonUtility.ToJson(State,true));
                if (File.Exists(SavePath)) File.Replace(temp,SavePath,SavePath+".bak"); else File.Move(temp,SavePath);
                dirty=false; message="농장을 저장했습니다.";
            } catch (Exception e) { message="저장 실패: "+e.Message; Debug.LogWarning(message); }
        }
        void OnApplicationPause(bool value) { if (value) Save(); }
        void OnDestroy() { if (State != null && (dirty || !saveBlocked)) Save(); foreach (var m in materials) if (m != null) Destroy(m); }
    }
}

