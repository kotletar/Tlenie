using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Источник света сценария концовки: салон автобуса, лампы пассажиров, окно кухни.</summary>
    public sealed class EndingLight : ILightSource
    {
        public Vector3 Pos;
        public float Radius;
        public float Intensity;
        public Color Col = Color.white;
        public Vector3 LightPosition { get { return Pos; } }
        public float LightRadius { get { return Radius; } }
        public float LightIntensity { get { return Intensity; } }
        public Color LightColor { get { return Col; } }
    }

    /// <summary>
    /// Поездка из концовки: салон автобуса (стены с проёмами окон, сиденья, люди, приборная панель), дорога за городом с фонарями,
    /// «рассвет» впереди и Тьма, которая оказывается за ним. Салон — дочерний объект автобуса и едет вместе с ним; дорога, рассвет и
    /// Тьма стоят в мире. Кузов автобуса изнутри не рисуется (его грани смотрят наружу), поэтому окна — просто проёмы в салоне, а
    /// за ними настоящий город, потом дорога.
    /// </summary>
    public sealed class RideStage
    {
        public const float FloorY = 0.62f;
        public const float SillY = 1.45f;
        public const float HeadY = 2.78f;
        public const float CeilY = 2.96f;
        public const float RoadX = 66f;      // ось дороги из города (клетки 10-11 по 6 м)
        public const float CityEdgeZ = 408f; // дальше города нет: дорогу и всё остальное достраиваем

        /// <summary>Где глаза героя, в системе автобуса: переднее правое сиденье.</summary>
        public static readonly Vector3 SeatEye = new Vector3(0.8f, 1.88f, 3.2f);

        public sealed class Passenger
        {
            public Transform Root;
            public Transform Torso;
            public Transform ArmL;
            public Transform ArmR;
            public GameObject EyesOpen;
            public GameObject EyesClosed;
            public Material HaloMat;
            public Material CoreMat;
            public EndingLight Light;
            public float Seed;
        }

        /// <summary>Насколько раскрылась Тьма за «рассветом» (0..1).</summary>
        public float GateOpen;

        public readonly List<Passenger> Passengers = new List<Passenger>();
        public readonly EndingLight[] Ceiling = new EndingLight[3];

        Transform interior;
        Transform world;
        readonly List<Mesh> meshes = new List<Mesh>();
        float gateZ;
        Bus bus;

        // дорога
        Material lampMat;
        Transform[] lampSprites;
        float[] lampZ;

        // рассвет-приманка
        Material glowMat;
        Transform glowT;

        // Тьма
        Transform gate;
        Transform[] blobs;
        Vector3[] blobPos;
        Vector3[] blobScale;
        Transform[] eyes;
        Vector3[] eyePos;
        float[] eyeSize;
        Material eyeMat;
        Material hazeMat;
        Transform haze;

        static Color32 C(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        static float S(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        public static RideStage Build(Bus bus, float gateZ, int boarded)
        {
            RideStage s = new RideStage();
            s.bus = bus;
            s.gateZ = gateZ;
            s.interior = new GameObject("EndingInterior").transform;
            s.interior.SetParent(bus.transform, false);
            s.interior.localPosition = Vector3.zero;
            s.world = new GameObject("EndingRoad").transform;
            s.BuildShell();
            s.BuildPassengers(boarded);
            s.BuildRoad();
            s.BuildDawn();
            s.BuildGate();
            return s;
        }

        public void Destroy()
        {
            if (interior != null) Object.Destroy(interior.gameObject);
            if (world != null) Object.Destroy(world.gameObject);
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null) Object.Destroy(meshes[i]);
            }
            meshes.Clear();
        }

        Mesh Keep(MeshBatch b, string name)
        {
            Mesh m = b.Build(name);
            meshes.Add(m);
            return m;
        }

        // ------------------------------------------------------------------ салон

        void Seat(MeshBatch b, float x, float z)
        {
            Color32 cloth = C(88, 34, 30);
            Color32 frame = C(30, 30, 34);
            b.Box(new Vector3(x, FloorY + 0.40f, z), new Vector3(0.84f, 0.10f, 0.48f), cloth);            // подушка (верх на 0.45 над полом)
            b.Box(new Vector3(x, FloorY + 0.82f, z - 0.27f), new Vector3(0.84f, 0.74f, 0.09f), cloth);     // спинка
            b.Box(new Vector3(x, FloorY + 0.19f, z), new Vector3(0.74f, 0.30f, 0.38f), frame);             // основание
        }

        void BuildShell()
        {
            MeshBatch b = new MeshBatch();
            // салон тёмный и тёплый: ближние лампы выжигают светлые поверхности, поэтому краски приглушены
            Color32 floorC = C(34, 30, 28);
            Color32 strip = C(46, 42, 36);
            Color32 wallC = C(92, 70, 40);
            Color32 wallLow = C(66, 50, 30);
            Color32 ceilC = C(92, 86, 74);
            Color32 pillar = C(58, 44, 26);
            Color32 rail = C(92, 94, 98);
            Color32 dark = C(24, 24, 28);
            float L = 10.4f;
            float mid = (FloorY + SillY) * 0.5f;

            b.Box(new Vector3(0f, FloorY, 0.05f), new Vector3(2.56f, 0.05f, L), floorC);
            b.Box(new Vector3(0f, FloorY + 0.03f, 0.05f), new Vector3(0.7f, 0.012f, L), strip);
            b.Box(new Vector3(0f, CeilY, 0.05f), new Vector3(2.56f, 0.05f, L), ceilC);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                b.Box(new Vector3(sd * 1.28f, mid, 0.05f), new Vector3(0.05f, SillY - FloorY, L), wallLow);                 // борт ниже окон
                b.Box(new Vector3(sd * 1.28f, (HeadY + CeilY) * 0.5f, 0.05f), new Vector3(0.05f, CeilY - HeadY, L), wallC);  // над окнами
                b.Box(new Vector3(sd * 1.25f, SillY + 0.02f, 0.05f), new Vector3(0.12f, 0.05f, L), pillar);                  // подоконник
                for (int i = 0; i < 9; i++) b.Box(new Vector3(sd * 1.265f, (SillY + HeadY) * 0.5f, -4.8f + i * 1.2f), new Vector3(0.07f, HeadY - SillY, 0.14f), pillar);   // простенки
            }
            // задняя стенка, перед
            b.Box(new Vector3(0f, (FloorY + CeilY) * 0.5f, -5.16f), new Vector3(2.56f, CeilY - FloorY, 0.05f), wallLow);
            b.Box(new Vector3(0f, mid, 5.28f), new Vector3(2.56f, SillY - FloorY, 0.05f), wallLow);
            b.Box(new Vector3(0f, (HeadY + CeilY) * 0.5f, 5.28f), new Vector3(2.56f, CeilY - HeadY, 0.06f), wallC);
            for (int sd = -1; sd <= 1; sd += 2) b.Box(new Vector3(sd * 1.2f, (SillY + HeadY) * 0.5f, 5.28f), new Vector3(0.14f, HeadY - SillY, 0.1f), pillar);
            // приборная панель, руль, пустое сиденье водителя: автобус едет сам
            b.Box(new Vector3(0f, FloorY + 0.27f, 4.95f), new Vector3(2.5f, 0.54f, 0.64f), C(30, 29, 28));
            b.Box(new Vector3(0f, FloorY + 0.55f, 4.80f), new Vector3(2.4f, 0.04f, 0.30f), C(18, 18, 20));
            Vector3 wc = new Vector3(-0.7f, FloorY + 0.76f, 4.42f);
            Vector3 n = new Vector3(0f, 0.5f, -0.87f);
            Vector3 u = new Vector3(1f, 0f, 0f);
            Vector3 v = Vector3.Cross(n, u).normalized;
            Vector3 prev = wc + u * 0.21f;
            for (int i = 1; i <= 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f;
                Vector3 p = wc + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * 0.21f;
                b.Rod(prev, p, 0.016f, 4, dark);
                prev = p;
            }
            b.Rod(wc - u * 0.2f, wc + u * 0.2f, 0.014f, 4, dark);
            b.Rod(wc, new Vector3(-0.7f, FloorY + 0.54f, 4.70f), 0.03f, 5, dark);
            Seat(b, -0.75f, 3.9f);
            // сиденья: по два ряда с каждой стороны
            float[] rows = { 3.2f, 1.6f, 0.0f, -1.6f, -3.2f };
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) Seat(b, -0.8f, rows[i]);
                Seat(b, 0.8f, rows[i]);
            }
            // поручни
            float[] poleZ = { 2.4f, -0.8f, -4.0f };
            for (int i = 0; i < poleZ.Length; i++) b.Rod(new Vector3(0f, FloorY, poleZ[i]), new Vector3(0f, CeilY, poleZ[i]), 0.022f, 6, rail);
            b.Rod(new Vector3(0f, CeilY - 0.1f, -4.8f), new Vector3(0f, CeilY - 0.1f, 4.6f), 0.02f, 6, rail);

            Mesh m = Keep(b, "PG_EndingInterior");
            Gfx.MeshObject(interior, m, Gfx.Lit(Color.white), "Shell");

            // плафоны под потолком: тёплый свет, который гаснет вместе с людьми
            Material lamp = OwnedResources.Keep(interior.gameObject, Gfx.Unlit(new Color(0.9f, 0.8f, 0.6f, 1f), null, false, false, 0.2f));
            float[] lz = { 2.9f, -0.5f, -3.9f };
            for (int i = 0; i < lz.Length; i++)
            {
                Gfx.Prim(PrimitiveType.Cube, interior, new Vector3(0f, CeilY - 0.04f, lz[i]), new Vector3(0.34f, 0.03f, 0.7f), lamp, false);
                Ceiling[i] = new EndingLight { Radius = 4.6f, Intensity = 0.0f, Col = new Color(1f, 0.82f, 0.56f, 1f) };
            }
        }

        public Vector3 CeilingLocal(int i)
        {
            float[] lz = { 2.9f, -0.5f, -3.9f };
            return new Vector3(0f, 2.15f, lz[i]);   // свет ниже потолка: у самой поверхности он выжег бы её в белое
        }

        // ------------------------------------------------------------------ люди

        static Transform Part(Transform parent, string name, System.Action<MeshBatch> build, Vector3 pivot, List<Mesh> keep)
        {
            MeshBatch hb = new MeshBatch();
            build(hb);
            Mesh hm = hb.Build("PG_EndingHuman_" + name);
            keep.Add(hm);
            GameObject go = Gfx.MeshObject(parent, hm, Gfx.Lit(Color.white), name);
            go.transform.localPosition = pivot;
            return go.transform;
        }

        void BuildPassengers(int boarded)
        {
            // кто где сидит: рядом с героем слева, потом по рядам
            Vector2[] seats = { new Vector2(-0.8f, 3.2f), new Vector2(0.8f, 1.6f), new Vector2(-0.8f, 1.6f), new Vector2(0.8f, 0f), new Vector2(-0.8f, 0f), new Vector2(0.8f, -1.6f) };
            int count = Mathf.Clamp(boarded, 0, seats.Length);
            for (int i = 0; i < count; i++)
            {
                int index = i * 3 + 1;
                Passenger p = new Passenger();
                p.Seed = i * 1.7f;
                GameObject go = new GameObject("Passenger" + i);
                go.transform.SetParent(interior, false);
                // корень — таз на подушке (над полом 0.45); туловище у стоящего человека начинается на земле, таз на 0.84 выше
                go.transform.localPosition = new Vector3(seats[i].x, FloorY + 0.45f, seats[i].y + 0.10f);
                p.Root = go.transform;
                p.Torso = Part(p.Root, "Torso", delegate(MeshBatch b) { Models.HumanBody(b, index); }, new Vector3(0f, -0.84f, 0f), meshes);
                Transform legL = Part(p.Root, "LegL", delegate(MeshBatch b) { Models.HumanLeg(b, index); }, new Vector3(-0.1f, 0f, 0.02f), meshes);
                Transform legR = Part(p.Root, "LegR", delegate(MeshBatch b) { Models.HumanLeg(b, index); }, new Vector3(0.1f, 0f, 0.02f), meshes);
                legL.localRotation = Quaternion.Euler(-78f, 0f, 0f);
                legR.localRotation = Quaternion.Euler(-78f, 0f, 0f);
                p.ArmL = Part(p.Torso, "ArmL", delegate(MeshBatch b) { Models.HumanArm(b, index); }, new Vector3(-0.33f, 1.38f, 0f), meshes);
                p.ArmR = Part(p.Torso, "ArmR", delegate(MeshBatch b) { Models.HumanArm(b, index); }, new Vector3(0.33f, 1.38f, 0f), meshes);
                p.ArmL.localRotation = Quaternion.Euler(-48f, 0f, 4f);
                p.ArmR.localRotation = Quaternion.Euler(-48f, 0f, -4f);
                p.EyesOpen = Part(p.Torso, "EyesOpen", delegate(MeshBatch b) { Models.HumanEyes(b, index, true); }, Vector3.zero, meshes).gameObject;
                p.EyesClosed = Part(p.Torso, "EyesClosed", delegate(MeshBatch b) { Models.HumanEyes(b, index, false); }, Vector3.zero, meshes).gameObject;
                p.EyesClosed.SetActive(false);

                // тёплое сердце человека: ядро на груди и ореол (как у выживших в игре)
                p.CoreMat = OwnedResources.Keep(go, Gfx.Unlit(new Color(0.9f, 0.5f, 0.12f, 1f), null, false, false, 0.3f));
                Gfx.Prim(PrimitiveType.Sphere, p.Torso, new Vector3(0f, 1.12f, 0.2f), new Vector3(0.1f, 0.1f, 0.1f), p.CoreMat, false);
                p.HaloMat = OwnedResources.Keep(go, Gfx.Unlit(new Color(1f, 0.8f, 0.45f, 0.4f), Gfx.TexHalo, true, false, 0.2f));
                Gfx.Sprite(p.Torso, new Vector3(0f, 1.1f, 0.05f), new Vector2(2.8f, 2.8f), p.HaloMat, false);
                p.Light = new EndingLight { Radius = 2.8f, Intensity = 0.0f, Col = new Color(1f, 0.78f, 0.45f, 1f) };
                Passengers.Add(p);
            }
        }

        /// <summary>Свет человека: 1 — тёплый, 0 — погас (глаза закрыты, огонёк на груди тёмный).</summary>
        public void SetPassenger(int i, float glow, float time)
        {
            Passenger p = Passengers[i];
            float flick = 0.92f + 0.08f * Mathf.Sin(time * 7f + p.Seed);
            float k = Mathf.Clamp01(glow) * flick;
            p.HaloMat.SetColor("_Color", new Color(1f, 0.8f, 0.45f, 0.62f * k));
            p.CoreMat.SetColor("_Color", Color.Lerp(new Color(0.08f, 0.06f, 0.06f, 1f), new Color(0.95f, 0.55f, 0.16f, 1f), k));
            p.Light.Intensity = 0.40f * k;
            bool open = glow > 0.12f;
            if (p.EyesOpen.activeSelf != open) p.EyesOpen.SetActive(open);
            if (p.EyesClosed.activeSelf == open) p.EyesClosed.SetActive(!open);
            float breath = Mathf.Sin(time * 1.5f + p.Seed) * 1.2f;
            p.ArmL.localRotation = Quaternion.Euler(-48f + breath, 0f, 4f);
            p.ArmR.localRotation = Quaternion.Euler(-48f - breath, 0f, -4f);
        }

        // ------------------------------------------------------------------ дорога за городом

        void BuildRoad()
        {
            MeshBatch b = new MeshBatch();
            float z0 = CityEdgeZ;
            float z1 = gateZ + 120f;
            float len = z1 - z0;
            float zc = (z0 + z1) * 0.5f;
            Color32 asphalt = C(34, 34, 38);
            Color32 verge = C(22, 22, 22);
            Color32 line = C(180, 172, 140);
            Color32 metal = C(44, 44, 48);
            b.Box(new Vector3(RoadX, -0.03f, zc), new Vector3(12f, 0.06f, len), asphalt);
            b.Box(new Vector3(RoadX - 12f, -0.04f, zc), new Vector3(12f, 0.06f, len), verge);
            b.Box(new Vector3(RoadX + 12f, -0.04f, zc), new Vector3(12f, 0.06f, len), verge);
            for (float z = z0 + 6f; z < z1; z += 8f) b.Box(new Vector3(RoadX, 0.0f, z), new Vector3(0.25f, 0.05f, 3.2f), line);
            b.Box(new Vector3(RoadX - 5.7f, 0.0f, zc), new Vector3(0.18f, 0.05f, len), line);
            b.Box(new Vector3(RoadX + 5.7f, 0.0f, zc), new Vector3(0.18f, 0.05f, len), line);

            List<Vector3> bulbs = new List<Vector3>();
            for (float z = z0 + 14f; z < gateZ + 60f; z += 26f)
            {
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float x = RoadX + sd * 6.8f;
                    b.Box(new Vector3(x, 2.6f, z), new Vector3(0.16f, 5.2f, 0.16f), metal);
                    b.Box(new Vector3(x - sd * 0.8f, 5.15f, z), new Vector3(1.6f, 0.1f, 0.12f), metal);
                    bulbs.Add(new Vector3(x - sd * 1.5f, 4.95f, z));
                }
            }
            Mesh m = Keep(b, "PG_EndingRoad");
            Gfx.MeshObject(world, m, Gfx.Lit(Color.white), "Road");

            lampMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(1f, 0.82f, 0.5f, 0.6f), Gfx.TexHalo, true, false, 0.05f));
            lampSprites = new Transform[bulbs.Count];
            lampZ = new float[bulbs.Count];
            for (int i = 0; i < bulbs.Count; i++)
            {
                lampSprites[i] = Gfx.Sprite(world, bulbs[i], new Vector2(2.2f, 2.2f), lampMat, false).transform;
                lampZ[i] = bulbs[i].z;
            }
        }

        /// <summary>Фонари вдоль дороги: гаснут те, что дальше фронта Тьмы (front — на сколько метров впереди автобуса ещё горит).</summary>
        public void SetRoadLamps(float busZ, float front)
        {
            for (int i = 0; i < lampSprites.Length; i++)
            {
                bool lit = lampZ[i] < busZ + front;
                if (lampSprites[i].gameObject.activeSelf != lit) lampSprites[i].gameObject.SetActive(lit);
            }
        }

        // ------------------------------------------------------------------ «рассвет» впереди

        void BuildDawn()
        {
            glowMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(1f, 0.82f, 0.5f, 0f), Gfx.TexHalo, true, false, 0f));
            glowT = Gfx.Sprite(world, new Vector3(RoadX, 22f, gateZ + 80f), new Vector2(240f, 130f), glowMat, false).transform;
        }

        /// <summary>Рассвет-приманка: k — яркость, violet — сколько в нём фиолетового (0 — золото, 1 — Тьма).</summary>
        public void SetDawn(float k, float violet)
        {
            Color gold = new Color(1f, 0.80f, 0.46f, 1f);
            Color dark = new Color(0.50f, 0.18f, 0.85f, 1f);
            Color c = Color.Lerp(gold, dark, violet);
            glowMat.SetColor("_Color", new Color(c.r, c.g, c.b, 0.85f * k));
        }

        // ------------------------------------------------------------------ Тьма за рассветом

        static float Rd(System.Random r, float a, float b)
        {
            return a + (float)r.NextDouble() * (b - a);
        }

        void BuildGate()
        {
            gate = new GameObject("EndingGate").transform;
            gate.SetParent(world, false);
            gate.position = new Vector3(RoadX, 0f, gateZ);
            System.Random r = new System.Random(4242);
            Material tar = Gfx.Lit(new Color(0.07f, 0.04f, 0.11f, 1f));
            int n = 52;
            blobs = new Transform[n];
            blobPos = new Vector3[n];
            blobScale = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p;
                float sc;
                if (i < 30)
                {
                    // арка над дорогой и по бокам
                    float a = Mathf.PI * (0.02f + 0.96f * i / 29f);
                    p = new Vector3(Mathf.Cos(a) * 15f + Rd(r, -1.5f, 1.5f), Mathf.Sin(a) * 11f + Rd(r, -1f, 1f), Rd(r, -3f, 3f));
                    sc = Rd(r, 4.4f, 6.4f);
                }
                else
                {
                    // стена за аркой
                    p = new Vector3(Rd(r, -13f, 13f), Rd(r, 0.4f, 8f), Rd(r, 5f, 15f));
                    sc = Rd(r, 4.6f, 7.2f);
                }
                blobPos[i] = p;
                blobScale[i] = new Vector3(sc, sc * 0.9f, sc);
                blobs[i] = Gfx.Prim(PrimitiveType.Sphere, gate, p, Vector3.zero, tar, false).transform;
            }

            eyeMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(0.8f, 0.55f, 1f, 0.95f), Gfx.TexHalo, true, false, 0.1f));
            int m = 90;
            eyes = new Transform[m];
            eyePos = new Vector3[m];
            eyeSize = new float[m];
            for (int i = 0; i < m; i++)
            {
                float a = Rd(r, 0.05f, 0.95f) * Mathf.PI;
                float rad = Rd(r, 3f, 14f);
                Vector3 p = new Vector3(Mathf.Cos(a) * (6f + rad), Mathf.Sin(a) * (3f + rad * 0.7f) + 0.5f, Rd(r, -5f, 1f));
                eyePos[i] = p;
                eyeSize[i] = Rd(r, 0.7f, 1.6f);
                eyes[i] = Gfx.Sprite(gate, p, new Vector2(eyeSize[i], eyeSize[i]), eyeMat, false).transform;
                eyes[i].localScale = Vector3.zero;
            }

            hazeMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(0.34f, 0.12f, 0.52f, 0f), Gfx.TexHalo, true, false, 0f));
            haze = Gfx.Sprite(gate, new Vector3(0f, 8f, -2f), new Vector2(120f, 50f), hazeMat, false).transform;
        }

        /// <summary>
        /// Тьма: open — как далеко она раскрылась (0 — нет, 1 — вся), squeeze — сходится над дорогой (автобус въезжает), time — для морганий.
        /// </summary>
        public void SetGate(float open, float squeeze, float time)
        {
            GateOpen = open;
            for (int i = 0; i < blobs.Length; i++)
            {
                float g = S(i * 0.008f, i * 0.008f + 0.55f, open);
                Vector3 p = blobPos[i];
                p.x = Mathf.Lerp(p.x, p.x * 0.18f, squeeze);
                p.z = Mathf.Lerp(p.z, p.z - 10f, squeeze);
                p.y += 0.25f * Mathf.Sin(time * 1.3f + i * 1.9f) * g;
                blobs[i].localPosition = p;
                blobs[i].localScale = blobScale[i] * (g * (1f + 0.05f * Mathf.Sin(time * 1.5f + i * 1.7f)));
            }
            for (int i = 0; i < eyes.Length; i++)
            {
                float g = S(0.25f + i * 0.004f, 0.7f + i * 0.004f, open);
                float blink = 0.5f + 0.5f * Mathf.Sin(time * 0.9f + i * 2.3f);
                float s = eyeSize[i] * g * (0.55f + 0.45f * blink);
                Vector3 p = eyePos[i];
                p.x = Mathf.Lerp(p.x, p.x * 0.18f, squeeze);
                eyes[i].localPosition = p;
                eyes[i].localScale = new Vector3(s, s, 1f);
            }
            hazeMat.SetColor("_Color", new Color(0.34f, 0.12f, 0.52f, 0.34f * S(0.1f, 0.8f, open)));
        }
    }
}
