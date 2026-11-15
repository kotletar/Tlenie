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
    public static class EnemyModels
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

        public static readonly Color32 CloakDark = new Color32(34, 30, 56, 255);
        public static readonly Color32 CloakLight = new Color32(52, 46, 84, 255);
        public static readonly Color32 CloakLining = new Color32(70, 60, 108, 255);

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
            });
        }

        /// <summary>Глаза Тени: две косые бледные пластины у лицевого провала (светятся, материал без освещения).</summary>
        public static Mesh ShadeEyes()
        {
            return Get("ShadeEyes", delegate(MeshBatch b)
            {
                Color32 white = C(255, 255, 255);
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * 0.105f;
                    Vector3 c = new Vector3(x, 2.40f, 0.685f);   // перед лицевым провалом капюшона (его передняя стенка на z ~ 0.65), иначе глаза внутри и не видны
                    // миндалевидный глаз со злым наклоном: внутренний уголок ниже, внешний выше; бледный, пустой, без зрачка. Двусторонний
                    Vector3 inner = c + new Vector3(-side * 0.060f, -0.030f, 0f);
                    Vector3 top = c + new Vector3(side * 0.005f, 0.040f, 0f);
                    Vector3 outer = c + new Vector3(side * 0.075f, 0.034f, 0f);
                    Vector3 low = c + new Vector3(side * 0.012f, -0.026f, 0f);
                    b.Quad(inner, top, outer, low, white);
                    b.Quad(low, outer, top, inner, white);
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
            });
        }

        /// <summary>Предплечье и кисть Тени (от локтя вниз): тонкое запястье, пятипалая кисть с длинными когтями.</summary>
        public static Mesh ShadeForeArm(int side)
        {
            return Get("ShadeFore" + side, delegate(MeshBatch b)
            {
                Color32 sleeve = Mul(CloakLight, 0.9f);
                Color32 skin = C(58, 54, 78);
                Color32 claw = C(150, 150, 178);
                b.Frustum(new Vector3(0f, -0.36f, 0f), Vector3.down, 0.068f, 0.040f, 0.72f, 7, sleeve, false, false);
                // широкий рваный манжет у запястья
                b.Frustum(new Vector3(0f, -0.66f, 0f), Vector3.down, 0.04f, 0.095f, 0.14f, 7, Mul(CloakDark, 1.2f), false, true);
                // кисть: ладонь и пять когтистых пальцев веером; пальцы тонкие и длинные, у каждого сустав
                b.Box(new Vector3(0f, -0.80f, 0.01f), new Vector3(0.12f, 0.10f, 0.05f), skin);
                for (int f = 0; f < 4; f++)
                {
                    float spread = (f - 1.5f) * 0.045f;
                    float len1 = 0.17f + 0.04f * Rnd(f, side + 2, 61);
                    Vector3 s0 = new Vector3(spread, -0.84f, 0.01f);
                    Vector3 s1 = s0 + new Vector3(spread * 0.9f, -len1, 0.035f);
                    Vector3 s2 = s1 + new Vector3(spread * 0.5f, -0.15f, 0.07f);
                    b.Rod(s0, s1, 0.011f, 5, skin);
                    b.Rod(s1, s2, 0.008f, 5, skin);
                    Spike(b, s2, (s2 - s1) + new Vector3(0f, -0.04f, 0.10f), 0.008f, 0.12f, 5, claw);
                }
                // большой палец в сторону
                Vector3 t0 = new Vector3(-side * 0.06f, -0.80f, 0.02f);
                Vector3 t1 = t0 + new Vector3(-side * 0.08f, -0.10f, 0.05f);
                b.Rod(t0, t1, 0.012f, 5, skin);
                Spike(b, t1, new Vector3(-side * 0.5f, -1f, 0.8f), 0.009f, 0.11f, 5, claw);
            });
        }

        /// <summary>Лоскут плаща сзади (от крепления вниз): три звена рваной ленты, ширина к концу сужается; двусторонний.</summary>
        public static Mesh ShadeTail(int variant)
        {
            return Get("ShadeTail" + variant, delegate(MeshBatch b)
            {
                float len = 1.15f + 0.2f * variant;
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

        /// <summary>Туша: бугристая масса с хребтом шипов и широкой впадиной пасти спереди (зубы верхней челюсти и губа — часть меша).</summary>
        public static Mesh EaterBody()
        {
            return Get("EaterBody", delegate(MeshBatch b)
            {
                Ring[] rings =
                {
                    new Ring(0.55f, 1.50f, 1.30f, 0f, 0f),
                    new Ring(1.10f, 2.00f, 1.75f, 0f, 0.02f),
                    new Ring(1.85f, 2.35f, 2.00f, 0f, 0.05f),
                    new Ring(2.70f, 2.20f, 1.90f, 0f, 0.10f),
                    new Ring(3.50f, 1.70f, 1.50f, 0f, 0.12f),
                    new Ring(4.20f, 1.00f, 0.95f, 0f, 0.10f),
                    new Ring(4.55f, 0.30f, 0.30f, 0f, 0.08f)
                };
                int sides = 18;
                Loft(b, rings, sides, true, true,
                    delegate(int i, int j) { return 0.88f + 0.22f * Rnd(i, j, 101) + 0.06f * Mathf.Sin(j * 1.7f + i); },
                    null,
                    delegate(int i, int j)
                    {
                        // кожа: поперечные складки (чередование тёмных и светлых поясов), пятна по шуму
                        float band = (i % 2 == 0) ? 0.85f : 1.1f;
                        float spot = 0.8f + 0.4f * Rnd(j, i, 102);
                        return Mul(Mix(FleshDark, FleshLight, i / 6f), band * spot);
                    });
                // хребет: ряд шипов вдоль спины и макушки
                for (int k = 0; k < 9; k++)
                {
                    float u = k / 8f;
                    Vector3 from = new Vector3(0f, Mathf.Lerp(3.9f, 1.6f, u), Mathf.Lerp(-0.2f, -1.9f, u));
                    Vector3 dir = new Vector3(0f, 1f - u * 0.4f, -0.5f - u);
                    Spike(b, from, dir, 0.14f - 0.05f * u, 0.55f - 0.15f * u, 6, Mul(FleshLight, 0.8f));
                }
                // рога над глазами: у каждого два изогнутых внутрь звена; клыки по углам пасти
                Color32 horn = C(78, 62, 78);
                Color32 tusk = C(226, 220, 198);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 h0 = new Vector3(side * 1.05f, 3.85f, 0.65f);
                    Vector3 d0 = new Vector3(side * 0.45f, 1f, 0.10f);
                    Spike(b, h0, d0, 0.19f, 0.75f, 6, horn);
                    Vector3 h1 = h0 + d0.normalized * 0.70f;
                    Spike(b, h1, new Vector3(side * 0.10f, 1f, 0.35f), 0.11f, 0.55f, 6, horn);
                    Spike(b, new Vector3(side * 1.30f, 1.40f, 1.98f), new Vector3(side * 0.45f, 1f, 0.30f), 0.12f, 0.70f, 6, tusk);
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
                // пасть: тёмно-красный провал спереди, губа и верхний ряд зубов
                BuildMouth(b);
            });
        }

        static void BuildMouth(MeshBatch b)
        {
            Color32 gum = C(94, 18, 32);
            Color32 deep = C(26, 4, 10);
            Color32 lip = C(64, 26, 62);
            Color32 tooth = C(230, 224, 202);
            float cy = 1.62f;
            float cz = 1.98f;   // передняя кромка пасти: чуть впереди поверхности туши (на этой высоте она на z ~ 1.85)
            // глотка — отдельный светящийся меш (EaterThroat), здесь только губа и зубы
            // губа: кольцо брусков по кромке
            int seg = 16;
            for (int j = 0; j < seg; j++)
            {
                float a = (j + 0.5f) * 6.2831853f / seg;
                Vector3 m = new Vector3(Mathf.Cos(a) * 1.30f, cy + Mathf.Sin(a) * 0.72f, cz + 0.02f);
                b.Box(m, new Vector3(0.34f, 0.22f, 0.20f), Mul(lip, 0.9f + 0.2f * Rnd(j, 2, 121)));
            }
            // верхние зубы: клыки вниз по верхней дуге
            for (int k = 0; k < 13; k++)
            {
                float u = (k + 0.5f) / 13f;
                float a = Mathf.Lerp(0.18f, Mathf.PI - 0.18f, u);
                Vector3 from = new Vector3(Mathf.Cos(a) * 1.22f, cy + Mathf.Sin(a) * 0.66f, cz + 0.06f);
                float len = 0.30f + 0.28f * Mathf.Abs(Mathf.Sin(u * 9f + 1f)) + (k % 4 == 0 ? 0.15f : 0f);
                Spike(b, from, new Vector3(0f, -1f, 0.15f), 0.075f, len, 5, tooth);
            }
            // второй ряд: мелкие зубы по краю глотки остриями внутрь, как у миноги (сверху их нет: там висят клыки). Языка у Пожирателя нет —
            // пасть выглядит глубже за счёт этого ряда и красного провала
            for (int k = 0; k < 18; k++)
            {
                float a = (k + 0.5f) * 6.2831853f / 18f;
                float ca = Mathf.Cos(a);
                float sa = Mathf.Sin(a);
                if (sa > 0.40f) continue;
                Vector3 from = new Vector3(ca * 0.98f, cy + sa * 0.50f, cz + 0.05f);
                Vector3 inward = new Vector3(-ca * 0.85f, -sa * 0.85f, 0.38f);
                Spike(b, from, inward, 0.055f, 0.20f + 0.09f * Rnd(k, 4, 141), 5, Mul(tooth, 0.86f));
            }
        }

        /// <summary>
        /// Глотка Пожирателя: два плоских эллипсоида в провале пасти, внутри красный, глубже почти чёрный. Светится слабо (материал без
        /// освещения): в темноте пасть видна как тлеющий провал за зубами, а не как дыра.
        /// </summary>
        public static Mesh EaterThroat()
        {
            return Get("EaterThroat", delegate(MeshBatch b)
            {
                float cy = 1.62f;
                float cz = 1.98f;
                Ball(b, new Vector3(0f, cy, cz - 0.04f), new Vector3(1.20f, 0.68f, 0.16f), 16, 4, C(255, 255, 255), null);
                Ball(b, new Vector3(0f, cy - 0.02f, cz + 0.05f), new Vector3(0.92f, 0.46f, 0.12f), 14, 4, C(150, 120, 120), null);
            });
        }

        /// <summary>Нижняя челюсть: дуга по нижнему краю пасти с зубами вверх; начало — в шарнире у задней кромки пасти.</summary>
        public static Mesh EaterJaw()
        {
            return Get("EaterJaw", delegate(MeshBatch b)
            {
                Color32 lip = C(64, 26, 62);
                Color32 tooth = C(222, 216, 194);
                Color32 gum = C(94, 18, 32);
                // шарнир в (0, 0, 0); челюсть вперёд вдоль +Z и вниз
                int seg = 11;
                for (int k = 0; k < seg; k++)
                {
                    float u0 = k / (float)seg;
                    float u1 = (k + 1) / (float)seg;
                    float a0 = Mathf.Lerp(Mathf.PI, 0f, u0);
                    float a1 = Mathf.Lerp(Mathf.PI, 0f, u1);
                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * 1.22f, -Mathf.Sin(a0) * 0.62f - 0.02f, 0.92f);
                    Vector3 p1 = new Vector3(Mathf.Cos(a1) * 1.22f, -Mathf.Sin(a1) * 0.62f - 0.02f, 0.92f);
                    Vector3 q0 = new Vector3(p0.x * 0.8f, p0.y - 0.30f, 0.30f);
                    Vector3 q1 = new Vector3(p1.x * 0.8f, p1.y - 0.30f, 0.30f);
                    b.Quad(p0, q0, q1, p1, Mix(gum, lip, 0.3f));
                    b.Quad(p1, q1, q0, p0, lip);
                    b.Box((p0 + p1) * 0.5f + new Vector3(0f, -0.06f, 0.04f), new Vector3(0.34f, 0.18f, 0.20f), Mul(lip, 0.9f + 0.2f * Rnd(k, 3, 131)));
                }
                for (int k = 0; k < 11; k++)
                {
                    float u = (k + 0.5f) / 11f;
                    float a = Mathf.Lerp(Mathf.PI - 0.22f, 0.22f, u);
                    Vector3 from = new Vector3(Mathf.Cos(a) * 1.14f, -Mathf.Sin(a) * 0.58f, 0.93f);
                    float len = 0.24f + 0.24f * Mathf.Abs(Mathf.Sin(u * 7f + 2f)) + (k % 3 == 0 ? 0.12f : 0f);
                    Spike(b, from, new Vector3(0f, 1f, -0.05f), 0.07f, len, 5, tooth);
                }
            });
        }

        /// <summary>Звено щупальца (от основания вдоль -Y... вдоль +Z): конус с присосками-бугорками; thickness — множитель толщины.</summary>
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
            });
        }

        /// <summary>Большой глаз Пожирателя: бледный шар (светится) со щелевым зрачком — отдельная пластина, чуть впереди шара.</summary>
        public static Mesh EaterEye(bool pupil)
        {
            return Get(pupil ? "EaterPupil" : "EaterEye", delegate(MeshBatch b)
            {
                if (!pupil)
                {
                    Ball(b, Vector3.zero, new Vector3(0.27f, 0.30f, 0.20f), 10, 6, C(255, 255, 255), null);
                }
                else
                {
                    // вертикальная щель на передней поверхности шара
                    Vector3 c = new Vector3(0f, 0f, 0.205f);
                    b.Quad(c + new Vector3(-0.028f, -0.16f, 0f), c + new Vector3(0.028f, -0.16f, 0f), c + new Vector3(0.028f, 0.16f, 0f), c + new Vector3(-0.028f, 0.16f, 0f), C(18, 8, 4));
                    b.Quad(c + new Vector3(-0.028f, 0.16f, 0f), c + new Vector3(0.028f, 0.16f, 0f), c + new Vector3(0.028f, -0.16f, 0f), c + new Vector3(-0.028f, -0.16f, 0f), C(18, 8, 4));
                }
            });
        }
    }
}
