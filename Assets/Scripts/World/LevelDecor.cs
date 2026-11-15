using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// «Живой» город: магазины с вывесками и товаром, парки (трава, дорожки, деревья, клумбы, фонтан, беседка, детская площадка),
    /// уличные деревья, мебель (почтовые ящики, будки, светофоры, велопарковки, киоски), провода, бельё, плющ и плакаты.
    /// Часть класса LevelBuilder: пользуется его пакетами мешей и проверкой «занятых» мест. Всё детерминировано (свой seed).
    /// </summary>
    public static partial class LevelBuilder
    {
        static BatchSet grassS;
        static BatchSet signS;
        static System.Random drng;
        static readonly List<Vector3> entrances = new List<Vector3>();   // двери и витрины: деревья и мебель их не заслоняют
        static readonly HashSet<long> grassTiles = new HashSet<long>();
        static readonly List<Vector3> treePts = new List<Vector3>();

        /// <summary>Центры парков (где стоит фонтан, беседка, площадка): для проверок и вида сверху.</summary>
        public static readonly List<Vector3> ParkCenters = new List<Vector3>();

        /// <summary>Места с плющом на фасадах и деревья (для проверок и снимков).</summary>
        public static readonly List<Vector3> IvyPatches = new List<Vector3>();
        public static readonly List<Vector3> IvyNormals = new List<Vector3>();
        public static IList<Vector3> TreePoints { get { return treePts; } }

        /// <summary>Витрины магазинов: точка на стене и нормаль наружу (для проверок: витрина должна быть твёрдой).</summary>
        public static readonly List<Vector3> ShopFronts = new List<Vector3>();
        public static readonly List<Vector3> ShopFrontNormals = new List<Vector3>();

        struct ShopSpot
        {
            public Vector3 Wall;
            public Vector3 N;
            public float Yaw;
            public int Kind;
            public Color32 Awning;
        }

        static readonly List<ShopSpot> shopSpots = new List<ShopSpot>();

        struct ShopDef
        {
            public string Name;
            public Color32 Board;
            public Color32 Ink;
        }

        static ShopDef MkShop(string name, int br, int bg, int bb, int ir, int ig, int ib)
        {
            ShopDef s = new ShopDef();
            s.Name = name;
            s.Board = Rgb(br, bg, bb);
            s.Ink = Rgb(ir, ig, ib);
            return s;
        }

        // индекс = тип магазина (см. Decor.ShopWindow)
        static readonly ShopDef[] Shops =
        {
            MkShop("ПРОДУКТЫ", 46, 72, 52, 214, 204, 150),
            MkShop("ХЛЕБ", 118, 78, 42, 236, 218, 172),
            MkShop("АПТЕКА", 212, 214, 206, 40, 120, 70),
            MkShop("ОВОЩИ", 58, 90, 48, 232, 204, 84),
            MkShop("МЯСО", 122, 40, 38, 238, 222, 204),
            MkShop("КАФЕ", 42, 38, 50, 232, 172, 84),
            MkShop("БАР", 30, 30, 40, 212, 74, 62),
            MkShop("ЦВЕТЫ", 110, 60, 100, 242, 222, 232),
            MkShop("КНИГИ", 54, 52, 96, 232, 226, 202),
            MkShop("ОБУВЬ", 92, 62, 42, 242, 232, 202),
            MkShop("ОДЕЖДА", 60, 50, 72, 242, 212, 124),
            MkShop("ЧАСЫ", 40, 60, 72, 232, 222, 164),
            MkShop("ПОЧТА", 36, 60, 120, 240, 240, 240),
            MkShop("ИГРУШКИ", 182, 62, 52, 252, 232, 94),
            MkShop("РЫБА", 40, 80, 112, 232, 242, 242),
            MkShop("ПАРИКМАХЕРСКАЯ", 52, 52, 54, 222, 64, 64)
        };

        static readonly int[] ResidentialShops = { 0, 1, 2, 5, 7, 12, 15, 9 };
        static readonly int[] HospitalShops = { 2, 7, 5, 0, 8 };

        static int ShopKindFor(int d)
        {
            if (d == 1) return drng.Next(Shops.Length);
            if (d == 0) return ResidentialShops[drng.Next(ResidentialShops.Length)];
            return HospitalShops[drng.Next(HospitalShops.Length)];
        }

        static bool NearEntrance(Vector3 p, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < entrances.Count; i++)
            {
                float dx = entrances[i].x - p.x;
                float dz = entrances[i].z - p.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        static bool NearTree(Vector3 p, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < treePts.Count; i++)
            {
                float dx = treePts[i].x - p.x;
                float dz = treePts[i].z - p.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        static float Rd(float a, float b)
        {
            return a + (float)drng.NextDouble() * (b - a);
        }

        // ------------------------------------------------------------------ магазин на фасаде

        /// <summary>Витрина с товаром, полосатый тент и вывеска над ним; запоминает место, чтобы позже поставить столики, ящики и т. п.</summary>
        static void AddShop(MeshBatch pb, Vector3 wall, Vector3 n, float yaw, int kind, Color32 awning)
        {
            ShopDef def = Shops[kind];
            Color32 trim = Models.Dim(def.Board, 0.9f);
            if (kind == 2) trim = Rgb(150, 154, 150);
            Color32 awn = Models.Dim(def.Board, 1.25f);
            if (kind == 2 || kind == 12) awn = awning;
            Decor.ShopWindow(pb, wall, yaw, kind, trim, drng);
            Decor.StripedAwning(pb, wall, yaw, 4.2f, awn);
            MeshBatch sb = signS.At(wall);
            Decor.WallSign(pb, sb, wall + Vector3.up * 3.6f, n, def.Name, def.Board, def.Ink, def.Name.Length > 9 ? 2 : 3, 0.8f, 4.1f);

            Vector3 t = Vector3.Cross(n, Vector3.up).normalized;
            // подвесные вывески сбоку: аптека — зелёный крест, часы — циферблат
            if (kind == 2)
            {
                Vector3 bp = wall + t * 2.35f + Vector3.up * 3.15f;
                Decor.BladeSign(pb, sb, bp, n, t, "+", Rgb(30, 110, 60), Rgb(236, 240, 236), 0.6f);
            }
            else if (kind == 11)
            {
                Vector3 bp = wall + t * 2.3f + Vector3.up * 3.2f + n * 0.3f;
                pb.Prism(bp + n * 0.35f, t, 0.42f, 0.12f, 12, Rgb(200, 190, 150), true);
                pb.Prism(bp + n * 0.35f + t * 0.07f, t, 0.46f, 0.04f, 12, Rgb(40, 36, 32), true);
                pb.BoxYaw(bp + n * 0.35f + t * 0.1f + Vector3.up * 0.12f, new Vector3(0.03f, 0.26f, 0.04f), yaw + 90f, Rgb(24, 24, 24), MeshBatch.AllFaces);
                pb.Rod(wall + t * 2.3f + Vector3.up * 3.2f, bp + n * 0.35f, 0.025f, 4, Rgb(40, 40, 44));
            }
            else if (kind == 5 || kind == 6 || kind == 8)
            {
                string[] txt = { "ОТКРЫТО", "СКИДКИ", "ЗАКРЫТО" };
                Vector3 bp = wall + t * -2.35f + Vector3.up * 3.15f;
                Decor.BladeSign(pb, sb, bp, n, t, txt[drng.Next(txt.Length)], Rgb(40, 40, 46), Rgb(232, 200, 120), 0.4f);
            }

            // витрина твёрдая: в неё нельзя зайти и встать между полками (коробка от стены до переплётов, ширина с пилястрами)
            Gfx.StaticBox(rootT, wall + n * 0.32f + Vector3.up * 1.3f, new Vector3(4.4f, 2.6f, 0.64f), "Shopfront", yaw);
            ShopFronts.Add(wall);
            ShopFrontNormals.Add(n);
            entrances.Add(wall);
            ShopSpot s = new ShopSpot();
            s.Wall = wall;
            s.N = n;
            s.Yaw = yaw;
            s.Kind = kind;
            s.Awning = awn;
            shopSpots.Add(s);
        }

        /// <summary>Мелочи на фасаде вне входов: плющ (жилой район, больница) и плакаты.</summary>
        static void WallExtras(MeshBatch pb, Vector3 wall, Vector3 n, Vector3 t, int d, float h)
        {
            double r = drng.NextDouble();
            MeshBatch sb = signS.At(wall);
            if (r < 0.14 && d != 3)
            {
                Decor.Ivy(pb, wall + n * 0.12f, n, t, Mathf.Min(h - 2f, 4.5f + (float)drng.NextDouble() * 3.5f), drng);
                IvyPatches.Add(wall);
                IvyNormals.Add(n);
            }
            else if (r < 0.40)
            {
                float off = Rd(-1.6f, 1.6f);
                float y = Rd(1.5f, 2.0f);
                int variant = drng.Next(SignAtlas.PosterVariants);
                if (d == 3 && variant < 6) variant = 7;   // в промзоне — предупреждения
                Decor.PosterOnWall(sb, wall + t * off + Vector3.up * y, n, variant, Rd(0.8f, 1.05f));
                if (drng.NextDouble() < 0.3)
                    Decor.PosterOnWall(sb, wall + t * (off + 0.9f) + Vector3.up * (y - 0.1f), n, drng.Next(SignAtlas.PosterVariants), Rd(0.7f, 0.9f));
            }
        }

        // ------------------------------------------------------------------ сборка декора

        static void BuildDecor(Transform root)
        {
            BuildParks(root);
            BuildStreetTrees(root);
            BuildShopExtras(root);
            BuildStreetFurniture(root);
            BuildWires();
            BuildLaundry();
            BuildStationSign(root);
        }

        // ------------------------------------------------------------------ деревья

        static Color32 LeafColor(bool allowDead)
        {
            double r = drng.NextDouble();
            Color32[] greens = { Rgb(44, 70, 40), Rgb(52, 80, 42), Rgb(38, 62, 36), Rgb(60, 84, 46), Rgb(46, 74, 48) };
            Color32[] autumn = { Rgb(124, 88, 34), Rgb(142, 70, 30), Rgb(104, 96, 38), Rgb(98, 58, 34) };
            if (r < 0.64) return greens[drng.Next(greens.Length)];
            if (r < 0.9 || !allowDead) return autumn[drng.Next(autumn.Length)];
            return Rgb(78, 70, 40);
        }

        /// <summary>Дерево с коллайдером ствола. kind 0 — лиственное, 1 — ель, 2 — тополь.</summary>
        static void PlaceTree(Transform root, Vector3 p, int kind, float scale)
        {
            PlaceTree(root, p, kind, scale, Vector3.zero, 1f);
        }

        /// <summary>Дерево с наклоном кроны (у стены: крона уходит на улицу, не врезаясь в фасад) и шириной кроны.</summary>
        static void PlaceTree(Transform root, Vector3 p, int kind, float scale, Vector3 lean, float spread)
        {
            p.y = 0f;
            Color32 leaf = kind == 1 ? Rgb(30, 54, 40) : LeafColor(true);
            Decor.Tree(propS.At(p), p, drng, kind, leaf, scale, lean, spread);
            float w = kind == 1 ? 1.1f * scale : 0.5f * scale;
            Gfx.StaticBox(root, p + new Vector3(0f, 1.3f * scale, 0f), new Vector3(w, 2.6f * scale, w), "Tree");
            treePts.Add(p);
        }

        static int PickTreeKind()
        {
            double r = drng.NextDouble();
            if (r < 0.62) return 0;
            if (r < 0.80) return 1;
            return 2;
        }

        /// <summary>Деревья вдоль улиц жилого района и рынка: по одному у стены, не у входов, не на фонарях и людях.</summary>
        static void BuildStreetTrees(Transform root)
        {
            float C = CityMap.Cell;
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Street) continue;
                    int dist = CityMap.District[x, z];
                    if (dist != 0 && dist != 1 && dist != 2) continue;
                    if (((x * 7 + z * 13) & 3) != 0) continue;
                    if (NearWall(x, z, 2)) continue;   // не у наростов Тьмы
                    for (int dir = 0; dir < 4; dir++)
                    {
                        if (CityMap.IsRoadCell(x + Dx[dir], z + Dz[dir])) continue;
                        Vector3 c = CellCenter(x, z, 0f);
                        Vector3 p = c + new Vector3(Dx[dir], 0f, Dz[dir]) * (C * 0.5f - 0.85f);
                        p.x += Dz[dir] != 0 ? Rd(-1.5f, 1.5f) : 0f;
                        p.z += Dx[dir] != 0 ? Rd(-1.5f, 1.5f) : 0f;
                        if (NearPoints(p, 8f) || Reserved(p, 3.2f) || NearEntrance(p, 3.4f) || NearTree(p, 9f)) continue;
                        // крона клонится на улицу: ствол у бордюра, а ветви не лезут в стену и не закрывают вывески
                        int tk = drng.NextDouble() < 0.7 ? 0 : 2;
                        Vector3 outward = new Vector3(-Dx[dir], 0f, -Dz[dir]);
                        PlaceTree(root, p, tk, Rd(0.82f, 0.95f), outward * (tk == 0 ? 1.5f : 0.7f), tk == 0 ? 0.8f : 1f);
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ парки

        static long TileKey(int tx, int tz)
        {
            return ((long)(tx + 1000) << 20) | (uint)(tz + 1000);
        }

        static bool OnGrass(Vector3 p)
        {
            return grassTiles.Contains(TileKey(Mathf.FloorToInt(p.x / 2f), Mathf.FloorToInt(p.z / 2f)));
        }

        /// <summary>
        /// Парк на площади r: периметр 3 м и крестовые дорожки остаются плиткой, четыре лужайки травяные (плитки 2x2 м — они
        /// обходят очаги, старт, лампы и людей). style: 0 — сад (клумба в центре), 1 — сквер с беседкой, 2 — фонтан,
        /// 3 — детская площадка с каруселью, качелями, горкой и песочницей.
        /// </summary>
        static void BuildPark(Transform root, CellRect r, int style)
        {
            float C = CityMap.Cell;
            float x0 = r.X0 * C;
            float x1 = (r.X1 + 1) * C;
            float z0 = r.Z0 * C;
            float z1 = (r.Z1 + 1) * C;
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            float edge = 4.0f;    // чётное число: плитки травы ложатся на сетку 2 м (OnGrass)
            float pw = 2.4f;
            float clearR = (style == 1 || style == 2) ? 4.6f : 0f;   // у беседки и фонтана трава не растёт

            // центральная деталь выбирает место, свободное от ламп и очагов
            Vector3 center = new Vector3(cx, 0f, cz);
            float need = style == 3 ? 9f : 5.5f;
            for (int k = 0; k < 40 && (Reserved(center, need) || NearPoints(center, need + 4f)); k++)
            {
                float a = k * 2.399f;
                float rad = 2f + k * 0.9f;
                center = new Vector3(cx + Mathf.Cos(a) * rad, 0f, cz + Mathf.Sin(a) * rad);
            }

            ParkCenters.Add(center);

            // трава: плитки по лужайкам
            for (float tz = z0 + edge; tz < z1 - edge - 1f; tz += 2f)
            {
                for (float tx = x0 + edge; tx < x1 - edge - 1f; tx += 2f)
                {
                    float mx = tx + 1f;
                    float mz = tz + 1f;
                    if (Mathf.Abs(mx - cx) < pw * 0.5f + 1f || Mathf.Abs(mz - cz) < pw * 0.5f + 1f) continue;   // крестовые дорожки
                    Vector3 m = new Vector3(mx, 0f, mz);
                    if (NearPoints(m, 4.2f) || Reserved(m, 2.4f)) continue;
                    if (clearR > 0f && (m - center).sqrMagnitude < clearR * clearR) continue;
                    MeshBatch gb = grassS.At(m);
                    float g = Rd(0.9f, 1.1f);
                    Color32 tint = new Color32((byte)(255 * Mathf.Min(1f, g)), (byte)(255 * Mathf.Min(1f, g)), (byte)(255 * Mathf.Min(1f, g)), 255);
                    gb.Quad(new Vector3(tx, 0.025f, tz), new Vector3(tx, 0.025f, tz + 2f), new Vector3(tx + 2f, 0.025f, tz + 2f), new Vector3(tx + 2f, 0.025f, tz), tint);
                    grassTiles.Add(TileKey(Mathf.FloorToInt(tx / 2f), Mathf.FloorToInt(tz / 2f)));
                }
            }

            MeshBatch pb = propS.At(center);
            switch (style)
            {
                case 0:   // сад: круглая клумба с кустами
                    Decor.FlowerBed(pb, center, 45f, 3.2f, 3.2f, drng);
                    Decor.Shrub(pb, center + new Vector3(2.4f, 0f, 0f), drng, 0.9f, Rgb(48, 76, 42));
                    Decor.Shrub(pb, center + new Vector3(-2.4f, 0f, 0f), drng, 0.9f, Rgb(48, 76, 42));
                    break;
                case 1:   // беседка
                    Decor.Gazebo(pb, center);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 0.7853982f + 0.3927f;
                        Gfx.StaticBox(root, center + new Vector3(Mathf.Cos(a) * 2.15f, 1.3f, Mathf.Sin(a) * 2.15f), new Vector3(0.3f, 2.6f, 0.3f), "GazeboPost");
                    }
                    break;
                case 2:   // фонтан
                    Decor.Fountain(pb, center);
                    Gfx.StaticBox(root, center + new Vector3(0f, 0.35f, 0f), new Vector3(5.2f, 0.7f, 5.2f), "Fountain");
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.5708f + 0.785f;
                        Vector3 bp = center + new Vector3(Mathf.Cos(a) * 4.4f, 0f, Mathf.Sin(a) * 4.4f);
                        Decor.FlowerBed(propS.At(bp), bp, a * Mathf.Rad2Deg + 90f, 2.4f, 1.0f, drng);
                    }
                    break;
                case 3:   // площадка
                    {
                        Decor.MerryGoRound(pb, center);
                        Gfx.StaticBox(root, center + new Vector3(0f, 0.2f, 0f), new Vector3(2.0f, 0.4f, 2.0f), "Carousel");
                        Vector3 sw = center + new Vector3(-6.2f, 0f, 1.5f);
                        Decor.Swing(propS.At(sw), sw, 90f);
                        for (int e = -1; e <= 1; e += 2)
                        {
                            Gfx.StaticBox(root, sw + new Vector3(0f, 1.2f, e * 1.8f), new Vector3(1.8f, 2.4f, 0.2f), "SwingFrame");
                        }
                        Vector3 sl = center + new Vector3(5.5f, 0f, -2.5f);
                        Decor.Slide(propS.At(sl), sl, -90f);
                        Gfx.StaticBox(root, sl + new Vector3(1.0f, 0.75f, 0f), new Vector3(1.2f, 1.5f, 1.2f), "SlideTower");
                        Vector3 sb = center + new Vector3(1.5f, 0f, 6.0f);
                        Decor.Sandbox(propS.At(sb), sb, 0f);
                        Gfx.StaticBox(root, sb + new Vector3(0f, 0.15f, 0f), new Vector3(2.7f, 0.3f, 2.7f), "Sandbox");
                        treePts.Add(sw);
                        treePts.Add(sl);
                        treePts.Add(sb);
                        treePts.Add(center);
                    }
                    break;
            }

            // деревья по лужайкам: сетка 7 м с разбросом
            for (float tz = z0 + edge + 2f; tz < z1 - edge - 2f; tz += 7f)
            {
                for (float tx = x0 + edge + 2f; tx < x1 - edge - 2f; tx += 7f)
                {
                    Vector3 p = new Vector3(tx + Rd(-1.6f, 1.6f), 0f, tz + Rd(-1.6f, 1.6f));
                    if (!OnGrass(p) || NearTree(p, 5.2f) || NearPoints(p, 7f) || Reserved(p, 3.5f)) continue;
                    if (drng.NextDouble() < 0.18) continue;
                    PlaceTree(root, p, PickTreeKind(), Rd(0.95f, 1.3f));
                }
            }

            // кусты и клумбы по углам лужаек, скамейки у дорожек
            Color32 wood = Rgb(110, 84, 52);
            for (int q = 0; q < 4; q++)
            {
                float sxn = (q & 1) == 0 ? -1f : 1f;
                float szn = (q & 2) == 0 ? -1f : 1f;
                Vector3 bed = new Vector3(cx + sxn * (pw * 0.5f + 2.2f), 0f, cz + szn * (pw * 0.5f + 2.2f));
                if (style != 0 && (bed - center).sqrMagnitude < 36f) continue;
                if (OnGrass(bed) && !Reserved(bed, 2.2f) && !NearPoints(bed, 6f))
                    Decor.FlowerBed(propS.At(bed), bed, 0f, 2.4f, 1.2f, drng);
            }
            for (int s = 0; s < 6; s++)
            {
                Vector3 bp;
                float yaw;
                // скамейки вдоль крестовых дорожек, лицом к дорожке
                if ((s & 1) == 0) { bp = new Vector3(cx + (s < 3 ? -1f : 1f) * Rd(7f, 13f), 0f, cz + pw * 0.5f + 0.9f); yaw = 180f; }
                else { bp = new Vector3(cx + pw * 0.5f + 0.9f, 0f, cz + (s < 3 ? -1f : 1f) * Rd(7f, 13f)); yaw = -90f; }
                if (!OnGrass(bp) || Reserved(bp, 2.2f) || NearPoints(bp, 5f) || NearTree(bp, 2.4f)) continue;
                Models.Bench(propS.At(bp), bp, yaw, wood);
                Gfx.StaticBox(root, bp + new Vector3(0f, 0.25f, 0f), new Vector3(2.0f, 0.5f, 0.6f), "Bench", yaw);
            }
        }

        /// <summary>Площади-парки: сад (стартовый двор), сквер с беседкой, площадь с фонтаном, детская площадка. Их же рисует вид сверху в предыстории.</summary>
        public static readonly CellRect[] ParkRects =
        {
            new CellRect(2, 2, 9, 8),
            new CellRect(2, 25, 11, 29),
            new CellRect(30, 48, 37, 53),
            new CellRect(8, 48, 13, 55)
        };

        static void BuildParks(Transform root)
        {
            for (int i = 0; i < ParkRects.Length; i++) BuildPark(root, ParkRects[i], i);
        }

        // ------------------------------------------------------------------ вокруг магазинов

        static void BuildShopExtras(Transform root)
        {
            Color32 wood = Rgb(110, 84, 52);
            string[] boards = { "МЕНЮ", "СКИДКИ", "ОТКРЫТО", "ХЛЕБ" };
            for (int i = 0; i < shopSpots.Count; i++)
            {
                ShopSpot s = shopSpots[i];
                Vector3 n = s.N;
                Vector3 t = Vector3.Cross(n, Vector3.up).normalized;
                MeshBatch pb = propS.At(s.Wall);
                MeshBatch sb = signS.At(s.Wall);
                switch (s.Kind)
                {
                    case 3:   // ящики с овощами у входа
                        for (int k = 0; k < 2; k++)
                        {
                            Vector3 p = s.Wall + n * 0.95f + t * (-1.0f + k * 1.9f);
                            if (Reserved(p, 1.8f)) continue;
                            Models.Crate(pb, p, s.Yaw, 0.9f, wood);
                            Color32[] veg = { Rgb(170, 50, 40), Rgb(200, 120, 30), Rgb(70, 130, 50) };
                            for (int b = 0; b < 5; b++)
                                Decor.Blob(pb, p + new Vector3(Rd(-0.3f, 0.3f), 0.98f, Rd(-0.3f, 0.3f)), 0.1f, 0.1f, 0.1f, veg[(k + b) % veg.Length], drng, 5, 3, 0.1f);
                            Gfx.StaticBox(root, p + new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.9f), "Crate", s.Yaw);
                        }
                        break;
                    case 5:   // столики кафе под зонтом
                        for (int k = 0; k < 2; k++)
                        {
                            Vector3 p = s.Wall + n * 2.3f + t * (-1.3f + k * 2.6f);
                            if (Reserved(p, 2.4f)) continue;
                            Decor.CafeSet(pb, p, drng.Next(360), drng, s.Awning, true);
                            Gfx.StaticBox(root, p + new Vector3(0f, 0.4f, 0f), new Vector3(0.95f, 0.8f, 0.95f), "CafeTable");
                        }
                        break;
                    case 7:   // ведёрки с цветами
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 p = s.Wall + n * 0.6f + t * (-1.9f + k * 0.55f);
                            if (Reserved(p, 1.2f)) continue;
                            pb.Frustum(p + new Vector3(0f, 0.2f, 0f), Vector3.up, 0.15f, 0.2f, 0.4f, 8, Rgb(90, 100, 110), true, false);
                            Color32[] fl = { Rgb(176, 52, 48), Rgb(190, 158, 44), Rgb(128, 70, 148), Rgb(206, 200, 186) };
                            for (int b = 0; b < 4; b++)
                                Decor.Blob(pb, p + new Vector3(Rd(-0.1f, 0.1f), Rd(0.45f, 0.65f), Rd(-0.1f, 0.1f)), 0.1f, 0.09f, 0.1f, fl[(k + b) % fl.Length], drng, 5, 3, 0.1f);
                        }
                        break;
                    case 12:  // почтовый ящик у входа
                        {
                            Vector3 p = s.Wall + n * 0.7f + t * 2.6f;
                            if (!Reserved(p, 1.2f))
                            {
                                Decor.Mailbox(pb, p, s.Yaw);
                                Gfx.StaticBox(root, p + new Vector3(0f, 0.7f, 0f), new Vector3(0.7f, 1.4f, 0.5f), "Mailbox", s.Yaw);
                            }
                        }
                        break;
                    case 15:  // вертушка парикмахера: красно-бело-синий столбик на стене
                        {
                            Vector3 p = s.Wall + t * 2.5f + n * 0.3f + Vector3.up * 2.0f;
                            pb.Prism(p, Vector3.up, 0.09f, 1.0f, 8, Rgb(210, 210, 204), true);
                            for (int k = 0; k < 3; k++)
                                pb.Prism(p + Vector3.up * (-0.3f + k * 0.3f), Vector3.up, 0.095f, 0.14f, 8, k == 1 ? Rgb(40, 64, 140) : Rgb(168, 36, 36), false);
                            pb.Prism(p + Vector3.up * 0.55f, Vector3.up, 0.11f, 0.06f, 8, Rgb(40, 40, 44), true);
                        }
                        break;
                    default:  // доска-меню у входа
                        if (drng.NextDouble() < 0.55)
                        {
                            Vector3 p = s.Wall + n * 1.9f + t * Rd(-1.9f, -0.6f);
                            if (!Reserved(p, 1.5f))
                            {
                                Decor.SandwichBoard(pb, sb, p, s.Yaw + Rd(-14f, 14f), boards[drng.Next(boards.Length)], Rgb(34, 44, 40), Rgb(228, 228, 214));
                                Gfx.StaticBox(root, p + new Vector3(0f, 0.45f, 0f), new Vector3(0.65f, 0.9f, 0.45f), "Board", s.Yaw);
                            }
                        }
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ уличная мебель

        /// <summary>
        /// Для проверки расстановки: сколько предметов уличной мебели (велостоянка, почтовый ящик, будка, автомат, киоск, тумба)
        /// стоят в стволе дерева. Должно быть 0. Подробности — в log (если он не null).
        /// </summary>
        public static int FurnitureInTrees(System.Text.StringBuilder log)
        {
            Physics.SyncTransforms();
            int bad = 0;
            Collider[] cols = Object.FindObjectsByType<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                string n = cols[i].gameObject.name;
                if (n != "BikeRack" && n != "Mailbox" && n != "PhoneBooth" && n != "Vending" && n != "Kiosk" && n != "Column") continue;
                Bounds b = cols[i].bounds;
                Collider[] hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < hits.Length; k++)
                {
                    if (hits[k].gameObject.name != "Tree") continue;
                    bad++;
                    if (log != null) log.AppendLine(n + " at " + b.center + " stands in a tree trunk at " + hits[k].bounds.center);
                    break;
                }
            }
            return bad;
        }

        static void BuildStreetFurniture(Transform root)
        {
            float C = CityMap.Cell;
            int mail = 0, phone = 0, bikes = 0, vend = 0, kiosks = 0, columns = 0, lights = 0;
            Color32[] bodies = { Rgb(56, 84, 110), Rgb(110, 60, 52), Rgb(60, 90, 70), Rgb(110, 98, 60) };

            // светофоры: у входов улиц на площади
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    if (CityMap.Kind[x, z] != CityMap.Street || lights >= 14) continue;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int px = x + Dx[dir];
                        int pz = z + Dz[dir];
                        if (!CityMap.InGrid(px, pz) || CityMap.Kind[px, pz] != CityMap.Plaza) continue;
                        // сторона стены (перпендикулярно направлению на площадь)
                        for (int side = 0; side < 4; side++)
                        {
                            if (Dx[side] * Dx[dir] + Dz[side] * Dz[dir] != 0) continue;
                            if (CityMap.IsRoadCell(x + Dx[side], z + Dz[side])) continue;
                            Vector3 c = CellCenter(x, z, 0f);
                            Vector3 p = c + new Vector3(Dx[dir], 0f, Dz[dir]) * (C * 0.5f - 1.0f) + new Vector3(Dx[side], 0f, Dz[side]) * (C * 0.5f - 0.7f);
                            if (Reserved(p, 2.2f) || NearEntrance(p, 2.5f) || NearPoints(p, 6f)) continue;
                            float yaw = Mathf.Atan2(-Dx[dir], -Dz[dir]) * Mathf.Rad2Deg;
                            Decor.TrafficLight(propS.At(p), p, yaw);
                            Gfx.StaticBox(root, p + new Vector3(0f, 1.9f, 0f), new Vector3(0.3f, 3.8f, 0.3f), "TrafficPole");
                            lights++;
                            break;
                        }
                    }
                }
            }

            // остальная мебель: случайные места вдоль стен
            for (int tries = 0; tries < 1400; tries++)
            {
                int x = drng.Next(CityMap.W);
                int z = drng.Next(CityMap.H);
                byte k = CityMap.Kind[x, z];
                if (k != CityMap.Street && k != CityMap.Plaza) continue;
                int dist = CityMap.District[x, z];
                if (dist == 3 && drng.NextDouble() < 0.7) continue;
                int wx = 0, wz = 0;
                if (!WallSide(x, z, out wx, out wz)) continue;
                Vector3 c = CellCenter(x, z, 0f);
                Vector3 p = c + new Vector3(wx, 0f, wz) * (C * 0.5f - 0.9f);
                p.x += Mathf.Abs(wz) * Rd(-2.2f, 2.2f);
                p.z += Mathf.Abs(wx) * Rd(-2.2f, 2.2f);
                if (NearPoints(p, 7f) || Reserved(p, 2.2f) || NearEntrance(p, 3.0f)) continue;
                float yaw = Mathf.Atan2(-wx, -wz) * Mathf.Rad2Deg;   // лицом от стены
                double r = drng.NextDouble();
                MeshBatch pb = propS.At(p);
                // Мебель не стоит в стволе уличного дерева (деревья ставятся раньше). Счётчик и бросок кубика остаются, как были,
                // поэтому остальная мебель города стоит там же, где стояла: пропадает только то, что было в дереве.
                if (r < 0.22 && mail < 10)
                {
                    mail++;
                    if (NearTree(p, 1.0f)) continue;
                    Decor.Mailbox(pb, p, yaw);
                    Gfx.StaticBox(root, p + new Vector3(0f, 0.7f, 0f), new Vector3(0.7f, 1.4f, 0.5f), "Mailbox", yaw);
                }
                else if (r < 0.34 && phone < 6)
                {
                    phone++;
                    if (NearTree(p, 1.2f)) continue;
                    Decor.PhoneBooth(pb, p, yaw);
                    Gfx.StaticBox(root, p + new Vector3(0f, 1.15f, 0f), new Vector3(1.0f, 2.3f, 1.0f), "PhoneBooth", yaw);
                }
                else if (r < 0.52 && bikes < 8)
                {
                    bikes++;
                    if (NearTree(p, 1.8f)) continue;   // стойка 2 м шириной: ствол дерева в ней — баг
                    Decor.BikeRack(pb, p, yaw);
                    Gfx.StaticBox(root, p + new Vector3(0f, 0.4f, 0.2f), new Vector3(2.0f, 0.8f, 0.5f), "BikeRack", yaw);
                }
                else if (r < 0.62 && vend < 6)
                {
                    vend++;
                    Color32 vbody = bodies[drng.Next(bodies.Length)];
                    if (NearTree(p, 1.2f)) continue;
                    Decor.Vending(pb, p, yaw, vbody);
                    Gfx.StaticBox(root, p + new Vector3(0f, 0.9f, 0f), new Vector3(1.0f, 1.8f, 0.85f), "Vending", yaw);
                }
                else if (r < 0.74 && kiosks < 5 && k == CityMap.Plaza)
                {
                    kiosks++;
                    Decor.Kiosk(pb, p, yaw, bodies[drng.Next(bodies.Length)], drng);
                    Gfx.StaticBox(root, p + new Vector3(0f, 1.15f, 0f), new Vector3(1.9f, 2.3f, 1.6f), "Kiosk", yaw);
                    MeshBatch sb = signS.At(p);
                    Vector3 fn = Models.RotY(Vector3.forward, yaw);
                    Decor.WallSign(pb, sb, p + new Vector3(0f, 2.05f, 0f) + fn * 0.85f, fn, "ГАЗЕТЫ", Rgb(40, 56, 40), Rgb(232, 220, 170), 2, 0.34f, 1.3f);
                }
                else if (r < 0.84 && columns < 4 && k == CityMap.Plaza)
                {
                    columns++;
                    Decor.PosterColumn(pb, p);
                    Gfx.StaticBox(root, p + new Vector3(0f, 1.3f, 0f), new Vector3(1.3f, 2.6f, 1.3f), "Column");
                    MeshBatch sb = signS.At(p);
                    for (int a = 0; a < 3; a++)
                    {
                        float ang = a * 2.0944f + (float)drng.NextDouble();
                        Vector3 nn = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                        Decor.PosterOnWall(sb, p + nn * 0.62f + Vector3.up * 1.4f, nn, drng.Next(SignAtlas.PosterVariants), 0.95f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ провода и бельё

        /// <summary>Провода поперёк широких улиц между кронштейнами на фасадах: на высоте 8-9 м, провисают.</summary>
        static void BuildWires()
        {
            float C = CityMap.Cell;
            Color32 wire = Rgb(22, 22, 24);
            int count = 0;
            for (int z = 2; z < CityMap.H - 2; z += 3)
            {
                for (int x = 1; x < CityMap.W - 3; x++)
                {
                    if (count > 34) return;
                    if (((x * 11 + z * 5) & 3) != 0) continue;
                    // улица вдоль z, две клетки шириной (x, x+1), по бокам стены
                    if (CityMap.Kind[x, z] == CityMap.Street && CityMap.Kind[x + 1, z] == CityMap.Street
                        && !CityMap.IsRoadCell(x - 1, z) && !CityMap.IsRoadCell(x + 2, z))
                    {
                        float zc = (z + 0.5f) * C;
                        float h0 = Rd(7.6f, 9.4f);
                        Vector3 a = new Vector3(x * C - 0.1f, h0, zc);
                        Vector3 b = new Vector3((x + 2) * C + 0.1f, h0 + Rd(-0.6f, 0.6f), zc + Rd(-1.2f, 1.2f));
                        MeshBatch wb = propS.At(a);
                        wb.BoxYaw(a + new Vector3(0.15f, 0f, 0f), new Vector3(0.3f, 0.12f, 0.12f), 0f, Rgb(50, 50, 54), MeshBatch.AllFaces);
                        wb.BoxYaw(b + new Vector3(-0.15f, 0f, 0f), new Vector3(0.3f, 0.12f, 0.12f), 0f, Rgb(50, 50, 54), MeshBatch.AllFaces);
                        for (int k = 0; k < 3; k++)
                        {
                            float off = (k - 1) * 0.35f;
                            Decor.Wire(wb, a + new Vector3(0.3f, 0f, off), b + new Vector3(-0.3f, 0f, off), 0.55f, wire);
                        }
                        count++;
                    }
                    // улица вдоль x, две клетки шириной (z, z+1)
                    if (z + 1 < CityMap.H && CityMap.Kind[x, z] == CityMap.Street && CityMap.Kind[x, z + 1] == CityMap.Street
                        && !CityMap.IsRoadCell(x, z - 1) && !CityMap.IsRoadCell(x, z + 2))
                    {
                        float xc = (x + 0.5f) * C;
                        float h0 = Rd(7.6f, 9.4f);
                        Vector3 a = new Vector3(xc, h0, z * C - 0.1f);
                        Vector3 b = new Vector3(xc + Rd(-1.2f, 1.2f), h0 + Rd(-0.6f, 0.6f), (z + 2) * C + 0.1f);
                        MeshBatch wb = propS.At(a);
                        wb.BoxYaw(a + new Vector3(0f, 0f, 0.15f), new Vector3(0.12f, 0.12f, 0.3f), 0f, Rgb(50, 50, 54), MeshBatch.AllFaces);
                        wb.BoxYaw(b + new Vector3(0f, 0f, -0.15f), new Vector3(0.12f, 0.12f, 0.3f), 0f, Rgb(50, 50, 54), MeshBatch.AllFaces);
                        for (int k = 0; k < 3; k++)
                        {
                            float off = (k - 1) * 0.35f;
                            Decor.Wire(wb, a + new Vector3(off, 0f, 0.3f), b + new Vector3(off, 0f, -0.3f), 0.55f, wire);
                        }
                        count++;
                    }
                }
            }
        }

        /// <summary>Бельё на верёвках поперёк узких переулков жилого района и рынка.</summary>
        static void BuildLaundry()
        {
            float C = CityMap.Cell;
            int count = 0;
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    if (count >= 16) return;
                    if (CityMap.Kind[x, z] != CityMap.Alley) continue;
                    int d = CityMap.District[x, z];
                    if (d > 1 || drng.NextDouble() > 0.16) continue;
                    float y = Rd(4.6f, 5.6f);
                    Vector3 c = CellCenter(x, z, y);
                    if (!CityMap.IsRoadCell(x - 1, z) && !CityMap.IsRoadCell(x + 1, z))
                    {
                        Decor.Laundry(propS.At(c), c + new Vector3(-C * 0.5f + 0.05f, 0f, Rd(-1f, 1f)), c + new Vector3(C * 0.5f - 0.05f, Rd(-0.4f, 0.4f), Rd(-1f, 1f)), drng);
                        count++;
                    }
                    else if (!CityMap.IsRoadCell(x, z - 1) && !CityMap.IsRoadCell(x, z + 1))
                    {
                        Decor.Laundry(propS.At(c), c + new Vector3(Rd(-1f, 1f), 0f, -C * 0.5f + 0.05f), c + new Vector3(Rd(-1f, 1f), Rd(-0.4f, 0.4f), C * 0.5f - 0.05f), drng);
                        count++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ вокзал

        /// <summary>Портал с вывеской «АВТОВОКЗАЛ» над въездом на площадь вокзала: видно с проспекта.</summary>
        static void BuildStationSign(Transform root)
        {
            float z = StationPos.z + 8f;
            float cx = BusPos.x;
            Vector3 n = new Vector3(0f, 0f, -1f);
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 p = new Vector3(cx + e * 7.5f, 0f, z);
                if (Reserved(p, 1.5f)) p.x += e * 1.5f;
                propS.At(p).BoxYaw(p + new Vector3(0f, 3.4f, 0f), new Vector3(0.5f, 6.8f, 0.5f), 0f, Rgb(54, 54, 60), MeshBatch.AllFaces);
                propS.At(p).BoxYaw(p + new Vector3(0f, 0.3f, 0f), new Vector3(0.9f, 0.6f, 0.9f), 0f, Rgb(40, 40, 44), MeshBatch.AllFaces);
                Gfx.StaticBox(root, p + new Vector3(0f, 3.4f, 0f), new Vector3(0.6f, 6.8f, 0.6f), "GantryPost");
            }
            Vector3 c = new Vector3(cx, 6.2f, z);
            MeshBatch pb = propS.At(c);
            pb.BoxYaw(new Vector3(cx, 7.1f, z), new Vector3(15.6f, 0.4f, 0.5f), 0f, Rgb(54, 54, 60), MeshBatch.AllFaces);
            Decor.WallSign(pb, signS.At(c), c, n, "АВТОВОКЗАЛ", Rgb(30, 70, 110), Rgb(236, 236, 226), 3, 1.7f, 12.5f);
            Decor.WallSign(pb, signS.At(c), c + n * 0.01f, -n, "ВЫХОД ИЗ ГОРОДА", Rgb(30, 70, 110), Rgb(236, 236, 226), 3, 1.2f, 12.5f);
        }
    }
}
