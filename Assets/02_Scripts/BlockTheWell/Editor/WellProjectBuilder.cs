using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace LowPolyFarm.BlockTheWell.Editor
{
    public static class WellProjectBuilder
    {
        public const string ScenePath="Assets/01_Scene/BlockTheWell.unity";
        [MenuItem("LowPolyFarm/Block The Well/Create Game Scene")]
        public static void Create()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
            if(File.Exists(ScenePath)){EditorSceneManager.OpenScene(ScenePath);return;}
            for(int i=SceneManager.sceneCount-1;i>=0;i--) {
                var failed=SceneManager.GetSceneAt(i);
                if(!string.IsNullOrEmpty(failed.path))continue;
                foreach(var go in failed.GetRootGameObjects())if(go.GetComponent<WellGame>()!=null){EditorSceneManager.CloseScene(failed,true);break;}
            }
            // Unity disallows additive creation beside an unnamed unsaved scene.
            // Preserve any unrelated unnamed content as a new backup, never discard it.
            for(int i=0;i<SceneManager.sceneCount;i++) {
                var open=SceneManager.GetSceneAt(i);
                if(string.IsNullOrEmpty(open.path) && open.rootCount>0)
                    EditorSceneManager.SaveScene(open,"Assets/01_Scene/PreservedScene_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+i+".unity");
            }
            bool empty=SceneManager.sceneCount==1 && SceneManager.GetActiveScene().rootCount==0 && string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,empty?NewSceneMode.Single:NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root=new GameObject("Block The Well");var game=root.AddComponent<WellGame>();
            var prefabPlayer=Load("Assets/03_Prefabs/Player.prefab");
            var player=Prefab(prefabPlayer,root.transform,Vector3.zero);
            foreach(var c in player.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>(true)){c.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
            foreach(var c in player.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true)){c.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
            var debug=player.GetComponent<CharacterMotionDebug>();if(debug!=null){debug.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(debug);}
            game.player=player.GetComponent<PlayerMovement>();game.player.walkSpeed=2.2f;game.player.runMultiplier=1;game.player.cameraRelative=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(game.player);
            var camera=new GameObject("Main Camera");camera.tag="MainCamera";camera.transform.SetParent(root.transform);
            var cam=camera.AddComponent<Camera>();cam.backgroundColor=new Color(.62f,.79f,.87f);cam.clearFlags=CameraClearFlags.SolidColor;cam.farClipPlane=100;
            camera.AddComponent<AudioListener>();game.orbit=camera.AddComponent<OrbitFollowCamera>();game.orbit.target=player.transform;game.orbit.pitch=48;game.orbit.distance=13;game.orbit.maxDistance=18;game.orbit.minDistance=7;game.orbit.minPitch=30;game.orbit.maxPitch=70;
            game.player.cameraTransform=camera.transform;camera.transform.rotation=Quaternion.Euler(48,0,0);camera.transform.position=new Vector3(0,10,-13);
            var sun=new GameObject("Sun");sun.transform.SetParent(root.transform);sun.transform.rotation=Quaternion.Euler(50,-30,0);var light=sun.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;
            game.stages=new WellStage[3];
            game.stages[0]=Stage(root.transform,1,new Vector3(0,0,-5),new[]{new Vector3(0,0,-2)},new[]{new Vector3(0,0,3)},Array.Empty<Vector3>());
            game.stages[1]=Stage(root.transform,2,new Vector3(0,0,-5),new[]{new Vector3(0,0,-2)},new[]{new Vector3(0,0,4)},new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(4,0,0)});
            game.stages[2]=Stage(root.transform,3,new Vector3(0,0,-5),new[]{new Vector3(-3,0,-2),new Vector3(3,0,-2)},new[]{new Vector3(-3,0,4),new Vector3(3,0,4)},new[]{new Vector3(0,0,-1),new Vector3(0,0,1),new Vector3(-5,0,1),new Vector3(5,0,1)});
            for(int i=0;i<3;i++)game.stages[i].gameObject.SetActive(i==0);
            player.transform.position=game.stages[0].spawn.position;PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            BuildUI(game,root.transform);
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            File.AppendAllText("Development_Log.md","\n- Unity 에디터 씬 생성 완료: "+DateTime.Now+". 에셋 실제 Renderer bounds를 측정해 배치 크기를 맞춤. 기존 씬은 덮어쓰지 않음.\n");
            // Only close clean scenes after the new scene is safely saved.
            for(int i=SceneManager.sceneCount-1;i>=0;i--){var other=SceneManager.GetSceneAt(i);if(other!=scene && !other.isDirty)EditorSceneManager.CloseScene(other,true);}
            Selection.activeGameObject=root;
        }
        static WellStage Stage(Transform parent,int n,Vector3 spawn,Vector3[] rocks,Vector3[] wells,Vector3[] obstacles)
        {
            var root=Group("Stage "+n,parent);var stage=root.gameObject.AddComponent<WellStage>();
            stage.title=new[]{"","Stage 1 - First prank","Stage 2 - Take the detour","Stage 3 - Double trouble"}[n];
            stage.hint=n==1?"WASD: move | Push the rock onto the well, then stop. | R: retry":n==2?"Push left around the wall. Walk around the rock to change push direction.":"Block BOTH wells. Complete one lane, then walk around to the other rock.";
            stage.spawn=Group("Player Spawn",root);stage.spawn.localPosition=spawn+Vector3.up*.08f;
            var ground=Group("Tiles",root);
            var soil=Load("Assets/04_Models/Soil/Prefabs/Soil_Base.prefab");
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++) {
                var tile=Visual(soil,ground,new Vector3(x*2,0,z*2),1.98f);
                tile.name=$"Tile {x},{z}";var bounds=BoundsOf(tile);tile.transform.position+=Vector3.up*(-bounds.max.y);
            }
            // One continuous collision floor avoids seams between decorative tile meshes.
            Box("Collision floor",root,new Vector3(0,-.25f,0),new Vector3(14,.5f,14),null,false);
            for(int i=0;i<4;i++){bool x=i<2;float side=i%2==0?7.2f:-7.2f;Box("Outer fence "+i,root,new Vector3(x?side:0,.6f,x?0:side),new Vector3(x?.35f:14.7f,1.2f,x?14.7f:.35f),Mat("Fence",new Color(.39f,.27f,.17f)));}
            foreach(var pos in obstacles)Box("Obstacle",root,pos+Vector3.up*.6f,new Vector3(1.8f,1.2f,1.8f),Mat("Obstacle",new Color(.34f,.4f,.27f)));
            var rockAsset=Load("Assets/04_Models/Rock/Prefabs/Rock_3k.prefab");
            stage.rocks=new WellRock[rocks.Length];
            for(int i=0;i<rocks.Length;i++) {
                var holder=Group("Rock "+(i+1),root);holder.localPosition=rocks[i];
                var model=Visual(rockAsset,holder,Vector3.zero,1.2f);var b=BoundsOf(model);model.transform.position+=holder.position-new Vector3(b.center.x,b.min.y,b.center.z);
                var collider=holder.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,.52f,0);collider.size=new Vector3(1.1f,1.04f,1.1f);
                var rb=holder.gameObject.AddComponent<Rigidbody>();rb.mass=8;rb.linearDamping=4;rb.angularDamping=8;rb.constraints=RigidbodyConstraints.FreezeRotation;rb.interpolation=RigidbodyInterpolation.Interpolate;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                holder.gameObject.AddComponent<Pushable>().speedFactor=.7f;stage.rocks[i]=holder.gameObject.AddComponent<WellRock>();
            }
            var wellAsset=Load("Assets/04_Models/StoneWell/Prefabs/StoneWell_Low5k.prefab");stage.wells=new WellBlockZone[wells.Length];
            for(int i=0;i<wells.Length;i++) {
                var holder=Group("Well "+(i+1),root);holder.localPosition=wells[i];
                var model=Visual(wellAsset,holder,Vector3.zero,1f);var b=BoundsOf(model);
                model.transform.position+=holder.position-new Vector3(b.center.x,b.max.y-.025f,b.center.z);
                // The masonry is sunk below the walking surface. No solid collider caps the opening.
                var zone=Group("BlockZone",holder);var trigger=zone.gameObject.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.center=Vector3.up*.55f;trigger.size=new Vector3(1.3f,1.4f,1.3f);
                stage.wells[i]=zone.gameObject.AddComponent<WellBlockZone>();
            }
            return stage;
        }
        static void BuildUI(WellGame game,Transform parent)
        {
            var uiObject=new GameObject("UI",typeof(RectTransform));uiObject.transform.SetParent(parent,false);
            var root=uiObject.transform;var canvas=uiObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=root.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);root.gameObject.AddComponent<GraphicRaycaster>();
            var events=Group("EventSystem",parent);events.gameObject.AddComponent<EventSystem>();events.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            game.heading=Text("Title",root,new Vector2(0,-25),new Vector2(1200,45),30);
            game.status=Text("Status",root,new Vector2(0,-75),new Vector2(1200,35),22);
            game.instruction=Text("Instructions",root,new Vector2(0,-115),new Vector2(1200,60),20);
            Text("Controls",root,new Vector2(0,-645),new Vector2(1200,35),18).text="WASD move   |   Right mouse orbit   |   Wheel zoom   |   R restart";
            game.actionButton=Button("Continue",root,new Vector2(0,-565),"START",out game.buttonText);
            game.restartButton=Button("Restart",root,new Vector2(-360,-565),"RESTART (R)",out _);
            game.quitButton=Button("Quit",root,new Vector2(360,-565),"QUIT",out _);
        }
        static Text Text(string name,Transform parent,Vector2 pos,Vector2 size,int fontSize)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
            var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(.5f,1);rt.pivot=new Vector2(.5f,1);rt.anchoredPosition=pos;rt.sizeDelta=size;
            var text=go.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=fontSize;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            var outline=go.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.85f);return text;
        }
        static Button Button(string name,Transform parent,Vector2 pos,string label,out Text text)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(.5f,1);rt.pivot=new Vector2(.5f,1);rt.anchoredPosition=pos;rt.sizeDelta=new Vector2(240,55);
            var image=go.AddComponent<Image>();image.color=new Color(.14f,.24f,.16f,.95f);var button=go.AddComponent<Button>();button.targetGraphic=image;
            text=Text("Label",go.transform,Vector2.zero,new Vector2(240,55),22);text.text=label;return button;
        }
        static GameObject Load(string path){var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(go==null)throw new FileNotFoundException(path);return go;}
        static Transform Group(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static GameObject Prefab(GameObject prefab,Transform parent,Vector3 pos){var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.transform.localPosition=pos;return go;}
        static GameObject Visual(GameObject prefab,Transform parent,Vector3 pos,float width)
        {
            var go=Prefab(prefab,parent,pos);foreach(var c in go.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
            var b=BoundsOf(go);go.transform.localScale*=width/Mathf.Max(b.size.x,b.size.z);PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);return go;
        }
        static Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
        static Material Mat(string name,Color color){const string folder="Assets/05_Materials/BlockTheWell";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/05_Materials","BlockTheWell");string path=folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=color};AssetDatabase.CreateAsset(m,path);}return m;}
        static GameObject Box(string name,Transform parent,Vector3 pos,Vector3 size,Material material,bool visible=true){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;go.GetComponent<Renderer>().enabled=visible;if(material!=null)go.GetComponent<Renderer>().sharedMaterial=material;return go;}
        [MenuItem("LowPolyFarm/Block The Well/Build Windows")]
        public static void Build()
        {
            Directory.CreateDirectory("Builds/BlockTheWell");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/BlockTheWell/BlockTheWell.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Temp/BlockWell/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("PC build failed; see Unity Console.");
        }
    }
}

