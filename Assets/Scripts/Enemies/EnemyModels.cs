using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Меши врагов (3D вместо плоских спрайтов): Тень в рваном плаще с капюшоном и длинными руками с когтями, Гаситель — колючий шар с
    /// раскалённым ядром, Пожиратель — туша с пастью, зубами, щупальцами и россыпью глаз. Всё строится кодом из колец
    /// (Loft: поверхность вращения с шумом радиуса и рваным краем), конусов-шипов и пластин, как кисти героя и люди в автобусе.
    /// Меши общие для всех врагов одного вида (строятся один раз, ключ в кеше), анимация — поворотом частей (EnemyRig).
    /// Оси частей: +Z — вперёд (куда смотрит враг), +Y — вверх, начало — на земле под телом.
    /// </summary>
    public static partial class EnemyModels
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        static Color32 C(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        static Color32 Mul(Color32 c, float k)
        {
            return new Color32((byte)Mathf.Clamp(c.r * k, 0f, 255f), (byte)Mathf.Clamp(c.g * k, 0f, 255f), (byte)Mathf.Clamp(c.b * k, 0f, 255f), 255);
        }

        static Color32 Mix(Color32 a, Color32 b, float k)
        {
            return new Color32((byte)Mathf.Lerp(a.r, b.r, k), (byte)Mathf.Lerp(a.g, b.g, k), (byte)Mathf.Lerp(a.b, b.b, k), 255);
        }

        /// <summary>Детерминированный шум 0..1 по трём целым.</summary>
        static float Rnd(int a, int b, int seed)
        {
            unchecked
            {
                int h = a * 73856093 ^ b * 19349663 ^ seed * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Меш из кеша (строится при первом обращении; уничтоженный Unity меш строится заново).</summary>
        static Mesh Get(string key, System.Action<MeshBatch> build)
        {
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            MeshBatch b = new MeshBatch();
            build(b);
            m = b.Build("PG_Enemy_" + key);
            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ общие построители

        public struct Ring
        {
            public float Y;       // высота центра
            public float Rx;      // полуось по X
            public float Rz;      // полуось по Z
            public float Cx;      // смещение центра по X
            public float Cz;      // смещение центра по Z

            public Ring(float y, float rx, float rz, float cx, float cz)
            {
                Y = y;
                Rx = rx;
                Rz = rz;
                Cx = cx;
                Cz = cz;
            }
        }

        /// <summary>
        /// Поверхность по кольцам снизу вверх (эллипсы с общей осью Y): sides граней по кругу. noise(кольцо, грань) — множитель радиуса,
        /// drop(кольцо, грань) — сдвиг вершины по Y (рваный подол), color(пояс, грань) — цвет грани. Нормали наружу при обходе углов от +X к +Z.
        /// </summary>
        public static void Loft(MeshBatch b, Ring[] rings, int sides, bool capBottom, bool capTop,
            System.Func<int, int, float> noise, System.Func<int, int, float> drop, System.Func<int, int, Color32> color)
        {
            int n = rings.Length;
            Vector3[,] p = new Vector3[n, sides];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    float a = j * 6.2831853f / sides;
                    float m = noise != null ? noise(i, j) : 1f;
                    float dy = drop != null ? drop(i, j) : 0f;
                    p[i, j] = new Vector3(rings[i].Cx + Mathf.Cos(a) * rings[i].Rx * m, rings[i].Y + dy, rings[i].Cz + Mathf.Sin(a) * rings[i].Rz * m);
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int j2 = (j + 1) % sides;
                    b.Quad(p[i, j], p[i + 1, j], p[i + 1, j2], p[i, j2], color(i, j));
                }
            }
            if (capBottom || capTop)
            {
                Vector3 cb = Vector3.zero;
                Vector3 ct = Vector3.zero;
                for (int j = 0; j < sides; j++) { cb += p[0, j]; ct += p[n - 1, j]; }
                cb /= sides;
                ct /= sides;
                for (int j = 0; j < sides; j++)
                {
                    int j2 = (j + 1) % sides;
                    if (capBottom) b.Tri(cb, p[0, j], p[0, j2], color(0, j));
                    if (capTop) b.Tri(ct, p[n - 1, j2], p[n - 1, j], color(n - 2, j));
                }
            }
        }

        /// <summary>Шип: конус от основания к кончику (длина len, радиус основания r0).</summary>
        static void Spike(MeshBatch b, Vector3 from, Vector3 dir, float r0, float len, int sides, Color32 col)
        {
            dir.Normalize();
            b.Frustum(from + dir * (len * 0.5f), dir, r0, 0.002f, len, sides, col, true, false);
        }

        /// <summary>Шар из колец (грани плоские): центр, радиусы, число граней и колец.</summary>
        static void Ball(MeshBatch b, Vector3 c, Vector3 radii, int sides, int rings, Color32 col, System.Func<int, int, float> noise)
        {
            Ring[] r = new Ring[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / rings;
                float k = Mathf.Cos(a);
                r[i] = new Ring(c.y + Mathf.Sin(a) * radii.y, Mathf.Max(0.001f, radii.x * k), Mathf.Max(0.001f, radii.z * k), c.x, c.z);
            }
            Loft(b, r, sides, false, false, noise, null, delegate(int i, int j) { return col; });
        }

        // ------------------------------------------------------------------ Тень

        // темнее прежнего: на чёрном плаще светящиеся глаза и оскал контрастнее
        public static readonly Color32 CloakDark = new Color32(28, 24, 46, 255);
        public static readonly Color32 CloakLight = new Color32(44, 38, 72, 255);
        public static readonly Color32 CloakLining = new Color32(60, 52, 96, 255);
        static readonly Color32 ShadeBone = new Color32(112, 106, 134, 255);

        /// <summary>Плащ и туловище: ссутулившийся силуэт от рваного подола до плеч, подол двигается зубцами и лоскутами.</summary>
        public static Mesh ShadeCloak()
        {
            return Get("ShadeCloak", delegate(MeshBatch b)
            {
                Ring[] rings =
                {
                    new Ring(0.30f, 0.74f, 0.62f, 0f, -0.02f),
                    new Ring(0.62f, 0.62f, 0.52f, 0f, 0.00f),
                    new Ring(1.05f, 0.50f, 0.42f, 0f, 0.03f),
                    new Ring(1.45f, 0.46f, 0.36f, 0f, 0.08f),
                    new Ring(1.80f, 0.54f, 0.34f, 0f, 0.14f),
                    new Ring(2.03f, 0.42f, 0.29f, 0f, 0.22f),
                    new Ring(2.15f, 0.20f, 0.19f, 0f, 0.30f)
                };
                int sides = 14;
                Loft(b, rings, sides, false, true,
                    delegate(int i, int j) { return 0.93f + 0.14f * Rnd(i, j, 11) + (i == 0 ? 0.08f * Rnd(j, 3, 5) : 0f); },
                    delegate(int i, int j)
                    {
                        if (i != 0) return 0f;
                        // рваный подол: то лоскут вниз, то зубец вверх
                        return j % 2 == 0 ? -0.26f * Rnd(j, 7, 21) : 0.10f * Rnd(j, 9, 22);
                    },
                    delegate(int i, int j)
                    {
                        // складки: вертикальные полосы разной яркости; чем выше, тем светлее и холоднее (отсвет ночного неба)
                        float fold = 0.78f + 0.44f * Rnd(j, 1, 31);
                        Color32 c = Mix(CloakDark, CloakLight, i / 6f);
                        return Mul(c, fold);
                    });
                // рваный воротник: острые лоскуты вокруг шеи торчат вверх и наружу
                for (int k = 0; k < 11; k++)
                {
                    float a = k * 6.2831853f / 11f;
                    Vector3 from = new Vector3(Mathf.Cos(a) * 0.23f, 2.08f, 0.30f + Mathf.Sin(a) * 0.21f);
                    Vector3 dir = new Vector3(Mathf.Cos(a) * 0.55f, 1f, Mathf.Sin(a) * 0.55f);
                    Spike(b, from, dir, 0.04f, 0.16f + 0.14f * Rnd(k, 6, 43), 4, Mul(CloakLining, 0.85f));
                }
                // лохмотья на плечах: по три клочка, свисают наружу и вниз
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 from = new Vector3(side * (0.34f + 0.10f * k), 1.99f - 0.03f * k, 0.12f + 0.03f * k);
                        Spike(b, from, new Vector3(side * 1f, -0.9f, 0.1f), 0.05f, 0.30f + 0.12f * Rnd(k, side + 2, 44), 4, Mul(CloakDark, 1.2f));
                    }
                }
                // костяные наплечники-шипы: острые, торчат вверх и чуть назад, силуэт с «рогатыми» плечами
                for (int side = -1; side <= 1; side += 2)
                {
                    Spike(b, new Vector3(side * 0.40f, 1.98f, 0.08f), new Vector3(side * 0.45f, 1f, -0.15f), 0.06f, 0.42f, 5, ShadeBone);
                    Spike(b, new Vector3(side * 0.30f, 2.02f, -0.06f), new Vector3(side * 0.25f, 1f, -0.55f), 0.045f, 0.30f, 5, Mul(ShadeBone, 0.85f));
                }
                // хребет прорвал плащ сзади: ряд бледных шипов вдоль спины сверху вниз
                for (int k = 0; k < 6; k++)
                {
                    float y = 2.0f - 0.20f * k;
                    float z = Mathf.Lerp(-0.10f, -0.38f, k / 5f);
                    Spike(b, new Vector3(0f, y, z), new Vector3(0f, 0.55f, -1f), 0.045f - 0.004f * k, 0.26f - 0.02f * k, 5, Mul(ShadeBone, 1f - 0.05f * k));
                }
                // лоскуты по подолу: длинные узкие треугольники, подкладка светлее
                for (int j = 0; j < sides; j++)
                {
                    float a = (j + 0.5f) * 6.2831853f / sides;
                    float len = 0.35f + 0.55f * Rnd(j, 5, 41);
                    Vector3 top = new Vector3(Mathf.Cos(a) * 0.70f, 0.34f, -0.02f + Mathf.Sin(a) * 0.58f);
                    Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.13f;
                    Vector3 tip = top + new Vector3(Mathf.Cos(a) * 0.08f, -len, Mathf.Sin(a) * 0.06f);
                    b.Tri(top - tangent, tip, top + tangent, Mul(CloakLining, 0.8f));
                    b.Tri(top + tangent, tip, top - tangent, Mul(CloakDark, 0.9f));
                }
            });
        }

        /// <summary>Капюшон: высокий, загнутый назад остриём, спереди тёмная пустота (грани цветом почти чёрные).</summary>
        public static Mesh ShadeHood()
        {
            return Get("ShadeHood", delegate(MeshBatch b)
            {
                Ring[] rings =
                {
                    new Ring(2.02f, 0.31f, 0.29f, 0f, 0.26f),
                    new Ring(2.26f, 0.31f, 0.31f, 0f, 0.35f),
                    new Ring(2.50f, 0.26f, 0.29f, 0f, 0.38f),
                    new Ring(2.70f, 0.16f, 0.21f, 0f, 0.30f),
                    new Ring(2.84f, 0.05f, 0.08f, 0f, 0.17f),
                    new Ring(2.90f, 0.012f, 0.02f, 0f, 0.05f)
                };
                int sides = 12;
                Loft(b, rings, sides, false, false,
                    delegate(int i, int j) { return 0.96f + 0.08f * Rnd(i, j, 51); },
                    null,
                    delegate(int i, int j)
                    {
                        float a = (j + 0.5f) * 6.2831853f / sides;
                        bool front = Mathf.Sin(a) > 0.55f && i < 4;   // лицо смотрит в +Z
                        if (front) return C(2, 2, 7);
                        return Mul(Mix(CloakDark, CloakLight, 0.55f), 0.8f + 0.4f * Rnd(j, i, 52));
                    });
                // кайма капюшона: тёмный обод вокруг лицевого провала
                for (int j = -3; j <= 3; j++)
                {
                    float a = 1.5707963f + j * 0.19f;
                    Vector3 p0 = new Vector3(Mathf.Cos(a) * 0.30f, 2.36f, 0.35f + Mathf.Sin(a) * 0.30f);
                    b.Box(p0, new Vector3(0.05f, 0.34f, 0.04f), Mul(CloakDark, 0.7f));
                }
                // венец шипов по верхнему ободу капюшона: рваный остроконечный силуэт, виден со всех сторон
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 6.2831853f / 8f + 0.2f;
                    Vector3 from = new Vector3(Mathf.Cos(a) * 0.20f, 2.62f, 0.33f + Mathf.Sin(a) * 0.22f);
                    Spike(b, from, new Vector3(Mathf.Cos(a) * 0.7f, 1f, Mathf.Sin(a) * 0.7f), 0.035f, 0.20f + 0.12f * Rnd(k, 8, 53), 4, Mul(CloakLining, 0.8f));
                }
            });
        }

        /// <summary>Глаза Тени: две крупные косые пластины у лицевого провала (светятся, материал без освещения).</summary>
        public static Mesh ShadeEyes()
        {
            return Get("ShadeEyes", delegate(MeshBatch b)
            {
                Color32 white = C(255, 255, 255);
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * 0.112f;
                    Vector3 c = new Vector3(x, 2.41f, 0.690f);   // перед лицевым провалом капюшона (его передняя стенка на z ~ 0.65), иначе глаза внутри и не видны
                    // крупный миндалевидный глаз с резким злым наклоном: внутренний уголок заметно ниже внешнего; бледный, пустой. Двусторонний
                    Vector3 inner = c + new Vector3(-side * 0.082f, -0.048f, 0f);
                    Vector3 top = c + new Vector3(side * 0.012f, 0.058f, 0f);
                    Vector3 outer = c + new Vector3(side * 0.104f, 0.046f, 0f);
                    Vector3 low = c + new Vector3(side * 0.020f, -0.034f, 0f);
                    b.Quad(inner, top, outer, low, white);
                    b.Quad(low, outer, top, inner, white);
                }
            });
        }

        /// <summary>
        /// Оскал Тени: светящаяся зазубренная улыбка в чёрной пустоте капюшона (пила из треугольников, зубы сверху и снизу вразбежку, края выше
        /// середины). Построен вокруг своего центра, чтобы в нападении раскрываться по высоте. Материал без освещения, как у глаз.
        /// </summary>
        public static Mesh ShadeGrin()
        {
            return Get("ShadeGrin", delegate(MeshBatch b)
            {
                Color32 white = C(255, 255, 255);
                int n = 10;
                float halfW = 0.17f;
                float h = 0.030f;
                for (int i = 0; i < n; i++)
                {
                    float x0 = -halfW + 2f * halfW * i / n;
                    float x1 = -halfW + 2f * halfW * (i + 1) / n;
                    float xm = (x0 + x1) * 0.5f;
                    float y0 = 1.9f * x0 * x0;
                    float y1 = 1.9f * x1 * x1;
                    float ym = 1.9f * xm * xm;
                    Vector3 p0;
                    Vector3 p1;
                    Vector3 tip;
                    if (i % 2 == 0)
                    {
                        // зуб сверху вниз: основание на верхней кромке, остриё на нижней
                        p0 = new Vector3(x0, y0 + h, 0f);
                        p1 = new Vector3(x1, y1 + h, 0f);
                        tip = new Vector3(xm, ym - h, 0f);
                    }
                    else
                    {
                        p0 = new Vector3(x0, y0 - h, 0f);
                        p1 = new Vector3(x1, y1 - h, 0f);
                        tip = new Vector3(xm, ym + h, 0f);
                    }
                    b.Tri(p0, tip, p1, white);
                    b.Tri(p0, p1, tip, white);
                }
            });
        }

        /// <summary>Обрывки Тьмы вокруг Тени: восемь тёмных осколков-ромбов на разной высоте и расстоянии (EnemyRig вращает их вокруг неё).</summary>
        public static Mesh ShadeShards()
        {
            return Get("ShadeShards", delegate(MeshBatch b)
            {
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 2.3999f;
                    float r = 0.62f + 0.28f * Rnd(k, 1, 181);
                    float y = 0.5f + 1.9f * Rnd(k, 2, 182);
                    float s = 0.05f + 0.06f * Rnd(k, 3, 183);
                    Vector3 c = new Vector3(Mathf.Cos(a) * r, y, 0.15f + Mathf.Sin(a) * r * 0.8f);
                    Ball(b, c, new Vector3(s * 0.7f, s * 1.7f, s * 0.5f), 4, 2, Mul(CloakLining, 0.7f + 0.5f * Rnd(k, 4, 184)), null);
                }
            });
        }

        /// <summary>Плечо Тени (от плеча вниз, начало в плече): рукав с отворотом; side -1 — левая, +1 — правая.</summary>
        public static Mesh ShadeUpperArm(int side)
        {
            return Get("ShadeUpper" + side, delegate(MeshBatch b)
            {
                Color32 sleeve = Mul(CloakLight, 0.95f);
                b.Frustum(new Vector3(0f, -0.34f, 0f), Vector3.down, 0.085f, 0.062f, 0.68f, 7, sleeve, false, true);
                b.Box(new Vector3(0f, 0.0f, 0f), new Vector3(0.20f, 0.17f, 0.20f), Mul(CloakDark, 1.1f));   // накладка на плече
                b.Frustum(new Vector3(0f, -0.64f, 0f), Vector3.down, 0.07f, 0.075f, 0.10f, 7, Mul(CloakLining, 0.8f), false, false);
                // костяной шип на локте, острие назад и вниз
                Spike(b, new Vector3(0f, -0.68f, -0.04f), new Vector3(0f, -0.35f, -1f), 0.04f, 0.20f, 5, ShadeBone);
            });
        }

        /// <summary>Предплечье и кисть Тени (от локтя вниз): тонкое запястье, пятипалая кисть с длинными когтями.</summary>
        public static Mesh ShadeForeArm(int side)
        {
            return Get("ShadeFore" + side, delegate(MeshBatch b)
            {
                Color32 sleeve = Mul(CloakLight, 0.9f);
                Color32 skin = C(58, 54, 78);
                Color32 claw = C(176, 176, 204);
                b.Frustum(new Vector3(0f, -0.36f, 0f), Vector3.down, 0.068f, 0.040f, 0.72f, 7, sleeve, false, false);
                // широкий рваный манжет у запястья
                b.Frustum(new Vector3(0f, -0.66f, 0f), Vector3.down, 0.04f, 0.095f, 0.14f, 7, Mul(CloakDark, 1.2f), false, true);
                // кисть: ладонь и пять очень длинных суставчатых пальцев веером; на концах изогнутые вперёд когти
                b.Box(new Vector3(0f, -0.80f, 0.01f), new Vector3(0.12f, 0.10f, 0.05f), skin);
                for (int f = 0; f < 4; f++)
                {
                    float spread = (f - 1.5f) * 0.045f;
                    float len1 = 0.19f + 0.05f * Rnd(f, side + 2, 61);
                    Vector3 s0 = new Vector3(spread, -0.84f, 0.01f);
                    Vector3 s1 = s0 + new Vector3(spread * 0.9f, -len1, 0.035f);
                    Vector3 s2 = s1 + new Vector3(spread * 0.5f, -0.17f, 0.08f);
                    b.Rod(s0, s1, 0.011f, 5, skin);
                    b.Rod(s1, s2, 0.008f, 5, skin);
                    Horn(b, s2, (s2 - s1) + new Vector3(0f, -0.04f, 0.10f), new Vector3(0f, -0.20f, 1.0f), 0.009f, 0.16f, 3, claw);
                }
                // большой палец в сторону
                Vector3 t0 = new Vector3(-side * 0.06f, -0.80f, 0.02f);
                Vector3 t1 = t0 + new Vector3(-side * 0.08f, -0.10f, 0.05f);
                b.Rod(t0, t1, 0.012f, 5, skin);
                Horn(b, t1, new Vector3(-side * 0.5f, -1f, 0.8f), new Vector3(0f, -0.20f, 1.0f), 0.009f, 0.13f, 3, claw);
            });
        }

        /// <summary>Лоскут плаща сзади (от крепления вниз): три звена рваной ленты, ширина к концу сужается; двусторонний.</summary>
        public static Mesh ShadeTail(int variant)
        {
            return Get("ShadeTail" + variant, delegate(MeshBatch b)
            {
                // три нижних ленты длинные, две верхние (от лопаток) короче: висят в воздухе, как обрывки плаща
                float len = variant < 3 ? 1.15f + 0.2f * variant : 1.00f + 0.12f * (variant - 3);
                int links = 4;
                Vector3[] left = new Vector3[links + 1];
                Vector3[] right = new Vector3[links + 1];
                for (int k = 0; k <= links; k++)
                {
                    float u = k / (float)links;
                    float w = Mathf.Lerp(0.20f, 0.035f, u) * (k == 0 ? 1f : 0.8f + 0.4f * Rnd(k, variant, 71));
                    float z = -0.30f * u * u + 0.05f * Mathf.Sin(u * 5f + variant);   // лента изогнута назад
                    left[k] = new Vector3(-w, -len * u, z);
                    right[k] = new Vector3(w, -len * u, z);
                }
                for (int k = 0; k < links; k++)
                {
                    Color32 c = Mul(CloakDark, 0.9f + 0.2f * Rnd(k, variant, 72));
                    b.Quad(left[k], left[k + 1], right[k + 1], right[k], c);
                    b.Quad(right[k], right[k + 1], left[k + 1], left[k], Mul(CloakLining, 0.7f));
                }
            });
        }

        // ------------------------------------------------------------------ Гаситель

        /// <summary>Шипы Гасителя: ёж из тонких чёрных шипов разной длины по сфере (спираль Фибоначчи) вокруг пустоты, где светится ядро.</summary>
        public static Mesh SnufferSpikes()
        {
            return Get("SnufferSpikes", delegate(MeshBatch b)
            {
                int n = 34;
                float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
                for (int i = 0; i < n; i++)
                {
                    float y = 1f - (i + 0.5f) * 2f / n;
                    float rad = Mathf.Sqrt(1f - y * y);
                    float a = i * golden;
                    Vector3 dir = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    float len = 0.20f + 0.18f * Rnd(i, 3, 81) + (i % 5 == 0 ? 0.10f : 0f);
                    Color32 col = Mix(C(20, 14, 26), C(52, 18, 20), Rnd(i, 4, 82));
                    Spike(b, dir * 0.14f, dir, 0.032f + 0.012f * Rnd(i, 5, 83), len, 5, col);
                }
            });
        }

        /// <summary>Ядро Гасителя: многогранный шарик (светится, материал без освещения).</summary>
        public static Mesh SnufferCore()
        {
            return Get("SnufferCore", delegate(MeshBatch b)
            {
                Ball(b, Vector3.zero, new Vector3(0.15f, 0.15f, 0.15f), 8, 5, C(255, 255, 255), delegate(int i, int j) { return 0.94f + 0.12f * Rnd(i, j, 91); });
            });
        }

        /// <summary>Искра: маленький октаэдр.</summary>
        public static Mesh Spark()
        {
            return Get("Spark", delegate(MeshBatch b)
            {
                Ball(b, Vector3.zero, new Vector3(0.025f, 0.035f, 0.025f), 4, 2, C(255, 255, 255), null);
            });
        }

        // ------------------------------------------------------------------ Пожиратель

        public static readonly Color32 FleshDark = new Color32(30, 18, 46, 255);
        public static readonly Color32 FleshLight = new Color32(52, 30, 72, 255);
        static readonly Color32 Bone = new Color32(190, 178, 160, 255);
        static readonly Color32 BoneDark = new Color32(124, 108, 94, 255);

        // Пасть плоская, спереди туши. Контур — суперэллипс: шире и «квадратнее» овала, углы пасти уходят в оскал.
        public const float MouthCy = 1.62f;
        public const float MouthCz = 1.98f;
        public const float MouthW = 1.50f;
        const float MouthH = 0.74f;

        /// <summary>Точка контура пасти (x, y от её центра) под углом a; k — масштаб (1 — кромка).</summary>
        static Vector2 MouthPoint(float a, float k)
        {
            float c = Mathf.Cos(a);
            float s = Mathf.Sin(a);
            const float e = 2f / 2.7f;
            return new Vector2(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e) * MouthW * k, Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e) * MouthH * k);
        }

        /// <summary>Четырёхугольник лицом наружу от точки inside (для выпуклых тел: порядок вершин заранее неизвестен).</summary>
        static void OutQuad(MeshBatch b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 inside, Color32 col)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 mid = (p0 + p1 + p2 + p3) * 0.25f;
            if (Vector3.Dot(n, mid - inside) >= 0f) b.Quad(p0, p1, p2, p3, col);
            else b.Quad(p3, p2, p1, p0, col);
        }

        static void OutTri(MeshBatch b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 inside, Color32 col)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 mid = (p0 + p1 + p2) / 3f;
            if (Vector3.Dot(n, mid - inside) >= 0f) b.Tri(p0, p1, p2, col);
            else b.Tri(p0, p2, p1, col);
        }

        /// <summary>Призма: выпуклый многоугольник (x, y) выдавлен по Z от z0 до z1, центр координат — origin; грани смотрят наружу.</summary>
        static void Slab(MeshBatch b, Vector3 origin, Vector2[] poly, float z0, float z1, Color32 side, Color32 front)
        {
            int n = poly.Length;
            Vector2 cm = Vector2.zero;
            for (int i = 0; i < n; i++) cm += poly[i] / n;
            Vector3 inside = origin + new Vector3(cm.x, cm.y, (z0 + z1) * 0.5f);
            Vector3 cf = origin + new Vector3(cm.x, cm.y, z1);
            Vector3 cb = origin + new Vector3(cm.x, cm.y, z0);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a0 = origin + new Vector3(poly[i].x, poly[i].y, z0);
                Vector3 a1 = origin + new Vector3(poly[j].x, poly[j].y, z0);
                Vector3 b1 = origin + new Vector3(poly[j].x, poly[j].y, z1);
                Vector3 b0 = origin + new Vector3(poly[i].x, poly[i].y, z1);
                OutQuad(b, a0, a1, b1, b0, inside, side);
                OutTri(b, cf, b0, b1, inside, front);
                OutTri(b, cb, a1, a0, inside, side);
            }
        }

        /// <summary>Изогнутый шип или рог из segs усечённых конусов: направление dir плавно заворачивает к bend. Возвращает точки стыков звеньев.</summary>
        static Vector3[] Horn(MeshBatch b, Vector3 from, Vector3 dir, Vector3 bend, float r0, float len, int segs, Color32 col)
        {
            dir.Normalize();
            Vector3[] pts = new Vector3[segs + 1];
            pts[0] = from;
            Vector3 p = from;
            float r = r0;
            float segLen = len / segs;
            for (int s = 0; s < segs; s++)
            {
                Vector3 d = (dir + bend * ((s + 0.5f) / segs)).normalized;
                float r1 = s == segs - 1 ? 0.003f : Mathf.Lerp(r0, 0.003f, (s + 1f) / segs);
                b.Frustum(p + d * (segLen * 0.5f), d, r, r1, segLen * 1.05f, 6, Mul(col, 1f - 0.05f * s), s == 0, false);
                p += d * segLen;
                pts[s + 1] = p;
                r = r1;
            }
            return pts;
        }

        /// <summary>Зуб из двух звеньев: основание жёлтое и тёмное у дёсен, острие светлое и чуть загнутое (bend прибавляется к направлению у острия).</summary>
        static void Tooth(MeshBatch b, Vector3 from, Vector3 dir, Vector3 bend, float r0, float len, int variant)
        {
            Color32 root = Mul(C(196, 168, 118), 0.82f + 0.30f * Rnd(variant, 1, 151));
            Color32 tip = Mul(C(238, 232, 214), 0.88f + 0.20f * Rnd(variant, 2, 152));
            dir.Normalize();
            Vector3 mid = from + dir * (len * 0.5f);
            Vector3 d2 = (dir + bend).normalized;
            b.Frustum(from + dir * (len * 0.25f), dir, r0, r0 * 0.62f, len * 0.52f, 5, root, true, false);
            b.Frustum(mid + d2 * (len * 0.25f), d2, r0 * 0.62f, 0.003f, len * 0.52f, 5, tip, false, false);
        }

        static readonly Ring[] EaterRings =
        {
            new Ring(0.55f, 1.50f, 1.30f, 0f, 0f),
            new Ring(1.10f, 2.00f, 1.75f, 0f, 0.02f),
            new Ring(1.85f, 2.35f, 2.00f, 0f, 0.05f),
            new Ring(2.70f, 2.20f, 1.90f, 0f, 0.10f),
            new Ring(3.50f, 1.70f, 1.50f, 0f, 0.12f),
            new Ring(4.20f, 1.00f, 0.95f, 0f, 0.10f),
            new Ring(4.55f, 0.30f, 0.30f, 0f, 0.08f)
        };
        const int EaterSides = 18;
        // Лобовая стенка туши в зоне пасти не выходит вперёд плоскости глотки: раньше выпуклая грань (шум радиуса до +16%) торчала сквозь
        // диски глотки тёмно-синим куском посреди пасти. Глотка начинается на MouthCz - 0.05, стенка кончается на 7 см позади неё.
        const float FaceLimit = MouthCz - 0.12f;

        /// <summary>Множитель радиуса вершины туши (шум); в зоне пасти ограничен так, чтобы стенка не торчала сквозь глотку.</summary>
        static float EaterScale(int i, int j)
        {
            float m = 0.88f + 0.22f * Rnd(i, j, 101) + 0.06f * Mathf.Sin(j * 1.7f + i);
            Ring r = EaterRings[i];
            float a = j * 6.2831853f / EaterSides;
            float s = Mathf.Sin(a);
            if (s > 0.3f && r.Y > 0.7f && r.Y < 3.0f && Mathf.Abs(Mathf.Cos(a)) * r.Rx * m < 2.0f)
            {
                float front = r.Cz + s * r.Rz * m;
                if (front > FaceLimit) m = (FaceLimit - r.Cz) / (s * r.Rz);
            }
            return m;
        }

        /// <summary>На сколько самая выдающаяся вперёд вершина туши в зоне пасти выходит за FaceLimit, метры (не больше нуля: стенка позади глотки); для проверки.</summary>
        public static float EaterFaceOverhang()
        {
            float worst = -10f;
            for (int i = 0; i < EaterRings.Length; i++)
            {
                Ring r = EaterRings[i];
                if (r.Y <= 0.7f || r.Y >= 3.0f) continue;
                for (int j = 0; j < EaterSides; j++)
                {
                    float a = j * 6.2831853f / EaterSides;
                    float s = Mathf.Sin(a);
                    float m = EaterScale(i, j);
                    if (s <= 0.3f || Mathf.Abs(Mathf.Cos(a)) * r.Rx * m >= 2.0f) continue;
                    worst = Mathf.Max(worst, r.Cz + s * r.Rz * m - FaceLimit);
                }
            }
            return worst;
        }

        /// <summary>
        /// Туша: бугристая масса с рубцами и тёмными жилами, хребет шипов, бычьи рога дугой, рёбра торчат из боков, венец шипов, бородавки;
        /// спереди широкая впадина пасти (губа и зубы верхней челюсти — часть меша).
        /// </summary>
        public static Mesh EaterBody()
        {
            return Get("EaterBody", delegate(MeshBatch b)
            {
                Ring[] rings = EaterRings;
                int sides = EaterSides;
                Color32 scar = C(120, 106, 134);
                Loft(b, rings, sides, true, true,
                    EaterScale,
                    null,
                    delegate(int i, int j)
                    {
                        // кожа: поперечные складки (тёмные и светлые пояса), пятна по шуму, тёмные жилы и бледные рубцы
                        float band = (i % 2 == 0) ? 0.85f : 1.1f;
                        float spot = 0.8f + 0.4f * Rnd(j, i, 102);
                        Color32 c = Mul(Mix(FleshDark, FleshLight, i / 6f), band * spot);
                        float r = Rnd(j, i, 103);
                        if (r > 0.88f) c = Mul(c, 0.45f);
                        else if (r < 0.05f && i >= 2) c = Mix(c, scar, 0.40f);
                        return c;
                    });
                // хребет: ряд шипов вдоль спины и макушки, бледнеют к концам
                for (int k = 0; k < 11; k++)
                {
                    float u = k / 10f;
                    Vector3 from = new Vector3(0f, Mathf.Lerp(3.95f, 1.6f, u), Mathf.Lerp(-0.25f, -1.9f, u));
                    Vector3 dir = new Vector3(0f, 1f - u * 0.4f, -0.5f - u);
                    Spike(b, from, dir, 0.15f - 0.05f * u, 0.62f - 0.18f * u + 0.12f * Rnd(k, 3, 161), 6, Mix(FleshLight, Bone, 0.45f));
                }
                // венец шипов вокруг макушки
                for (int k = 0; k < 9; k++)
                {
                    float a = k * 6.2831853f / 9f + 0.3f;
                    Vector3 from = new Vector3(Mathf.Cos(a) * 1.05f, 3.95f, 0.10f + Mathf.Sin(a) * 0.92f);
                    Spike(b, from, new Vector3(Mathf.Cos(a) * 0.6f, 1f, Mathf.Sin(a) * 0.6f), 0.07f, 0.30f + 0.18f * Rnd(k, 13, 163), 5, Mix(FleshLight, Bone, 0.5f));
                }
                Color32 horn = C(84, 66, 84);
                for (int side = -1; side <= 1; side += 2)
                {
                    // большие рога бычьей дугой: вверх и наружу, потом вперёд и внутрь; сзади рожки поменьше
                    Horn(b, new Vector3(side * 1.00f, 3.85f, 0.55f), new Vector3(side * 0.55f, 1f, -0.05f), new Vector3(-side * 0.75f, -0.35f, 1.05f), 0.26f, 1.7f, 5, horn);
                    Horn(b, new Vector3(side * 0.55f, 4.05f, -0.15f), new Vector3(side * 0.30f, 1f, -0.35f), new Vector3(-side * 0.2f, 0f, 0.5f), 0.13f, 0.75f, 3, horn);
                    // рёбра прорвали бока: бледные костяные дуги, загнутые назад и вверх
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 from = new Vector3(side * (2.15f - 0.05f * k), 2.05f + 0.50f * k, 0.55f - 0.65f * k);
                        Horn(b, from, new Vector3(side * 1f, 0.25f, -0.35f), new Vector3(0f, 0.55f, -0.9f), 0.085f - 0.012f * k, 0.95f - 0.12f * k, 3, Bone);
                    }
                }
                // бородавчатые наросты по бокам
                for (int k = 0; k < 14; k++)
                {
                    float a = k * 2.3999f;
                    float y = 0.9f + 3.0f * Rnd(k, 1, 111);
                    float rad = Mathf.Lerp(1.8f, 2.2f, 1f - Mathf.Abs(y - 2.2f) / 2f) * 0.93f;
                    Vector3 c = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad * 0.88f);
                    if (c.z > 0.9f && y > 0.9f && y < 2.9f) continue;   // не на пасти
                    Ball(b, c, new Vector3(0.22f, 0.2f, 0.22f), 6, 3, Mul(FleshLight, 1.1f), null);
                }
                BuildMouth(b);
            });
        }

        static void BuildMouth(MeshBatch b)
        {
            Color32 gum = C(104, 18, 34);
            Color32 lip = C(70, 28, 62);
            float cy = MouthCy;
            float cz = MouthCz;
            // губа: неровное кольцо брусков по контуру, часть бледные струпья
            int seg = 28;
            for (int j = 0; j < seg; j++)
            {
                float a = j * 6.2831853f / seg;   // кирпич ровно на оси: сверху и снизу по центру нет щели, в которой торчал красный диск глотки
                Vector2 p = MouthPoint(a, 1.04f);
                Color32 c = Mul(lip, 0.78f + 0.44f * Rnd(j, 4, 123));
                if (Rnd(j, 5, 124) > 0.82f) c = Mix(c, C(128, 112, 124), 0.4f);
                b.Box(new Vector3(p.x, cy + p.y, cz + 0.02f + 0.05f * Rnd(j, 6, 125)),
                    new Vector3(0.24f + 0.16f * Rnd(j, 2, 121), 0.17f + 0.10f * Rnd(j, 3, 122), 0.20f), c);
            }
            // дёсны под верхними зубами
            for (int k = 0; k < 18; k++)
            {
                float a = Mathf.Lerp(0.10f, Mathf.PI - 0.10f, (k + 0.5f) / 18f);
                Vector2 p = MouthPoint(a, 0.97f);
                b.Box(new Vector3(p.x, cy + p.y - 0.04f, cz + 0.05f), new Vector3(0.20f, 0.10f, 0.12f), Mul(gum, 0.85f + 0.30f * Rnd(k, 7, 126)));
            }
            // верхние зубы: два десятка клыков разной длины, каждый шестой длинный; загнуты к центру пасти
            for (int k = 0; k < 24; k++)
            {
                float a = Mathf.Lerp(0.10f, Mathf.PI - 0.10f, (k + 0.5f) / 24f);
                Vector2 p = MouthPoint(a, 0.95f);
                bool fang = k % 6 == 2;
                float len = 0.22f + 0.28f * Rnd(k, 8, 127) + (fang ? 0.42f : 0f);
                float r0 = 0.050f + 0.022f * Rnd(k, 9, 128) + (fang ? 0.03f : 0f);
                Tooth(b, new Vector3(p.x, cy + p.y, cz + 0.07f), new Vector3(-p.x * 0.04f, -1f, 0.16f), new Vector3(-Mathf.Sign(p.x) * 0.1f, 0.25f, 0.55f), r0, len, k);
            }
            // внутренние ряды: мелкие зубы по краю глотки остриями в центр (как у миноги), глубже ещё меньше; сверху их нет, там висят клыки
            for (int ring = 0; ring < 2; ring++)
            {
                int n = ring == 0 ? 22 : 14;
                float k0 = ring == 0 ? 0.70f : 0.42f;
                for (int k = 0; k < n; k++)
                {
                    float a = (k + 0.5f) * 6.2831853f / n + ring * 0.2f;
                    if (Mathf.Sin(a) > 0.45f) continue;
                    Vector2 p = MouthPoint(a, k0);
                    Vector3 from = new Vector3(p.x, cy + p.y, cz + 0.05f + 0.05f * ring);
                    float len = (0.22f + 0.10f * Rnd(k, 10 + ring, 129)) * (ring == 0 ? 1f : 0.75f);
                    Tooth(b, from, new Vector3(-p.x, -p.y * 1.4f, 0.55f + 0.1f * ring), new Vector3(0f, 0f, 0.5f), 0.045f - 0.012f * ring, len, 40 * ring + k);
                }
            }
        }

        /// <summary>
        /// Глотка Пожирателя: стопка дисков по контуру пасти от красного края к чёрному центру, светлее и темнее по очереди (рёбра пищевода).
        /// Светится слабо (материал без освещения, цвет множится на цвет вершин): в темноте пасть видна как тлеющая бездна за зубами.
        /// </summary>
        public static Mesh EaterThroat()
        {
            return Get("EaterThroat", delegate(MeshBatch b)
            {
                float[] scale = { 1.00f, 0.84f, 0.68f, 0.52f, 0.37f, 0.23f, 0.11f };
                int[] shade = { 235, 110, 170, 75, 120, 40, 15 };
                int n = 32;
                for (int i = 0; i < scale.Length; i++)
                {
                    Color32 col = C(shade[i], shade[i], shade[i]);
                    float z = MouthCz - 0.05f + 0.022f * i;
                    Vector3 c = new Vector3(0f, MouthCy, z);
                    for (int j = 0; j < n; j++)
                    {
                        Vector2 p0 = MouthPoint(j * 6.2831853f / n, scale[i] * 0.99f);
                        Vector2 p1 = MouthPoint((j + 1) * 6.2831853f / n, scale[i] * 0.99f);
                        Vector3 v0 = new Vector3(p0.x, MouthCy + p0.y, z);
                        Vector3 v1 = new Vector3(p1.x, MouthCy + p1.y, z);
                        b.Tri(c, v0, v1, col);   // лицом вперёд
                        b.Tri(c, v1, v0, col);   // и назад: диск видно и сбоку
                    }
                }
            });
        }

        /// <summary>Нижняя челюсть: дуга по нижнему краю пасти с рядом зубов вверх (два длинных клыка); начало — в шарнире у задней кромки пасти.</summary>
        public static Mesh EaterJaw()
        {
            return Get("EaterJaw", delegate(MeshBatch b)
            {
                Color32 lip = C(70, 28, 62);
                Color32 gum = C(104, 18, 34);
                // шарнир в (0, 0, 0); челюсть вперёд вдоль +Z и вниз
                int seg = 14;
                for (int k = 0; k < seg; k++)
                {
                    float a0 = Mathf.Lerp(Mathf.PI, 2f * Mathf.PI, k / (float)seg);
                    float a1 = Mathf.Lerp(Mathf.PI, 2f * Mathf.PI, (k + 1) / (float)seg);
                    Vector2 m0 = MouthPoint(a0, 1.0f);
                    Vector2 m1 = MouthPoint(a1, 1.0f);
                    Vector3 p0 = new Vector3(m0.x, m0.y - 0.02f, 0.92f);
                    Vector3 p1 = new Vector3(m1.x, m1.y - 0.02f, 0.92f);
                    Vector3 q0 = new Vector3(p0.x * 0.8f, p0.y - 0.30f, 0.30f);
                    Vector3 q1 = new Vector3(p1.x * 0.8f, p1.y - 0.30f, 0.30f);
                    b.Quad(p0, q0, q1, p1, Mix(gum, lip, 0.3f));
                    b.Quad(p1, q1, q0, p0, lip);
                    b.Box((p0 + p1) * 0.5f + new Vector3(0f, -0.06f, 0.04f), new Vector3(0.26f + 0.10f * Rnd(k, 3, 131), 0.18f, 0.20f), Mul(lip, 0.85f + 0.30f * Rnd(k, 4, 132)));
                }
                for (int k = 0; k < 18; k++)
                {
                    float a = Mathf.Lerp(Mathf.PI + 0.12f, 2f * Mathf.PI - 0.12f, (k + 0.5f) / 18f);
                    Vector2 p = MouthPoint(a, 0.95f);
                    bool fang = k == 4 || k == 13;
                    float len = 0.20f + 0.24f * Rnd(k, 11, 133) + (fang ? 0.40f : 0f);
                    float r0 = 0.048f + 0.020f * Rnd(k, 12, 134) + (fang ? 0.03f : 0f);
                    Tooth(b, new Vector3(p.x, p.y, 0.93f), new Vector3(-p.x * 0.05f, 1f, -0.08f), new Vector3(-Mathf.Sign(p.x) * 0.1f, -0.05f, 0.6f), r0, len, 100 + k);
                }
            });
        }

        /// <summary>Жвало у угла пасти (side: -1 левое, +1 правое): толстый изогнутый рог вперёд и внутрь с шипами по внутренней кромке; начало — в шарнире.</summary>
        public static Mesh EaterMandible(int side)
        {
            return Get("EaterMandible" + side, delegate(MeshBatch b)
            {
                // бледная пожелтевшая кость: тёмные жвала сливались с тушей и не читались
                Color32 chitin = C(168, 150, 124);
                // клыки-жвала загибаются вперёд, вниз и внутрь, как у кабана: спереди это бивни по бокам пасти, а не планка поперёк лица
                Vector3[] pts = Horn(b, Vector3.zero, new Vector3(side * 0.30f, -0.12f, 1f), new Vector3(-side * 0.85f, -0.60f, 0.15f), 0.22f, 1.35f, 5, chitin);
                for (int k = 1; k < pts.Length - 1; k++)
                    Spike(b, pts[k], new Vector3(-side * 1f, -0.12f, 0.45f), 0.05f, 0.24f + 0.05f * k, 5, Bone);
                Spike(b, pts[1], new Vector3(side * 1f, 0.4f, 0.2f), 0.06f, 0.26f, 5, BoneDark);
                Spike(b, pts[2], new Vector3(side * 1f, 0.5f, 0.1f), 0.05f, 0.22f, 5, BoneDark);
            });
        }

        /// <summary>Слюна: три тонкие нити с каплей на конце, свисают с верхней челюсти (начало — у кромки пасти).</summary>
        public static Mesh EaterDrool()
        {
            return Get("EaterDrool", delegate(MeshBatch b)
            {
                Color32 wet = C(196, 206, 192);
                float[] xs = { -0.62f, 0.18f, 0.85f };
                float[] ls = { 0.50f, 0.72f, 0.40f };
                for (int k = 0; k < xs.Length; k++)
                {
                    Vector3 top = new Vector3(xs[k], 0f, 0f);
                    Vector3 end = top + new Vector3(0.02f * (k - 1), -ls[k], 0.03f);
                    b.Rod(top, end, 0.011f, 4, wet);
                    Ball(b, end + new Vector3(0f, -0.03f, 0f), new Vector3(0.028f, 0.040f, 0.028f), 5, 3, Mul(wet, 1.1f), null);
                }
            });
        }

        /// <summary>Звено щупальца (от основания вдоль +Z): конус с присосками снизу, шипами-зацепами сверху и по бокам; на конце коготь.</summary>
        public static Mesh EaterTentacle(int link)
        {
            return Get("EaterTentacle" + link, delegate(MeshBatch b)
            {
                float[] r0 = { 0.42f, 0.32f, 0.24f, 0.16f };
                float[] r1 = { 0.32f, 0.24f, 0.16f, 0.06f };
                float[] ln = { 0.95f, 0.85f, 0.75f, 0.65f };
                Color32 c = Mix(FleshDark, FleshLight, 0.35f + 0.1f * link);
                b.Frustum(new Vector3(0f, 0f, ln[link] * 0.5f), Vector3.forward, r0[link], r1[link], ln[link], 8, c, false, link == 3);
                // присоски: ряд светлых бугорков снизу
                for (int k = 0; k < 3; k++)
                {
                    float u = (k + 0.7f) / 3.4f;
                    float r = Mathf.Lerp(r0[link], r1[link], u);
                    b.Box(new Vector3(0f, -r * 0.95f, ln[link] * u), new Vector3(r * 0.9f, r * 0.34f, 0.16f), C(120, 74, 112));
                }
                // шипы-зацепы: острия назад, чтобы щупальце цеплялось за добычу
                for (int k = 0; k < 2; k++)
                {
                    float u = 0.28f + 0.38f * k;
                    float r = Mathf.Lerp(r0[link], r1[link], u);
                    float z = ln[link] * u;
                    float sl = 0.13f + 0.03f * (3 - link);
                    Spike(b, new Vector3(0f, r * 0.85f, z), new Vector3(0f, 0.9f, -0.55f), 0.034f + 0.006f * (3 - link), sl, 4, Bone);
                    if (link < 3)
                    {
                        Spike(b, new Vector3(r * 0.85f, 0f, z), new Vector3(0.9f, 0.1f, -0.5f), 0.030f, sl * 0.8f, 4, BoneDark);
                        Spike(b, new Vector3(-r * 0.85f, 0f, z), new Vector3(-0.9f, 0.1f, -0.5f), 0.030f, sl * 0.8f, 4, BoneDark);
                    }
                }
                if (link == 3) Horn(b, new Vector3(0f, 0f, ln[3]), new Vector3(0f, 0.1f, 1f), new Vector3(0f, -1.2f, 0f), 0.05f, 0.34f, 3, Bone);
            });
        }

        static Vector3 EyeSurf(float x, float y)
        {
            float t = 1f - (x / 0.27f) * (x / 0.27f) - (y / 0.30f) * (y / 0.30f);
            return new Vector3(x, y, 0.20f * Mathf.Sqrt(Mathf.Max(0f, t)) + 0.012f);
        }

        /// <summary>Глазное яблоко Пожирателя: бледный шар (светится, материал без освещения) с красными прожилками к краям.</summary>
        public static Mesh EaterEye()
        {
            return Get("EaterEyeball", delegate(MeshBatch b)
            {
                Ball(b, Vector3.zero, new Vector3(0.27f, 0.30f, 0.20f), 12, 7, C(255, 255, 255), null);
                Color32 red = C(235, 70, 50);
                for (int k = 0; k < 10; k++)
                {
                    float side = k < 5 ? -1f : 1f;
                    float y0 = (k % 5 - 2) * 0.10f;
                    Vector3 p0 = EyeSurf(side * 0.255f, y0);
                    Vector3 p1 = EyeSurf(side * (0.12f + 0.03f * Rnd(k, 1, 141)), y0 * 0.4f + 0.02f * (k % 3 - 1));
                    Vector3 w = new Vector3(0f, 0.010f, 0f);
                    b.Quad(p0 - w, p1 - w, p1 + w, p0 + w, red);
                    b.Quad(p0 + w, p1 + w, p1 - w, p0 - w, red);
                }
            });
        }

        /// <summary>Радужка и зрачок (двигаются по глазу вслед за героем): янтарный восьмиугольник и узкая щель-ромб; плоские, двусторонние.</summary>
        public static Mesh EaterGaze()
        {
            return Get("EaterGaze", delegate(MeshBatch b)
            {
                Color32 iris = C(232, 150, 34);
                Color32 pupil = C(6, 2, 1);
                Vector3 c = new Vector3(0f, 0f, 0.207f);
                int n = 8;
                for (int i = 0; i < n; i++)
                {
                    Vector3 v0 = c + new Vector3(Mathf.Cos(i * 6.2831853f / n) * 0.155f, Mathf.Sin(i * 6.2831853f / n) * 0.17f, 0f);
                    Vector3 v1 = c + new Vector3(Mathf.Cos((i + 1) * 6.2831853f / n) * 0.155f, Mathf.Sin((i + 1) * 6.2831853f / n) * 0.17f, 0f);
                    b.Tri(c, v0, v1, iris);
                    b.Tri(c, v1, v0, iris);
                }
                Vector3 pc = c + new Vector3(0f, 0f, 0.006f);
                Vector3 top = pc + new Vector3(0f, 0.15f, 0f);
                Vector3 bot = pc + new Vector3(0f, -0.15f, 0f);
                Vector3 l = pc + new Vector3(-0.036f, 0f, 0f);
                Vector3 r = pc + new Vector3(0.036f, 0f, 0f);
                b.Tri(top, l, r, pupil);
                b.Tri(top, r, l, pupil);
                b.Tri(bot, r, l, pupil);
                b.Tri(bot, l, r, pupil);
            });
        }

        /// <summary>
        /// Глазница, веко и бровь (side: -1 левый глаз, +1 правый): тёмная скошенная плита нависает над глазом злым углом (внутренний край ниже
        /// наружного), по верхней кромке костяные шипы. Веко — отдельная деталь: опускается при моргании и в нападении.
        /// </summary>
        public static Mesh EaterLid(int side)
        {
            return Get("EaterLid" + side, delegate(MeshBatch b)
            {
                Color32 skin = Mul(FleshDark, 1.05f);
                Color32 rim = Mul(FleshLight, 1.2f);
                Ball(b, new Vector3(0f, 0f, -0.04f), new Vector3(0.42f, 0.42f, 0.13f), 12, 5, Mul(FleshDark, 0.8f), null);
                Vector2[] poly =
                {
                    new Vector2(side * 0.44f, 0.20f), new Vector2(-side * 0.40f, -0.04f),
                    new Vector2(-side * 0.42f, 0.50f), new Vector2(side * 0.50f, 0.66f)
                };
                Slab(b, Vector3.zero, poly, 0.02f, 0.27f, rim, skin);
                for (int k = 0; k < 4; k++)
                {
                    float u = 0.15f + 0.23f * k;
                    Vector3 at = new Vector3(Mathf.Lerp(-side * 0.40f, side * 0.48f, u), Mathf.Lerp(0.52f, 0.64f, u), 0.16f);
                    Spike(b, at, new Vector3(side * 0.25f, 1f, 0.25f), 0.045f, 0.16f + 0.05f * Rnd(k, side + 3, 171), 5, Bone);
                }
            });
        }
    }
}
