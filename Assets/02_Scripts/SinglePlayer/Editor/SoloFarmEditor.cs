using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LowPolyFarm.SinglePlayer.Editor
{
    public static class SoloFarmEditor
    {
        public const string ScenePath = "Assets/01_Scene/Single.unity";
        [InitializeOnLoadMethod]
        static void CompleteRequestedSetup()
        {
            const string request = "Temp/SingleVerification/open.request";
            if (!System.IO.File.Exists(request)) return;
            EditorApplication.delayCall += () => {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                System.IO.File.Delete(request);
                ValidateLoop();
                for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) {
                        Debug.Log("Single scene ready. Open it through LowPolyFarm > Single when your current scene is saved."); return;
                    }
                Open();
            };
        }
        [MenuItem("LowPolyFarm/Single/Open Single Scene")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) { EditorSceneManager.OpenScene(ScenePath); return; }
            System.IO.Directory.CreateDirectory("Assets/01_Scene");
            var settings = AssetDatabase.LoadAssetAtPath<SoloFarmSettings>("Assets/12_Settings/SinglePlayer/SoloSettings.asset");
            if (settings == null) {
                settings = ScriptableObject.CreateInstance<SoloFarmSettings>();
                string[] names = { "Carrot", "Cabbage", "Pumpkin" };
                for(int i=0;i<names.Length;i++) settings.crops[i].prefab=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/04_Models/{names[i]}/Prefabs/{names[i]}.prefab");
                settings.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03_Prefabs/Player.prefab");
                settings.wellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/04_Models/StoneWell/Prefabs/StoneWell_Low5k.prefab");
                settings.soilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/04_Models/Soil/Prefabs/Soil_Base.prefab");
                settings.hoePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Veresen/BasicTools/Prefabs/hoe.prefab");
                AssetDatabase.CreateAsset(settings,"Assets/12_Settings/SinglePlayer/SoloSettings.asset");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Single Farm"); root.AddComponent<SoloFarmGame>().settings=settings;
            EditorSceneManager.SaveScene(scene,ScenePath);
            SoloScenePlacement.Place();
            Selection.activeGameObject=root;
        }

        [MenuItem("LowPolyFarm/Single/Validate Farming Loop")]
        public static void ValidateLoop()
        {
            var s = ScriptableObject.CreateInstance<SoloFarmSettings>();
            try {
                var f=SoloFarmState.New(s); var rng=new System.Random(123);
                Require(!f.Till(99,99),"out of bounds");
                Require(f.Till(0,0) && !f.Till(0,0),"tilling");
                var t=f.At(0,0);
                Require(!f.Plant(t),"empty hands"); f.hands.Add(0);
                Require(f.Plant(t) && f.hands.Count==0,"plant consumes crop");
                f.Tick(100,s,rng); Require(t.remaining==0 && t.elapsed==0,"water required");
                Require(!f.Water(t),"empty water"); f.water=3;
                Require(f.Water(t) && f.water==2 && !f.Water(t),"water consumption");
                f.Tick(19,s,rng); Require(t.remaining==0,"growth duration");
                f.Tick(1,s,rng); Require(t.remaining>=2 && t.remaining<=5,"yield");
                Require(f.Harvest(t,s) && f.Harvest(t,s),"harvesting");
                Require(!f.Harvest(t,s),"hand limit");
                Require(f.Sell(s) && f.money==20 && f.hands.Count==0,"sales");
                Require(!f.Buy(1,s),"insufficient money"); f.money=200;
                Require(f.Buy(1,s) && f.money==160,"purchase");
                Require(f.Expand(s) && f.money==110 && f.radius==4,"expansion");
                var copy=JsonUtility.FromJson<SoloFarmState>(JsonUtility.ToJson(f));
                Require(copy.Valid(s) && copy.money==f.money && copy.hands[0]==1,"save roundtrip");
                copy.hands.Add(99); Require(!copy.Valid(s),"invalid save rejected");
                Debug.Log("Single farm: 16 farming/economy/save checks passed.");
            } finally { UnityEngine.Object.DestroyImmediate(s); }
        }
        static void Require(bool condition,string name) { if(!condition) throw new Exception("Single farm check failed: "+name); }
    }
}
