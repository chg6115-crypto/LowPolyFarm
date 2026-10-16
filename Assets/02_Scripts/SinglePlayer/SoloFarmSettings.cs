using System;
using System.Collections.Generic;
using UnityEngine;

namespace LowPolyFarm.SinglePlayer
{
    [CreateAssetMenu(menuName = "LowPolyFarm/Single Player/Settings")]
    public sealed class SoloFarmSettings : ScriptableObject
    {
        public int startingCrops = 3, handCapacity = 2, waterCapacity = 3;
        public int initialRadius = 3, maximumRadius = 9, expansionPrice = 50;
        public float reach = 2.5f;
        [Header("Existing project assets")]
        public GameObject playerPrefab, wellPrefab, soilPrefab, hoePrefab;
        public Vector3 hoeLocalPosition = new Vector3(0f, .02f, .02f);
        public Vector3 hoeLocalEuler = new Vector3(0f, 0f, -75f);
        [Tooltip("Held hoe longest dimension in world metres; compensates imported hand-bone scale.")]
        public float hoeScale = .35f;
        public Crop[] crops = {
            new Crop { name = "당근", sell = 10, buy = 0, seconds = 20, weights = new int[] { 5, 4, 1, 0 }, color = new Color(1,.45f,.1f) },
            new Crop { name = "양배추", sell = 25, buy = 40, seconds = 35, weights = new int[] { 2, 4, 3, 1 }, color = new Color(.4f,.8f,.3f) },
            new Crop { name = "호박", sell = 60, buy = 100, seconds = 50, weights = new int[] { 0, 2, 4, 4 }, color = new Color(1,.65f,.1f) }
        };
        [Serializable] public sealed class Crop
        {
            public string name;
            public int sell, buy;
            public float seconds;
            public int[] weights;
            public Color color;
            public GameObject prefab;
            public int Yield(System.Random random)
            {
                int total = 0;
                for (int i = 0; i < 4; i++) total += Mathf.Max(0, weights[i]);
                if (total == 0) return 2;
                int pick = random.Next(total);
                for (int i = 0; i < 4; i++) { pick -= Mathf.Max(0, weights[i]); if (pick < 0) return i + 2; }
                return 2;
            }
        }
    }

    [Serializable] public sealed class SoloFarmState
    {
        public int version = 1, money, radius, water, reserve;
        public List<int> hands = new List<int>();
        public List<Tile> tiles = new List<Tile>();
        [Serializable] public sealed class Tile
        {
            public int x, z, crop = -1, remaining;
            public bool watered;
            public float elapsed;
        }
        public static SoloFarmState New(SoloFarmSettings s) => new SoloFarmState { radius = s.initialRadius, reserve = s.startingCrops };
        public Tile At(int x, int z) => tiles.Find(t => t.x == x && t.z == z);
        public bool Till(int x, int z)
        {
            if (Mathf.Abs(x) > radius || Mathf.Abs(z) > radius || At(x,z) != null) return false;
            tiles.Add(new Tile { x = x, z = z }); return true;
        }
        public bool Plant(Tile t)
        {
            if (t == null || t.crop >= 0 || hands.Count == 0) return false;
            t.crop = hands[0]; hands.RemoveAt(0); t.elapsed = 0; t.watered = false; return true;
        }
        public bool Water(Tile t)
        {
            if (t == null || t.crop < 0 || t.watered || water <= 0) return false;
            water--; t.watered = true; return true;
        }
        public bool Harvest(Tile t, SoloFarmSettings s)
        {
            if (t == null || t.remaining <= 0 || hands.Count >= s.handCapacity) return false;
            hands.Add(t.crop);
            if (--t.remaining == 0) { t.crop = -1; t.watered = false; t.elapsed = 0; }
            return true;
        }
        public void Tick(float delta, SoloFarmSettings s, System.Random random)
        {
            foreach (var t in tiles)
                if (t.crop >= 0 && t.watered && t.remaining == 0) {
                    t.elapsed += Mathf.Max(0, delta);
                    if (t.elapsed >= s.crops[t.crop].seconds) t.remaining = s.crops[t.crop].Yield(random);
                }
        }
        public bool Buy(int id, SoloFarmSettings s)
        {
            if (id < 0 || id >= s.crops.Length || s.crops[id].buy <= 0 || hands.Count >= s.handCapacity || money < s.crops[id].buy) return false;
            money -= s.crops[id].buy; hands.Add(id); return true;
        }
        public bool Sell(SoloFarmSettings s)
        {
            if (hands.Count == 0) return false;
            foreach (int id in hands) money += s.crops[id].sell;
            hands.Clear(); return true;
        }
        public bool Expand(SoloFarmSettings s)
        {
            if (radius >= s.maximumRadius || money < s.expansionPrice) return false;
            money -= s.expansionPrice; radius++; return true;
        }
        public bool Valid(SoloFarmSettings s)
        {
            if (version != 1 || money < 0 || radius < s.initialRadius || radius > s.maximumRadius || water < 0 || water > s.waterCapacity || reserve < 0 || reserve > s.startingCrops || hands == null || hands.Count > s.handCapacity || tiles == null) return false;
            foreach (int id in hands) if (id < 0 || id >= s.crops.Length) return false;
            var used = new HashSet<Vector2Int>();
            foreach (var t in tiles) {
                if (t == null || Mathf.Abs(t.x) > radius || Mathf.Abs(t.z) > radius || !used.Add(new Vector2Int(t.x,t.z)) || t.crop < -1 || t.crop >= s.crops.Length || t.remaining < 0 || t.remaining > 5 || float.IsNaN(t.elapsed) || float.IsInfinity(t.elapsed) || t.elapsed < 0) return false;
                if (t.crop == -1 && (t.watered || t.remaining != 0)) return false;
                if (t.remaining > 0 && !t.watered) return false;
            }
            return true;
        }
    }
}
