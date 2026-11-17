using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Меши Великого Гасителя (босс последней минуты): шар-броня радиусом 3.4 м. Сзади глухая броня с шипами и венцом рогов, спереди шесть
    /// лепестков-створок на шарнирах по экватору: закрытые, они сходятся к дулу на переднем полюсе (когтями друг к другу), открытые
    /// раскрывают чашу с глазом-ядром. Оси: +Z — вперёд (лицом к герою), начало — в центре шара. Вся геометрия плоская (грани), цвета в
    /// вершинах, как у остальных врагов.
    /// </summary>
    public static partial class EnemyModels
    {
        public const float BossScale = 1.3f;                   // во сколько раз тело в игре крупнее чертежа (радиус шара 3.4 -> 4.4 м)
        public const float BossR = 3.4f;                       // радиус брони (в чертеже)
        public const float BossShell = 0.34f;                  // толщина створки
        public const float BossInnerR = BossR - BossShell;     // радиус нутра
        public const int BossPetals = 6;

        /// <summary>Направление по полярному углу от +Z (0 — передний полюс) и азимуту вокруг оси Z от +X к +Y, градусы.</summary>
        public static Vector3 BossDir(float thetaDeg, float phiDeg)
        {
            float t = thetaDeg * Mathf.Deg2Rad;
            float p = phiDeg * Mathf.Deg2Rad;
            float s = Mathf.Sin(t);
            return new Vector3(s * Mathf.Cos(p), s * Mathf.Sin(p), Mathf.Cos(t));
        }

        static readonly Color32 BossBoneDim = new Color32(128, 120, 146, 255);

        /// <summary>
        /// Корпус: раскалённая чаша (внутренняя сфера без передней крышки: сквозь раскрытые створки виден её тёмно-багровый жар),
        /// глухая броня сзади с шипами, венец из шести изогнутых рогов у экватора, шарнирные узлы створок.
        /// </summary>
        public static Mesh BossHull()
        {
            return Get("BossHull", delegate(MeshBatch b)
            {
                // чаша: нормали внутрь, к центру
                const int cols = 14;
                const int bowlRows = 7;
                Vector3[,] v = new Vector3[bowlRows + 1, cols + 1];
                for (int i = 0; i <= bowlRows; i++)
                {
                    float th = Mathf.Lerp(52f, 178f, i / (float)bowlRows);
                    for (int j = 0; j <= cols; j++)
                    {
                        float k = 1f + 0.05f * (Rnd(i, j % cols, 311) - 0.5f);
                        v[i, j] = BossDir(th, j * 360f / cols) * (BossInnerR * k);
                    }
                }
                for (int i = 0; i < bowlRows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        Vector3 mid = (v[i, j] + v[i + 1, j + 1]) * 0.5f;
                        float heat = 1f - Mathf.Clamp01(i / (float)bowlRows * 1.15f);   // у края чаши жарче
                        Color32 col = Mix(C(26, 10, 16), C(150, 54, 24), heat * (0.55f + 0.45f * Rnd(i, j, 312)));
                        OutQuad(b, v[i, j], v[i + 1, j], v[i + 1, j + 1], v[i, j + 1], mid.normalized * (BossR * 3f), col);
                    }
                }
                // кольцо зубцов по краю чаши
                for (int j = 0; j < cols; j++)
                {
                    float phi = (j + 0.5f) * 360f / cols;
                    Vector3 at = BossDir(52f, phi) * (BossInnerR * 0.99f);
                    Spike(b, at, BossDir(38f, phi), 0.17f, 0.85f + 0.3f * Rnd(j, 1, 313), 5, Mix(Bone, BoneDark, 0.4f));
                }

                // глухая броня сзади (от экватора до заднего полюса)
                const int shellRows = 6;
                Vector3[,] s = new Vector3[shellRows + 1, cols + 1];
                for (int i = 0; i <= shellRows; i++)
                {
                    float th = Mathf.Lerp(90f, 176f, i / (float)shellRows);
                    for (int j = 0; j <= cols; j++)
                    {
                        float k = 1f + 0.07f * (Rnd(i, j % cols, 314) - 0.4f);
                        s[i, j] = BossDir(th, j * 360f / cols) * (BossR * k);
                    }
                }
                for (int i = 0; i < shellRows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        Color32 col = Mix(C(30, 24, 48), C(74, 62, 102), Rnd(i, j, 315));
                        OutQuad(b, s[i, j], s[i + 1, j], s[i + 1, j + 1], s[i, j + 1], Vector3.zero, col);
                    }
                }
                // шипы по всему шару, кроме переднего полушария (там шипы створок)
                const int spikes = 46;
                float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
                for (int i = 0; i < spikes; i++)
                {
                    float y = 1f - (i + 0.5f) * 2f / spikes;
                    float rad = Mathf.Sqrt(1f - y * y);
                    float a = i * golden;
                    Vector3 dir = new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, y);   // y здесь — вдоль оси Z
                    if (dir.z > 0.12f) continue;
                    float len = 1.2f + 2.0f * Rnd(i, 5, 316) + (i % 6 == 0 ? 1.3f : 0f);
                    Spike(b, dir * (BossR * 0.97f), dir, 0.30f + 0.12f * Rnd(i, 6, 317), len, 6, Mix(C(34, 28, 54), BossBoneDim, Rnd(i, 7, 318)));
                }
                // венец: шесть длинных рогов у экватора между шарнирами, загнутых назад
                for (int k = 0; k < BossPetals; k++)
                {
                    float phi = k * 60f + 30f;
                    Vector3 dir = BossDir(97f, phi);
                    Horn(b, dir * (BossR * 0.98f), dir, new Vector3(0f, 0f, -1.25f), 0.42f, 4.6f, 4, Mix(Bone, BoneDark, 0.55f));
                }
                // шарнирные узлы створок
                for (int k = 0; k < BossPetals; k++)
                {
                    Vector3 dir = BossDir(90f, k * 60f);
                    b.Frustum(dir * (BossR * 1.0f), dir, 0.52f, 0.40f, 0.8f, 6, BoneDark, true, true);
                }
                // у заднего полюса пять коротких толстых шипов
                for (int k = 0; k < 5; k++)
                {
                    Vector3 dir = BossDir(158f, k * 72f);
                    Spike(b, dir * (BossR * 0.97f), dir, 0.34f, 2.3f, 6, Mix(C(40, 32, 62), BossBoneDim, 0.5f));
                }
            });
        }

        /// <summary>Обломки брони вокруг шара: шестнадцать тёмных осколков-ромбов в диске радиусом 5-7 м вокруг оси «вперёд» (EnemyRig медленно вращает их вокруг неё).</summary>
        public static Mesh BossDebris()
        {
            return Get("BossDebris", delegate(MeshBatch b)
            {
                for (int k = 0; k < 16; k++)
                {
                    float a = k * 2.3999f;
                    float r = 5.2f + 2.2f * Rnd(k, 1, 331);
                    float y = -2.6f + 5.2f * Rnd(k, 2, 332);
                    float s = 0.32f + 0.42f * Rnd(k, 3, 333);
                    Vector3 c = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, y);   // кольцо вокруг оси «вперёд»: осколки не проходят перед глазом
                    Ball(b, c, new Vector3(s * 0.6f, s * 1.5f, s * 0.5f), 4, 2, Mix(C(36, 30, 58), C(78, 66, 108), Rnd(k, 4, 334)), null);
                }
            });
        }

        /// <summary>
        /// Створка: сферический клин между двумя меридианами (от экватора до переднего полюса), двойная — наружная броня и раскалённое нутро —
        /// с торцами. Координаты относительно шарнира (на экваторе, точка (R, 0, 0)); на шарнир створка поворачивается вокруг оси Y. По наружной
        /// стороне три шипа вдоль оси клина и четыре поменьше по бокам, на кончике изогнутый коготь, который вместе с остальными сходится к дулу.
        /// </summary>
        public static Mesh BossPetal()
        {
            return Get("BossPetal", delegate(MeshBatch b)
            {
                const int rows = 7;
                const int cols = 4;
                const float th0 = 90f;
                const float th1 = 11f;
                const float half = 27f;
                Vector3 hinge = new Vector3(BossR, 0f, 0f);
                Vector3[,] o = new Vector3[rows + 1, cols + 1];
                Vector3[,] n = new Vector3[rows + 1, cols + 1];
                for (int i = 0; i <= rows; i++)
                {
                    float th = Mathf.Lerp(th0, th1, i / (float)rows);
                    for (int j = 0; j <= cols; j++)
                    {
                        float ph = Mathf.Lerp(-half, half, j / (float)cols);
                        Vector3 d = BossDir(th, ph);
                        float k = 1f + 0.04f * (Rnd(i, j, 321) - 0.5f);
                        o[i, j] = d * (BossR * k) - hinge;
                        n[i, j] = d * (BossInnerR * k) - hinge;
                    }
                }
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        Vector3 dm = ((o[i, j] + o[i + 1, j + 1]) * 0.5f + hinge).normalized;
                        Vector3 inside = dm * (BossR - BossShell * 0.5f) - hinge;
                        float tip = i / (float)rows;
                        Color32 outer = Mix(C(46, 38, 70), C(84, 70, 112), Rnd(i, j, 322));
                        outer = Mix(outer, C(30, 24, 46), tip * 0.35f);
                        OutQuad(b, o[i, j], o[i + 1, j], o[i + 1, j + 1], o[i, j + 1], inside, outer);
                        Color32 inner = Mix(C(150, 52, 24), C(232, 118, 46), Rnd(i, j, 323));
                        inner = Mix(inner, C(110, 36, 20), tip * 0.4f);
                        OutQuad(b, n[i, j], n[i + 1, j], n[i + 1, j + 1], n[i, j + 1], inside, inner);
                    }
                }
                // торцы
                for (int i = 0; i < rows; i++)
                {
                    Vector3 refL = (o[i, 1] + n[i, 1] + o[i + 1, 1] + n[i + 1, 1]) * 0.25f;
                    Vector3 refR = (o[i, cols - 1] + n[i, cols - 1] + o[i + 1, cols - 1] + n[i + 1, cols - 1]) * 0.25f;
                    OutQuad(b, o[i, 0], o[i + 1, 0], n[i + 1, 0], n[i, 0], refL, BoneDark);
                    OutQuad(b, o[i, cols], o[i + 1, cols], n[i + 1, cols], n[i, cols], refR, BoneDark);
                }
                for (int j = 0; j < cols; j++)
                {
                    Vector3 refP = (o[rows - 1, j] + n[rows - 1, j] + o[rows - 1, j + 1] + n[rows - 1, j + 1]) * 0.25f;
                    Vector3 refE = (o[1, j] + n[1, j] + o[1, j + 1] + n[1, j + 1]) * 0.25f;
                    OutQuad(b, o[rows, j], o[rows, j + 1], n[rows, j + 1], n[rows, j], refP, BoneDark);
                    OutQuad(b, o[0, j], o[0, j + 1], n[0, j + 1], n[0, j], refE, BoneDark);
                }
                // шипы вдоль оси клина и по бокам
                float[] ths = { 68f, 46f, 26f };
                float[] lens = { 1.75f, 1.35f, 1.0f };
                float[] rads = { 0.31f, 0.26f, 0.20f };
                for (int k = 0; k < ths.Length; k++)
                {
                    Vector3 d = BossDir(ths[k], 0f);
                    Spike(b, d * (BossR * 0.98f) - hinge, d, rads[k], lens[k], 6, Mix(C(150, 142, 160), BossBoneDim, 0.45f));
                }
                float[] sideTh = { 74f, 52f };
                for (int k = 0; k < sideTh.Length; k++)
                {
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector3 d = BossDir(sideTh[k], sgn * (17f - 5f * k));
                        Spike(b, d * (BossR * 0.98f) - hinge, d, 0.2f, 0.95f - 0.2f * k, 5, BossBoneDim);
                    }
                }
                // коготь на кончике: вперёд и внутрь, к оси (клинья всех шести сходятся к дулу)
                Vector3 tipPoint = BossDir(th1, 0f) * (BossR * 0.99f) - hinge;
                Horn(b, tipPoint, new Vector3(-0.18f, 0f, 1f), new Vector3(-1.0f, 0f, 0.1f), 0.22f, 2.0f, 3, Bone);
            });
        }
    }
}
