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
    /// Поездка из концовки: салон автобуса (стены с проёмами окон, сиденья, водитель за рулём, люди, приборная панель), дорога за городом
    /// с фонарями, «рассвет» впереди и Тьма, которая оказывается за ним и поглощает автобус. Салон — дочерний объект автобуса и едет
    /// вместе с ним; дорога, рассвет и Тьма стоят в мире. Кузов автобуса изнутри не рисуется (его грани смотрят наружу), поэтому окна —
    /// просто проёмы в салоне, а за ними настоящий город, потом дорога.
    ///
    /// Тьма — не шары, а объёмный дым (SmokeField): стена клубов с рваным краем, на которую падает свет рассвета с обратной стороны;
    /// передняя клубящаяся кромка; языки, которые выбрасываются вперёд над дорогой и смыкаются над автобусом; низкий туман, стелющийся
    /// по асфальту; а когда фронт доходит до автобуса, тьма просачивается в салон через окна и растекается по полу. Всё считается от
    /// времени сценария (SetDarkness), без накопления состояния.
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

        /// <summary>Где сидит водитель (x, z в системе автобуса): переднее левое сиденье, лицом вперёд.</summary>
        public static readonly Vector2 DriverSeat = new Vector2(-0.75f, 3.9f);

        const float WheelR = 0.21f;

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
            public bool IsDriver;
            public Transform Lean;   // водитель: корпус наклоняется и поворачивается вокруг таза
            public Transform Head;   // водитель: голова поворачивается на шее
        }

        /// <summary>Насколько раскрылась Тьма за «рассветом» (0..1).</summary>
        public float GateOpen;

        /// <summary>Сколько клубов дыма нарисовано в последнем кадре (для автотестов: Тьма реально видна).</summary>
        public int DarkPuffsDrawn;
        public int CabinPuffsDrawn;

        public readonly List<Passenger> Passengers = new List<Passenger>();
        public readonly EndingLight[] Ceiling = new EndingLight[3];

        /// <summary>За рулём сидит водитель (для автотестов; первый в списке Passengers, гаснет первым).</summary>
        public bool HasDriver { get { return driver != null && Passengers.Count > 0 && Passengers[0] == driver; } }

        /// <summary>Для автотестов: расстояние по полу от водителя до ближайшего из жителей, м (9, если жителей нет). Вплотную к водителю никто не сидит.</summary>
        public float DriverClearance()
        {
            if (driver == null) return 9f;
            float best = 9f;
            for (int i = 0; i < Passengers.Count; i++)
            {
                if (Passengers[i] == driver) continue;
                Vector3 d = Passengers[i].Root.localPosition - driver.Root.localPosition;
                d.y = 0f;
                best = Mathf.Min(best, d.magnitude);
            }
            return best;
        }

        Transform interior;
        Transform world;
        readonly List<Mesh> meshes = new List<Mesh>();
        float gateZ;
        Bus bus;

        // руль и водитель
        Transform wheelTurn;
        Passenger driver;

        // дорога
        Material lampMat;
        Transform[] lampSprites;
        float[] lampZ;

        // рассвет-приманка
        Material glowMat;
        Transform glowT;

        // Тьма
        Transform gate;
        Texture2D smokeAtlas;
        SmokeField smoke;
        SmokeField cabin;
        Def[] defs;
        CabinDef[] cabinDefs;
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

        /// <summary>Кусочно-плавная кривая по ключам: на каждом отрезке разгон и торможение (скорость в ключах нулевая).</summary>
        public static float Ease(float[] ts, float[] vs, float x)
        {
            int n = ts.Length;
            if (x <= ts[0]) return vs[0];
            if (x >= ts[n - 1]) return vs[n - 1];
            int i = 0;
            while (i < n - 2 && x >= ts[i + 1]) i++;
            return Mathf.Lerp(vs[i], vs[i + 1], S(ts[i], ts[i + 1], x));
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
            s.BuildDriver();
            s.BuildPassengers(boarded);
            s.BuildRoad();
            s.BuildDawn();
            s.BuildGate();
            return s;
        }

        public void Destroy()
        {
            if (smoke != null) smoke.Destroy();
            if (cabin != null) cabin.Destroy();
            smoke = null;
            cabin = null;
            if (smokeAtlas != null) Object.Destroy(smokeAtlas);
            smokeAtlas = null;
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
            // приборная панель, рулевая колонка и сиденье водителя (сам водитель — BuildDriver, руль — отдельная вращающаяся деталь)
            b.Box(new Vector3(0f, FloorY + 0.27f, 4.95f), new Vector3(2.5f, 0.54f, 0.64f), C(30, 29, 28));
            b.Box(new Vector3(0f, FloorY + 0.55f, 4.80f), new Vector3(2.4f, 0.04f, 0.30f), C(18, 18, 20));
            Vector3 wc = new Vector3(-0.7f, FloorY + 0.76f, 4.42f);
            b.Rod(wc, new Vector3(-0.7f, FloorY + 0.54f, 4.70f), 0.03f, 5, dark);
            Seat(b, DriverSeat.x, DriverSeat.y);
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

            // руль: обод со спицами в своей плоскости (ось — вдоль нормали колеса), поворачивается вокруг этой оси
            Vector3 n = new Vector3(0f, 0.5f, -0.87f).normalized;
            Vector3 u = new Vector3(1f, 0f, 0f);
            Transform wheelBase = new GameObject("WheelBase").transform;
            wheelBase.SetParent(interior, false);
            wheelBase.localPosition = wc;
            wheelBase.localRotation = Quaternion.LookRotation(n, Vector3.Cross(n, u));
            wheelTurn = new GameObject("WheelTurn").transform;
            wheelTurn.SetParent(wheelBase, false);
            MeshBatch wb = new MeshBatch();
            Color32 wheelC = C(70, 68, 76);   // светлее приборной панели, чтобы обод читался на её фоне
            Vector3 prev = new Vector3(WheelR, 0f, 0f);
            for (int i = 1; i <= 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f;
                Vector3 p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * WheelR;
                wb.Rod(prev, p, 0.022f, 4, wheelC);
                prev = p;
            }
            wb.Rod(new Vector3(-WheelR + 0.01f, 0f, 0f), new Vector3(WheelR - 0.01f, 0f, 0f), 0.016f, 4, wheelC);
            wb.Rod(new Vector3(0f, 0f, 0f), new Vector3(0f, WheelR - 0.01f, 0f), 0.016f, 4, wheelC);
            wb.Rod(new Vector3(0f, 0f, -0.01f), new Vector3(0f, 0f, 0.035f), 0.04f, 6, wheelC);
            Gfx.MeshObject(wheelTurn, Keep(wb, "PG_EndingWheel"), Gfx.Lit(Color.white), "Wheel");

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

        /// <summary>Тёплое сердце человека: ядро на груди и ореол (как у выживших в игре), свет для окружающих.</summary>
        void AddHeart(Passenger p, GameObject owner)
        {
            p.CoreMat = OwnedResources.Keep(owner, Gfx.Unlit(new Color(0.9f, 0.5f, 0.12f, 1f), null, false, false, 0.3f));
            Gfx.Prim(PrimitiveType.Sphere, p.Torso, new Vector3(0f, 1.12f, 0.2f), new Vector3(0.1f, 0.1f, 0.1f), p.CoreMat, false);
            p.HaloMat = OwnedResources.Keep(owner, Gfx.Unlit(new Color(1f, 0.8f, 0.45f, 0.4f), Gfx.TexHalo, true, false, 0.2f));
            Gfx.Sprite(p.Torso, new Vector3(0f, 1.1f, 0.05f), new Vector2(2.8f, 2.8f), p.HaloMat, false);
            p.Light = new EndingLight { Radius = 2.8f, Intensity = 0.0f, Col = new Color(1f, 0.78f, 0.45f, 1f) };
        }

        /// <summary>
        /// Водитель: форменная куртка и фуражка, сидит у руля лицом вперёд, держит обод в «десять и два». Голова и корпус отдельные части:
        /// он оглядывается на героя и на дорогу. Свет у него такой же тёплый, как у пассажиров, и гаснет первым.
        /// </summary>
        void BuildDriver()
        {
            Passenger p = new Passenger();
            p.IsDriver = true;
            p.Seed = 5.3f;
            GameObject go = new GameObject("Driver");
            go.transform.SetParent(interior, false);
            go.transform.localPosition = new Vector3(DriverSeat.x, FloorY + 0.45f, DriverSeat.y + 0.10f);
            p.Root = go.transform;
            GameObject leanGo = new GameObject("Lean");
            leanGo.transform.SetParent(p.Root, false);
            p.Lean = leanGo.transform;
            p.Torso = Part(p.Lean, "Torso", delegate(MeshBatch b) { Models.HumanBody(b, Models.DriverLook, 1); }, new Vector3(0f, -0.84f, 0f), meshes);
            p.Head = Part(p.Torso, "Head", delegate(MeshBatch b) { Models.HumanBody(b, Models.DriverLook, 2); }, new Vector3(0f, Models.HeadPivotY, 0f), meshes);
            Transform legL = Part(p.Root, "LegL", delegate(MeshBatch b) { Models.HumanLeg(b, Models.DriverLook); }, new Vector3(-0.1f, 0f, 0.02f), meshes);
            Transform legR = Part(p.Root, "LegR", delegate(MeshBatch b) { Models.HumanLeg(b, Models.DriverLook); }, new Vector3(0.1f, 0f, 0.02f), meshes);
            legL.localRotation = Quaternion.Euler(-58f, 0f, 0f);   // колени вперёд, ступни под приборной панелью
            legR.localRotation = Quaternion.Euler(-52f, 0f, 0f);
            p.ArmL = Part(p.Torso, "ArmL", delegate(MeshBatch b) { Models.HumanArm(b, Models.DriverLook); }, new Vector3(-0.33f, 1.38f, 0f), meshes);
            p.ArmR = Part(p.Torso, "ArmR", delegate(MeshBatch b) { Models.HumanArm(b, Models.DriverLook); }, new Vector3(0.33f, 1.38f, 0f), meshes);
            p.EyesOpen = Part(p.Head, "EyesOpen", delegate(MeshBatch b) { Models.HumanEyes(b, Models.DriverLook, true, Models.HeadPivotY); }, Vector3.zero, meshes).gameObject;
            p.EyesClosed = Part(p.Head, "EyesClosed", delegate(MeshBatch b) { Models.HumanEyes(b, Models.DriverLook, false, Models.HeadPivotY); }, Vector3.zero, meshes).gameObject;
            p.EyesClosed.SetActive(false);
            AddHeart(p, go);
            driver = p;
            Passengers.Add(p);
            PoseDriver(p, 0f);
        }

        // кто куда смотрит: время, поворот головы вправо (к герою), градусы. Следит за дорогой, на секунду оборачивается к герою, когда тот смотрит на него.
        static readonly float[] DriverLookT = { 0f, 1.9f, 2.7f, 3.6f, 4.4f, 6.9f, 7.6f, 8.5f, 40f };
        static readonly float[] DriverLookYaw = { 0f, 0f, 78f, 78f, 0f, 0f, -22f, 0f, 0f };

        /// <summary>Поза водителя в момент time: руль, корпус, голова, руки на ободе (руки считаются от плеч к рукояткам обода).</summary>
        void PoseDriver(Passenger p, float time)
        {
            // автобус уходит вправо на ось дороги: руль вправо, потом выравнивается
            float u = Mathf.Clamp01((time - 1.0f) / 5.2f);
            float steer = 11f * Mathf.Sin(u * Mathf.PI * 2f) + 0.8f * Mathf.Sin(time * 1.3f + 1f);
            wheelTurn.localRotation = Quaternion.Euler(0f, 0f, steer);

            float look = Ease(DriverLookT, DriverLookYaw, time);
            float breath = Mathf.Sin(time * 1.5f + p.Seed);
            float slump = S(13.55f, 14.8f, time);   // свет погас: водитель обмякает
            float lean = 5f + 1.2f * breath + 14f * slump;
            float twist = 0.3f * look;
            p.Lean.localRotation = Quaternion.Euler(lean, twist, 0f);
            p.Head.localRotation = Quaternion.Euler(1.5f * Mathf.Sin(time * 0.9f) + 24f * slump, look - twist, 2.5f * slump);
            for (int a = 0; a < 2; a++)
            {
                float side = a == 0 ? -1f : 1f;
                float ang = (-90f + side * 60f) * Mathf.Deg2Rad;   // «десять» и «два» часов на ободе
                Vector3 grip = wheelTurn.TransformPoint(new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * WheelR);
                Transform arm = a == 0 ? p.ArmL : p.ArmR;
                Vector3 d = grip - arm.position;
                float len = d.magnitude;
                if (len < 0.05f) continue;
                arm.rotation = Quaternion.FromToRotation(Vector3.down, d / len);
                arm.localScale = new Vector3(1f, Mathf.Clamp(len / 0.61f, 0.75f, 1.15f), 1f);
            }
        }

        void BuildPassengers(int boarded)
        {
            // кто где сидит: сзади героя и через проход, потом по рядам. Слева у самого водителя (x -0.8, z 3.2) сидеть некому: там нет
            // сиденья, а человек оказывался в 0,7 м за спиной водителя и ногами в его кресле
            Vector2[] seats = { new Vector2(0.8f, 1.6f), new Vector2(-0.8f, 1.6f), new Vector2(0.8f, 0f), new Vector2(-0.8f, 0f), new Vector2(0.8f, -1.6f), new Vector2(-0.8f, -1.6f) };
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
                AddHeart(p, go);
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
            if (p.IsDriver)
            {
                PoseDriver(p, time);
                return;
            }
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

        /// <summary>Фонари вдоль дороги: гаснут те, что Тьма уже накрыла (дальше её переднего края frontZ). До появления Тьмы горят все.</summary>
        public void SetRoadLamps(float frontZ)
        {
            for (int i = 0; i < lampSprites.Length; i++)
            {
                bool lit = lampZ[i] < frontZ;
                if (lampSprites[i].gameObject.activeSelf != lit) lampSprites[i].gameObject.SetActive(lit);
            }
        }

        // ------------------------------------------------------------------ «рассвет» впереди

        void BuildDawn()
        {
            glowMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(1f, 0.82f, 0.5f, 0f), Gfx.TexHalo, true, false, 0f));
            glowMat.renderQueue = 2990;   // раньше дыма: иначе зарево, сложенное аддитивно, светило бы поверх Тьмы и она не могла бы его закрыть
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

        // ------------------------------------------------------------------ Тьма за рассветом: объёмный дым

        // Система Тьмы: x вбок от оси дороги, y вверх, z вглубь (от переднего края к горизонту). Её начало надвигается на автобус.
        const float Advance = 30f;       // на сколько метров Тьма надвигается на автобус за время поездки к ней
        const float FrontDepth = 14f;    // передний край клубящейся кромки лежит на столько метров перед центром

        enum Kind { Wall, Billow, Mist, Tongue, Core }

        struct Def
        {
            public Kind K;
            public Vector3 B;       // основание в системе Тьмы
            public Vector3 C;       // язык: опорная точка дуги
            public Vector3 T;       // язык: кончик
            public float S0;        // язык: доля вдоль языка
            public float Size;
            public float Appear;    // с какого момента растёт
            public float Grow;      // за сколько секунд вырастает
            public float Roll;
            public float Spin;
            public float Seed;
            public int Tile;
        }

        struct CabinDef
        {
            public Vector3 A;       // откуда (окно), в системе автобуса
            public Vector3 B;       // куда плывёт
            public float Size;
            public float Appear;
            public float Alpha;
            public float Roll;
            public float Seed;
            public int Tile;
        }

        static float Rd(System.Random r, float a, float b)
        {
            return a + (float)r.NextDouble() * (b - a);
        }

        /// <summary>Где сейчас центр Тьмы по Z: надвигается на автобус и к моменту поглощения (TEngulf) совпадает с исходной точкой.</summary>
        float GateCenterZ(float t)
        {
            return gateZ + Advance * (1f - S(EndingDirector.TOpen, EndingDirector.TEngulf, t));
        }

        /// <summary>Передний край Тьмы по Z (по нему гаснут фонари): до появления Тьмы далеко впереди.</summary>
        public float FrontZ(float t)
        {
            if (t < EndingDirector.TOpen - 0.4f) return gateZ + 500f;
            return GateCenterZ(t) - FrontDepth;
        }

        void BuildGate()
        {
            gate = new GameObject("EndingGate").transform;
            gate.SetParent(world, false);
            gate.position = new Vector3(RoadX, 0f, gateZ);
            smokeAtlas = SmokeField.MakeAtlas();
            System.Random r = new System.Random(4242);
            float to = EndingDirector.TOpen;
            List<Def> list = new List<Def>();

            // стена: высокая масса позади, куполом, шире и выше дороги
            for (int i = 0; i < 56; i++)
            {
                Def d = new Def();
                d.K = Kind.Wall;
                float x = Rd(r, -58f, 58f);
                float cap = 12f + 28f * (1f - (x / 62f) * (x / 62f));
                d.B = new Vector3(x, Rd(r, 4f, cap), Rd(r, 2f, 34f));
                d.Size = Rd(r, 16f, 30f);
                d.Appear = to - 0.4f + Rd(r, 0f, 1.6f);
                d.Grow = Rd(r, 2.0f, 3.0f);
                list.Add(d);
            }
            // передняя клубящаяся кромка; у самой дороги — самые тёмные клубы («пасть»)
            for (int i = 0; i < 34; i++)
            {
                Def d = new Def();
                d.K = Kind.Billow;
                bool mouth = i < 10;
                float x = mouth ? Rd(r, 3f, 14f) * (r.Next(2) == 0 ? -1f : 1f) : Rd(r, -34f, 34f);
                d.B = new Vector3(x, mouth ? Rd(r, 2f, 14f) : Rd(r, 3f, 24f), Rd(r, -14f, 3f));
                d.Size = mouth ? Rd(r, 8f, 14f) : Rd(r, 9f, 19f);
                d.Appear = to + Rd(r, 0f, 1.8f);
                d.Grow = Rd(r, 1.6f, 2.4f);
                list.Add(d);
            }
            // ядро: плотная чёрная масса вдоль дороги внутри Тьмы, чтобы автобус въезжал не в прозрачный туман, а в сплошную тьму
            for (int i = 0; i < 50; i++)
            {
                Def d = new Def();
                d.K = Kind.Core;
                float size = Rd(r, 9f, 17f);
                d.B = new Vector3(Rd(r, -11f, 11f), Mathf.Max(0.4f * size, Rd(r, 1f, 15f)), Rd(r, -8f, 30f));
                d.Size = size;
                d.Appear = to + 0.8f + Rd(r, 0f, 2.0f);
                d.Grow = Rd(r, 1.8f, 2.6f);
                list.Add(d);
            }
            // туман над самой дорогой: тянется навстречу автобусу
            for (int i = 0; i < 30; i++)
            {
                Def d = new Def();
                d.K = Kind.Mist;
                float size = Rd(r, 7f, 13f);
                d.B = new Vector3(Rd(r, -13f, 13f), 0.45f * size + Rd(r, 0.3f, 1.5f), Rd(r, -58f, 2f));
                d.Size = size;
                d.Appear = to + 0.6f + Rd(r, 0f, 2.2f);
                d.Grow = Rd(r, 1.4f, 2.0f);
                list.Add(d);
            }
            // языки: цепочки клубов, которые выбрасываются вперёд над дорогой и смыкаются над автобусом
            for (int k = 0; k < 6; k++)
            {
                float side = k % 2 == 0 ? -1f : 1f;
                Vector3 b0 = new Vector3(side * Rd(r, 14f, 30f), Rd(r, 12f, 24f), Rd(r, -4f, 6f));
                Vector3 t0 = new Vector3(side * Rd(r, 0.5f, 7f), Rd(r, 3f, 8f), Rd(r, -34f, -22f));
                Vector3 c0 = new Vector3(side * Mathf.Abs(b0.x) * 0.75f, Mathf.Max(b0.y, 20f) + Rd(r, 2f, 6f), (b0.z + t0.z) * 0.5f);
                float appear = to + 1.1f + 0.28f * k;
                for (int j = 0; j < 8; j++)
                {
                    Def d = new Def();
                    d.K = Kind.Tongue;
                    d.B = b0;
                    d.C = c0;
                    d.T = t0;
                    d.S0 = j / 7f;
                    d.Size = Mathf.Lerp(9.5f, 3.6f, d.S0) * Rd(r, 0.85f, 1.15f);
                    d.Appear = appear;
                    d.Grow = 2.4f;
                    list.Add(d);
                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                Def d = list[i];
                d.Roll = Rd(r, -25f, 25f);
                d.Spin = Rd(r, -2f, 2f);
                d.Seed = Rd(r, 0f, 20f);
                d.Tile = r.Next(4);
                list[i] = d;
            }
            defs = list.ToArray();
            smoke = new SmokeField(world, smokeAtlas, defs.Length, 3000, "EndingDarkness");

            // глаза во тьме: светящиеся точки на передней кромке, моргают
            eyeMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(0.8f, 0.55f, 1f, 0.95f), Gfx.TexHalo, true, false, 0.1f));
            int m = 90;
            eyes = new Transform[m];
            eyePos = new Vector3[m];
            eyeSize = new float[m];
            for (int i = 0; i < m; i++)
            {
                float side = r.Next(2) == 0 ? -1f : 1f;
                Vector3 p = new Vector3(side * Rd(r, 3f, 40f), Rd(r, 3f, 30f), Rd(r, -10f, 8f));
                eyePos[i] = p;
                eyeSize[i] = Rd(r, 1.4f, 3.4f);
                eyes[i] = Gfx.Sprite(gate, p, new Vector2(eyeSize[i], eyeSize[i]), eyeMat, false).transform;
                eyes[i].localScale = Vector3.zero;
            }

            // фиолетовое зарево позади стены: на нём её силуэт читается
            hazeMat = OwnedResources.Keep(world.gameObject, Gfx.Unlit(new Color(0.34f, 0.12f, 0.52f, 0f), Gfx.TexHalo, true, false, 0f));
            hazeMat.renderQueue = 2995;
            haze = Gfx.Sprite(gate, new Vector3(0f, 16f, 44f), new Vector2(240f, 100f), hazeMat, false).transform;

            // тьма в салоне: клубы у окон и у лобового стекла и низкий туман по полу (в системе автобуса)
            List<CabinDef> cl = new List<CabinDef>();
            for (int i = 0; i < 28; i++)
            {
                CabinDef c = new CabinDef();
                float side = i % 2 == 0 ? -1f : 1f;
                float z = Rd(r, -5f, 5.2f);
                c.A = new Vector3(side * Rd(r, 1.1f, 1.4f), Rd(r, 1.3f, 2.9f), z);
                c.B = new Vector3(side * Rd(r, 0.3f, 0.9f), c.A.y + Rd(r, -0.5f, 0.2f), z + Rd(r, -0.6f, 0.2f));
                c.Size = Rd(r, 0.9f, 1.8f);
                c.Appear = 14.15f + (5.2f - z) / 12f + Rd(r, 0f, 0.4f);
                c.Alpha = 0.85f;
                cl.Add(c);
            }
            for (int i = 0; i < 8; i++)
            {
                CabinDef c = new CabinDef();
                c.A = new Vector3(Rd(r, -1.2f, 1.2f), Rd(r, 1.5f, 2.9f), Rd(r, 4.9f, 5.35f));
                c.B = new Vector3(c.A.x * 0.8f, c.A.y - Rd(r, 0f, 0.4f), c.A.z - Rd(r, 0.3f, 1.2f));
                c.Size = Rd(r, 1.2f, 2.2f);
                c.Appear = 14.0f + Rd(r, 0f, 0.35f);
                c.Alpha = 0.9f;
                cl.Add(c);
            }
            for (int i = 0; i < 14; i++)
            {
                CabinDef c = new CabinDef();
                float z = Rd(r, -5f, 5f);
                c.A = new Vector3(Rd(r, -1.1f, 1.1f), Rd(r, 0.75f, 1.1f), z);
                c.B = new Vector3(c.A.x * 0.6f, c.A.y + Rd(r, 0f, 0.15f), z);
                c.Size = Rd(r, 1.2f, 2.2f);
                c.Appear = 14.4f + (5.2f - z) / 12f + Rd(r, 0f, 0.5f);
                c.Alpha = 0.55f;
                cl.Add(c);
            }
            for (int i = 0; i < cl.Count; i++)
            {
                CabinDef c = cl[i];
                c.Roll = Rd(r, -25f, 25f);
                c.Seed = Rd(r, 0f, 20f);
                c.Tile = r.Next(4);
                cl[i] = c;
            }
            cabinDefs = cl.ToArray();
            cabin = new SmokeField(world, smokeAtlas, cabinDefs.Length, 3020, "EndingCabinDark");   // вершины в мировых координатах, как и у наружного дыма
            cabin.NearA = 0.35f;   // в салоне клубы мелкие и их надо видеть вблизи
            cabin.NearB = 1.3f;
        }

        static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float s)
        {
            float u = 1f - s;
            return u * u * a + 2f * u * s * c + s * s * b;
        }

        /// <summary>
        /// Тьма в момент t: двигает клубы, их размер и прозрачность, глаза, зарево и дым в салоне. camPos нужна для воздушной перспективы
        /// (далёкая масса светлее и фиолетовее). Меши собираются отдельно, в LateUpdate (DrawDarkness), когда камера уже стоит.
        /// </summary>
        public void SetDarkness(float t, Vector3 camPos)
        {
            float to = EndingDirector.TOpen;
            float open = S(to, 13.6f, t);
            GateOpen = open;
            float gz = GateCenterZ(t);
            gate.position = new Vector3(RoadX, 0f, gz);
            Vector3 g = gate.position;
            Color haze1 = new Color(0.46f, 0.17f, 0.66f, 1f);

            for (int i = 0; i < defs.Length; i++)
            {
                Def d = defs[i];
                float grow = S(d.Appear, d.Appear + d.Grow, t);
                SmokeField.Puff p = new SmokeField.Puff();
                p.Tile = d.Tile;
                p.Roll = d.Roll + d.Spin * t;
                Vector3 pos;
                float alpha;
                Color col;
                if (d.K == Kind.Tongue)
                {
                    float s = d.S0 * grow;
                    pos = Bezier(d.B, d.C, d.T, s);
                    pos.y += 0.6f * Mathf.Sin(t * 1.4f + d.Seed + d.S0 * 5f);
                    pos.x += 0.8f * Mathf.Sin(t * 1.1f + d.Seed * 0.7f + d.S0 * 4f);
                    p.Size = d.Size * Mathf.Clamp01(grow * 3f - d.S0 * 2f);
                    alpha = 0.92f;
                    col = new Color(0.14f, 0.055f, 0.22f, 1f);
                }
                else
                {
                    float k = d.Size * 0.05f;
                    pos = d.B + new Vector3(Mathf.Sin(t * 0.55f + d.Seed) * k, Mathf.Sin(t * 0.43f + d.Seed * 1.9f) * k * 0.7f, Mathf.Sin(t * 0.61f + d.Seed * 0.7f) * k);
                    p.Size = d.Size * grow * (1f + 0.04f * Mathf.Sin(t * 1.3f + d.Seed));
                    if (d.K == Kind.Wall)
                    {
                        float rim = Mathf.Clamp01((d.B.y - 14f) / 22f) * 0.6f + Mathf.Clamp01((Mathf.Abs(d.B.x) - 28f) / 28f) * 0.4f;
                        col = Color.Lerp(new Color(0.26f, 0.10f, 0.40f, 1f), new Color(0.50f, 0.20f, 0.70f, 1f), rim);
                        alpha = 0.96f;
                    }
                    else if (d.K == Kind.Billow)
                    {
                        float rim = Mathf.Clamp01((d.B.y - 12f) / 14f) * 0.5f;
                        col = Color.Lerp(new Color(0.10f, 0.04f, 0.16f, 1f), new Color(0.30f, 0.12f, 0.44f, 1f), rim);
                        alpha = 0.94f;
                    }
                    else if (d.K == Kind.Core)
                    {
                        col = new Color(0.07f, 0.03f, 0.11f, 1f);
                        alpha = 0.96f;
                    }
                    else
                    {
                        col = new Color(0.22f, 0.09f, 0.34f, 1f);
                        alpha = 0.5f;
                    }
                }
                p.Pos = g + pos;
                float far = Mathf.Clamp01((Vector3.Distance(p.Pos, camPos) - 70f) / 300f) * 0.65f;
                col = Color.Lerp(col, haze1, far);
                col.a = alpha * Mathf.Clamp01(grow * 2f);
                p.Tint = col;
                smoke.P[i] = p;
            }

            // глаза и зарево
            for (int i = 0; i < eyes.Length; i++)
            {
                float ge = S(0.25f + i * 0.004f, 0.7f + i * 0.004f, open);
                float blink = 0.5f + 0.5f * Mathf.Sin(t * 0.9f + i * 2.3f);
                float s = eyeSize[i] * ge * (0.55f + 0.45f * blink);
                eyes[i].localPosition = eyePos[i];
                eyes[i].localScale = new Vector3(s, s, 1f);
            }
            hazeMat.SetColor("_Color", new Color(0.34f, 0.12f, 0.52f, 0.30f * S(0.1f, 0.8f, open) * (1f - S(13.9f, 15.0f, t))));

            // дым в салоне
            for (int i = 0; i < cabinDefs.Length; i++)
            {
                CabinDef c = cabinDefs[i];
                float grow = S(c.Appear, c.Appear + 1.1f, t);
                SmokeField.Puff p = new SmokeField.Puff();
                Vector3 local = Vector3.Lerp(c.A, c.B, grow);
                local.y += 0.05f * Mathf.Sin(t * 1.6f + c.Seed);
                p.Pos = bus.transform.TransformPoint(local);
                p.Size = c.Size * (0.5f + 0.5f * grow);
                p.Roll = c.Roll + 6f * t * (c.Seed > 10f ? 1f : -1f);
                p.Tile = c.Tile;
                p.Tint = new Color(0.22f, 0.09f, 0.34f, c.Alpha * 0.8f * grow);   // у Тьмы собственный слабый фиолетовый отсвет: в чёрном салоне клубы читаются
                cabin.P[i] = p;
            }
        }

        /// <summary>Показать или спрятать Тьму целиком (в кухне она не нужна: это другое место мира).</summary>
        public void SetDarknessVisible(bool on)
        {
            if (smoke != null) smoke.SetActive(on);
            if (cabin != null) cabin.SetActive(on);
            if (gate != null && gate.gameObject.activeSelf != on) gate.gameObject.SetActive(on);
        }

        /// <summary>Собрать меши дыма для камеры (LateUpdate, когда камера уже стоит там, где её увидит кадр).</summary>
        public void DrawDarkness(Vector3 camPos, Quaternion camRot)
        {
            if (smoke == null) return;
            DarkPuffsDrawn = smoke.Rebuild(camPos, camRot);
            CabinPuffsDrawn = cabin.Rebuild(camPos, camRot);
        }
    }
}
