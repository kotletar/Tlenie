using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Строит город по карте CityMap: пол и разметка, здания (районы с разными фасадами), переулки,
    /// машины, лампы, очаги, наросты Тьмы, люди, генератор, автовокзал, небо и ориентиры.
    /// Геометрия собирается в несколько мешей (MeshBatch) — сотни объектов за несколько draw call'ов.
    /// Раскладка детерминирована (seed). Хотите свой арт — заменяйте AddCar/AddSolid и т.п. на Instantiate(префаб).
    /// </summary>
    public static partial class LevelBuilder
    {
        public static Vector3 StartPos = new Vector3(33f, 0.1f, 45f);
        public const float StartYaw = 0f;
        public static Vector3 StationPos = new Vector3(63f, 0.1f, 357f);
        public static Vector3 BusPos = new Vector3(63f, 0f, 381f);

        static readonly float[] HMin = { 14f, 9f, 18f, 11f };
        static readonly float[] HMax = { 26f, 15f, 32f, 20f };

        /// <summary>Размер чанка (м): город режется на куски, камера и ChunkCuller рисуют только близкие.</summary>
        const float ChunkSize = 48f;
        const int NB = MeshBatch.NoBottom;

        /// <summary>Высота здания в каждой клетке города (0 — не здание): по ней предыстория рисует огни в окнах.</summary>
        public static readonly float[,] BuildingHeight = new float[CityMap.W, CityMap.H];

        /// <summary>Центры дверей и витрин на фасадах (м, на уровне земли у стены).</summary>
        public static IList<Vector3> Entrances { get { return entrances; } }

        static System.Random rng;
        static Transform rootT;
        static BatchSet floorS;
        static BatchSet plazaS;
        static BatchSet curbS;
        static BatchSet markS;
        static BatchSet propS;
        static BatchSet[] bldS;
        static readonly List<Mesh> builtMeshes = new List<Mesh>();
        static readonly List<Renderer> chunkRenderers = new List<Renderer>();
        static Material blinkMat;

        /// <summary>Сколько вершин статической геометрии собрано (для статистики) и сколько кусков карты создано.</summary>
        public static int StaticVertices;
        public static int StaticChunks;
        public static ChunkCuller Culler;

        /// <summary>Батч «мелочи» (фонари, хлам, детали) в чанке под точкой p. Работает только пока строится город.</summary>
        public static MeshBatch PropBatchAt(Vector3 p)
        {
            return propS != null ? propS.At(p.x, p.z) : null;
        }

        static GameObject[] beaconObjs;

        /// <summary>Виден ли сейчас столб света над вокзалом (для проверок).</summary>
        public static bool BeaconActive { get { return beaconObjs != null && beaconObjs.Length > 0 && beaconObjs[0] != null && beaconObjs[0].activeSelf; } }

        /// <summary>Столб света над вокзалом: в предыстории (кадр с улицей) его прячут, в игре он ориентир.</summary>
        public static void SetBeaconVisible(bool visible)
        {
            if (beaconObjs == null) return;
            for (int i = 0; i < beaconObjs.Length; i++)
            {
                if (beaconObjs[i] != null) beaconObjs[i].SetActive(visible);
            }
        }

        /// <summary>Тут же положить меш в список, чтобы убрать его при перестройке города.</summary>
        public static void TrackMesh(Mesh m)
        {
            builtMeshes.Add(m);
        }
        static int blinkers;
        static bool towerDone;
        static int chimneys;
        static readonly List<Vector3> hearthPts = new List<Vector3>();

        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly int[] Dx = { 1, -1, 0, 0 };
        static readonly int[] Dz = { 0, 0, 1, -1 };

        /// <summary>
        /// Зерно города: 0 — классика и первый город бесконечного режима (раскладка не меняется ни на байт), дальше города отличаются
        /// лампами, реквизитом и декором (карта улиц та же). Задаёт GameManager перед BuildWorld.
        /// </summary>
        public static int CitySeed;

        public static void Build(Transform root)
        {
            CityMap.Build();
            rootT = root;
            rng = new System.Random(20261114 + CitySeed * 7919);

            // меши прошлого мира (QuitToMenu, перезапуск): сами они со сценой не удаляются
            for (int i = 0; i < builtMeshes.Count; i++)
            {
                if (builtMeshes[i] != null) Object.Destroy(builtMeshes[i]);
            }
            builtMeshes.Clear();
            chunkRenderers.Clear();

            floorS = new BatchSet(ChunkSize);
            plazaS = new BatchSet(ChunkSize);
            curbS = new BatchSet(ChunkSize);
            markS = new BatchSet(ChunkSize);
            propS = new BatchSet(ChunkSize);
            bldS = new BatchSet[4];
            for (int i = 0; i < 4; i++) bldS[i] = new BatchSet(ChunkSize);
            System.Array.Clear(BuildingHeight, 0, BuildingHeight.Length);
            grassS = new BatchSet(ChunkSize);
            signS = new BatchSet(ChunkSize);
            drng = new System.Random(55117 + CitySeed * 104729);
            entrances.Clear();
            shopSpots.Clear();
            grassTiles.Clear();
            treePts.Clear();
            ParkCenters.Clear();
            ShopFronts.Clear();
            ShopFrontNormals.Clear();
            IvyPatches.Clear();
            IvyNormals.Clear();
            SignAtlas.Begin();
            blinkMat = null;
            blinkers = 0;
            towerDone = false;
            chimneys = 0;
            clutterSpots.Clear();

            Pt sp = CityMap.CenterOf(CityMap.StartX, CityMap.StartZ);
            StartPos = new Vector3(sp.X, 0.1f, sp.Z + 6f);
            Pt bp = CityMap.CenterOf(CityMap.BusX, CityMap.BusZ);
            BusPos = new Vector3(bp.X, 0f, bp.Z);
            StationPos = new Vector3(bp.X, 0.1f, bp.Z - 24f);

            hearthPts.Clear();
            for (int i = 0; i < CityMap.Hearths.Count; i++)
            {
                Pt h = CityMap.CenterOf(CityMap.Hearths[i].X, CityMap.Hearths[i].Z);
                hearthPts.Add(new Vector3(h.X, 0f, h.Z));
            }

            BuildSky(root);
            BuildFloor(root);
            BuildBuildings(root);
            BuildTunnel(root);
            BuildBoundaries(root);
            BuildLamps(root);
            BuildHearths(root);
            BuildGrowths(root);
            BuildSurvivors(root);
            BuildGenerators(root);
            stationLamps = StationLampSpots();
            BuildProps(root);
            BuildStation(root);
            BuildDecor(root);
            Flush(root);
            FixSurvivorSpots();
            BuildPizzas(root);
            BuildVaults(root);   // самым последним и со своим генератором: остальной город не сдвигается
        }

        // ------------------------------------------------------------------ помощники

        static float R(float a, float b)
        {
            return a + (float)rng.NextDouble() * (b - a);
        }

        static Vector3 CellCenter(int cx, int cz, float y)
        {
            return new Vector3((cx + 0.5f) * CityMap.Cell, y, (cz + 0.5f) * CityMap.Cell);
        }

        static Color32 Gray(float v)
        {
            byte b = (byte)Mathf.Clamp(v * 255f, 0f, 255f);
            return new Color32(b, b, b, 255);
        }

        static Color32 Rgb(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        static Vector3 RotY(Vector3 v, float yawDeg)
        {
            float rad = yawDeg * 0.0174532925f;
            float cs = Mathf.Cos(rad);
            float sn = Mathf.Sin(rad);
            return new Vector3(v.x * cs + v.z * sn, v.y, -v.x * sn + v.z * cs);
        }

        public static Color DistrictTint(int d)
        {
            switch (d)
            {
                case 0: return new Color(1.00f, 0.78f, 0.42f, 1f);   // жилой: тёплый жёлтый
                case 1: return new Color(0.62f, 1.00f, 0.80f, 1f);   // рынок: мятный
                case 2: return new Color(0.72f, 0.88f, 1.00f, 1f);   // больница: холодный белый
                default: return new Color(1.00f, 0.60f, 0.28f, 1f);  // промзона: оранжевый
            }
        }

        static bool NearPoints(Vector3 c, float r)
        {
            for (int i = 0; i < hearthPts.Count; i++)
            {
                float dx = hearthPts[i].x - c.x;
                float dz = hearthPts[i].z - c.z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            float sx = StartPos.x - c.x;
            float sz = StartPos.z - c.z;
            if (sx * sx + sz * sz < r * r) return true;
            float bx = BusPos.x - c.x;
            float bz = BusPos.z - c.z;
            return bx * bx + bz * bz < (r + 8f) * (r + 8f);
        }

        static Vector3[] stationLamps = new Vector3[0];

        /// <summary>Сколько выживших пришлось переставить, потому что реквизит оказался на них (для проверки).</summary>
        public static int SurvivorsMoved;

        /// <summary>
        /// Места, которые реквизит обязан обходить: лампы (к ним подходят за светом), выжившие, генераторы и лампы вокзала.
        /// Без этого машина вставала на человека и закрывала фонарь. Звать, когда лампы, люди и генераторы уже созданы.
        /// </summary>
        static bool Reserved(Vector3 c, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Vector3 b = Lamp.All[i].BasePos;
                float dx = b.x - c.x;
                float dz = b.z - c.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Vector3 b = Survivor.All[i].SpawnPos;
                float dx = b.x - c.x;
                float dz = b.z - c.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            for (int i = 0; i < Generator.All.Count; i++)
            {
                Vector3 b = Generator.All[i].BasePos;
                float dx = b.x - c.x;
                float dz = b.z - c.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            for (int i = 0; i < stationLamps.Length; i++)
            {
                float dx = stationLamps[i].x - c.x;
                float dz = stationLamps[i].z - c.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>Фонари по краям площади вокзала (их ставит BuildStation, но места нужны раньше — реквизиту).</summary>
        static Vector3[] StationLampSpots()
        {
            CellRect st = CityMap.Station;
            float C = CityMap.Cell;
            return new Vector3[]
            {
                new Vector3(st.X0 * C + 1.2f, 0f, st.Z0 * C + 1.2f),
                new Vector3((st.X1 + 1) * C - 1.2f, 0f, st.Z0 * C + 1.2f),
                new Vector3(st.X0 * C + 1.2f, 0f, (st.Z1 + 1) * C - 1.2f),
                new Vector3((st.X1 + 1) * C - 1.2f, 0f, (st.Z1 + 1) * C - 1.2f),
                new Vector3(st.X0 * C + (st.X1 - st.X0 + 1) * C * 0.5f, 0f, st.Z0 * C + 1.2f)
            };
        }

        /// <summary>Свободно ли место для человека: внутри нет ни машины, ни ящика, ни барьера, ни здания.</summary>
        static bool SpotFree(Vector3 p)
        {
            Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.4f, 0.4f);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i];
                if (c.isTrigger) continue;
                if (c.GetComponentInParent<Survivor>() != null) continue;
                if (c.GetComponentInParent<PlayerController>() != null) continue;
                if (c.gameObject.name == "Floor") continue;
                return false;
            }
            return true;
        }

        /// <summary>Страховка: если человек всё же стоит внутри чего-то твёрдого, переставить его на свободное место рядом.</summary>
        static void FixSurvivorSpots()
        {
            SurvivorsMoved = 0;
            Physics.SyncTransforms();
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Survivor s = Survivor.All[i];
                Vector3 p = s.SpawnPos;
                p.y = 0f;
                if (SpotFree(p)) continue;
                bool moved = false;
                for (int ring = 1; ring <= 6 && !moved; ring++)
                {
                    float rad = 0.7f * ring;
                    for (int k = 0; k < 12 && !moved; k++)
                    {
                        float a = k * 30f * Mathf.Deg2Rad;
                        Vector3 q = p + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                        if (!CityMap.CanStand(q.x, q.z, 0.5f, true)) continue;
                        if (!SpotFree(q)) continue;
                        s.Relocate(q);
                        SurvivorsMoved++;
                        moved = true;
                    }
                }
            }
        }

        static bool NearWall(int x, int z, int radius)
        {
            for (int i = x - radius; i <= x + radius; i++)
            {
                for (int j = z - radius; j <= z + radius; j++)
                {
                    if (CityMap.InGrid(i, j) && CityMap.Blocked[i, j]) return true;
                }
            }
            return false;
        }

        /// <summary>Клетка переулка, у которой дорога только с одной стороны (торец тупика).</summary>
        static bool IsDeadEndCell(int x, int z)
        {
            return CityMap.InGrid(x, z) && CityMap.Kind[x, z] == CityMap.Alley && RoadNeighbors(x, z) == 1;
        }

        static int RoadNeighbors(int x, int z)
        {
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                if (CityMap.IsRoadCell(x + Dx[i], z + Dz[i])) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ небо, горизонт, маяк

        static void BuildSky(Transform root)
        {
            float C = CityMap.Cell;
            Material sky = OwnedResources.Keep(root.gameObject, Gfx.SkyMaterial());
            if (sky != null)
            {
                GameObject dome = Gfx.Prim(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(960f, 960f, 960f), sky, false);
                dome.name = "SkyDome";
                dome.AddComponent<SkyFollow>();
            }

            // За городом уже день: рассвет за выездом с вокзала виден как свечение над крышами.
            float ex = CityMap.ExitRoad.Center.X;
            float north = CityMap.H * C;
            Material hor = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.78f, 0.48f, 0.55f), Gfx.TexHorizon, true, false, 0f));
            Gfx.Prim(PrimitiveType.Quad, root, new Vector3(ex, 12f, north + 30f), new Vector3(150f, 24f, 1f), hor, false).name = "DawnStrip";
            Material glow = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.9f, 0.68f, 0.36f), Gfx.TexHalo, true, false, 0f));
            Gfx.Prim(PrimitiveType.Quad, root, new Vector3(ex, 16f, north + 28f), new Vector3(140f, 70f, 1f), glow, false).name = "DawnGlow";

            // Столб света над автовокзалом: виден над крышами из любого района.
            Material pillar = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.93f, 0.72f, 0.16f), null, true, false, 0f));
            GameObject beacon = Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(BusPos.x, 90f, BusPos.z), new Vector3(9f, 90f, 9f), pillar, false);
            beacon.name = "StationBeacon";
            beacon.AddComponent<BeaconFade>();
            Material pillarCore = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.97f, 0.85f, 0.22f), null, true, false, 0f));
            GameObject core = Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(BusPos.x, 90f, BusPos.z), new Vector3(3.2f, 90f, 3.2f), pillarCore, false);
            core.name = "StationBeaconCore";
            core.AddComponent<BeaconFade>();
            beaconObjs = new GameObject[] { beacon, core };

            GameObject dust = new GameObject("Dust");
            dust.transform.SetParent(root, false);
            DustMotes dm = dust.AddComponent<DustMotes>();
            dm.Init();
        }

        // ------------------------------------------------------------------ пол, бордюры, разметка

        static void BuildFloor(Transform root)
        {
            float C = CityMap.Cell;
            Gfx.StaticBox(root, new Vector3(CityMap.W * C * 0.5f, -0.5f, CityMap.H * C * 0.5f),
                new Vector3(CityMap.W * C + 80f, 1f, CityMap.H * C + 80f), "Floor");

            Color32 paint = Rgb(205, 205, 190);
            Color32 yellow = Rgb(205, 170, 60);
            for (int x = 0; x < CityMap.W; x++)
            {
                for (int z = 0; z < CityMap.H; z++)
                {
                    byte k = CityMap.Kind[x, z];
                    if (k == CityMap.Building) continue;
                    float x0 = x * C;
                    float z0 = z * C;
                    float x1 = x0 + C;
                    float z1 = z0 + C;
                    float mx = x0 + C * 0.5f;
                    float mz = z0 + C * 0.5f;
                    MeshBatch b = ((k == CityMap.Plaza) ? plazaS : floorS).At(mx, mz);
                    b.Quad(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0), White);

                    // бордюры вдоль стен (у бордюра есть верх и торец к дороге; низ и тыл не видны)
                    MeshBatch cb = curbS.At(mx, mz);
                    if (!CityMap.IsRoadCell(x + 1, z)) cb.BoxYaw(new Vector3(x1 - 0.45f, 0.07f, z0 + C * 0.5f), new Vector3(0.9f, 0.14f, C), 0f, White, NB);
                    if (!CityMap.IsRoadCell(x - 1, z)) cb.BoxYaw(new Vector3(x0 + 0.45f, 0.07f, z0 + C * 0.5f), new Vector3(0.9f, 0.14f, C), 0f, White, NB);
                    if (!CityMap.IsRoadCell(x, z + 1)) cb.BoxYaw(new Vector3(x0 + C * 0.5f, 0.07f, z1 - 0.45f), new Vector3(C, 0.14f, 0.9f), 0f, White, NB);
                    if (!CityMap.IsRoadCell(x, z - 1)) cb.BoxYaw(new Vector3(x0 + C * 0.5f, 0.07f, z0 + 0.45f), new Vector3(C, 0.14f, 0.9f), 0f, White, NB);
                    MeshBatch markB = markS.At(mx, mz);

                    // разметка двухполосных улиц: пунктир по оси
                    if (k == CityMap.Street)
                    {
                        bool nsLeft = IsKind(x + 1, z, CityMap.Street) && !IsKind(x - 1, z, CityMap.Street)
                                      && IsStreetish(x, z + 1) && IsStreetish(x, z - 1);
                        bool ewBottom = IsKind(x, z + 1, CityMap.Street) && !IsKind(x, z - 1, CityMap.Street)
                                        && IsStreetish(x + 1, z) && IsStreetish(x - 1, z);
                        if (nsLeft)
                        {
                            markB.Quad(new Vector3(x1 - 0.1f, 0.03f, z0 + 0.8f), new Vector3(x1 - 0.1f, 0.03f, z0 + 3.2f), new Vector3(x1 + 0.1f, 0.03f, z0 + 3.2f), new Vector3(x1 + 0.1f, 0.03f, z0 + 0.8f), yellow);
                        }
                        if (ewBottom)
                        {
                            markB.Quad(new Vector3(x0 + 0.8f, 0.03f, z1 - 0.1f), new Vector3(x0 + 0.8f, 0.03f, z1 + 0.1f), new Vector3(x0 + 3.2f, 0.03f, z1 + 0.1f), new Vector3(x0 + 3.2f, 0.03f, z1 - 0.1f), yellow);
                        }
                        // пешеходный переход у выхода на площадь
                        if (CityMap.IsRoadCell(x, z + 1) && CityMap.Kind[x, z + 1] == CityMap.Plaza)
                            Crosswalk(markB, x0, z1 - 2.2f, C, true, paint);
                        if (CityMap.IsRoadCell(x, z - 1) && CityMap.Kind[x, z - 1] == CityMap.Plaza)
                            Crosswalk(markB, x0, z0 + 0.4f, C, true, paint);
                        if (CityMap.IsRoadCell(x + 1, z) && CityMap.Kind[x + 1, z] == CityMap.Plaza)
                            Crosswalk(markB, x1 - 2.2f, z0, C, false, paint);
                        if (CityMap.IsRoadCell(x - 1, z) && CityMap.Kind[x - 1, z] == CityMap.Plaza)
                            Crosswalk(markB, x0 + 0.4f, z0, C, false, paint);
                    }
                }
            }
        }

        static bool IsKind(int x, int z, byte k)
        {
            return CityMap.InGrid(x, z) && CityMap.Kind[x, z] == k;
        }

        static bool IsStreetish(int x, int z)
        {
            return CityMap.InGrid(x, z) && (CityMap.Kind[x, z] == CityMap.Street || CityMap.Kind[x, z] == CityMap.Tunnel);
        }

        /// <summary>Полосатый переход: 5 полос поперёк клетки.</summary>
        static void Crosswalk(MeshBatch markB, float x0, float z0, float c, bool stripesAlongX, Color32 col)
        {
            for (int i = 0; i < 5; i++)
            {
                float o = 0.5f + i * 1.12f;
                if (stripesAlongX)
                {
                    markB.Quad(new Vector3(x0 + o, 0.03f, z0), new Vector3(x0 + o, 0.03f, z0 + 1.8f), new Vector3(x0 + o + 0.55f, 0.03f, z0 + 1.8f), new Vector3(x0 + o + 0.55f, 0.03f, z0), col);
                }
                else
                {
                    markB.Quad(new Vector3(x0, 0.03f, z0 + o), new Vector3(x0, 0.03f, z0 + o + 0.55f), new Vector3(x0 + 1.8f, 0.03f, z0 + o + 0.55f), new Vector3(x0 + 1.8f, 0.03f, z0 + o), col);
                }
            }
        }

        // ------------------------------------------------------------------ здания

        static void BuildBuildings(Transform root)
        {
            int W = CityMap.W;
            int H = CityMap.H;
            bool[,] used = new bool[W, H];

            // Больница: огромное здание над тоннелем (ориентир района: красный крест)
            Hospital(root, used);
            // Крылья вокзала по бокам выезда
            StationWings(root, used);

            for (int z = 0; z < H; z++)
            {
                for (int x = 0; x < W; x++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Building || used[x, z]) continue;
                    int d = CityMap.District[x, z];
                    int maxW = 1 + rng.Next(3);
                    int maxD = 1 + rng.Next(3);
                    int w = 1;
                    while (w < maxW && x + w < W && CityMap.Kind[x + w, z] == CityMap.Building && !used[x + w, z] && CityMap.District[x + w, z] == d) w++;
                    int dd = 1;
                    while (dd < maxD && z + dd < H)
                    {
                        bool ok = true;
                        for (int i = 0; i < w; i++)
                        {
                            if (CityMap.Kind[x + i, z + dd] != CityMap.Building || used[x + i, z + dd] || CityMap.District[x + i, z + dd] != d) { ok = false; break; }
                        }
                        if (!ok) break;
                        dd++;
                    }
                    for (int i = 0; i < w; i++)
                    {
                        for (int j = 0; j < dd; j++) used[x + i, z + j] = true;
                    }
                    float h = Mathf.Round(R(HMin[d], HMax[d]) / 3f) * 3f;
                    AddBuilding(root, x, z, w, dd, d, h);
                }
            }
        }

        static bool NearRoad(int x, int z, int w, int dd)
        {
            for (int i = x - 1; i <= x + w; i++)
            {
                for (int j = z - 1; j <= z + dd; j++)
                {
                    if (CityMap.IsRoadCell(i, j)) return true;
                }
            }
            return false;
        }

        static void AddBuilding(Transform root, int x, int z, int w, int dd, int d, float h)
        {
            float C = CityMap.Cell;
            Vector3 size = new Vector3(w * C, h, dd * C);
            Vector3 center = new Vector3(x * C + size.x * 0.5f, h * 0.5f, z * C + size.z * 0.5f);
            for (int bi = 0; bi < w; bi++)
            {
                for (int bj = 0; bj < dd; bj++) BuildingHeight[x + bi, z + bj] = h;
            }
            float g = R(0.85f, 1.15f);
            Color32 tint = new Color32((byte)Mathf.Clamp(g * 255f, 0f, 255f), (byte)Mathf.Clamp(g * 250f, 0f, 255f), (byte)Mathf.Clamp(g * 246f, 0f, 255f), 255);
            bldS[d].At(center).BoxYaw(center, size, 0f, tint, NB);

            bool near = NearRoad(x, z, w, dd);
            if (near)
            {
                Gfx.StaticBox(root, center, size, "Building");
                FacadeDetails(x, z, w, dd, d, h);
            }

            // крыша: техника, надстройки
            float top = h;
            if (near || rng.NextDouble() < 0.3)
            {
                if (rng.NextDouble() < 0.60)
                {
                    float bx = center.x + R(-size.x * 0.3f, size.x * 0.3f);
                    float bz = center.z + R(-size.z * 0.3f, size.z * 0.3f);
                    Models.RoofAC(propS.At(bx, bz), new Vector3(bx, top, bz), R(0f, 90f));
                    if (rng.NextDouble() < 0.4)
                    {
                        float bx2 = center.x + R(-size.x * 0.3f, size.x * 0.3f);
                        float bz2 = center.z + R(-size.z * 0.3f, size.z * 0.3f);
                        Models.RoofAC(propS.At(bx2, bz2), new Vector3(bx2, top, bz2), R(0f, 90f));
                    }
                }
                if (rng.NextDouble() < 0.22)
                {
                    float hx = center.x + R(-size.x * 0.25f, size.x * 0.25f);
                    float hz = center.z + R(-size.z * 0.25f, size.z * 0.25f);
                    Models.RoofHatch(propS.At(hx, hz), new Vector3(hx, top, hz), R(0f, 90f));
                }
                if (rng.NextDouble() < 0.16)
                {
                    float ax = center.x + R(-2f, 2f);
                    float az = center.z + R(-2f, 2f);
                    Models.Antenna(propS.At(ax, az), new Vector3(ax, top, az), R(6f, 11f));
                }
            }

            // водонапорная башня — ориентир первого района
            if (!towerDone && d == 0 && h >= 24f && x > 6 && near)
            {
                towerDone = true;
                Models.WaterTank(propS.At(center), new Vector3(center.x, top, center.z));
                AddBlinker(new Vector3(center.x, top + 9.2f, center.z));
            }

            // заводские трубы с оранжевым свечением в промзоне
            if (d == 3 && chimneys < 3 && w * dd >= 4 && h >= 14f)
            {
                chimneys++;
                float chH = h + 24f;
                MeshBatch cb = propS.At(center);
                cb.Frustum(new Vector3(center.x, chH * 0.5f, center.z), Vector3.up, 1.8f, 1.35f, chH, 12, Rgb(62, 58, 56), false, false);
                for (int ring = 1; ring < 5; ring++)
                    cb.Prism(new Vector3(center.x, chH * ring / 5f, center.z), Vector3.up, 1.9f - 0.1f * ring, 0.25f, 12, Rgb(38, 36, 36), false);
                Material glow = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.5f, 0.2f, 1f), null, false, false, 0.1f));
                Gfx.Prim(PrimitiveType.Cube, rootT, new Vector3(center.x, chH + 0.6f, center.z), new Vector3(3.0f, 1.4f, 3.0f), glow, false).name = "ChimneyGlow";
                AddBlinker(new Vector3(center.x, chH + 2.2f, center.z));
            }

            // красные огни на самых высоких крышах
            if (h >= 30f && blinkers < 14 && rng.NextDouble() < 0.5)
            {
                AddBlinker(new Vector3(center.x, top + 10.4f, center.z));
            }
        }

        // ------------------------------------------------------------------ детали фасадов

        /// <summary>
        /// Лепнина и мелочи на сторонах здания, обращённых к улицам: цоколь, карниз, парапет, угловые пилястры, двери,
        /// витрины с тентами (рынок), балконы (жилой район), водосточные трубы, промышленные трубы.
        /// Всё идёт в общие чанки (один меш на кусок карты), а не отдельными объектами.
        /// </summary>
        static void FacadeDetails(int x, int z, int w, int dd, int d, float h)
        {
            float C = CityMap.Cell;
            Color32 concrete = (d == 3) ? Rgb(64, 60, 56) : ((d == 2) ? Rgb(86, 90, 98) : Rgb(80, 76, 70));
            Color32 plinth = Models.Dim(concrete, 0.72f);
            // козырьки тусклые и грязные: лунный свет на верхних гранях не должен делать их «светящимися»
            Color32[] awnings = { Rgb(88, 40, 38), Rgb(40, 64, 80), Rgb(50, 76, 56), Rgb(98, 80, 42) };

            for (int s = 0; s < 4; s++)
            {
                int cells = (s < 2) ? dd : w;
                Vector3 n;
                Vector3 t;
                Vector3 origin;
                switch (s)
                {
                    case 0: n = new Vector3(1f, 0f, 0f); t = new Vector3(0f, 0f, 1f); origin = new Vector3((x + w) * C, 0f, z * C); break;
                    case 1: n = new Vector3(-1f, 0f, 0f); t = new Vector3(0f, 0f, 1f); origin = new Vector3(x * C, 0f, z * C); break;
                    case 2: n = new Vector3(0f, 0f, 1f); t = new Vector3(1f, 0f, 0f); origin = new Vector3(x * C, 0f, (z + dd) * C); break;
                    default: n = new Vector3(0f, 0f, -1f); t = new Vector3(1f, 0f, 0f); origin = new Vector3(x * C, 0f, z * C); break;
                }
                float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;

                bool[] road = new bool[cells];
                for (int i = 0; i < cells; i++)
                {
                    switch (s)
                    {
                        case 0: road[i] = CityMap.IsRoadCell(x + w, z + i); break;
                        case 1: road[i] = CityMap.IsRoadCell(x - 1, z + i); break;
                        case 2: road[i] = CityMap.IsRoadCell(x + i, z + dd); break;
                        default: road[i] = CityMap.IsRoadCell(x + i, z - 1); break;
                    }
                }

                int i0 = 0;
                while (i0 < cells)
                {
                    if (!road[i0]) { i0++; continue; }
                    int i1 = i0;
                    while (i1 < cells && road[i1]) i1++;
                    FacadeRun(origin, n, t, yaw, i0 * C, i1 * C, d, h, concrete, plinth, awnings);
                    i0 = i1;
                }
            }
        }

        static void FacadeRun(Vector3 origin, Vector3 n, Vector3 t, float yaw, float a0, float a1, int d, float h,
                              Color32 concrete, Color32 plinth, Color32[] awnings)
        {
            float C = CityMap.Cell;
            float len = a1 - a0;
            Vector3 mid = origin + t * ((a0 + a1) * 0.5f);
            MeshBatch pb = propS.At(mid);
            Vector3 up = Vector3.up;

            // цоколь, карниз, парапет вдоль всего участка
            pb.BoxYaw(mid + n * 0.13f + up * 0.45f, new Vector3(len + 0.04f, 0.9f, 0.3f), yaw, plinth, NB);
            pb.BoxYaw(mid + n * 0.22f + up * (h - 0.4f), new Vector3(len + 0.5f, 0.45f, 0.55f), yaw, concrete, NB);
            pb.BoxYaw(mid + n * 0.04f + up * (h - 0.9f), new Vector3(len + 0.1f, 0.14f, 0.1f), yaw, Models.Dim(concrete, 0.7f), NB);
            pb.BoxYaw(mid - n * 0.14f + up * (h + 0.35f), new Vector3(len, 0.7f, 0.28f), yaw, Models.Dim(concrete, 0.9f), NB);

            // угловые пилястры (жилой район и больница)
            if (d == 0 || d == 2)
            {
                for (int e = 0; e < 2; e++)
                {
                    float a = e == 0 ? a0 : a1;
                    Vector3 pp = origin + t * a + n * 0.12f + up * (h * 0.5f);
                    pb.BoxYaw(pp, new Vector3(0.5f, h, 0.42f), yaw, Models.Dim(concrete, 1.08f), NB);
                }
            }

            // водосточная труба у начала участка
            if (rng.NextDouble() < 0.45)
                Models.Downpipe(pb, origin + t * (a0 + 0.35f) + n * 0.1f, h - 0.6f);

            // промзона: трубы вдоль стены
            if (d == 3 && rng.NextDouble() < 0.6)
            {
                float ph = R(2.6f, 4.2f);
                Models.WallPipe(pb, origin + t * (a0 + 0.2f) + n * 0.35f + up * ph, origin + t * (a1 - 0.2f) + n * 0.35f + up * ph, 0.17f);
                if (rng.NextDouble() < 0.5)
                    pb.Rod(origin + t * (a0 + len * 0.5f) + n * 0.35f, origin + t * (a0 + len * 0.5f) + n * 0.35f + up * ph, 0.12f, 6, Rgb(66, 52, 44));
            }

            // двери, витрины и балконы по ячейкам участка (по клетке 6 м)
            int cellsInRun = Mathf.RoundToInt(len / C);
            for (int c = 0; c < cellsInRun; c++)
            {
                float a = a0 + (c + 0.5f) * C;
                Vector3 wall = origin + t * a;
                double r = rng.NextDouble();
                bool entrance = false;
                // торец тупика: там лампа-награда (и, возможно, генератор), витрину не ставим — только дверь
                bool deadEnd = IsDeadEndCell(Mathf.FloorToInt((wall.x + n.x * C * 0.5f) / C), Mathf.FloorToInt((wall.z + n.z * C * 0.5f) / C));
                if (d == 1)
                {
                    // рынок: магазин с витриной, товаром и вывеской (AddShop) или просто дверь
                    if (r < 0.58 && !deadEnd) { AddShop(pb, wall, n, yaw, ShopKindFor(d), awnings[rng.Next(awnings.Length)]); entrance = true; }
                    else if (r < 0.72) { Models.Door(pb, wall, yaw, concrete); entrance = true; entrances.Add(wall); }
                }
                else if (d == 0 || d == 2)
                {
                    // жилой район и больница: изредка магазинчик на первом этаже
                    if (r < 0.13 && !deadEnd) { AddShop(pb, wall, n, yaw, ShopKindFor(d), awnings[rng.Next(awnings.Length)]); entrance = true; }
                    else if (r < (d == 0 ? 0.32 : 0.26)) { Models.Door(pb, wall, yaw, concrete); entrance = true; entrances.Add(wall); }
                }
                else if (d == 3 && r < 0.25) { Models.Door(pb, wall, yaw, Rgb(60, 58, 56)); entrance = true; entrances.Add(wall); }
                if (!entrance) WallExtras(pb, wall, n, t, d, h);   // плющ и плакаты на глухих участках стены

                if (d == 0 && rng.NextDouble() < 0.40)
                {
                    int floors = Mathf.FloorToInt(h / 3f);
                    for (int f = 2; f < floors - 1; f += 2)
                    {
                        if (rng.NextDouble() < 0.5) continue;
                        float off = entrance ? 0f : R(-1.2f, 1.2f);
                        Models.Balcony(pb, wall + t * off + up * (f * 3f + 0.35f), yaw, 2.4f);
                    }
                }
            }
        }

        static void AddBlinker(Vector3 pos)
        {
            if (blinkMat == null)
            {
                blinkMat = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.1f, 0.08f, 1f), null, false, false, 0f));
                GameObject go = new GameObject("RoofBlink");
                go.transform.SetParent(rootT, false);
                RoofBlink rb = go.AddComponent<RoofBlink>();
                rb.Mat = blinkMat;
            }
            blinkers++;
            Gfx.Prim(PrimitiveType.Sphere, rootT, pos, new Vector3(0.9f, 0.9f, 0.9f), blinkMat, false).name = "RoofLight";
        }

        /// <summary>Здание больницы: светящийся красный крест на стороне, обращённой к улице.</summary>
        static void Hospital(Transform root, bool[,] used)
        {
            int x0 = 35, z0 = 39, x1 = 39, z1 = 46;
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (!CityMap.InGrid(x, z) || CityMap.Kind[x, z] != CityMap.Building) return;
                }
            }
            float C = CityMap.Cell;
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++) used[x, z] = true;
            }
            float h = 39f;
            Vector3 size = new Vector3((x1 - x0 + 1) * C, h, (z1 - z0 + 1) * C);
            Vector3 center = new Vector3(x0 * C + size.x * 0.5f, h * 0.5f, z0 * C + size.z * 0.5f);
            bldS[2].At(center).BoxYaw(center, size, 0f, Gray(1.0f), NB);
            Gfx.StaticBox(root, center, size, "Hospital");
            FacadeDetails(x0, z0, x1 - x0 + 1, z1 - z0 + 1, 2, h);

            // крест на западной стене (над тоннелем и улицей)
            float wx = x0 * C - 0.2f;
            float cz = center.z;
            Material board = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(0.92f, 0.95f, 1f, 1f), null, false, false, 0.12f));
            Material cross = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(1f, 0.12f, 0.1f, 1f), null, false, false, 0.12f));
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(wx, 29f, cz), new Vector3(0.4f, 11f, 11f), board, false).name = "HospitalBoard";
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(wx - 0.3f, 29f, cz), new Vector3(0.3f, 8.4f, 2.6f), cross, false).name = "HospitalCrossV";
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(wx - 0.3f, 29f, cz), new Vector3(0.3f, 2.6f, 8.4f), cross, false).name = "HospitalCrossH";
            AddBlinker(new Vector3(center.x, h + 1.5f, center.z));
        }

        /// <summary>Здание автовокзала: два крыла по бокам выезда, вывеска, навес.</summary>
        static void StationWings(Transform root, bool[,] used)
        {
            float C = CityMap.Cell;
            CellRect st = CityMap.Station;
            CellRect ex = CityMap.ExitRoad;
            int zA = st.Z1 + 1;
            int zB = ex.Z1;
            int[,] wings = { { st.X0, ex.X0 - 1 }, { ex.X1 + 1, st.X1 } };
            for (int wi = 0; wi < 2; wi++)
            {
                int xa = wings[wi, 0];
                int xb = wings[wi, 1];
                bool ok = true;
                for (int x = xa; x <= xb && ok; x++)
                {
                    for (int z = zA; z <= zB; z++)
                    {
                        if (!CityMap.InGrid(x, z) || CityMap.Kind[x, z] != CityMap.Building) { ok = false; break; }
                    }
                }
                if (!ok) continue;
                for (int x = xa; x <= xb; x++)
                {
                    for (int z = zA; z <= zB; z++) used[x, z] = true;
                }
                float h = 17f;
                Vector3 size = new Vector3((xb - xa + 1) * C, h, (zB - zA + 1) * C);
                Vector3 center = new Vector3(xa * C + size.x * 0.5f, h * 0.5f, zA * C + size.z * 0.5f);
                bldS[3].At(center).BoxYaw(center, size, 0f, Gray(1.05f), NB);
                Gfx.StaticBox(root, center, size, "StationWing");
                FacadeDetails(xa, zA, xb - xa + 1, zB - zA + 1, 3, h);

                // вывеска на южной стене, обращённой к площади
                Material sign = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(0.18f, 0.78f, 0.66f, 1f), null, false, false, 0.12f));
                Gfx.Prim(PrimitiveType.Cube, root, new Vector3(center.x, 12.5f, zA * C - 0.25f), new Vector3(size.x * 0.7f, 3.2f, 0.4f), sign, false).name = "StationSign";
                Material frame = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(0.95f, 0.95f, 0.85f, 1f), null, false, false, 0.12f));
                Gfx.Prim(PrimitiveType.Cube, root, new Vector3(center.x, 12.5f, zA * C - 0.1f), new Vector3(size.x * 0.7f + 0.8f, 4.0f, 0.2f), frame, false).name = "StationSignFrame";
            }
        }

        // ------------------------------------------------------------------ тоннель

        static void BuildTunnel(Transform root)
        {
            float C = CityMap.Cell;
            int x0 = int.MaxValue, x1 = -1, z0 = int.MaxValue, z1 = -1;
            for (int x = 0; x < CityMap.W; x++)
            {
                for (int z = 0; z < CityMap.H; z++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Tunnel) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (z < z0) z0 = z;
                    if (z > z1) z1 = z;
                }
            }
            if (x1 < 0) return;
            Vector3 size = new Vector3((x1 - x0 + 1) * C, 1.0f, (z1 - z0 + 1) * C);
            Vector3 center = new Vector3(x0 * C + size.x * 0.5f, 5.2f, z0 * C + size.z * 0.5f);
            propS.At(center).Box(center, size, Gray(0.35f));
            Gfx.StaticBox(root, center, size, "TunnelRoof");
            // над тоннелем «железная дорога»: насыпь и ограждение
            propS.At(center).Box(new Vector3(center.x, 6.0f, center.z), new Vector3(size.x + 12f, 0.8f, size.z), Gray(0.3f));

            // рёбра жёсткости: бетонные пилястры вдоль стен и поперечные балки под потолком каждые 6 м
            Color32 rib = Gray(0.30f);
            float xl = x0 * C;
            float xr = (x1 + 1) * C;
            for (int zc = z0; zc <= z1 + 1; zc++)
            {
                float zz = zc * C;
                MeshBatch tb = propS.At((xl + xr) * 0.5f, zz);
                tb.BoxYaw(new Vector3(xl + 0.3f, 2.5f, zz), new Vector3(0.6f, 5.0f, 0.6f), 0f, rib, NB);
                tb.BoxYaw(new Vector3(xr - 0.3f, 2.5f, zz), new Vector3(0.6f, 5.0f, 0.6f), 0f, rib, NB);
                tb.BoxYaw(new Vector3((xl + xr) * 0.5f, 4.55f, zz), new Vector3(xr - xl, 0.5f, 0.55f), 0f, rib, MeshBatch.AllFaces);
            }
            // сточный жёлоб и бетонный цоколь вдоль стен
            for (int zc = z0; zc <= z1; zc++)
            {
                float zz = (zc + 0.5f) * C;
                MeshBatch tb = propS.At((xl + xr) * 0.5f, zz);
                tb.BoxYaw(new Vector3(xl + 0.2f, 0.4f, zz), new Vector3(0.4f, 0.8f, C - 0.7f), 0f, Gray(0.26f), NB);
                tb.BoxYaw(new Vector3(xr - 0.2f, 0.4f, zz), new Vector3(0.4f, 0.8f, C - 0.7f), 0f, Gray(0.26f), NB);
            }
        }

        // ------------------------------------------------------------------ границы карты

        static void BuildBoundaries(Transform root)
        {
            float C = CityMap.Cell;
            float w = CityMap.W * C;
            float hz = CityMap.H * C;
            Gfx.StaticBox(root, new Vector3(-3f, 10f, hz * 0.5f), new Vector3(6f, 20f, hz + 12f), "WallW");
            Gfx.StaticBox(root, new Vector3(w + 3f, 10f, hz * 0.5f), new Vector3(6f, 20f, hz + 12f), "WallE");
            Gfx.StaticBox(root, new Vector3(w * 0.5f, 10f, -3f), new Vector3(w + 12f, 20f, 6f), "WallS");
            float ex0 = CityMap.ExitRoad.X0 * C;
            float ex1 = (CityMap.ExitRoad.X1 + 1) * C;
            Gfx.StaticBox(root, new Vector3(ex0 * 0.5f - 3f, 10f, hz + 3f), new Vector3(ex0 + 6f, 20f, 6f), "WallNL");
            Gfx.StaticBox(root, new Vector3((ex1 + w) * 0.5f + 3f, 10f, hz + 3f), new Vector3(w - ex1 + 6f, 20f, 6f), "WallNR");
            // невидимая пробка в проёме: дальше свет, но выйти нельзя
            Gfx.StaticBox(root, new Vector3((ex0 + ex1) * 0.5f, 5f, hz + 16f), new Vector3(ex1 - ex0 + 4f, 10f, 2f), "ExitStop");
        }

        // ------------------------------------------------------------------ лампы, очаги

        static void BuildLamps(Transform root)
        {
            float C = CityMap.Cell;
            List<Vector3> placed = new List<Vector3>();
            for (int i = 0; i < hearthPts.Count; i++) placed.Add(hearthPts[i]);
            int decoys = 0;
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    byte k = CityMap.Kind[x, z];
                    if (k == CityMap.Building || k == CityMap.Tunnel) continue;
                    int period = (k == CityMap.Alley) ? 5 : 4;
                    if (((x * 3 + z * 5) % period) != 0) continue;
                    int wx = 0, wz = 0;
                    bool found = false;
                    int start = (x + z) & 3;
                    for (int i = 0; i < 4 && !found; i++)
                    {
                        int d = (start + i) & 3;
                        if (!CityMap.IsRoadCell(x + Dx[d], z + Dz[d]))
                        {
                            wx = Dx[d];
                            wz = Dz[d];
                            found = true;
                        }
                    }
                    if (!found) continue;
                    if (NearWall(x, z, 1)) continue;
                    Vector3 wallDir = new Vector3(wx, 0f, wz);
                    // не по центру клетки (там двери и витрины), а у границы клеток — столб между окнами
                    Vector3 pos;
                    if (!LampSpot(x, z, wallDir, ((x * 5 + z * 3) & 1) == 0 ? 1f : -1f, 2.9f, out pos)) continue;
                    bool near = false;
                    for (int i = 0; i < placed.Count; i++)
                    {
                        if ((placed[i] - pos).sqrMagnitude < 17f * 17f) { near = true; break; }
                    }
                    if (near) continue;
                    placed.Add(pos);
                    // клетка и все броски кубика — как были; сам столб при необходимости отодвигаем от угла (см. ClearLampSpot)
                    MakeLamp(root, pos, ClearLampSpot(pos, -wallDir), -wallDir, k, ref decoys);
                }
            }

            // Тупики: лампа в конце — награда за вылазку
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Alley || RoadNeighbors(x, z) != 1) continue;
                    Vector3 c = CellCenter(x, z, 0f);
                    bool has = false;
                    for (int i = 0; i < placed.Count; i++)
                    {
                        if ((placed[i] - c).sqrMagnitude < 9f * 9f) { has = true; break; }
                    }
                    if (has) continue;
                    int wx = 0, wz = 0;
                    for (int d = 0; d < 4; d++)
                    {
                        if (!CityMap.IsRoadCell(x + Dx[d], z + Dz[d])) { wx = Dx[d]; wz = Dz[d]; break; }
                    }
                    Vector3 wallDir = new Vector3(wx, 0f, wz);
                    Vector3 pos;
                    // на торцевой стене тупика тоже может быть дверь по центру: смещаем в сторону
                    // (витрин на торцах тупиков нет, но дверь возможна: от её центра столб держим не ближе 1.5 м)
                    if (!LampSpot(x, z, wallDir, ((x + z) & 1) == 0 ? 1f : -1f, 2.1f, 1.5f, out pos)) pos = c + wallDir * (C * 0.5f - 0.8f);
                    placed.Add(pos);
                    Lamp.Create(root, ClearLampSpot(pos, -wallDir), -wallDir, 1f, false, false, false, DistrictTint(CityMap.District[x, z]));
                }
            }
        }

        /// <summary>
        /// Хватает ли места столбу с кронштейном: у основания, у конца кронштейна и у плафона по обе стороны от оси (на lateral метров
        /// вдоль стены) должна быть дорога. Иначе столб стоит в углу или у торца стены: плафон упирается в перпендикулярный фасад —
        /// фонарь «в доме» и лежит вдоль стены (так было на скриншоте владельца). Для проверки расстановки (GameSmoke, стенд).
        /// </summary>
        public static bool LampHasRoom(Vector3 p, Vector3 armDir, float lateral)
        {
            Vector3 along = new Vector3(-armDir.z, 0f, armDir.x);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int r = 0; r < 3; r++)
                {
                    float reach = r == 0 ? 0f : (r == 1 ? 1.2f : 1.6f);
                    Vector3 q = p + armDir * reach + along * (lateral * side);
                    if (!CityMap.IsRoad(q.x, q.z, false)) return false;
                }
            }
            return true;
        }

        /// <summary>Витрина рядом: она 4.4 м шириной и твёрдая, столб не должен стоять у её края (ShopFronts — центры витрин у стены).</summary>
        static bool NearShopFront(Vector3 p, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < ShopFronts.Count; i++)
            {
                float dx = ShopFronts[i].x - p.x;
                float dz = ShopFronts[i].z - p.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>
        /// Место для столба: выбранное (p0), если вокруг свободно (зазор 2.4 м до боковых стен, чтобы кронштейн не лежал вдоль
        /// фасада); иначе ближайшее подходящее вдоль той же стены (шаг 0.4 м, обе стороны): дорога под ногами, стена продолжается,
        /// не у двери и не у витрины. Если зазор 2.4 м найти нельзя (узкий тупик с дверью), довольствуемся 1.5, потом 1.0 и 0.8 м.
        /// Совсем нет места — p0. Выбор клетки, расстояния между
        /// лампами и броски кубика от этого не зависят: сдвигается только сам столб, лампы стоят в тех же клетках и на тех же стенах.
        /// </summary>
        static Vector3 ClearLampSpot(Vector3 p0, Vector3 armDir)
        {
            float[] levels = { 2.4f, 1.5f, 1.0f, 0.8f };
            Vector3 along = new Vector3(-armDir.z, 0f, armDir.x);
            Vector3 wallDir = -armDir;
            for (int li = 0; li < levels.Length; li++)
            {
                if (LampHasRoom(p0, armDir, levels[li])) return p0;
                for (int step = 1; step <= 16; step++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 p = p0 + along * (0.4f * step * side);
                        if (!CityMap.IsRoad(p.x, p.z, false)) continue;
                        if (CityMap.IsRoad(p.x + wallDir.x * 1.6f, p.z + wallDir.z * 1.6f, false)) continue;   // стена должна продолжаться
                        if (NearEntrance(p, 1.5f) || NearShopFront(p, 2.7f)) continue;                         // не у двери и не у витрины
                        if (!LampHasRoom(p, armDir, levels[li])) continue;
                        return p;
                    }
                }
            }
            return p0;
        }

        /// <summary>
        /// Место фонаря у стены со сдвигом вдоль неё. По центру клетки на фасаде стоят двери и витрины (FacadeRun), а столб
        /// прямо перед дверью или витриной — баг. Сдвиг к границе клеток: столб оказывается между окнами. Место остаётся
        /// дорогой, и за ним должна продолжаться та же стена.
        /// </summary>
        static bool LampSpot(int cx, int cz, Vector3 wallDir, float sign, float off, out Vector3 pos)
        {
            return LampSpot(cx, cz, wallDir, sign, off, 2.5f, out pos);
        }

        /// <summary>
        /// entranceClear — на каком расстоянии от центра двери/витрины столб не ставим (витрина 4.4 м и твёрдая: столб не должен
        /// оказаться у неё на углу или внутри; на стыке соседних клеток расстояние до центра витрины 2.9 м и столб проходит).
        /// </summary>
        static bool LampSpot(int cx, int cz, Vector3 wallDir, float sign, float off, float entranceClear, out Vector3 pos)
        {
            float C = CityMap.Cell;
            Vector3 b = CellCenter(cx, cz, 0f) + wallDir * (C * 0.5f - 0.8f);
            Vector3 along = new Vector3(-wallDir.z, 0f, wallDir.x);
            for (int k = 0; k < 2; k++)
            {
                Vector3 p = b + along * (off * (k == 0 ? sign : -sign));
                if (!CityMap.IsRoad(p.x, p.z, false)) continue;
                if (CityMap.IsRoad(p.x + wallDir.x * 1.6f, p.z + wallDir.z * 1.6f, false)) continue;   // стена должна продолжаться
                if (NearEntrance(p, entranceClear)) continue;                                           // не у двери и не у витрины (в т. ч. соседней стены)
                pos = p;
                return true;
            }
            pos = b;
            return false;
        }

        /// <summary>pos — клетка и расклад (от него зависят свойства лампы), spot — где столб стоит на самом деле (ClearLampSpot).</summary>
        static void MakeLamp(Transform root, Vector3 pos, Vector3 spot, Vector3 armDir, byte kind, ref int decoys)
        {
            int d = CityMap.DistrictAt(pos.x, pos.z);
            double r = rng.NextDouble() + GameDifficulty.LampWearBonus;   // бесконечный город: дальше — больше потухших (в классике 0)
            float charge;
            bool unstable = false;
            bool decoy = false;
            Vector3 flat = new Vector3(pos.x - StartPos.x, 0f, pos.z - StartPos.z);
            bool nearStart = flat.magnitude < 45f;
            if (nearStart) charge = 1f;
            else if (r < 0.52) charge = 1f;
            else if (r < 0.64) { charge = 0.7f; unstable = true; }
            else if (r < 0.74) { charge = 0.45f; unstable = true; }
            else charge = 0f;

            if (!nearStart && kind == CityMap.Street && decoys < 5 && rng.NextDouble() < 0.12 && !NearPoints(pos, 22f))
            {
                decoy = true;
                charge = 1f;
                unstable = false;
                decoys++;
            }
            Lamp.Create(root, spot, armDir, charge, unstable, decoy, false, DistrictTint(d));
        }

        static void BuildHearths(Transform root)
        {
            for (int i = 0; i < hearthPts.Count; i++)
            {
                int d = CityMap.DistrictAt(hearthPts[i].x, hearthPts[i].z);
                Lamp.Create(root, hearthPts[i], Vector3.forward, 1.5f, false, false, true, DistrictTint(d));
            }
        }

        static void BuildGrowths(Transform root)
        {
            for (int i = 0; i < CityMap.Walls.Count; i++) Growth.Create(root, CityMap.Walls[i], i);
        }

        static void BuildSurvivors(Transform root)
        {
            for (int i = 0; i < CityMap.Survivors.Count; i++)
            {
                CellPos c = CityMap.Survivors[i];
                Vector3 p = CellCenter(c.X, c.Z, 0f);
                // чуть в сторону от оси, чтобы не стоять посреди прохода
                p.x += (i % 2 == 0 ? 1f : -1f) * 0.8f;
                Survivor.Create(root, p, i);
            }
        }

        static void BuildGenerators(Transform root)
        {
            float C = CityMap.Cell;
            for (int i = 0; i < CityMap.Generators.Count; i++)
            {
                CellPos g = CityMap.Generators[i];
                Vector3 p = CellCenter(g.X, g.Z, 0f);
                p.x += C * 0.5f - 1.4f;
                Generator.Create(root, p);
            }
            // Лампы в тоннеле: темны, пока не запущен генератор
            for (int z = 39; z <= 43; z += 2)
            {
                int xcell = (z % 4 == 3) ? 33 : 34;
                float wall = (xcell == 33) ? -1f : 1f;
                Vector3 pos = CellCenter(xcell, z, 0f) + new Vector3(wall * (C * 0.5f - 0.8f), 0f, 0f);
                Lamp.Create(root, pos, new Vector3(-wall, 0f, 0f), 0f, false, false, false, DistrictTint(2));
            }
        }

        // ------------------------------------------------------------------ реквизит

        /// <summary>Коробка без твёрдого тела. Все грани: её могут видеть снизу (балки, козырьки, перемычки над головой).</summary>
        static void AddBox(Vector3 center, Vector3 size, Color32 col, float yaw)
        {
            propS.At(center).BoxYaw(center, size, yaw, col, MeshBatch.AllFaces);
        }

        static void AddSolid(Vector3 center, Vector3 size, Color32 col, float yaw)
        {
            propS.At(center).BoxYaw(center, size, yaw, col, NB);
            Gfx.StaticBox(rootT, center, size, "Solid", yaw);
        }

        static void AddCar(Vector3 pos, float yaw, Color32 body)
        {
            Models.Car(propS.At(pos), pos, yaw, body);
            Gfx.StaticBox(rootT, pos + new Vector3(0f, 0.9f, 0f), new Vector3(1.9f, 1.8f, 4.3f), "Car", yaw);
        }

        /// <summary>Единичный вектор от центра клетки к стене (первой непроходимой соседней клетке); false — стен рядом нет.</summary>
        static bool WallSide(int x, int z, out int wx, out int wz)
        {
            wx = 0;
            wz = 0;
            for (int d = 0; d < 4; d++)
            {
                if (!CityMap.IsRoadCell(x + Dx[d], z + Dz[d])) { wx = Dx[d]; wz = Dz[d]; return true; }
            }
            return false;
        }

        static void BuildProps(Transform root)
        {
            float C = CityMap.Cell;
            Color32[] carCols =
            {
                Rgb(120, 40, 36), Rgb(40, 62, 110), Rgb(88, 90, 96), Rgb(80, 92, 52), Rgb(120, 108, 90), Rgb(150, 150, 146)
            };

            // машины вдоль стен и «брошенные» на площадях
            int cars = 0;
            for (int tries = 0; tries < 700 && cars < 48; tries++)
            {
                int x = rng.Next(CityMap.W);
                int z = rng.Next(CityMap.H);
                byte k = CityMap.Kind[x, z];
                if (k != CityMap.Street && k != CityMap.Plaza) continue;
                if (NearWall(x, z, 2)) continue;
                Vector3 c = CellCenter(x, z, 0f);
                if (NearPoints(c, 12f)) continue;
                if (CityMap.Station.X0 <= x && x <= CityMap.Station.X1 && CityMap.Station.Z0 <= z && z <= CityMap.Station.Z1 && rng.NextDouble() < 0.6) continue;
                int wx = 0, wz = 0;
                bool wall = WallSide(x, z, out wx, out wz);
                float yaw;
                Vector3 pos;
                if (wall)
                {
                    pos = c + new Vector3(wx, 0f, wz) * (C * 0.5f - 2.4f);
                    yaw = (wx != 0) ? 0f : 90f;
                    pos.x += (float)(rng.NextDouble() - 0.5) * 1.5f * Mathf.Abs(wz);
                    pos.z += (float)(rng.NextDouble() - 0.5) * 1.5f * Mathf.Abs(wx);
                    yaw += R(-6f, 6f);
                }
                else
                {
                    if (k != CityMap.Plaza) continue;
                    pos = c + new Vector3(R(-2f, 2f), 0f, R(-2f, 2f));
                    yaw = R(0f, 180f);
                }
                if (Reserved(pos, 5.0f)) continue;   // не на людях и не у фонарей: к лампе должен быть подход
                AddCar(pos, yaw, carCols[rng.Next(carCols.Length)]);
                cars++;
            }

            // переулки: баки, ящики, бочки
            Color32 green = Rgb(44, 82, 56);
            Color32 wood = Rgb(110, 84, 52);
            Color32 rust = Rgb(112, 62, 42);
            Color32 can = Rgb(74, 78, 76);
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Alley || NearWall(x, z, 1)) continue;
                    if (rng.NextDouble() > 0.28) continue;
                    int wx = 0, wz = 0;
                    WallSide(x, z, out wx, out wz);
                    Vector3 c = CellCenter(x, z, 0f) + new Vector3(wx, 0f, wz) * (C * 0.5f - 0.9f);
                    float yaw = (wx != 0) ? 90f : 0f;
                    if (Reserved(c, 2.8f)) continue;
                    clutterSpots.Add(new Vector4(c.x, 0f, c.z, 2.3f));
                    MeshBatch pb = propS.At(c);
                    double r = rng.NextDouble();
                    if (r < 0.35)
                    {
                        Models.Dumpster(pb, c, yaw, green);
                        Gfx.StaticBox(root, c + new Vector3(0f, 0.65f, 0f), new Vector3(1.9f, 1.3f, 1.0f), "Dumpster", yaw);
                        if (rng.NextDouble() < 0.6) Models.TrashCan(pb, c + new Vector3(wz != 0 ? 1.6f : 0f, 0f, wx != 0 ? 1.6f : 0f), can);
                    }
                    else if (r < 0.65)
                    {
                        Models.Crate(pb, c, yaw, 1.0f, wood);
                        Gfx.StaticBox(root, c + new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f), "Crate", yaw);
                        Vector3 c2 = c + new Vector3(0.45f, 1.0f, 0.1f);
                        Models.Crate(pb, c2, yaw + 20f, 0.8f, Models.Dim(wood, 0.9f));
                    }
                    else
                    {
                        Models.Barrel(pb, c, rust);
                        Models.Barrel(pb, c + new Vector3(0.7f, 0f, 0.2f), Models.Dim(rust, 1.12f));
                        Models.TrashCan(pb, c + new Vector3(-0.2f, 0f, -0.9f), can);
                        Gfx.StaticBox(root, c + new Vector3(0.35f, 0.45f, 0.1f), new Vector3(1.3f, 0.9f, 0.7f), "Barrels", 0f);
                    }
                }
            }

            // завал в конце тупиков
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Alley || RoadNeighbors(x, z) != 1) continue;
                    Vector3 c = CellCenter(x, z, 0f);
                    clutterSpots.Add(new Vector4(c.x, 0f, c.z, 2.8f));
                    Models.Rubble(propS.At(c), c, rng, 7);
                }
            }

            // конусы и барьеры у наростов Тьмы
            Color32 barrier = Rgb(122, 122, 116);
            for (int wi = 0; wi < CityMap.Walls.Count; wi++)
            {
                CellRect wr = CityMap.Walls[wi];
                Pt cen = wr.Center;
                bool streetAlongX = wr.SizeZ >= wr.SizeX;        // стена перегораживает улицу, идущую по X
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 p = streetAlongX
                            ? new Vector3(cen.X + side * (wr.SizeX * 0.5f + 5f + i * 0.4f), 0f, cen.Z + (i - 1) * 3.2f)
                            : new Vector3(cen.X + (i - 1) * 3.2f, 0f, cen.Z + side * (wr.SizeZ * 0.5f + 5f + i * 0.4f));
                        Models.Cone(propS.At(p), p);
                    }
                    Vector3 bp = streetAlongX
                        ? new Vector3(cen.X + side * (wr.SizeX * 0.5f + 8f), 0f, cen.Z)
                        : new Vector3(cen.X, 0f, cen.Z + side * (wr.SizeZ * 0.5f + 8f));
                    // барьер без коллайдера: его обходят люди и враги по карте дорог
                    Models.JerseyBarrier(propS.At(bp), bp, streetAlongX ? 0f : 90f, barrier);
                }
            }

            // рыночные ряды на площади J2
            Color32[] awn = { Rgb(100, 42, 38), Rgb(42, 70, 90), Rgb(52, 84, 58), Rgb(110, 88, 44) };
            float[,] stalls =
            {
                { 219f, 150f }, { 219f, 162f }, { 219f, 174f }, { 219f, 186f }, { 198f, 186f }, { 212f, 186f }, { 200f, 149f }, { 214f, 149f }
            };
            for (int i = 0; i < stalls.GetLength(0); i++)
            {
                float sx = stalls[i, 0];
                float sz = stalls[i, 1];
                if (!CityMap.IsRoad(sx, sz, false)) continue;
                if (Reserved(new Vector3(sx, 0f, sz), 2.8f)) continue;   // не на фонаре и не на человеке
                float yaw = (i < 4) ? 90f : 0f;
                // целый ларёк: прилавок, стенка с полками, стойки, тент с нижней гранью, вывеска, товар (Models.Stall)
                Models.Stall(propS.At(sx, sz), new Vector3(sx, 0f, sz), yaw, awn[i % awn.Length], i % 3);
                Vector3 counter = new Vector3(sx, 0.52f, sz) + RotY(new Vector3(0f, 0f, 0.55f), yaw);
                Gfx.StaticBox(rootT, counter, new Vector3(3.1f, 1.04f, 0.9f), "Solid", yaw);
                Vector3 back = new Vector3(sx, 1.1f, sz) + RotY(new Vector3(0f, 0f, -1.02f), yaw);
                Gfx.StaticBox(rootT, back, new Vector3(3.2f, 2.2f, 0.14f), "Solid", yaw);
            }

            // арка рынка над въездом на площадь
            Material archSign = OwnedResources.Keep(rootT.gameObject, Gfx.Unlit(new Color(0.16f, 0.62f, 0.5f, 1f), null, false, false, 0.12f));
            float ax = 30f * C - 0.4f;
            float az0 = 27f * C;
            float az1 = 29f * C;
            AddSolid(new Vector3(ax, 4f, az0 + 0.6f), new Vector3(1.0f, 8f, 1.0f), Gray(0.3f), 0f);
            AddSolid(new Vector3(ax, 4f, az1 - 0.6f), new Vector3(1.0f, 8f, 1.0f), Gray(0.3f), 0f);
            AddBox(new Vector3(ax, 8.2f, (az0 + az1) * 0.5f), new Vector3(1.2f, 1.2f, az1 - az0), Gray(0.3f), 0f);
            AddBox(new Vector3(ax, 0.5f, az0 + 0.6f), new Vector3(1.5f, 1.0f, 1.5f), Gray(0.24f), 0f);
            AddBox(new Vector3(ax, 0.5f, az1 - 0.6f), new Vector3(1.5f, 1.0f, 1.5f), Gray(0.24f), 0f);
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(ax - 0.7f, 8.2f, (az0 + az1) * 0.5f), new Vector3(0.2f, 0.7f, az1 - az0 - 1.6f), archSign, false).name = "MarketSign";

            // площади: скамейки, клумбы с мёртвыми деревьями, баки
            int trees = 0;
            for (int tries = 0; tries < 220; tries++)
            {
                int x = rng.Next(CityMap.W);
                int z = rng.Next(CityMap.H);
                if (CityMap.Kind[x, z] != CityMap.Plaza || NearWall(x, z, 2)) continue;
                Vector3 c = CellCenter(x, z, 0f) + new Vector3(R(-2f, 2f), 0f, R(-2f, 2f));
                if (NearPoints(c, 9f)) continue;
                if (Reserved(c, 3.5f)) continue;
                MeshBatch pb = propS.At(c);
                double r = rng.NextDouble();
                if (r < 0.42)
                {
                    float by = R(0f, 180f);
                    Models.Bench(pb, c, by, wood);
                    Gfx.StaticBox(root, c + new Vector3(0f, 0.25f, 0f), new Vector3(2.0f, 0.5f, 0.6f), "Bench", by);
                }
                else if (r < 0.8)
                {
                    Models.Planter(pb, c, Gray(0.26f));
                    Gfx.StaticBox(root, c + new Vector3(0f, 0.3f, 0f), new Vector3(1.7f, 0.6f, 1.7f), "Planter", 0f);
                }
                else if (trees < 14)
                {
                    trees++;
                    Models.DeadTree(pb, c, rng);
                    Gfx.StaticBox(root, c + new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 2.4f, 0.7f), "Tree", 0f);
                }
                else
                {
                    Models.TrashCan(pb, c, Rgb(74, 78, 76));
                }
            }

            // улицы: гидранты, знаки, мусорные баки вдоль стен
            int hydrants = 0, signs = 0;
            for (int tries = 0; tries < 600 && (hydrants < 14 || signs < 18); tries++)
            {
                int x = rng.Next(CityMap.W);
                int z = rng.Next(CityMap.H);
                byte k = CityMap.Kind[x, z];
                if (k != CityMap.Street) continue;
                if (NearWall(x, z, 2)) continue;
                int wx = 0, wz = 0;
                if (!WallSide(x, z, out wx, out wz)) continue;
                Vector3 c = CellCenter(x, z, 0f);
                if (NearPoints(c, 8f)) continue;
                Vector3 p = c + new Vector3(wx, 0f, wz) * (C * 0.5f - 0.55f);
                p.x += (float)(rng.NextDouble() - 0.5) * 3.2f * Mathf.Abs(wz);
                p.z += (float)(rng.NextDouble() - 0.5) * 3.2f * Mathf.Abs(wx);
                if (Reserved(p, 1.6f)) continue;
                if (rng.NextDouble() < 0.5 && hydrants < 14)
                {
                    hydrants++;
                    Models.Hydrant(propS.At(p), p);
                }
                else if (signs < 18)
                {
                    signs++;
                    Color32[] boards = { Rgb(30, 70, 110), Rgb(120, 110, 40), Rgb(110, 40, 36), Rgb(40, 90, 60) };
                    Models.StreetSign(propS.At(p), p, (wx != 0) ? 90f * wx : (wz > 0 ? 180f : 0f), boards[rng.Next(boards.Length)]);
                }
            }
        }

        // ------------------------------------------------------------------ пицца

        /// <summary>Кучи хлама в переулках (x, -, z, радиус): баки, ящики, бочки, завалы в тупиках. Пицца лежит не в них.</summary>
        static readonly List<Vector4> clutterSpots = new List<Vector4>();

        /// <summary>Сколько пицц разложено и для скольких клеток из CityMap.Pizzas места не нашлось (для проверки).</summary>
        public static int PizzasPlaced;
        public static int PizzasMissed;

        /// <summary>Расстояние от точки до ближайшей стены (по четырём осям, не больше 3.2 м) и единичный вектор к ней.</summary>
        static float WallDistance(Vector3 p, out Vector3 dir)
        {
            float best = 3.2f;
            dir = Vector3.forward;
            for (int d = 0; d < 4; d++)
            {
                for (float s = 0.2f; s < best; s += 0.2f)
                {
                    if (!CityMap.IsRoad(p.x + Dx[d] * s, p.z + Dz[d] * s, false))
                    {
                        best = s;
                        dir = new Vector3(Dx[d], 0f, Dz[d]);
                        break;
                    }
                }
            }
            return best;
        }

        /// <summary>Свободно ли на полметра вокруг точки: нет ни ящика, ни бака, ни машины, ни стены (пол не в счёт).</summary>
        static bool PizzaSpotFree(Vector3 p)
        {
            Collider[] hits = Physics.OverlapSphere(p + Vector3.up * 0.4f, 0.5f);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i];
                if (c.isTrigger) continue;
                if (c.gameObject.name == "Floor") continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Место для пиццы внутри клетки переулка: на дороге, не у хлама, ламп, людей, дверей и очагов, ближе к стене,
        /// но не вплотную. Крышка коробки откинута к стене. false — в клетке места нет.
        /// </summary>
        static bool FindPizzaSpot(int cx, int cz, out Vector3 pos, out float yaw)
        {
            Vector3 c = CellCenter(cx, cz, 0f);
            pos = c;
            yaw = 0f;
            float bestScore = float.MaxValue;
            Vector3 bestWall = Vector3.forward;
            bool found = false;
            for (float ox = -2.4f; ox <= 2.41f; ox += 0.6f)
            {
                for (float oz = -2.4f; oz <= 2.41f; oz += 0.6f)
                {
                    Vector3 p = c + new Vector3(ox, 0f, oz);
                    if (!CityMap.CanStand(p.x, p.z, 0.4f, false)) continue;
                    Vector3 wd;
                    float wall = WallDistance(p, out wd);
                    if (wall < 1.3f) continue;   // бордюр-тротуар шириной 0.9 м: коробка лежит на асфальте, не на его кромке
                    if (Reserved(p, 1.9f) || NearPoints(p, 7f) || NearEntrance(p, 1.4f)) continue;
                    bool clutter = false;
                    for (int k = 0; k < clutterSpots.Count; k++)
                    {
                        Vector4 s = clutterSpots[k];
                        float dx = s.x - p.x;
                        float dz = s.z - p.z;
                        if (dx * dx + dz * dz < s.w * s.w) { clutter = true; break; }
                    }
                    if (clutter || !PizzaSpotFree(p)) continue;
                    float score = Mathf.Abs(wall - 1.9f) + R(0f, 0.35f);   // у тротуара, но не на нём; чуть случайности
                    if (score < bestScore)
                    {
                        bestScore = score;
                        pos = p;
                        bestWall = wd;
                        found = true;
                    }
                }
            }
            if (!found) return false;
            yaw = Mathf.Atan2(-bestWall.x, -bestWall.z) * Mathf.Rad2Deg + R(-25f, 25f);   // открытая сторона — от стены
            return true;
        }

        static void BuildPizzas(Transform root)
        {
            PizzasPlaced = 0;
            PizzasMissed = 0;
            Physics.SyncTransforms();
            for (int i = 0; i < CityMap.Pizzas.Count; i++)
            {
                CellPos cp = CityMap.Pizzas[i];
                Vector3 pos;
                float yaw;
                bool ok = FindPizzaSpot(cp.X, cp.Z, out pos, out yaw);
                for (int n = 0; n < 4 && !ok; n++)   // в клетке тесно: соседняя клетка того же переулка
                {
                    int nx = cp.X + Dx[n];
                    int nz = cp.Z + Dz[n];
                    if (CityMap.InGrid(nx, nz) && CityMap.Kind[nx, nz] == CityMap.Alley) ok = FindPizzaSpot(nx, nz, out pos, out yaw);
                }
                if (!ok)
                {
                    PizzasMissed++;
                    Debug.LogWarning("[PokaGoryu] No free pizza spot in cell " + cp.X + "," + cp.Z);
                    continue;
                }
                Pizza.Create(root, new Vector3(pos.x, 0f, pos.z), yaw);
                PizzasPlaced++;
            }
        }

        // ------------------------------------------------------------------ вокзал

        static void BuildStation(Transform root)
        {
            Bus.Create(root, BusPos);

            // фонари по краям площади вокзала — холодный дневной свет
            CellRect st = CityMap.Station;
            Color pale = new Color(0.85f, 0.95f, 1f, 1f);
            Vector3[] lp = StationLampSpots();
            for (int i = 0; i < lp.Length; i++)
            {
                Vector3 inward = new Vector3(st.Center.X - lp[i].x, 0f, st.Center.Z - lp[i].z).normalized;
                Lamp.Create(root, lp[i], inward, 1f, false, false, false, pale);
            }

            // павильон ожидания, скамейки, киоск
            Color32 wood = Rgb(110, 84, 52);
            for (int i = 0; i < 4; i++)
            {
                Vector3 bp = new Vector3(BusPos.x - 14f - i * 5f, 0f, BusPos.z - 10f);
                Models.Bench(propS.At(bp), bp, 0f, wood);
                Gfx.StaticBox(root, bp + new Vector3(0f, 0.25f, 0f), new Vector3(2.0f, 0.5f, 0.6f), "Bench", 0f);
            }
            Vector3 sp = new Vector3(BusPos.x - 9f, 0f, BusPos.z - 16f);
            Models.BusShelter(propS.At(sp), sp, 0f);
            Gfx.StaticBox(root, sp + new Vector3(0f, 1.3f, -0.7f), new Vector3(3.4f, 2.6f, 0.2f), "Shelter", 0f);
            AddSolid(new Vector3(BusPos.x + 12f, 1.2f, BusPos.z - 6f), new Vector3(3f, 2.4f, 2f), Rgb(50, 70, 80), 0f);
            AddBox(new Vector3(BusPos.x + 12f, 2.5f, BusPos.z - 6f), new Vector3(3.3f, 0.2f, 2.3f), Rgb(36, 50, 58), 0f);
        }

        // ------------------------------------------------------------------ сборка мешей

        static void Flush(Transform root)
        {
            chunkRenderers.Clear();
            StaticVertices = floorS.VertexCount + plazaS.VertexCount + curbS.VertexCount + markS.VertexCount + propS.VertexCount
                             + grassS.VertexCount + signS.VertexCount;
            for (int i = 0; i < 4; i++) StaticVertices += bldS[i].VertexCount;

            SignAtlas.End();
            int chunks = 0;
            chunks += grassS.Flush(root, Gfx.LitWorld(Gfx.TexGrass, 4f, 0f, 0.2f, false, false), "Grass", chunkRenderers);
            chunks += signS.Flush(root, Gfx.LitWorld(SignAtlas.Tex, 0f, 0f, 0.12f, false, false), "Signs", chunkRenderers);
            chunks += floorS.Flush(root, Gfx.LitWorld(Gfx.TexAsphalt, 12f, 0f, 1.4f, false, true), "Roads", chunkRenderers);
            chunks += plazaS.Flush(root, Gfx.LitWorld(Gfx.TexPavement, 6f, 0f, 1.0f, false, true), "Plazas", chunkRenderers);
            chunks += curbS.Flush(root, Gfx.LitWorld(Gfx.TexPavement, 6f, 0f, 0.6f, false, true), "Curbs", chunkRenderers);
            chunks += markS.Flush(root, Gfx.Lit(Color.white), "Markings", chunkRenderers);
            chunks += propS.Flush(root, Gfx.Lit(Color.white), "Props", chunkRenderers);
            for (int i = 0; i < 4; i++)
            {
                chunks += bldS[i].Flush(root, Gfx.LitWorld(Gfx.TexFacade[i], 24f, 0f, 0.08f, true), "Buildings" + i, chunkRenderers);
            }
            StaticChunks = chunks;

            GameObject cg = new GameObject("ChunkCuller");
            cg.transform.SetParent(root, false);
            Culler = cg.AddComponent<ChunkCuller>();
            for (int i = 0; i < chunkRenderers.Count; i++)
            {
                Renderer r = chunkRenderers[i];
                Culler.Add(r);
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) builtMeshes.Add(mf.sharedMesh);
            }
        }
    }
}
