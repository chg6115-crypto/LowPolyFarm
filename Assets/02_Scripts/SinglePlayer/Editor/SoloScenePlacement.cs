using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LowPolyFarm.SinglePlayer.Editor
{
    // Authoring only: this never runs on Play and never replaces an existing layout.
    public static class SoloScenePlacement
    {
        [MenuItem("LowPolyFarm/Single/Place Scene Objects")]
        public static void Place()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != SoloFarmEditor.ScenePath)
                throw new System.InvalidOperationException("Open Single in Edit mode first.");
            var game = Object.FindAnyObjectByType<SoloFarmGame>();
            if (game == null) throw new System.InvalidOperationException("Single Farm component missing.");
            var serialized = new SerializedObject(game);
            if (serialized.FindProperty("player").objectReferenceValue != null) {
                Debug.Log("Scene objects already placed. Edit their transforms directly in the Hierarchy."); return;
            }
            Undo.RecordObject(game,"Connect Single scene objects");
            var world = Group("Environment", game.transform);
            var props = Group("Farm Facilities", game.transform);
            var actors = Group("Actors", game.transform);
            var presentation = Group("Camera and Lighting", game.transform);
            var indicators = Group("Farm Indicators", game.transform);
            Cube("Ground",world,new Vector3(0,-.3f,0),new Vector3(36,.6f,36),Material("Meadow",new Color(.37f,.57f,.28f)),true);
            var house = Group("Warehouse",props); house.position = new Vector3(-6,0,-12);
            Cube("Body",house,Vector3.up,new Vector3(2,2,2),Material("Warehouse",new Color(.65f,.38f,.23f)),true);
            Cube("Roof",house,Vector3.up*2.1f,new Vector3(2.5f,.3f,2.5f),Material("Roof",new Color(.3f,.2f,.15f)),true);
            Label("WAREHOUSE / E",house,new Vector3(0,3,0));
            var well = Prefab(game.settings.wellPrefab,props,new Vector3(6,0,-12));
            if (well.GetComponentsInChildren<Collider>().Length == 0)
                foreach (var mf in well.GetComponentsInChildren<MeshFilter>()) {
                    var c=Undo.AddComponent<MeshCollider>(mf.gameObject); c.sharedMesh=mf.sharedMesh;
                }
            Label("WATER / E",well.transform,new Vector3(0,2,0));
            var player = Prefab(game.settings.playerPrefab,actors,new Vector3(-6,.07f,-14));
            // Only this scene instance is offline. The original shared prefab is untouched.
            foreach (var c in player.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>(true)) { c.enabled=false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
            foreach (var c in player.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true)) { c.enabled=false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
            var cameraRoot=Group("Main Camera",presentation);
            var camera=Undo.AddComponent<Camera>(cameraRoot.gameObject);
            Undo.AddComponent<AudioListener>(cameraRoot.gameObject);
            camera.tag="MainCamera"; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.65f,.82f,.9f); camera.farClipPlane=150;
            var orbit=Undo.AddComponent<OrbitFollowCamera>(cameraRoot.gameObject);
            orbit.target=player.transform;
            cameraRoot.rotation=Quaternion.Euler(orbit.pitch,orbit.yaw,0);
            cameraRoot.position=player.transform.position+Vector3.up*orbit.pivotHeight-cameraRoot.forward*orbit.distance;
            var movement=player.GetComponent<PlayerMovement>(); movement.cameraTransform=cameraRoot;
            PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
            var sunRoot=Group("Sun",presentation); sunRoot.rotation=Quaternion.Euler(50,-30,0);
            var sun=Undo.AddComponent<Light>(sunRoot.gameObject); sun.type=LightType.Directional; sun.intensity=1.5f;
            Transform hand=null;
            foreach(var bone in player.GetComponentsInChildren<Transform>(true)) if(bone.name=="RightHand") { hand=bone; break; }
            if(hand==null) throw new System.InvalidOperationException("Player RightHand bone missing.");
            var hoe=Prefab(game.settings.hoePrefab,hand,Vector3.zero); hoe.name="Held Hoe";
            hoe.transform.position=hand.position+hand.rotation*game.settings.hoeLocalPosition;
            hoe.transform.localRotation=Quaternion.Euler(game.settings.hoeLocalEuler);
            var renderers=hoe.GetComponentsInChildren<Renderer>();
            if(renderers.Length>0) {
                var b=renderers[0].bounds; foreach(var r in renderers)b.Encapsulate(r.bounds);
                float longest=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
                if(longest>.0001f)hoe.transform.localScale*=game.settings.hoeScale/longest;
            }
            foreach(var c in hoe.GetComponentsInChildren<Collider>(true)) { c.enabled=false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
            PrefabUtility.RecordPrefabInstancePropertyModifications(hoe.transform);
            Prefab(game.settings.hoePrefab,house,new Vector3(1.6f,0,0)).name="Spare Hoe";
            var walls=Group("Map Bounds",world);
            for(int i=0;i<4;i++) {
                bool x=i<2;float side=i%2==0?17.5f:-17.5f;
                var wall=Group("Boundary "+i,walls); wall.localPosition=new Vector3(x?side:0,2,x?0:side);
                Undo.AddComponent<BoxCollider>(wall.gameObject).size=new Vector3(x?1:36,4,x?36:1);
            }
            var marker=Cube("Interaction Target",indicators,Vector3.zero,new Vector3(.95f,.025f,.95f),Material("Target",Color.yellow),false);
            marker.SetActive(false);
            var boundary=Group("Farm Boundary",indicators);
            float edge=game.settings.initialRadius+.5f;
            for(int i=0;i<4;i++) {
                bool x=i<2;float side=i%2==0?edge:-edge;
                Cube("Edge "+i,boundary,new Vector3(x?side:0,.03f,x?0:side),new Vector3(x?.06f:edge*2,.04f,x?edge*2:.06f),Material("Boundary",new Color(.95f,.85f,.5f)),false);
            }
            serialized.FindProperty("player").objectReferenceValue=player.transform;
            serialized.FindProperty("warehousePoint").objectReferenceValue=house;
            serialized.FindProperty("wellPoint").objectReferenceValue=well.transform;
            serialized.FindProperty("view").objectReferenceValue=camera;
            serialized.FindProperty("heldHoe").objectReferenceValue=hoe;
            serialized.FindProperty("marker").objectReferenceValue=marker.transform;
            serialized.FindProperty("boundary").objectReferenceValue=boundary;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            Selection.activeGameObject=game.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(0,0,-5),new Vector3(30,10,30)),false);
            Debug.Log("Single scene objects placed and saved. No static world generation on Play.");
        }
        static Transform Group(string name,Transform parent)
        {
            var go=new GameObject(name); Undo.RegisterCreatedObjectUndo(go,"Place Single objects"); go.transform.SetParent(parent,false); return go.transform;
        }
        static GameObject Prefab(GameObject source,Transform parent,Vector3 localPosition)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);
            Undo.RegisterCreatedObjectUndo(go,"Place Single prefab");
            go.transform.localPosition=localPosition; PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform); return go;
        }
        static GameObject Cube(string name,Transform parent,Vector3 pos,Vector3 scale,Material mat,bool collide)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); Undo.RegisterCreatedObjectUndo(go,"Place Single objects");
            go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=mat;
            if(!collide)Undo.DestroyObjectImmediate(go.GetComponent<Collider>()); return go;
        }
        static Material Material(string name,Color color)
        {
            const string folder="Assets/05_Materials/SinglePlayer";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/05_Materials","SinglePlayer");
            string path=folder+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=color};AssetDatabase.CreateAsset(mat,path);} return mat;
        }
        static void Label(string text,Transform parent,Vector3 position)
        {
            var root=Group("Label",parent);root.localPosition=position;root.rotation=Quaternion.Euler(65,0,0);
            var mesh=Undo.AddComponent<TextMesh>(root.gameObject);mesh.text=text;mesh.characterSize=.18f;mesh.fontSize=40;mesh.anchor=TextAnchor.MiddleCenter;mesh.color=Color.white;
        }
    }
}
