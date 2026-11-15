using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Кухня для предыстории: комната, стол, плита, окно на город. Статичное собрано в один меш (MeshBatch, как весь город),
    /// живое — отдельными объектами: лампа, свеча, пар над кружкой, часы, кружка (её двигает сценарий) и огни города за окном.
    /// Координаты — в системе комнаты: начало в центре пола, +X вправо, +Y вверх, +Z к окну. Комната стоит далеко от
    /// города (Origin), поэтому в кадре кроме неё ничего нет, а небо рисуется как обычно.
    /// </summary>
    public class Kitchen
    {
        // Ключевые точки (комната). Игрок сидит за столом лицом к окну; корень игрока опущен, чтобы глаза были на высоте 1.17 м.
        public const float TableTop = 0.776f;
        public const float MugHeight = 0.117f;   // кружка чуть крупнее настоящей: так читается и в руках героя, и на 360p
        public static readonly Vector3 SeatRoot = new Vector3(0f, -0.45f, -0.46f);
        public static readonly Vector3 MugRest = new Vector3(0.02f, TableTop, 0.20f);       // низ кружки на столе
        public static readonly Vector3 CandleBase = new Vector3(0.32f, TableTop, 0.40f);
        public static readonly Vector3 FlamePos = new Vector3(0.32f, 1.0f, 0.40f);
        public static readonly Vector3 LampPos = new Vector3(0f, 1.90f, 0.40f);
        public static readonly Vector3 ClockCenter = new Vector3(2.165f, 1.98f, 0.25f);

        // Окно: подоконник и шторы заданы данными, чтобы проверка могла убедиться, что шторы не проходят сквозь подоконник.
        public static readonly Bounds SillBox = new Bounds(new Vector3(0f, 0.96f, 2.22f), new Vector3(2.3f, 0.05f, 0.34f));
        const float CurtainX = 1.2f;          // середина каждой половины шторы от оси окна
        const float CurtainW = 0.42f;
        const float CurtainZ = 2.27f;         // полотно между рамой (z 2.33) и краем подоконника (z 2.05), над самим подоконником
        const float CurtainD = 0.07f;
        const float CurtainFoldZ = 2.225f;    // складки чуть выступают из полотна в комнату
        const float CurtainBottom = 0.995f;   // подол на 1 см выше подоконника (верх его 0.985): раньше шторы доходили до 0.81 и проходили сквозь него
        const float CurtainTop = 2.43f;       // карниз на высоте 2.42 проходит сквозь верх полотна, как через петли

        /// <summary>Коробки штор: для каждой половины полотно, потом три складки (в этом порядке).</summary>
        public static List<Bounds> CurtainBoxes()
        {
            List<Bounds> list = new List<Bounds>();
            float h = CurtainTop - CurtainBottom;
            float midY = (CurtainTop + CurtainBottom) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = s * CurtainX;
                list.Add(new Bounds(new Vector3(cx, midY, CurtainZ), new Vector3(CurtainW, h, CurtainD)));
                for (int f = 0; f < 3; f++) list.Add(new Bounds(new Vector3(cx + (f - 1) * 0.12f, midY, CurtainFoldZ), new Vector3(0.05f, h, 0.03f)));
            }
            return list;
        }

        /// <summary>Насколько глубоко самая «глубокая» коробка шторы заходит в подоконник, метры; 0 — шторы подоконника не касаются.</summary>
        public static float CurtainSillOverlap()
        {
            float worst = 0f;
            List<Bounds> boxes = CurtainBoxes();
            for (int i = 0; i < boxes.Count; i++)
            {
                Bounds c = boxes[i];
                float ox = Mathf.Min(c.max.x, SillBox.max.x) - Mathf.Max(c.min.x, SillBox.min.x);
                float oy = Mathf.Min(c.max.y, SillBox.max.y) - Mathf.Max(c.min.y, SillBox.min.y);
                float oz = Mathf.Min(c.max.z, SillBox.max.z) - Mathf.Max(c.min.z, SillBox.min.z);
                if (ox > 0f && oy > 0f && oz > 0f) worst = Mathf.Max(worst, Mathf.Min(ox, Mathf.Min(oy, oz)));
            }
            return worst;
        }

        /// <summary>Строка отчёта для стенда: где подоконник, где низ штор и на сколько они заходят в подоконник.</summary>
        public static string CurtainReport()
        {
            return "window: sill y " + SillBox.min.y.ToString("0.000") + ".." + SillBox.max.y.ToString("0.000") + ", z " + SillBox.min.z.ToString("0.00") + ".." + SillBox.max.z.ToString("0.00")
                + "; curtains y " + CurtainBottom.ToString("0.000") + ".." + CurtainTop.ToString("0.000")
                + "; curtain bottom above the sill top by " + ((CurtainBottom - SillBox.max.y) * 100f).ToString("0.0") + " cm"
                + "; overlap into the sill " + (CurtainSillOverlap() * 100f).ToString("0.0") + " cm";
        }

        public Transform Root;
        public Vector3 Origin;

        // Кружка для столкновений с кистями: корпус — усечённый конус (радиус у дна и у края), ручка вместе с зазором до стенки — коробка
        // (в зазор между ручкой и стенкой, 7-10 мм, пальцы не лезут). Размеры совпадают с мешем в BuildLiveParts.
        const float MugRadiusBottom = 0.052f;
        const float MugRadiusTop = 0.057f;
        // по Z коробка шире самой ручки (0,024 м): ладонь, лежащая у ручки, должна выходить наружу (+X), а не вперёд или назад
        // в зависимости от того, по какую сторону от оси она оказалась на этом кадре (иначе рука дёргается)
        static readonly Vector3 HandleMin = new Vector3(0.05f, 0.012f, -0.04f);
        static readonly Vector3 HandleMax = new Vector3(0.098f, 0.115f, 0.04f);

        /// <summary>
        /// Шар (центр в мировых координатах, радиус): на сколько его надо сдвинуть, чтобы он вышел из кружки; нулевой вектор, если не касается.
        /// Кружка берётся с её текущим положением (её носят ко рту). Так кисти в кат-сценах не проходят сквозь кружку и ручку
        /// (HeroBody.HandObstacle).
        /// </summary>
        public Vector3 MugPush(Vector3 world, float radius)
        {
            if (Mug == null || !Mug.gameObject.activeInHierarchy) return Vector3.zero;
            Vector3 q = Mug.InverseTransformPoint(world);

            // корпус
            Vector3 body = Vector3.zero;
            float yc = Mathf.Clamp(q.y, 0f, MugHeight);
            float rad = Mathf.Lerp(MugRadiusBottom, MugRadiusTop, yc / MugHeight);
            Vector2 flat = new Vector2(q.x, q.z);
            float dist = flat.magnitude;
            if (dist < rad && q.y > 0f && q.y < MugHeight)
            {
                // центр внутри: у самого края (верхние 1.5 см) выходим вверх, ниже — вбок; выбор по высоте, а не по тому, какой путь короче:
                // у середины стенки «ближе» то вбок, то вверх, и рука на соседних кадрах дёргалась бы
                float side = rad - dist + radius;
                float up = MugHeight - q.y + radius;
                Vector2 dir = dist > 1e-4f ? flat / dist : Vector2.right;
                body = q.y > MugHeight - 0.015f ? Vector3.up * up : new Vector3(dir.x, 0f, dir.y) * side;
            }
            else
            {
                Vector2 cp = dist > rad ? flat * (rad / dist) : flat;
                Vector3 d = q - new Vector3(cp.x, yc, cp.y);
                float dd = d.magnitude;
                if (dd < radius && dd > 1e-6f) body = d / dd * (radius - dd);
            }

            // ручка
            Vector3 handle = Vector3.zero;
            Vector3 hc = new Vector3(Mathf.Clamp(q.x, HandleMin.x, HandleMax.x), Mathf.Clamp(q.y, HandleMin.y, HandleMax.y), Mathf.Clamp(q.z, HandleMin.z, HandleMax.z));
            Vector3 hd = q - hc;
            float hdd = hd.magnitude;
            if (hdd > 1e-6f)
            {
                if (hdd < radius) handle = hd / hdd * (radius - hdd);
            }
            else
            {
                // центр внутри коробки: наружу (+X) или вверх, что ближе (пальцы входят справа или сверху). Вниз, вперёд и назад не выходим:
                // направление скакало бы между ними (рывки руки по 5-9 см), а в сторону кружки нельзя (там корпус).
                float toRight = HandleMax.x - q.x + radius;
                float toUp = HandleMax.y - q.y + radius;
                handle = toRight <= toUp ? Vector3.right * toRight : Vector3.up * toUp;
            }
            Vector3 push = body.sqrMagnitude >= handle.sqrMagnitude ? body : handle;
            return push.sqrMagnitude > 0f ? Mug.TransformVector(push) : Vector3.zero;
        }

        // живые детали
        public Transform Mug;
        public readonly Transform[] Steam = new Transform[3];
        public Transform Flame;
        public Transform FlameCore;
        public Transform FlameHalo;
        public Transform LampHalo;
        public Transform ClockHour;
        public Transform ClockMinute;
        public Transform ClockSecond;
        public Material BulbMat;
        public Material LampHaloMat;
        public Material FlameMat;
        public Material FlameCoreMat;
        public Material FlameHaloMat;
        public Material SteamMat;
        public readonly Transform[] Smoke = new Transform[3];
        public Material SmokeMat;
        public Transform[] Haze;
        public Material HazeMat;

        // огни города за окном
        struct Win
        {
            public float X;
            public float Jit;
            public float Bright;
            public Color32 Col;
        }

        Win[] wins;
        Color32[] winColors;
        Mesh winMesh;
        Mesh roomMesh;
        Mesh lampMesh;
        Mesh mugMesh;
        Mesh skylineMesh;

        static Color32 C(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        public static Kitchen Build(Vector3 origin)
        {
            Kitchen k = new Kitchen();
            k.Origin = origin;
            GameObject root = new GameObject("IntroKitchen");
            root.transform.position = origin;
            k.Root = root.transform;
            k.BuildRoom();
            k.BuildLiveParts();
            k.BuildSkyline();
            k.BuildWetParts();
            return k;
        }

        // ------------------------------------------------------------------ концовка: сон за столом и мокрый стул

        /// <summary>Ноги, мокрое пятно, лужа, капли и пар — только для концовки (в предыстории скрыты).</summary>
        public Transform WetRoot;
        Mesh wetMesh;
        Material puddleMat;
        Material dripMat;
        Material wetSteamMat;
        Transform puddle;
        readonly Transform[] drips = new Transform[3];
        readonly Transform[] wetSteam = new Transform[4];

        void BuildWetParts()
        {
            WetRoot = new GameObject("EndingWet").transform;
            WetRoot.SetParent(Root, false);
            WetRoot.localPosition = Vector3.zero;
            MeshBatch b = new MeshBatch();
            Color32 denim = C(52, 66, 98);
            Color32 denimWet = C(26, 34, 60);
            Color32 shoe = C(34, 30, 30);
            for (int s = -1; s <= 1; s += 2)
            {
                b.BoxYaw(new Vector3(s * 0.13f, 0.52f, -0.30f), new Vector3(0.17f, 0.14f, 0.52f), 0f, denim, MeshBatch.AllFaces);    // бедро (под столом)
                b.BoxYaw(new Vector3(s * 0.13f, 0.27f, -0.06f), new Vector3(0.13f, 0.50f, 0.14f), 0f, denim, MeshBatch.AllFaces);   // голень
                b.BoxYaw(new Vector3(s * 0.13f, 0.035f, 0.02f), new Vector3(0.14f, 0.07f, 0.28f), 0f, shoe, MeshBatch.AllFaces);    // ботинок
            }
            b.BoxYaw(new Vector3(0f, 0.5925f, -0.42f), new Vector3(0.36f, 0.006f, 0.34f), 0f, denimWet, 4);    // мокрое пятно сверху
            b.BoxYaw(new Vector3(-0.13f, 0.5925f, -0.22f), new Vector3(0.12f, 0.006f, 0.22f), 0f, denimWet, 4);
            b.BoxYaw(new Vector3(0.13f, 0.5925f, -0.22f), new Vector3(0.12f, 0.006f, 0.22f), 0f, denimWet, 4);
            wetMesh = b.Build("PG_KitchenLap");
            Gfx.MeshObject(WetRoot, wetMesh, Gfx.Lit(Color.white), "Lap");

            puddleMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(0.88f, 0.78f, 0.22f, 0.5f), null, false, true, 0.1f));
            puddle = Gfx.Prim(PrimitiveType.Cylinder, WetRoot, new Vector3(0f, 0.014f, -0.30f), new Vector3(0.9f, 0.002f, 0.8f), puddleMat, false).transform;
            dripMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(0.95f, 0.86f, 0.32f, 0.9f), Gfx.TexHalo, false, true, 0.05f));
            for (int i = 0; i < drips.Length; i++) drips[i] = Gfx.Sprite(WetRoot, Vector3.zero, new Vector2(1f, 1f), dripMat, true).transform;
            wetSteamMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(0.96f, 0.95f, 0.86f, 0.12f), Gfx.TexHalo, true, false, 0f));
            for (int i = 0; i < wetSteam.Length; i++) wetSteam[i] = Gfx.Sprite(WetRoot, Vector3.zero, new Vector2(1f, 1f), wetSteamMat, false).transform;
            WetRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Мокрый стул: spread 0 — ничего нет (корень скрыт), 1 — лужа растеклась, с бедра капает и поднимается тёплый пар.
        /// time — для капель и пара.
        /// </summary>
        public void SetWet(float spread, float time)
        {
            bool on = spread > 0.001f;
            if (WetRoot.gameObject.activeSelf != on) WetRoot.gameObject.SetActive(on);
            if (!on) return;
            float s = Mathf.Clamp01(spread);
            puddle.localScale = new Vector3(Mathf.Lerp(0.12f, 0.95f, s), 0.002f, Mathf.Lerp(0.10f, 0.80f, s));
            puddleMat.SetColor("_Color", new Color(0.88f, 0.78f, 0.22f, 0.50f * Mathf.Min(1f, s * 4f)));
            for (int i = 0; i < drips.Length; i++)
            {
                float ph = Mathf.Repeat(time * 1.15f + i * 0.37f, 1f);
                drips[i].localPosition = new Vector3(-0.06f + i * 0.06f, Mathf.Lerp(0.52f, 0.02f, ph * ph), -0.30f - 0.03f * i);
                drips[i].localScale = new Vector3(0.016f, 0.034f * Mathf.Sin(ph * Mathf.PI) * Mathf.Min(1f, s * 5f), 1f);
            }
            for (int i = 0; i < wetSteam.Length; i++)
            {
                float ph = Mathf.Repeat(time * 0.35f + i * 0.25f, 1f);
                wetSteam[i].localPosition = new Vector3(-0.12f + i * 0.08f + 0.02f * Mathf.Sin(time + i * 2f), 0.62f + ph * 0.34f, -0.36f);
                float sz = (0.10f + 0.12f * ph) * Mathf.Sin(Mathf.PI * ph) * Mathf.Min(1f, s * 3f);
                wetSteam[i].localScale = new Vector3(sz, sz, 1f);
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            if (wetMesh != null) Object.Destroy(wetMesh);
            if (roomMesh != null) Object.Destroy(roomMesh);
            if (mugMesh != null) Object.Destroy(mugMesh);
            if (winMesh != null) Object.Destroy(winMesh);
            if (lampMesh != null) Object.Destroy(lampMesh);
            if (skylineMesh != null) Object.Destroy(skylineMesh);
        }

        public Vector3 World(Vector3 roomPos)
        {
            return Origin + roomPos;
        }

        // ------------------------------------------------------------------ комната

        void BuildRoom()
        {
            MeshBatch b = new MeshBatch();
            Color32 floorA = C(132, 114, 90);
            Color32 floorB = C(88, 76, 60);
            Color32 teal = C(62, 94, 88);
            Color32 cream = C(146, 124, 94);
            Color32 trim = C(196, 186, 164);
            Color32 baseboard = C(96, 82, 66);
            Color32 ceiling = C(168, 160, 144);
            Color32 wood = C(122, 84, 50);
            Color32 woodD = C(80, 54, 34);
            Color32 dark = C(34, 32, 34);
            Color32 metal = C(140, 144, 148);
            Color32 white = C(200, 198, 190);

            // пол: плитка-«шахматка» и подложка
            for (int ix = 0; ix < 8; ix++)
            {
                for (int iz = 0; iz < 7; iz++)
                {
                    float x = -2.2f + 0.275f + ix * 0.55f;
                    float z = -1.5f + 0.2786f + iz * 0.557f;
                    b.BoxYaw(new Vector3(x, 0f, z), new Vector3(0.55f, 0.02f, 0.557f), 0f, ((ix + iz) & 1) == 0 ? floorA : floorB, 4);
                }
            }
            b.Box(new Vector3(0f, -0.07f, 0.45f), new Vector3(4.5f, 0.12f, 4.0f), C(52, 44, 36));
            b.BoxYaw(new Vector3(0f, 2.75f, 0.45f), new Vector3(4.5f, 0.1f, 4.0f), 0f, ceiling, 8);

            // стены: низ — краска, верх светлее, между ними планка; внутренние грани смотрят в комнату
            b.BoxYaw(new Vector3(-2.25f, 0.5f, 0.45f), new Vector3(0.1f, 1.0f, 4.0f), 0f, teal, 1);
            b.BoxYaw(new Vector3(-2.25f, 1.85f, 0.45f), new Vector3(0.1f, 1.7f, 4.0f), 0f, cream, 1);
            b.Box(new Vector3(-2.19f, 1.0f, 0.45f), new Vector3(0.02f, 0.05f, 3.9f), trim);
            b.Box(new Vector3(-2.19f, 0.06f, 0.45f), new Vector3(0.02f, 0.12f, 3.9f), baseboard);

            b.BoxYaw(new Vector3(2.25f, 0.5f, 0.45f), new Vector3(0.1f, 1.0f, 4.0f), 0f, teal, 2);
            b.BoxYaw(new Vector3(2.25f, 1.85f, 0.45f), new Vector3(0.1f, 1.7f, 4.0f), 0f, cream, 2);
            b.Box(new Vector3(2.19f, 1.0f, 0.45f), new Vector3(0.02f, 0.05f, 3.9f), trim);
            b.Box(new Vector3(2.19f, 0.06f, 0.45f), new Vector3(0.02f, 0.12f, 3.9f), baseboard);

            b.BoxYaw(new Vector3(0f, 0.5f, -1.55f), new Vector3(4.5f, 1.0f, 0.1f), 0f, teal, 16);
            b.BoxYaw(new Vector3(0f, 1.85f, -1.55f), new Vector3(4.5f, 1.7f, 0.1f), 0f, cream, 16);
            b.Box(new Vector3(0f, 1.0f, -1.49f), new Vector3(4.4f, 0.05f, 0.02f), trim);
            b.Box(new Vector3(0f, 0.06f, -1.49f), new Vector3(4.4f, 0.12f, 0.02f), baseboard);

            // передняя стена с окном: x от -1 до 1, y от 0.95 до 2.25
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = s * 1.625f;
                b.BoxYaw(new Vector3(cx, 0.5f, 2.45f), new Vector3(1.25f, 1.0f, 0.1f), 0f, teal, 32);
                b.BoxYaw(new Vector3(cx, 1.85f, 2.45f), new Vector3(1.25f, 1.7f, 0.1f), 0f, cream, 32);
                b.Box(new Vector3(cx, 1.0f, 2.39f), new Vector3(1.25f, 0.05f, 0.02f), trim);
                b.Box(new Vector3(cx, 0.06f, 2.39f), new Vector3(1.25f, 0.12f, 0.02f), baseboard);
            }
            b.BoxYaw(new Vector3(0f, 0.475f, 2.45f), new Vector3(2.0f, 0.95f, 0.1f), 0f, teal, 32);
            b.BoxYaw(new Vector3(0f, 2.475f, 2.45f), new Vector3(2.0f, 0.45f, 0.1f), 0f, cream, 32);

            // окно: рама, переплёт, подоконник, шторы
            b.Box(new Vector3(0f, 0.97f, 2.37f), new Vector3(2.12f, 0.06f, 0.08f), white);
            b.Box(new Vector3(0f, 2.23f, 2.37f), new Vector3(2.12f, 0.06f, 0.08f), white);
            b.Box(new Vector3(-1.03f, 1.6f, 2.37f), new Vector3(0.06f, 1.32f, 0.08f), white);
            b.Box(new Vector3(1.03f, 1.6f, 2.37f), new Vector3(0.06f, 1.32f, 0.08f), white);
            b.Box(new Vector3(0f, 1.6f, 2.37f), new Vector3(0.04f, 1.26f, 0.05f), white);
            b.Box(new Vector3(0f, 1.6f, 2.37f), new Vector3(2.0f, 0.04f, 0.05f), white);
            b.Box(SillBox.center, SillBox.size, white);
            Color32 curt = C(118, 44, 48);
            Color32 curtD = C(90, 30, 36);
            List<Bounds> curtains = CurtainBoxes();
            for (int i = 0; i < curtains.Count; i++) b.Box(curtains[i].center, curtains[i].size, i % 4 == 0 ? curt : curtD);
            b.Rod(new Vector3(-1.5f, 2.42f, 2.28f), new Vector3(1.5f, 2.42f, 2.28f), 0.014f, 6, dark);

            // цветок на подоконнике
            b.Frustum(new Vector3(0.62f, 1.05f, 2.2f), Vector3.up, 0.05f, 0.07f, 0.12f, 10, C(150, 78, 52), true, true);
            Color32 leaf = C(54, 104, 56);
            for (int i = 0; i < 7; i++)
            {
                float yaw = i * 51f;
                Vector3 off = Models.RotY(new Vector3(0f, 0f, 0.04f), yaw);
                b.BoxYaw(new Vector3(0.62f + off.x, 1.17f + (i % 2) * 0.03f, 2.2f + off.z), new Vector3(0.02f, 0.2f - (i % 3) * 0.04f, 0.07f), yaw, leaf);
            }

            // стол с клетчатой скатертью
            b.Box(new Vector3(0f, 0.745f, 0.45f), new Vector3(1.3f, 0.04f, 0.85f), wood);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2) b.Box(new Vector3(sx * 0.58f, 0.36f, 0.45f + sz * 0.36f), new Vector3(0.06f, 0.72f, 0.06f), woodD);
            }
            b.Box(new Vector3(0f, 0.769f, 0.45f), new Vector3(1.24f, 0.008f, 0.79f), C(150, 52, 44));
            Color32 plaid = C(184, 96, 80);
            for (int i = 0; i < 4; i++)
            {
                b.Box(new Vector3(-0.45f + i * 0.3f, 0.7735f, 0.45f), new Vector3(0.03f, 0.002f, 0.79f), plaid);
                b.Box(new Vector3(0f, 0.7745f, 0.45f - 0.28f + i * 0.187f), new Vector3(1.24f, 0.002f, 0.03f), plaid);
            }

            // на столе: тарелка с хлебом, газета, кофейник, подсвечник со свечой
            b.Prism(new Vector3(-0.32f, TableTop + 0.006f, 0.22f), Vector3.up, 0.10f, 0.012f, 14, white, true);
            b.BoxYaw(new Vector3(-0.32f, TableTop + 0.04f, 0.22f), new Vector3(0.16f, 0.05f, 0.07f), 20f, C(172, 122, 66), MeshBatch.AllFaces);
            b.BoxYaw(new Vector3(-0.42f, TableTop + 0.003f, 0.60f), new Vector3(0.30f, 0.004f, 0.22f), 18f, C(212, 206, 190), 4);
            for (int i = 0; i < 6; i++) b.BoxYaw(new Vector3(-0.42f, TableTop + 0.006f, 0.52f + i * 0.03f), new Vector3(0.24f, 0.002f, 0.006f), 18f, C(70, 66, 62), 4);
            b.Frustum(new Vector3(0.62f, TableTop + 0.07f, 0.62f), Vector3.up, 0.065f, 0.045f, 0.14f, 10, metal, true, true);
            b.Prism(new Vector3(0.62f, TableTop + 0.155f, 0.62f), Vector3.up, 0.032f, 0.03f, 10, dark, true);
            b.Rod(new Vector3(0.68f, TableTop + 0.04f, 0.62f), new Vector3(0.74f, TableTop + 0.12f, 0.62f), 0.01f, 6, dark);

            b.Prism(new Vector3(CandleBase.x, TableTop + 0.006f, CandleBase.z), Vector3.up, 0.055f, 0.012f, 12, C(176, 138, 58), true);
            b.Prism(new Vector3(CandleBase.x, TableTop + 0.022f, CandleBase.z), Vector3.up, 0.022f, 0.02f, 10, C(176, 138, 58), true);
            b.Prism(new Vector3(CandleBase.x, TableTop + 0.012f + 0.085f, CandleBase.z), Vector3.up, 0.016f, 0.17f, 10, C(236, 228, 200), true);
            b.Prism(new Vector3(CandleBase.x, TableTop + 0.012f + 0.176f, CandleBase.z), Vector3.up, 0.003f, 0.012f, 5, dark, true);

            // стул справа
            b.Box(new Vector3(1.05f, 0.46f, 0.45f), new Vector3(0.42f, 0.04f, 0.42f), woodD);
            b.Box(new Vector3(1.24f, 0.74f, 0.45f), new Vector3(0.04f, 0.52f, 0.42f), woodD);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2) b.Box(new Vector3(1.05f + sx * 0.18f, 0.22f, 0.45f + sz * 0.18f), new Vector3(0.04f, 0.44f, 0.04f), woodD);
            }

            // левая стена: холодильник, кухонный гарнитур, плита, мойка, верхние шкафы
            b.Box(new Vector3(-1.8f, 0.875f, -1.1f), new Vector3(0.7f, 1.75f, 0.68f), C(180, 184, 188));
            b.Box(new Vector3(-1.445f, 1.2f, -1.27f), new Vector3(0.02f, 0.5f, 0.03f), metal);
            b.Box(new Vector3(-1.445f, 0.55f, -1.27f), new Vector3(0.02f, 0.3f, 0.03f), metal);
            b.Box(new Vector3(-1.447f, 1.1f, -1.1f), new Vector3(0.012f, 0.012f, 0.66f), C(120, 124, 128));

            Color32 cab = C(148, 136, 112);
            Color32 door = C(164, 150, 122);
            float[] modz = { -0.4f, 0.2f, 0.8f, 1.4f, 2.0f };
            for (int i = 0; i < modz.Length; i++)
            {
                float z = modz[i];
                if (i == 2)
                {
                    // плита
                    b.Box(new Vector3(-1.89f, 0.43f, z), new Vector3(0.62f, 0.86f, 0.6f), C(188, 186, 178));
                    b.Box(new Vector3(-1.58f, 0.40f, z), new Vector3(0.02f, 0.5f, 0.5f), dark);
                    b.Box(new Vector3(-1.565f, 0.68f, z), new Vector3(0.02f, 0.02f, 0.44f), metal);
                    b.Box(new Vector3(-1.89f, 0.885f, z), new Vector3(0.62f, 0.012f, 0.6f), dark);
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            b.Prism(new Vector3(-1.89f + sx * 0.14f, 0.896f, z + sz * 0.14f), Vector3.up, 0.07f, 0.012f, 12, C(62, 60, 66), true);
                            b.Prism(new Vector3(-1.89f + sx * 0.14f, 0.903f, z + sz * 0.14f), Vector3.up, 0.04f, 0.012f, 10, C(28, 28, 32), true);
                        }
                    }
                    // чайник
                    b.Frustum(new Vector3(-1.75f, 0.905f + 0.08f, z + 0.14f), Vector3.up, 0.10f, 0.075f, 0.16f, 12, C(150, 44, 38), true, true);
                    b.Prism(new Vector3(-1.75f, 0.905f + 0.17f, z + 0.14f), Vector3.up, 0.018f, 0.03f, 8, dark, true);
                    b.Rod(new Vector3(-1.75f, 0.98f, z + 0.22f), new Vector3(-1.75f, 1.04f, z + 0.30f), 0.012f, 6, C(150, 44, 38));
                    b.Rod(new Vector3(-1.75f - 0.09f, 1.0f, z + 0.14f), new Vector3(-1.75f + 0.09f, 1.0f, z + 0.14f), 0.008f, 6, dark);
                    continue;
                }
                b.Box(new Vector3(-1.89f, 0.43f, z), new Vector3(0.62f, 0.86f, 0.6f), cab);
                b.Box(new Vector3(-1.575f, 0.43f, z), new Vector3(0.02f, 0.72f, 0.54f), door);
                b.Box(new Vector3(-1.56f, 0.66f, z + 0.2f), new Vector3(0.02f, 0.12f, 0.02f), dark);
            }
            b.Box(new Vector3(-1.89f, 0.43f, 2.33f), new Vector3(0.62f, 0.86f, 0.14f), cab);
            Color32 top = C(190, 186, 176);
            b.Box(new Vector3(-1.86f, 0.88f, -0.1f), new Vector3(0.68f, 0.04f, 1.2f), top);
            b.Box(new Vector3(-1.86f, 0.88f, 1.75f), new Vector3(0.68f, 0.04f, 1.3f), top);
            // мойка и кран
            b.Box(new Vector3(-1.86f, 0.9f, 2.0f), new Vector3(0.5f, 0.01f, 0.44f), C(96, 100, 106));
            b.Rod(new Vector3(-2.08f, 0.9f, 2.0f), new Vector3(-2.08f, 1.12f, 2.0f), 0.013f, 6, metal);
            b.Rod(new Vector3(-2.08f, 1.12f, 2.0f), new Vector3(-1.96f, 1.12f, 2.0f), 0.013f, 6, metal);
            // плитка над столешницей
            b.Box(new Vector3(-2.194f, 1.18f, 0.85f), new Vector3(0.012f, 0.56f, 3.1f), C(196, 194, 188));
            for (int i = 0; i < 12; i++) b.Box(new Vector3(-2.19f, 1.18f, -0.65f + i * 0.28f), new Vector3(0.006f, 0.56f, 0.01f), C(150, 148, 142));
            // верхние шкафы и вытяжка
            b.Box(new Vector3(-2.03f, 1.82f, -0.15f), new Vector3(0.34f, 0.72f, 1.1f), cab);
            b.Box(new Vector3(-2.03f, 1.82f, 1.8f), new Vector3(0.34f, 0.72f, 1.1f), cab);
            for (int i = 0; i < 4; i++)
            {
                float z = (i < 2) ? -0.42f + i * 0.55f : 1.52f + (i - 2) * 0.55f;
                b.Box(new Vector3(-1.857f, 1.82f, z), new Vector3(0.012f, 0.66f, 0.5f), door);
                b.Box(new Vector3(-1.85f, 1.6f, z + 0.2f), new Vector3(0.02f, 0.1f, 0.02f), dark);
            }
            b.Box(new Vector3(-1.97f, 1.62f, 0.8f), new Vector3(0.45f, 0.1f, 0.7f), C(132, 136, 140));
            b.Box(new Vector3(-2.08f, 2.2f, 0.8f), new Vector3(0.2f, 1.0f, 0.3f), C(132, 136, 140));

            // правая стена: дверь, полка с банками и радио
            b.Box(new Vector3(2.17f, 1.0f, 1.8f), new Vector3(0.04f, 1.98f, 0.82f), woodD);
            b.Box(new Vector3(2.15f, 1.0f, 1.8f), new Vector3(0.02f, 1.7f, 0.62f), C(102, 72, 44));
            b.Box(new Vector3(2.13f, 1.0f, 1.45f), new Vector3(0.04f, 0.04f, 0.12f), C(176, 138, 58));
            b.Box(new Vector3(2.15f, 1.02f, 1.36f), new Vector3(0.05f, 2.04f, 0.05f), trim);
            b.Box(new Vector3(2.15f, 1.02f, 2.24f), new Vector3(0.05f, 2.04f, 0.05f), trim);
            b.Box(new Vector3(2.15f, 2.04f, 1.8f), new Vector3(0.05f, 0.05f, 0.93f), trim);
            b.Box(new Vector3(2.075f, 1.45f, 0.1f), new Vector3(0.25f, 0.03f, 1.3f), wood);
            Color32[] jar = { C(150, 40, 36), C(196, 140, 40), C(70, 110, 54), C(214, 206, 188), C(120, 70, 40) };
            for (int i = 0; i < 5; i++)
            {
                b.Prism(new Vector3(2.07f, 1.465f + 0.07f, -0.35f + i * 0.14f), Vector3.up, 0.045f, 0.14f - (i % 2) * 0.03f, 10, jar[i], true);
                b.Prism(new Vector3(2.07f, 1.465f + 0.145f - (i % 2) * 0.03f, -0.35f + i * 0.14f), Vector3.up, 0.047f, 0.012f, 10, C(190, 186, 176), true);
            }
            b.Box(new Vector3(2.07f, 1.465f + 0.07f, 0.6f), new Vector3(0.16f, 0.14f, 0.28f), woodD);
            b.Box(new Vector3(1.99f, 1.465f + 0.07f, 0.6f), new Vector3(0.012f, 0.1f, 0.2f), C(110, 90, 60));
            b.Prism(new Vector3(1.985f, 1.465f + 0.1f, 0.7f), Vector3.left, 0.02f, 0.02f, 8, C(190, 180, 150), true);
            b.Prism(new Vector3(1.985f, 1.465f + 0.04f, 0.7f), Vector3.left, 0.02f, 0.02f, 8, C(190, 180, 150), true);
            // настенные часы: циферблат, ободок, четыре отметки
            b.Prism(new Vector3(2.185f, ClockCenter.y, ClockCenter.z), Vector3.left, 0.19f, 0.025f, 16, C(70, 54, 44), true);
            b.Prism(new Vector3(2.172f, ClockCenter.y, ClockCenter.z), Vector3.left, 0.165f, 0.02f, 16, C(238, 234, 222), true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                b.Box(new Vector3(2.16f, ClockCenter.y + Mathf.Cos(a) * 0.14f, ClockCenter.z + Mathf.Sin(a) * 0.14f), new Vector3(0.006f, (i % 2 == 0) ? 0.035f : 0.012f, (i % 2 == 0) ? 0.012f : 0.035f), dark);
            }

            roomMesh = b.Build("PG_Kitchen");
            Gfx.MeshObject(Root, roomMesh, Gfx.Lit(Color.white), "Room");

            // абажур: снаружи и изнутри (изнутри — обратный порядок вершин), шнур и патрон
            MeshBatch lb = new MeshBatch();
            Color32 shade = C(196, 184, 150);
            Color32 shadeIn = C(235, 226, 190);
            lb.Rod(new Vector3(0f, 2.7f, LampPos.z), new Vector3(0f, LampPos.y + 0.28f, LampPos.z), 0.008f, 5, dark);
            lb.Frustum(new Vector3(0f, LampPos.y + 0.18f, LampPos.z), Vector3.down, 0.07f, 0.26f, 0.2f, 14, shade, true, false);
            InnerCone(lb, new Vector3(0f, LampPos.y + 0.28f, LampPos.z), 0.065f, new Vector3(0f, LampPos.y + 0.08f, LampPos.z), 0.25f, 14, shadeIn);
            lampMesh = lb.Build("PG_KitchenLamp");
            Gfx.MeshObject(Root, lampMesh, Gfx.Lit(Color.white), "LampShade");
        }

        /// <summary>Внутренняя поверхность усечённого конуса (вертикальная ось): грани смотрят в ось.</summary>
        static void InnerCone(MeshBatch b, Vector3 top, float rTop, Vector3 bottom, float rBottom, int sides, Color32 col)
        {
            float step = 6.2831853f / sides;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                Vector3 t0 = top + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * rTop;
                Vector3 t1 = top + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * rTop;
                Vector3 b0 = bottom + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * rBottom;
                Vector3 b1 = bottom + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * rBottom;
                b.Quad(b1, t1, t0, b0, col);   // в этом базисе такой порядок даёт грани, обращённые к оси
            }
        }

        // ------------------------------------------------------------------ живые детали

        void BuildLiveParts()
        {
            // лампочка и ореол
            BulbMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(1f, 0.92f, 0.72f, 1f), null, false, false, 0f));
            GameObject bulb = Gfx.Prim(PrimitiveType.Sphere, Root, LampPos + new Vector3(0f, 0.02f, 0f), new Vector3(0.09f, 0.11f, 0.09f), BulbMat, false);
            bulb.name = "Bulb";
            LampHaloMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(1f, 0.78f, 0.42f, 0.30f), Gfx.TexHalo, true, false, 0f));
            GameObject lh = Gfx.Sprite(Root, LampPos + new Vector3(0f, -0.02f, 0f), new Vector2(1.5f, 1.5f), LampHaloMat, false);
            LampHalo = lh.transform;

            // кружка (движется по сценарию): корпус, кофе, ручка
            MeshBatch mb = new MeshBatch();
            Color32 ceramic = C(228, 228, 234);
            mb.Frustum(new Vector3(0f, MugHeight * 0.5f, 0f), Vector3.up, 0.052f, 0.057f, MugHeight, 12, ceramic, true, false);
            InnerCone(mb, new Vector3(0f, MugHeight, 0f), 0.056f, new Vector3(0f, 0.014f, 0f), 0.050f, 12, C(214, 214, 220));
            mb.Prism(new Vector3(0f, MugHeight - 0.018f, 0f), Vector3.up, 0.050f, 0.004f, 12, C(46, 28, 18), true);
            mb.Box(new Vector3(0.075f, MugHeight * 0.78f, 0f), new Vector3(0.022f, 0.016f, 0.018f), ceramic);
            mb.Box(new Vector3(0.075f, MugHeight * 0.30f, 0f), new Vector3(0.022f, 0.016f, 0.018f), ceramic);
            mb.Box(new Vector3(0.088f, MugHeight * 0.54f, 0f), new Vector3(0.016f, MugHeight * 0.52f, 0.018f), ceramic);
            mugMesh = mb.Build("PG_Mug");
            Mug = Gfx.MeshObject(Root, mugMesh, Gfx.Lit(Color.white), "Mug").transform;

            // пар над кружкой
            SteamMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(0.9f, 0.9f, 0.96f, 0.13f), Gfx.TexHalo, true, false, 0f));
            for (int i = 0; i < 3; i++)
            {
                GameObject s = Gfx.Sprite(Root, Vector3.zero, new Vector2(0.06f, 0.06f), SteamMat, false);
                s.name = "Steam" + i;
                Steam[i] = s.transform;
            }

            // свеча: пламя, яркая сердцевина, ореол
            FlameMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(1f, 0.58f, 0.16f, 0.95f), Gfx.TexHalo, true, false, 0f));
            FlameCoreMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(1f, 0.93f, 0.62f, 1f), Gfx.TexHalo, true, false, 0f));
            FlameHaloMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(1f, 0.62f, 0.26f, 0.22f), Gfx.TexHalo, true, false, 0f));
            Flame = Gfx.Sprite(Root, FlamePos, new Vector2(0.05f, 0.11f), FlameMat, true).transform;
            FlameCore = Gfx.Sprite(Root, FlamePos + new Vector3(0f, -0.012f, 0f), new Vector2(0.022f, 0.05f), FlameCoreMat, true).transform;
            FlameHalo = Gfx.Sprite(Root, FlamePos, new Vector2(0.55f, 0.55f), FlameHaloMat, false).transform;

            // дымок после погасшей свечи
            SmokeMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(0.55f, 0.55f, 0.6f, 0.0f), Gfx.TexHalo, true, false, 0f));
            for (int i = 0; i < 3; i++)
            {
                GameObject s = Gfx.Sprite(Root, FlamePos, new Vector2(0.08f, 0.08f), SmokeMat, false);
                s.name = "Smoke" + i;
                Smoke[i] = s.transform;
            }

            // стрелки часов: оси вращения у центра циферблата, стрелка вытянута вдоль +Y
            Material hand = Gfx.Lit(new Color(0.08f, 0.08f, 0.08f, 1f));
            Material secondMat = Gfx.Lit(new Color(0.62f, 0.1f, 0.08f, 1f));
            ClockHour = MakeHand(hand, 0.085f, 0.014f, 0.01f);
            ClockMinute = MakeHand(hand, 0.125f, 0.010f, 0.012f);
            ClockSecond = MakeHand(secondMat, 0.140f, 0.004f, 0.014f);

            // тёмная дымка Тьмы над городом (двигается вместе с фронтом, когда гаснут огни)
            HazeMat = OwnedResources.Keep(Root.gameObject, Gfx.Unlit(new Color(0.34f, 0.12f, 0.52f, 0f), Gfx.TexHalo, true, false, 0f));
            Haze = new Transform[5];
            for (int i = 0; i < Haze.Length; i++)
            {
                GameObject h = Gfx.Sprite(Root, new Vector3(0f, 16f, 100f), new Vector2(70f, 40f), HazeMat, true);
                h.name = "Haze" + i;
                Haze[i] = h.transform;
            }
        }

        Transform MakeHand(Material m, float length, float thick, float depthOffset)
        {
            GameObject pivot = new GameObject("ClockHand");
            pivot.transform.SetParent(Root, false);
            pivot.transform.localPosition = ClockCenter + new Vector3(-depthOffset, 0f, 0f);
            Gfx.Prim(PrimitiveType.Cube, pivot.transform, new Vector3(0f, length * 0.5f - 0.01f, 0f), new Vector3(0.006f, length, thick), m, false);
            return pivot.transform;
        }

        /// <summary>Время на часах: час и минута (стрелки по часовой, если смотреть на стену с кухни).</summary>
        public void SetClock(float hour, float minute, float second)
        {
            ClockHour.localRotation = Quaternion.Euler(-(hour * 30f + minute * 0.5f), 0f, 0f);
            ClockMinute.localRotation = Quaternion.Euler(-(minute * 6f), 0f, 0f);
            ClockSecond.localRotation = Quaternion.Euler(-(Mathf.Floor(second) * 6f), 0f, 0f);
        }

        // ------------------------------------------------------------------ огни города за окном

        void BuildSkyline()
        {
            System.Random rng = new System.Random(77);
            MeshBatch bld = new MeshBatch();
            List<Vector3> wv = new List<Vector3>();
            List<int> wt = new List<int>();
            List<Win> wl = new List<Win>();

            float[] rowZ = { 90f, 130f, 175f };
            float[] hMin = { 9f, 14f, 20f };
            float[] hMax = { 26f, 34f, 44f };
            float[] half = { 80f, 110f, 145f };
            Color32[] rowCol = { C(10, 12, 20), C(15, 18, 30), C(21, 26, 42) };
            Color32[] warm = { C(255, 214, 140), C(255, 190, 110), C(255, 226, 170), C(206, 226, 255) };

            for (int row = 0; row < 3; row++)
            {
                float x = -half[row];
                while (x < half[row])
                {
                    float w = 7f + (float)rng.NextDouble() * 8f;
                    float h = hMin[row] + (float)rng.NextDouble() * (hMax[row] - hMin[row]);
                    float depth = 10f;
                    float cx = x + w * 0.5f;
                    float z = rowZ[row] + (float)rng.NextDouble() * 10f;
                    bld.Box(new Vector3(cx, h * 0.5f - 8f, z), new Vector3(w, h + 16f, depth), rowCol[row]);

                    // окна на стороне, обращённой к кухне (-Z)
                    float zf = z - depth * 0.5f - 0.06f;
                    int cols = Mathf.Max(1, (int)(w / 3.2f));
                    int rows = Mathf.Max(1, (int)((h - 2f) / 3.6f));
                    for (int ci = 0; ci < cols; ci++)
                    {
                        for (int ri = 0; ri < rows; ri++)
                        {
                            if (rng.NextDouble() < 0.38) continue;   // этот дом и так тёмный
                            float wx = x + (ci + 0.5f) * (w / cols);
                            float wy = 2.4f + ri * 3.6f;
                            int vi = wv.Count;
                            wv.Add(new Vector3(wx - 0.75f, wy - 1.0f, zf));
                            wv.Add(new Vector3(wx - 0.75f, wy + 1.0f, zf));
                            wv.Add(new Vector3(wx + 0.75f, wy + 1.0f, zf));
                            wv.Add(new Vector3(wx + 0.75f, wy - 1.0f, zf));
                            wt.Add(vi); wt.Add(vi + 1); wt.Add(vi + 2);
                            wt.Add(vi); wt.Add(vi + 2); wt.Add(vi + 3);
                            Win win = new Win();
                            win.X = wx;
                            win.Jit = (float)(rng.NextDouble() * 2.0 - 1.0) * 6f;
                            win.Bright = 0.55f + (float)rng.NextDouble() * 0.45f;
                            win.Col = warm[rng.Next(warm.Length)];
                            wl.Add(win);
                        }
                    }
                    x += w + 1f + (float)rng.NextDouble() * 3f;
                }
            }

            Mesh sil = bld.Build("PG_Skyline");
            Gfx.MeshObject(Root, sil, OwnedResources.Keep(Root.gameObject, Gfx.Unlit(Color.white, null, false, false, 0.2f)), "Skyline");
            skylineMesh = sil;

            wins = wl.ToArray();
            winColors = new Color32[wv.Count];
            winMesh = new Mesh();
            winMesh.name = "PG_SkylineWindows";
            winMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            winMesh.SetVertices(wv);
            winMesh.SetTriangles(wt, 0);
            winMesh.bounds = new Bounds(new Vector3(0f, 20f, 130f), new Vector3(400f, 80f, 200f));
            Gfx.MeshObject(Root, winMesh, OwnedResources.Keep(Root.gameObject, Gfx.Unlit(Color.white, null, true, false, 0.35f)), "SkylineWindows");
            SetCity(300f, 0f);
        }

        /// <summary>
        /// Огни города: окна к востоку от фронта темноты (x > waveX) погашены, у самого фронта мигают. t — время для мерцания.
        /// </summary>
        public void SetCity(float waveX, float t)
        {
            if (wins == null) return;
            for (int i = 0; i < wins.Length; i++)
            {
                Win w = wins[i];
                float edge = (w.X + w.Jit) - waveX;     // < 0: ещё не накрыто
                float k = 1f;
                if (edge >= 0f) k = 0f;
                else if (edge > -5f)
                {
                    // перед гашением окно дёргается
                    float n = Mathf.PerlinNoise(i * 0.37f, t * 14f);
                    k = n > 0.45f ? 1f : 0.15f;
                }
                float br = w.Bright * k;
                Color32 c = new Color32((byte)(w.Col.r * br), (byte)(w.Col.g * br), (byte)(w.Col.b * br), 255);
                int vi = i * 4;
                winColors[vi] = c;
                winColors[vi + 1] = c;
                winColors[vi + 2] = c;
                winColors[vi + 3] = c;
            }
            winMesh.colors32 = winColors;
        }

        /// <summary>Фиолетовая дымка на фронте Тьмы: alpha 0 — не видна.</summary>
        public void SetHaze(float waveX, float alpha)
        {
            HazeMat.SetColor("_Color", new Color(0.34f, 0.12f, 0.52f, alpha));
            for (int i = 0; i < Haze.Length; i++)
            {
                Haze[i].localPosition = new Vector3(waveX + 6f + i * 13f, 12f + (i % 2) * 5f, 98f + i * 3f);
            }
        }
    }
}
