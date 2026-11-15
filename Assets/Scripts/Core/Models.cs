using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Детальные модели из коробок и призм (MeshBatch): машины, фонари, люди, автобус, генератор, уличный хлам, детали зданий.
    /// Всё собирается в общие меши (цвет — в цвете вершин), поэтому сотни деталей стоят считанные draw call'ы.
    /// У нижних граней предметов на земле нет смысла (их не видно) — они отбрасываются, это экономит вершины.
    /// Локальные оси предмета: +Z — вперёд (лицом), +X — вправо; yaw поворачивает предмет вокруг вертикали.
    /// </summary>
    public static class Models
    {
        const int NB = MeshBatch.NoBottom;

        public static Color32 Col(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        public static Color32 Dim(Color32 c, float k)
        {
            return new Color32(
                (byte)Mathf.Clamp(c.r * k, 0f, 255f),
                (byte)Mathf.Clamp(c.g * k, 0f, 255f),
                (byte)Mathf.Clamp(c.b * k, 0f, 255f), 255);
        }

        public static Vector3 RotY(Vector3 v, float yawDeg)
        {
            float rad = yawDeg * 0.0174532925f;
            float cs = Mathf.Cos(rad);
            float sn = Mathf.Sin(rad);
            return new Vector3(v.x * cs + v.z * sn, v.y, -v.x * sn + v.z * cs);
        }

        // коробка / призма в локальной системе предмета
        static void LBox(MeshBatch b, Vector3 o, float yaw, Vector3 local, Vector3 size, Color32 col)
        {
            b.BoxYaw(o + RotY(local, yaw), size, yaw, col, NB);
        }

        static void LBoxAll(MeshBatch b, Vector3 o, float yaw, Vector3 local, Vector3 size, Color32 col)
        {
            b.BoxYaw(o + RotY(local, yaw), size, yaw, col, MeshBatch.AllFaces);
        }

        static void LPrism(MeshBatch b, Vector3 o, float yaw, Vector3 local, Vector3 axis, float r, float len, int sides, Color32 col, bool caps)
        {
            b.Prism(o + RotY(local, yaw), RotY(axis, yaw), r, len, sides, col, caps);
        }

        // ------------------------------------------------------------------ машина

        /// <summary>Брошенная машина: кузов со ступенчатым капотом и багажником, стойки, стёкла, бамперы, фары, колёса.</summary>
        public static void Car(MeshBatch b, Vector3 pos, float yaw, Color32 body)
        {
            Color32 glass = Col(16, 20, 28);
            Color32 dark = Col(26, 26, 30);
            Color32 tyre = Col(14, 14, 16);
            Color32 hub = Col(70, 70, 76);
            Color32 roof = Dim(body, 0.92f);
            Color32 sill = Col(34, 34, 38);

            LBox(b, pos, yaw, new Vector3(0f, 0.58f, 0f), new Vector3(1.8f, 0.5f, 4.2f), body);
            LBox(b, pos, yaw, new Vector3(0f, 0.34f, 0f), new Vector3(1.82f, 0.18f, 3.7f), sill);
            LBox(b, pos, yaw, new Vector3(0f, 0.88f, 1.35f), new Vector3(1.72f, 0.14f, 1.4f), Dim(body, 1.06f));
            LBox(b, pos, yaw, new Vector3(0f, 0.88f, -1.55f), new Vector3(1.72f, 0.14f, 1.0f), Dim(body, 1.02f));
            LBox(b, pos, yaw, new Vector3(0f, 1.18f, -0.18f), new Vector3(1.5f, 0.5f, 1.95f), glass);
            LBox(b, pos, yaw, new Vector3(0f, 1.46f, -0.18f), new Vector3(1.56f, 0.07f, 2.05f), roof);
            // стойки крыши
            float[] px = { -0.76f, 0.76f };
            float[] pz = { 0.76f, -1.12f };
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++) LBox(b, pos, yaw, new Vector3(px[i], 1.18f, pz[j]), new Vector3(0.07f, 0.52f, 0.08f), body);
            }
            // бамперы и фары
            LBox(b, pos, yaw, new Vector3(0f, 0.4f, 2.14f), new Vector3(1.74f, 0.2f, 0.14f), dark);
            LBox(b, pos, yaw, new Vector3(0f, 0.4f, -2.14f), new Vector3(1.74f, 0.2f, 0.14f), dark);
            LBox(b, pos, yaw, new Vector3(-0.6f, 0.66f, 2.1f), new Vector3(0.3f, 0.14f, 0.06f), Col(96, 98, 92));
            LBox(b, pos, yaw, new Vector3(0.6f, 0.66f, 2.1f), new Vector3(0.3f, 0.14f, 0.06f), Col(96, 98, 92));
            LBox(b, pos, yaw, new Vector3(-0.64f, 0.68f, -2.1f), new Vector3(0.3f, 0.12f, 0.06f), Col(70, 16, 14));
            LBox(b, pos, yaw, new Vector3(0.64f, 0.68f, -2.1f), new Vector3(0.3f, 0.12f, 0.06f), Col(70, 16, 14));
            // колёса: 8-гранные, диск ступицы
            float[] wx = { -0.9f, 0.9f };
            float[] wz = { -1.4f, 1.4f };
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    float sgn = wx[i] < 0f ? -1f : 1f;
                    LPrism(b, pos, yaw, new Vector3(wx[i], 0.33f, wz[j]), new Vector3(1f, 0f, 0f), 0.33f, 0.24f, 8, tyre, true);
                    LPrism(b, pos, yaw, new Vector3(wx[i] + sgn * 0.13f, 0.33f, wz[j]), new Vector3(1f, 0f, 0f), 0.16f, 0.04f, 8, hub, true);
                }
            }
        }

        // ------------------------------------------------------------------ фонарь

        /// <summary>
        /// Уличный фонарь: плита, конусный постамент, коническая мачта, изогнутый кронштейн, корпус светильника.
        /// Возвращает положение лампочки относительно основания (там живут динамические огонь, ореол и конус света).
        /// </summary>
        public static Vector3 LampPost(MeshBatch b, Vector3 p, Vector3 armDir)
        {
            armDir.y = 0f;
            if (armDir.sqrMagnitude < 0.01f) armDir = Vector3.forward;
            armDir.Normalize();
            float yaw = Mathf.Atan2(armDir.x, armDir.z) * Mathf.Rad2Deg;
            Color32 metal = Col(36, 36, 40);
            Color32 dark = Col(22, 22, 26);

            b.BoxYaw(p + new Vector3(0f, 0.06f, 0f), new Vector3(0.56f, 0.12f, 0.56f), 0f, dark, NB);
            b.Frustum(p + new Vector3(0f, 0.42f, 0f), Vector3.up, 0.21f, 0.12f, 0.62f, 8, metal, false, false);
            b.Prism(p + new Vector3(0f, 0.76f, 0f), Vector3.up, 0.155f, 0.07f, 8, dark, true);
            b.Frustum(p + new Vector3(0f, 2.55f, 0f), Vector3.up, 0.095f, 0.064f, 3.55f, 8, metal, false, true);
            b.Prism(p + new Vector3(0f, 2.0f, 0f), Vector3.up, 0.11f, 0.05f, 8, dark, false);

            Vector3 top = p + new Vector3(0f, 4.3f, 0f);
            Vector3 a1 = top + armDir * 0.24f + new Vector3(0f, 0.28f, 0f);
            Vector3 a2 = top + armDir * 0.74f + new Vector3(0f, 0.37f, 0f);
            Vector3 a3 = top + armDir * 1.14f + new Vector3(0f, 0.2f, 0f);
            b.Rod(top, a1, 0.052f, 6, metal);
            b.Rod(a1, a2, 0.046f, 6, metal);
            b.Rod(a2, a3, 0.042f, 6, metal);

            Vector3 headC = p + armDir * 1.2f + new Vector3(0f, 4.42f, 0f);
            b.BoxYaw(headC, new Vector3(0.36f, 0.12f, 0.74f), yaw, metal, NB);
            b.BoxYaw(headC + new Vector3(0f, 0.09f, 0f), new Vector3(0.3f, 0.05f, 0.64f), yaw, dark, NB);
            b.BoxYaw(headC + new Vector3(0f, -0.075f, 0f), new Vector3(0.28f, 0.03f, 0.6f), yaw, dark, MeshBatch.AllFaces);   // рамка плафона
            return armDir * 1.2f + new Vector3(0f, 4.3f, 0f);
        }

        /// <summary>Бочка-жаровня очага: ржавая бочка с обручами, кромка, угли и поленья.</summary>
        public static void Brazier(MeshBatch b, Vector3 p)
        {
            Color32 rust = Col(74, 46, 34);
            Color32 metal = Col(40, 38, 40);
            Color32 ash = Col(28, 22, 20);
            Color32 wood = Col(60, 42, 28);
            b.Frustum(p + new Vector3(0f, 0.5f, 0f), Vector3.up, 0.40f, 0.46f, 1.0f, 10, rust, false, false);
            b.Prism(p + new Vector3(0f, 0.25f, 0f), Vector3.up, 0.425f, 0.07f, 10, metal, false);
            b.Prism(p + new Vector3(0f, 0.74f, 0f), Vector3.up, 0.45f, 0.07f, 10, metal, false);
            b.Prism(p + new Vector3(0f, 1.0f, 0f), Vector3.up, 0.5f, 0.08f, 10, metal, true);
            b.Prism(p + new Vector3(0f, 0.97f, 0f), Vector3.up, 0.42f, 0.04f, 10, ash, true);
            b.Rod(p + new Vector3(-0.3f, 1.05f, -0.12f), p + new Vector3(0.3f, 1.1f, 0.1f), 0.06f, 5, wood);
            b.Rod(p + new Vector3(-0.12f, 1.05f, 0.3f), p + new Vector3(0.1f, 1.1f, -0.3f), 0.06f, 5, wood);
            b.Rod(p + new Vector3(-0.28f, 1.08f, 0.2f), p + new Vector3(0.3f, 1.08f, -0.16f), 0.05f, 5, wood);
            b.BoxYaw(p + new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.08f, 0.9f), 0f, metal, NB);
        }

        // ------------------------------------------------------------------ уличный хлам

        public static void TrashCan(MeshBatch b, Vector3 p, Color32 col)
        {
            b.Frustum(p + new Vector3(0f, 0.4f, 0f), Vector3.up, 0.27f, 0.33f, 0.8f, 8, col, false, true);
            b.Frustum(p + new Vector3(0f, 0.84f, 0f), Vector3.up, 0.34f, 0.3f, 0.08f, 8, Dim(col, 0.8f), true, true);
            b.Prism(p + new Vector3(0f, 0.9f, 0f), Vector3.up, 0.05f, 0.06f, 5, Dim(col, 0.6f), true);
            b.Prism(p + new Vector3(0f, 0.45f, 0f), Vector3.up, 0.335f, 0.05f, 8, Dim(col, 0.7f), false);
        }

        public static void Dumpster(MeshBatch b, Vector3 p, float yaw, Color32 col)
        {
            Color32 dark = Dim(col, 0.62f);
            LBox(b, p, yaw, new Vector3(0f, 0.62f, 0f), new Vector3(1.9f, 1.0f, 1.0f), col);
            LBox(b, p, yaw, new Vector3(0f, 0.12f, 0f), new Vector3(1.8f, 0.16f, 0.9f), Col(24, 24, 26));
            LBox(b, p, yaw, new Vector3(-0.47f, 1.14f, 0f), new Vector3(0.94f, 0.07f, 1.06f), dark);
            LBox(b, p, yaw, new Vector3(0.52f, 1.2f, 0f), new Vector3(0.82f, 0.06f, 1.04f), dark);
            for (int i = -1; i <= 1; i++) LBox(b, p, yaw, new Vector3(i * 0.62f, 0.62f, 0.52f), new Vector3(0.09f, 0.92f, 0.05f), dark);
            LBox(b, p, yaw, new Vector3(0f, 1.1f, 0.52f), new Vector3(1.94f, 0.08f, 0.07f), dark);
            LPrism(b, p, yaw, new Vector3(-0.8f, 0.1f, 0.5f), new Vector3(1f, 0f, 0f), 0.1f, 0.06f, 6, Col(12, 12, 14), true);
            LPrism(b, p, yaw, new Vector3(0.8f, 0.1f, 0.5f), new Vector3(1f, 0f, 0f), 0.1f, 0.06f, 6, Col(12, 12, 14), true);
        }

        public static void Crate(MeshBatch b, Vector3 p, float yaw, float size, Color32 wood)
        {
            float h = size;
            LBoxAll(b, p, yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(size, h, size), wood);
            Color32 post = Dim(wood, 0.62f);
            float o = size * 0.5f - 0.04f;
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    float sx = i == 0 ? -o : o;
                    float sz = j == 0 ? -o : o;
                    LBox(b, p, yaw, new Vector3(sx, h * 0.5f, sz), new Vector3(0.09f, h + 0.02f, 0.09f), post);
                }
            }
            LBox(b, p, yaw, new Vector3(0f, h * 0.5f, size * 0.5f + 0.012f), new Vector3(size - 0.1f, 0.09f, 0.03f), Dim(wood, 0.8f));
            LBox(b, p, yaw, new Vector3(size * 0.5f + 0.012f, h * 0.5f, 0f), new Vector3(0.03f, 0.09f, size - 0.1f), Dim(wood, 0.8f));
        }

        public static void Barrel(MeshBatch b, Vector3 p, Color32 col)
        {
            b.Frustum(p + new Vector3(0f, 0.45f, 0f), Vector3.up, 0.27f, 0.3f, 0.9f, 10, col, false, true);
            b.Prism(p + new Vector3(0f, 0.2f, 0f), Vector3.up, 0.305f, 0.06f, 10, Dim(col, 0.7f), false);
            b.Prism(p + new Vector3(0f, 0.7f, 0f), Vector3.up, 0.305f, 0.06f, 10, Dim(col, 0.7f), false);
            b.Prism(p + new Vector3(0f, 0.9f, 0f), Vector3.up, 0.29f, 0.04f, 10, Dim(col, 0.6f), true);
        }

        public static void Bench(MeshBatch b, Vector3 p, float yaw, Color32 wood)
        {
            Color32 metal = Col(30, 30, 34);
            for (int i = 0; i < 3; i++) LBox(b, p, yaw, new Vector3(0f, 0.46f, -0.18f + i * 0.17f), new Vector3(1.9f, 0.05f, 0.14f), wood);
            for (int i = 0; i < 2; i++) LBox(b, p, yaw, new Vector3(0f, 0.7f + i * 0.17f, -0.27f), new Vector3(1.9f, 0.12f, 0.04f), Dim(wood, 0.92f));
            LBox(b, p, yaw, new Vector3(-0.85f, 0.23f, 0f), new Vector3(0.07f, 0.46f, 0.5f), metal);
            LBox(b, p, yaw, new Vector3(0.85f, 0.23f, 0f), new Vector3(0.07f, 0.46f, 0.5f), metal);
            LBox(b, p, yaw, new Vector3(-0.85f, 0.62f, -0.27f), new Vector3(0.07f, 0.4f, 0.05f), metal);
            LBox(b, p, yaw, new Vector3(0.85f, 0.62f, -0.27f), new Vector3(0.07f, 0.4f, 0.05f), metal);
        }

        public static void Planter(MeshBatch b, Vector3 p, Color32 concrete)
        {
            LBoxAll(b, p, 0f, new Vector3(0f, 0.3f, 0f), new Vector3(1.7f, 0.6f, 1.7f), concrete);
            LBoxAll(b, p, 0f, new Vector3(0f, 0.66f, 0f), new Vector3(1.5f, 0.12f, 1.5f), Col(34, 30, 24));
            b.Rod(p + new Vector3(0f, 0.7f, 0f), p + new Vector3(0.1f, 1.5f, 0.05f), 0.05f, 5, Col(40, 30, 24));
            b.Rod(p + new Vector3(0.05f, 1.2f, 0.03f), p + new Vector3(0.5f, 1.6f, 0.2f), 0.03f, 4, Col(40, 30, 24));
            b.Rod(p + new Vector3(0.05f, 1.1f, 0.03f), p + new Vector3(-0.4f, 1.55f, -0.1f), 0.03f, 4, Col(40, 30, 24));
        }

        public static void Hydrant(MeshBatch b, Vector3 p)
        {
            Color32 red = Col(110, 28, 24);
            b.Prism(p + new Vector3(0f, 0.36f, 0f), Vector3.up, 0.12f, 0.72f, 8, red, false);
            b.Prism(p + new Vector3(0f, 0.76f, 0f), Vector3.up, 0.15f, 0.08f, 8, Dim(red, 0.8f), true);
            b.Prism(p + new Vector3(0f, 0.84f, 0f), Vector3.up, 0.07f, 0.1f, 6, red, true);
            b.Prism(p + new Vector3(0.14f, 0.5f, 0f), Vector3.right, 0.05f, 0.14f, 6, Dim(red, 0.9f), true);
            b.Prism(p + new Vector3(-0.14f, 0.5f, 0f), Vector3.left, 0.05f, 0.14f, 6, Dim(red, 0.9f), true);
        }

        public static void StreetSign(MeshBatch b, Vector3 p, float yaw, Color32 board)
        {
            b.Prism(p + new Vector3(0f, 1.3f, 0f), Vector3.up, 0.04f, 2.6f, 6, Col(60, 60, 64), true);
            LBoxAll(b, p, yaw, new Vector3(0f, 2.35f, 0.05f), new Vector3(0.7f, 0.5f, 0.03f), board);
            LBoxAll(b, p, yaw, new Vector3(0f, 2.35f, 0.025f), new Vector3(0.74f, 0.54f, 0.02f), Col(60, 60, 64));
        }

        public static void JerseyBarrier(MeshBatch b, Vector3 p, float yaw, Color32 concrete)
        {
            LBox(b, p, yaw, new Vector3(0f, 0.17f, 0f), new Vector3(0.64f, 0.34f, 3.0f), concrete);
            LBox(b, p, yaw, new Vector3(0f, 0.55f, 0f), new Vector3(0.36f, 0.42f, 3.0f), concrete);
            LBoxAll(b, p, yaw, new Vector3(0f, 0.78f, 0f), new Vector3(0.3f, 0.06f, 2.98f), Dim(concrete, 1.15f));
            LBox(b, p, yaw, new Vector3(0.19f, 0.6f, 0f), new Vector3(0.02f, 0.18f, 0.5f), Col(150, 70, 28));
            LBox(b, p, yaw, new Vector3(-0.19f, 0.6f, 0f), new Vector3(0.02f, 0.18f, 0.5f), Col(150, 70, 28));
        }

        public static void Cone(MeshBatch b, Vector3 p)
        {
            Color32 orange = Col(190, 84, 24);
            b.BoxYaw(p + new Vector3(0f, 0.025f, 0f), new Vector3(0.46f, 0.05f, 0.46f), 0f, Dim(orange, 0.5f), NB);
            b.Frustum(p + new Vector3(0f, 0.4f, 0f), Vector3.up, 0.18f, 0.05f, 0.7f, 6, orange, false, true);
            b.Prism(p + new Vector3(0f, 0.36f, 0f), Vector3.up, 0.12f, 0.1f, 6, Col(190, 190, 180), false);
        }

        public static void Rubble(MeshBatch b, Vector3 c, System.Random rng, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float r1 = (float)rng.NextDouble();
                float r2 = (float)rng.NextDouble();
                float r3 = (float)rng.NextDouble();
                float r4 = (float)rng.NextDouble();
                float s = 0.25f + r3 * 0.7f;
                Color32 col = (rng.NextDouble() < 0.5) ? Col(70 + (int)(r4 * 24), 66 + (int)(r4 * 20), 62 + (int)(r4 * 18)) : Col(88 + (int)(r4 * 20), 52 + (int)(r4 * 12), 40);
                b.BoxYaw(c + new Vector3((r1 - 0.5f) * 3.6f, s * 0.35f, (r2 - 0.5f) * 3.6f), new Vector3(s, s * 0.7f, s * (0.7f + r4 * 0.6f)), r1 * 180f, col, NB);
            }
        }

        // ------------------------------------------------------------------ пицца

        /// <summary>
        /// Картонная коробка с откинутой назад крышкой: остаётся лежать, когда пиццу съели.
        /// Локальные оси: крышка у -Z, открытая сторона смотрит в +Z; низ коробки на земле.
        /// </summary>
        public static void PizzaBox(MeshBatch b)
        {
            Color32 card = Col(138, 100, 62);
            Color32 inner = Col(168, 126, 80);
            // дно и невысокие бортики
            b.BoxYaw(new Vector3(0f, 0.025f, 0f), new Vector3(0.50f, 0.05f, 0.50f), 0f, card, NB);
            b.BoxYaw(new Vector3(0f, 0.07f, 0.245f), new Vector3(0.50f, 0.04f, 0.012f), 0f, Dim(card, 1.08f), NB);
            b.BoxYaw(new Vector3(0f, 0.07f, -0.245f), new Vector3(0.50f, 0.04f, 0.012f), 0f, Dim(card, 0.95f), NB);
            b.BoxYaw(new Vector3(0.245f, 0.07f, 0f), new Vector3(0.012f, 0.04f, 0.48f), 0f, Dim(card, 1.0f), NB);
            b.BoxYaw(new Vector3(-0.245f, 0.07f, 0f), new Vector3(0.012f, 0.04f, 0.48f), 0f, Dim(card, 1.0f), NB);
            // крышка: петля по задней кромке, панель откинута на ~100° и стоит почти вертикально
            float a = 100f * Mathf.Deg2Rad;
            Vector3 d = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * 0.50f;
            Vector3 hl = new Vector3(-0.25f, 0.09f, -0.25f);
            Vector3 hr = new Vector3(0.25f, 0.09f, -0.25f);
            b.Quad(hl, hr, hr + d, hl + d, inner);                // внутренняя сторона смотрит вперёд
            b.Quad(hl, hl + d, hr + d, hr, Dim(card, 0.85f));     // наружная — назад
            // жирное пятно на внутренней стороне: тёмная полоса, чтобы крышка не была плоским пятном
            Vector3 g0 = hl + d * 0.35f + new Vector3(0.06f, 0f, 0.004f);
            Vector3 g1 = hr + d * 0.35f - new Vector3(0.06f, 0f, -0.004f);
            b.Quad(g0, g1, g1 + d * 0.22f, g0 + d * 0.22f, Dim(inner, 0.78f));
        }

        static Vector3 PizzaP(float ang, float r, float y)
        {
            return new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
        }

        /// <summary>
        /// Пицца, разрезанная на 6 ломтиков; group 0..2 — по два ломтика (120°). Центр пиццы в начале координат, низ на высоте 0.05 м
        /// (дно коробки). Каждая группа собирается в свой меш: пока пиццу едят, куски пропадают по одному.
        /// </summary>
        public static void PizzaSlices(MeshBatch b, int group)
        {
            Color32 cheese = Col(230, 172, 64);
            Color32 crust = Col(188, 120, 52);
            Color32 pep = Col(152, 38, 30);
            const float R = 0.225f;        // внешний радиус
            const float Rin = 0.190f;      // где кончается сыр и начинается корочка
            const float y0 = 0.05f;        // низ (дно коробки)
            const float yCheese = 0.072f;  // верх сыра
            const float yCrust = 0.088f;   // верх корочки
            const int seg = 3;
            for (int s = 0; s < 2; s++)
            {
                int sector = group * 2 + s;
                float a0 = (sector * 60f + 1.5f) * Mathf.Deg2Rad;     // между ломтиками — узкая щель: видно, что пицца порезана
                float a1 = (sector * 60f + 58.5f) * Mathf.Deg2Rad;
                for (int i = 0; i < seg; i++)
                {
                    float p0 = Mathf.Lerp(a0, a1, i / (float)seg);
                    float p1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    // сыр сверху (веером от центра), корочка кольцом, наружная и внутренняя стенки корочки
                    b.Tri(PizzaP(p0, 0f, yCheese), PizzaP(p1, Rin, yCheese), PizzaP(p0, Rin, yCheese), cheese);
                    b.Quad(PizzaP(p0, Rin, yCrust), PizzaP(p1, Rin, yCrust), PizzaP(p1, R, yCrust), PizzaP(p0, R, yCrust), crust);
                    b.Quad(PizzaP(p0, R, y0), PizzaP(p0, R, yCrust), PizzaP(p1, R, yCrust), PizzaP(p1, R, y0), Dim(crust, 0.8f));
                    b.Quad(PizzaP(p0, Rin, yCheese), PizzaP(p1, Rin, yCheese), PizzaP(p1, Rin, yCrust), PizzaP(p0, Rin, yCrust), Dim(crust, 0.9f));
                }
                // боковые грани ломтика (видны, когда соседний уже съеден)
                for (int side = 0; side < 2; side++)
                {
                    float an = side == 0 ? a0 : a1;
                    Vector3 c0 = PizzaP(an, 0f, y0), c1 = PizzaP(an, 0f, yCheese);
                    Vector3 m0 = PizzaP(an, Rin, y0), m1 = PizzaP(an, Rin, yCheese), m2 = PizzaP(an, Rin, yCrust);
                    Vector3 o0 = PizzaP(an, R, y0), o1 = PizzaP(an, R, yCrust);
                    if (side == 1)
                    {
                        b.Quad(c0, m0, m1, c1, Dim(cheese, 0.8f));
                        b.Quad(m0, o0, o1, m2, Dim(crust, 0.85f));
                    }
                    else
                    {
                        b.Quad(c0, c1, m1, m0, Dim(cheese, 0.8f));
                        b.Quad(m0, m2, o1, o0, Dim(crust, 0.85f));
                    }
                }
                // колбаска: по две кружки на ломтик
                float am = (a0 + a1) * 0.5f;
                Vector3 pa = PizzaP(am - 0.12f, 0.095f, yCheese + 0.006f);
                Vector3 pb = PizzaP(am + 0.09f, 0.160f, yCheese + 0.006f);
                b.Frustum(pa, Vector3.up, 0.033f, 0.033f, 0.012f, 7, pep, false, true);
                b.Frustum(pb, Vector3.up, 0.030f, 0.030f, 0.012f, 7, Dim(pep, 1.08f), false, true);
            }
        }

        public static void DeadTree(MeshBatch b, Vector3 p, System.Random rng)
        {
            Color32 bark = Col(38, 30, 26);
            b.Frustum(p + new Vector3(0f, 2.0f, 0f), Vector3.up, 0.3f, 0.13f, 4.0f, 7, bark, false, true);
            b.Frustum(p + new Vector3(0f, 0.2f, 0f), Vector3.up, 0.42f, 0.3f, 0.4f, 7, Dim(bark, 0.9f), false, false);
            int branches = 5 + rng.Next(3);
            for (int i = 0; i < branches; i++)
            {
                float yaw = (float)rng.NextDouble() * 6.2831853f;
                float h = 2.2f + (float)rng.NextDouble() * 1.9f;
                float len = 1.0f + (float)rng.NextDouble() * 1.3f;
                Vector3 s = p + new Vector3(0f, h, 0f);
                Vector3 e = s + new Vector3(Mathf.Cos(yaw) * len, 0.5f + (float)rng.NextDouble() * 0.9f, Mathf.Sin(yaw) * len);
                b.Rod(s, e, 0.07f, 5, bark);
                float yaw2 = yaw + (rng.NextDouble() < 0.5 ? 0.7f : -0.7f);
                Vector3 e2 = e + new Vector3(Mathf.Cos(yaw2) * len * 0.5f, 0.35f, Mathf.Sin(yaw2) * len * 0.5f);
                b.Rod(e, e2, 0.04f, 4, bark);
            }
        }

        // ------------------------------------------------------------------ рыночный ларёк

        /// <summary>Стопка мелких коробок (овощи, фрукты, консервы) на столешнице: p — низ стопки в координатах ларька.</summary>
        static void Pile(MeshBatch b, Vector3 o, float yaw, Vector3 local, Color32 col, int n, float s)
        {
            for (int k = 0; k < n; k++)
            {
                float off = (k % 2 == 0 ? 0.03f : -0.03f);
                LBoxAll(b, o, yaw, local + new Vector3(off, s * 0.5f + k * s * 0.9f, -off), new Vector3(s, s * 0.9f, s), Dim(col, 0.9f + 0.05f * (k % 3)));
            }
        }

        /// <summary>
        /// Рыночный ларёк: прилавок спереди, задняя стенка с полками, боковые щиты, четыре стойки, полосатый тент со скатом
        /// и бахромой (у тента есть и нижняя грань: снизу он крыша, а не «каркас»), вывеска и товар.
        /// +Z — сторона покупателя, ширина 3.2 м, глубина 2.5 м. variant: 0 — овощи и фрукты, 1 — рыба и бутылки, 2 — ткани и посуда.
        /// </summary>
        public static void Stall(MeshBatch b, Vector3 p, float yaw, Color32 awning, int variant)
        {
            Color32 wood = Col(98, 72, 44);
            Color32 woodD = Col(62, 46, 30);
            Color32 metal = Col(52, 54, 56);
            Color32 canvas2 = Col(168, 158, 132);

            // прилавок, столешница, доски на фасаде
            LBoxAll(b, p, yaw, new Vector3(0f, 0.52f, 0.55f), new Vector3(3.0f, 1.04f, 0.8f), wood);
            LBoxAll(b, p, yaw, new Vector3(0f, 1.065f, 0.55f), new Vector3(3.12f, 0.05f, 0.92f), woodD);
            for (int i = 0; i < 7; i++) LBox(b, p, yaw, new Vector3(-1.2f + i * 0.4f, 0.5f, 0.962f), new Vector3(0.03f, 0.96f, 0.02f), woodD);

            // задняя стенка и три полки, боковые щиты
            LBoxAll(b, p, yaw, new Vector3(0f, 1.2f, -1.02f), new Vector3(3.2f, 2.4f, 0.08f), woodD);
            for (int i = 0; i < 3; i++) LBoxAll(b, p, yaw, new Vector3(0f, 0.85f + i * 0.5f, -0.86f), new Vector3(3.0f, 0.04f, 0.3f), wood);
            for (int s = -1; s <= 1; s += 2) LBoxAll(b, p, yaw, new Vector3(s * 1.56f, 0.7f, -0.35f), new Vector3(0.05f, 1.4f, 1.5f), wood);

            // стойки: сзади выше (скат тента идёт вперёд и вниз)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                LBoxAll(b, p, yaw, new Vector3(sx * 1.58f, 1.26f, 1.2f), new Vector3(0.09f, 2.52f, 0.09f), metal);
                LBoxAll(b, p, yaw, new Vector3(sx * 1.58f, 1.4f, -1.2f), new Vector3(0.09f, 2.8f, 0.09f), metal);
            }
            // балки под тентом
            LBoxAll(b, p, yaw, new Vector3(0f, 2.5f, 1.2f), new Vector3(3.3f, 0.07f, 0.07f), metal);
            LBoxAll(b, p, yaw, new Vector3(0f, 2.78f, -1.2f), new Vector3(3.3f, 0.07f, 0.07f), metal);

            // тент: полосы двух цветов, верх и низ, бахрома у переднего края
            int stripes = 8;
            float sw = 3.5f / stripes;
            for (int i = 0; i < stripes; i++)
            {
                Color32 c = (i & 1) == 0 ? awning : canvas2;
                float xa = -1.75f + i * sw;
                float xb = xa + sw;
                Vector3 a = p + RotY(new Vector3(xa, 2.85f, -1.25f), yaw);
                Vector3 bb = p + RotY(new Vector3(xa, 2.56f, 1.3f), yaw);
                Vector3 cc = p + RotY(new Vector3(xb, 2.56f, 1.3f), yaw);
                Vector3 d = p + RotY(new Vector3(xb, 2.85f, -1.25f), yaw);
                b.Quad(a, bb, cc, d, c);
                Vector3 drop = Vector3.down * 0.04f;
                b.Quad(a + drop, d + drop, cc + drop, bb + drop, Dim(c, 0.7f));
                LBoxAll(b, p, yaw, new Vector3((xa + xb) * 0.5f, 2.44f, 1.3f), new Vector3(sw - 0.02f, 0.24f, 0.03f), c);
            }
            // боковые края тента (чтобы не было просвета сбоку)
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = p + RotY(new Vector3(s * 1.75f, 2.85f, -1.25f), yaw);
                Vector3 bb = p + RotY(new Vector3(s * 1.75f, 2.56f, 1.3f), yaw);
                Vector3 cc = p + RotY(new Vector3(s * 1.75f, 2.52f, 1.3f), yaw);
                Vector3 d = p + RotY(new Vector3(s * 1.75f, 2.81f, -1.25f), yaw);
                if (s > 0) b.Quad(a, bb, cc, d, Dim(awning, 0.8f)); else b.Quad(a, d, cc, bb, Dim(awning, 0.8f));
            }

            // вывеска под бахромой
            LBoxAll(b, p, yaw, new Vector3(0f, 2.16f, 1.33f), new Vector3(1.7f, 0.36f, 0.05f), woodD);
            LBoxAll(b, p, yaw, new Vector3(0f, 2.16f, 1.36f), new Vector3(1.58f, 0.26f, 0.02f), Dim(awning, 1.15f));

            // товар
            if (variant == 0)
            {
                Pile(b, p, yaw, new Vector3(-1.0f, 1.09f, 0.55f), Col(150, 40, 32), 3, 0.2f);
                Pile(b, p, yaw, new Vector3(-0.45f, 1.09f, 0.6f), Col(190, 120, 36), 3, 0.2f);
                Pile(b, p, yaw, new Vector3(0.15f, 1.09f, 0.55f), Col(74, 120, 52), 4, 0.2f);
                Crate(b, p + RotY(new Vector3(0.95f, 1.09f, 0.5f), yaw), yaw + 12f, 0.5f, Dim(wood, 1.1f));
                Pile(b, p, yaw, new Vector3(0.95f, 1.59f, 0.5f), Col(170, 150, 60), 2, 0.16f);
                for (int i = 0; i < 4; i++)
                {
                    float x = -1.1f + i * 0.7f;
                    b.Rod(p + RotY(new Vector3(x, 2.5f, 1.15f), yaw), p + RotY(new Vector3(x, 2.0f, 1.15f), yaw), 0.008f, 4, woodD);
                    LBoxAll(b, p, yaw, new Vector3(x, 1.9f, 1.15f), new Vector3(0.12f, 0.16f, 0.12f), (i & 1) == 0 ? Col(150, 40, 32) : Col(206, 190, 150));
                }
                for (int i = 0; i < 5; i++) Pile(b, p, yaw, new Vector3(-1.2f + i * 0.6f, 0.87f, -0.86f), Col(110 + i * 12, 90, 50), 2, 0.15f);
            }
            else if (variant == 1)
            {
                for (int i = 0; i < 2; i++)
                {
                    LBoxAll(b, p, yaw, new Vector3(-0.9f + i * 1.1f, 1.12f, 0.55f), new Vector3(0.9f, 0.06f, 0.55f), Col(120, 126, 130));
                    for (int k = 0; k < 4; k++)
                        LBoxAll(b, p, yaw, new Vector3(-0.9f + i * 1.1f, 1.18f, 0.4f + k * 0.1f), new Vector3(0.6f, 0.05f, 0.08f), Col(150, 156, 160));
                }
                for (int i = 0; i < 6; i++)
                {
                    float x = -1.25f + i * 0.5f;
                    b.Prism(p + RotY(new Vector3(x, 1.0f, -0.86f), yaw), Vector3.up, 0.04f, 0.28f, 6, i % 2 == 0 ? Col(40, 90, 56) : Col(96, 60, 30), true);
                }
                for (int i = 0; i < 4; i++)
                {
                    float x = -1.0f + i * 0.65f;
                    b.Rod(p + RotY(new Vector3(x, 2.5f, 1.15f), yaw), p + RotY(new Vector3(x, 2.1f, 1.15f), yaw), 0.008f, 4, woodD);
                    LBoxAll(b, p, yaw, new Vector3(x, 1.95f, 1.15f), new Vector3(0.07f, 0.3f, 0.05f), Col(140, 148, 152));
                }
            }
            else
            {
                Color32[] rolls = { Col(120, 36, 40), Col(40, 66, 100), Col(60, 100, 60), Col(150, 120, 50) };
                for (int i = 0; i < 4; i++)
                    b.Prism(p + RotY(new Vector3(-1.0f + i * 0.62f, 1.18f, 0.55f), yaw), RotY(Vector3.right, yaw), 0.09f, 0.55f, 8, rolls[i], true);
                for (int i = 0; i < 3; i++)
                    b.Prism(p + RotY(new Vector3(1.1f, 1.11f + i * 0.04f, 0.5f), yaw), Vector3.up, 0.12f - i * 0.015f, 0.04f, 10, Col(190, 186, 176), true);
                for (int i = 0; i < 5; i++) LBoxAll(b, p, yaw, new Vector3(-1.2f + i * 0.6f, 0.95f, -0.86f), new Vector3(0.3f, 0.2f, 0.24f), rolls[i % 4]);
                for (int i = 0; i < 4; i++)
                {
                    float x = -1.1f + i * 0.72f;
                    b.Rod(p + RotY(new Vector3(x, 2.5f, 1.15f), yaw), p + RotY(new Vector3(x, 1.85f, 1.15f), yaw), 0.008f, 4, woodD);
                    LBoxAll(b, p, yaw, new Vector3(x, 1.7f, 1.15f), new Vector3(0.3f, 0.3f, 0.03f), rolls[i]);
                }
            }
        }

        public static void BusShelter(MeshBatch b, Vector3 p, float yaw)
        {
            Color32 metal = Col(48, 52, 56);
            Color32 glass = Col(18, 24, 30);
            LBoxAll(b, p, yaw, new Vector3(0f, 2.55f, 0f), new Vector3(3.4f, 0.1f, 1.6f), metal);
            float[] px = { -1.6f, 1.6f };
            float[] pz = { -0.7f, 0.7f };
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++) LBox(b, p, yaw, new Vector3(px[i], 1.25f, pz[j]), new Vector3(0.1f, 2.5f, 0.1f), metal);
            }
            LBox(b, p, yaw, new Vector3(0f, 1.3f, -0.7f), new Vector3(3.0f, 1.9f, 0.04f), glass);
            LBox(b, p, yaw, new Vector3(1.62f, 1.25f, 0f), new Vector3(0.05f, 1.7f, 1.2f), glass);
            LBox(b, p, yaw, new Vector3(0f, 0.5f, -0.55f), new Vector3(2.4f, 0.06f, 0.4f), Col(90, 66, 42));
            LBox(b, p, yaw, new Vector3(-1.2f, 1.2f, -0.66f), new Vector3(0.8f, 1.4f, 0.06f), Col(54, 58, 70));
        }

        // ------------------------------------------------------------------ человек (части для анимации ходьбы)

        class HumanStyle
        {
            public Color32 coat, coatLow, pants, shoe, skin, skinDark, hair;
            public int style;
            public bool scarf, pack;
        }

        /// <summary>Внешность водителя автобуса (концовка): форменная куртка, фуражка со значком, седина, без шарфа и рюкзака.</summary>
        public const int DriverLook = -1;

        /// <summary>Основание шеи, м над землёй: вокруг этой точки поворачивается отдельная голова (HumanBody, часть 2).</summary>
        public const float HeadPivotY = 1.5f;

        static HumanStyle LookOf(int index)
        {
            if (index == DriverLook)
            {
                HumanStyle d = LookOf(11);   // кожа, штаны и обувь как у обычного человека, остальное — форма
                d.coat = Col(40, 50, 74);
                d.coatLow = Dim(d.coat, 0.72f);
                d.hair = Col(112, 112, 110);
                d.style = 2;
                d.scarf = false;
                d.pack = false;
                return d;
            }
            System.Random rng = new System.Random(index * 7919 + 13);
            Color c = Color.HSVToRGB((index * 0.137f) % 1f, 0.30f, 0.34f);
            HumanStyle s = new HumanStyle();
            s.coat = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
            s.coatLow = Dim(s.coat, 0.72f);
            s.pants = Col(34, 32, 40);
            s.shoe = Col(20, 18, 20);
            float sk = 0.85f + 0.25f * (float)rng.NextDouble();
            s.skin = Col((int)(150 * sk), (int)(114 * sk), (int)(92 * sk));
            s.skinDark = Dim(s.skin, 0.80f);
            Color32[] hairs = { Col(26, 22, 20), Col(70, 48, 30), Col(130, 104, 60), Col(110, 110, 108) };
            s.hair = hairs[index % hairs.Length];
            s.style = index % 3;
            s.scarf = index % 2 == 0;
            s.pack = index % 3 != 1;
            return s;
        }

        /// <summary>
        /// Туловище и голова человека: пальто, застёжка, шарф, рюкзак, шея, голова с лицом (нос, рот, брови, уши), причёска,
        /// капюшон (с открытым лицом) или кепка. Глаза — отдельно (HumanEyes): открытые и закрытые. Начало — на земле под ногами,
        /// человек смотрит в +Z.
        /// </summary>
        public static void HumanBody(MeshBatch b, int index)
        {
            HumanBody(b, index, 0);
        }

        /// <summary>
        /// То же по частям: 0 — всё вместе; 1 — туловище с шеей, шарфом и рюкзаком; 2 — одна голова (лицо, причёска, капюшон или кепка)
        /// с началом в основании шеи (HeadPivotY): её можно поворачивать отдельно (водитель оглядывается).
        /// </summary>
        public static void HumanBody(MeshBatch b, int index, int part)
        {
            HumanStyle s = LookOf(index);
            Vector3 o = part == 2 ? new Vector3(0f, -HeadPivotY, 0f) : Vector3.zero;
            if (part != 2)
            {
                LBoxAll(b, o, 0f, new Vector3(0f, 0.88f, 0f), new Vector3(0.5f, 0.32f, 0.29f), s.coatLow);
                LBoxAll(b, o, 0f, new Vector3(0f, 1.2f, 0f), new Vector3(0.46f, 0.34f, 0.26f), s.coat);
                LBoxAll(b, o, 0f, new Vector3(0f, 1.4f, 0f), new Vector3(0.56f, 0.1f, 0.28f), Dim(s.coat, 1.1f));
                LBoxAll(b, o, 0f, new Vector3(0f, 1.0f, 0.148f), new Vector3(0.025f, 0.62f, 0.012f), Dim(s.coat, 0.55f));   // застёжка
                for (int i = 0; i < 3; i++) LBoxAll(b, o, 0f, new Vector3(0.035f, 0.82f + i * 0.17f, 0.152f), new Vector3(0.03f, 0.03f, 0.012f), Col(150, 138, 96));
                LBoxAll(b, o, 0f, new Vector3(0f, 1.5f, 0f), new Vector3(0.12f, 0.08f, 0.12f), s.skinDark);   // шея
                if (index == DriverLook) LBoxAll(b, o, 0f, new Vector3(0f, 1.468f, 0.07f), new Vector3(0.2f, 0.05f, 0.12f), Col(176, 174, 166));   // воротник рубашки
            }

            if (part != 1)
            {
                // голова и лицо (передняя грань головы — z = +0.12)
                LBoxAll(b, o, 0f, new Vector3(0f, 1.63f, 0f), new Vector3(0.24f, 0.26f, 0.24f), s.skin);
                LBoxAll(b, o, 0f, new Vector3(-0.125f, 1.62f, 0.0f), new Vector3(0.02f, 0.06f, 0.04f), s.skinDark);        // уши
                LBoxAll(b, o, 0f, new Vector3(0.125f, 1.62f, 0.0f), new Vector3(0.02f, 0.06f, 0.04f), s.skinDark);
                LBoxAll(b, o, 0f, new Vector3(0f, 1.615f, 0.13f), new Vector3(0.035f, 0.055f, 0.03f), Dim(s.skin, 0.88f));  // нос
                LBoxAll(b, o, 0f, new Vector3(0f, 1.567f, 0.123f), new Vector3(0.075f, 0.014f, 0.012f), Col(112, 52, 50));  // рот
                LBoxAll(b, o, 0f, new Vector3(-0.055f, 1.69f, 0.123f), new Vector3(0.06f, 0.014f, 0.012f), Dim(s.hair, 0.8f));   // брови
                LBoxAll(b, o, 0f, new Vector3(0.055f, 1.69f, 0.123f), new Vector3(0.06f, 0.014f, 0.012f), Dim(s.hair, 0.8f));

                if (s.style == 0)
                {
                    // причёска: макушка, затылок, чёлка
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.755f, -0.01f), new Vector3(0.26f, 0.09f, 0.26f), s.hair);
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.66f, -0.115f), new Vector3(0.25f, 0.23f, 0.05f), s.hair);
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.728f, 0.125f), new Vector3(0.245f, 0.045f, 0.03f), s.hair);
                    LBoxAll(b, o, 0f, new Vector3(-0.128f, 1.67f, -0.02f), new Vector3(0.02f, 0.17f, 0.2f), s.hair);
                    LBoxAll(b, o, 0f, new Vector3(0.128f, 1.67f, -0.02f), new Vector3(0.02f, 0.17f, 0.2f), s.hair);
                }
                else if (s.style == 1)
                {
                    // капюшон: лицо открыто (раньше здесь был чёрный квадрат)
                    Color32 hood = Dim(s.coat, 0.85f);
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.785f, -0.015f), new Vector3(0.31f, 0.07f, 0.32f), hood);
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.65f, -0.15f), new Vector3(0.31f, 0.3f, 0.06f), hood);
                    LBoxAll(b, o, 0f, new Vector3(-0.15f, 1.65f, -0.02f), new Vector3(0.05f, 0.3f, 0.3f), hood);
                    LBoxAll(b, o, 0f, new Vector3(0.15f, 1.65f, -0.02f), new Vector3(0.05f, 0.3f, 0.3f), hood);
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.748f, 0.145f), new Vector3(0.25f, 0.035f, 0.05f), Dim(hood, 0.9f));
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.715f, 0.123f), new Vector3(0.2f, 0.03f, 0.012f), Dim(s.hair, 1.1f));   // прядь из-под капюшона
                }
                else
                {
                    // кепка с козырьком и затылок
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.775f, 0f), new Vector3(0.27f, 0.07f, 0.27f), Dim(s.coat, 0.6f));
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.735f, 0.14f), new Vector3(0.27f, 0.025f, 0.12f), Dim(s.coat, 0.5f));
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.67f, -0.115f), new Vector3(0.245f, 0.17f, 0.05f), s.hair);
                    if (index == DriverLook)
                    {
                        LBoxAll(b, o, 0f, new Vector3(0f, 1.78f, 0.138f), new Vector3(0.055f, 0.035f, 0.012f), Col(190, 160, 70));   // значок на фуражке
                        LBoxAll(b, o, 0f, new Vector3(-0.126f, 1.7f, 0f), new Vector3(0.02f, 0.1f, 0.2f), Dim(s.hair, 0.9f));       // седые виски
                        LBoxAll(b, o, 0f, new Vector3(0.126f, 1.7f, 0f), new Vector3(0.02f, 0.1f, 0.2f), Dim(s.hair, 0.9f));
                    }
                }
            }
            if (part != 2)
            {
                if (s.scarf) LBoxAll(b, o, 0f, new Vector3(0f, 1.455f, 0.01f), new Vector3(0.3f, 0.1f, 0.3f), Col(120, 40, 36));
                if (s.pack)
                {
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.15f, -0.2f), new Vector3(0.34f, 0.4f, 0.14f), Col(48, 58, 44));
                    LBoxAll(b, o, 0f, new Vector3(0f, 1.04f, -0.275f), new Vector3(0.26f, 0.16f, 0.02f), Col(38, 46, 36));
                }
            }
        }

        /// <summary>Глаза: открытые (белок и зрачок) или закрытые (тёмная чёрточка). Рисуются перед лицом, начало — как у тела.</summary>
        public static void HumanEyes(MeshBatch b, int index, bool open)
        {
            HumanEyes(b, index, open, 0f);
        }

        /// <summary>pivotY больше нуля: глаза строятся от основания шеи на этой высоте (для отдельной головы, HumanBody часть 2).</summary>
        public static void HumanEyes(MeshBatch b, int index, bool open, float pivotY)
        {
            Vector3 o = new Vector3(0f, -pivotY, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.055f;
                if (open)
                {
                    LBoxAll(b, o, 0f, new Vector3(x, 1.655f, 0.126f), new Vector3(0.05f, 0.032f, 0.012f), Col(226, 222, 214));
                    LBoxAll(b, o, 0f, new Vector3(x - side * 0.004f, 1.654f, 0.133f), new Vector3(0.024f, 0.028f, 0.012f), Col(30, 26, 28));
                }
                else
                {
                    LBoxAll(b, o, 0f, new Vector3(x, 1.65f, 0.126f), new Vector3(0.05f, 0.01f, 0.012f), Col(40, 30, 30));
                }
            }
        }

        /// <summary>Нога (штанина и ботинок): начало — в тазобедренном суставе, нога висит вниз; качается вокруг оси X.</summary>
        public static void HumanLeg(MeshBatch b, int index)
        {
            HumanStyle s = LookOf(index);
            Vector3 o = Vector3.zero;
            LBoxAll(b, o, 0f, new Vector3(0f, -0.41f, 0f), new Vector3(0.17f, 0.82f, 0.2f), s.pants);
            LBoxAll(b, o, 0f, new Vector3(0f, -0.64f, 0.0f), new Vector3(0.175f, 0.06f, 0.205f), Dim(s.pants, 0.8f));   // колено
            LBoxAll(b, o, 0f, new Vector3(0f, -0.79f, 0.05f), new Vector3(0.19f, 0.1f, 0.32f), s.shoe);
            LBoxAll(b, o, 0f, new Vector3(0f, -0.745f, 0.0f), new Vector3(0.18f, 0.03f, 0.21f), Dim(s.pants, 0.7f));   // низ штанины
        }

        /// <summary>Рука (рукав, манжета, кисть): начало — в плечевом суставе, рука висит вниз.</summary>
        public static void HumanArm(MeshBatch b, int index)
        {
            HumanStyle s = LookOf(index);
            Vector3 o = Vector3.zero;
            LBoxAll(b, o, 0f, new Vector3(0f, -0.28f, 0f), new Vector3(0.12f, 0.56f, 0.15f), s.coat);
            LBoxAll(b, o, 0f, new Vector3(0f, -0.545f, 0f), new Vector3(0.13f, 0.05f, 0.16f), s.coatLow);
            LBoxAll(b, o, 0f, new Vector3(0f, -0.61f, 0f), new Vector3(0.1f, 0.12f, 0.11f), s.skin);
        }

        // ------------------------------------------------------------------ автобус и генератор

        /// <summary>Кузов заброшенного автобуса: центр на земле, длина вдоль Z (перед — +Z), без динамических огней.</summary>
        public static void BusBody(MeshBatch b)
        {
            Color32 body = Col(150, 104, 34);
            Color32 bodyLow = Dim(body, 0.82f);
            Color32 glass = Col(14, 18, 26);
            Color32 dark = Col(24, 24, 28);
            Color32 cream = Col(168, 158, 136);
            Vector3 o = Vector3.zero;

            // нижний кузов — от 0.4 до 1.6 м, верхний (пояс окон) — от 1.6 до 3.0 м: без зазора между ними
            // (раньше между 1.6 и 1.9 м было сквозное «разрезание» автобуса)
            LBoxAll(b, o, 0f, new Vector3(0f, 1.0f, 0f), new Vector3(2.7f, 1.2f, 11f), bodyLow);
            LBoxAll(b, o, 0f, new Vector3(0f, 2.3f, 0f), new Vector3(2.62f, 1.4f, 10.8f), body);
            LBoxAll(b, o, 0f, new Vector3(0f, 3.07f, 0f), new Vector3(2.58f, 0.14f, 10.8f), Dim(body, 1.08f));
            // окна по бортам: тёмное стекло и простенки
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 1.322f;
                LBoxAll(b, o, 0f, new Vector3(x, 2.45f, 0f), new Vector3(0.04f, 0.86f, 10.1f), glass);
                for (int i = 0; i < 9; i++)
                {
                    float z = -4.8f + i * 1.2f;
                    LBoxAll(b, o, 0f, new Vector3(side * 1.335f, 2.45f, z), new Vector3(0.06f, 0.92f, 0.16f), Dim(body, 0.95f));
                }
                LBoxAll(b, o, 0f, new Vector3(side * 1.352f, 1.62f, 0f), new Vector3(0.025f, 0.14f, 10.8f), cream);
                // колёсные арки и колёса
                float[] wz = { -3.6f, 3.6f };
                for (int k = 0; k < 2; k++)
                {
                    LBoxAll(b, o, 0f, new Vector3(side * 1.36f, 0.72f, wz[k]), new Vector3(0.03f, 0.86f, 1.5f), Col(10, 10, 12));
                    b.Prism(new Vector3(side * 1.22f, 0.55f, wz[k]), Vector3.right, 0.55f, 0.32f, 10, Col(14, 14, 16), true);
                    b.Prism(new Vector3(side * 1.4f, 0.55f, wz[k]), Vector3.right, 0.26f, 0.05f, 10, Col(84, 84, 90), true);
                }
            }
            // лобовое и заднее стекло, фары, бампера
            LBoxAll(b, o, 0f, new Vector3(0f, 2.4f, 5.42f), new Vector3(2.3f, 1.0f, 0.04f), glass);
            LBoxAll(b, o, 0f, new Vector3(0f, 2.4f, -5.42f), new Vector3(2.0f, 0.8f, 0.04f), glass);
            LBoxAll(b, o, 0f, new Vector3(0f, 3.0f, 5.45f), new Vector3(1.7f, 0.28f, 0.05f), Col(30, 54, 50));
            LBoxAll(b, o, 0f, new Vector3(0f, 0.55f, 5.55f), new Vector3(2.74f, 0.3f, 0.2f), dark);
            LBoxAll(b, o, 0f, new Vector3(0f, 0.55f, -5.55f), new Vector3(2.74f, 0.3f, 0.2f), dark);
            LBoxAll(b, o, 0f, new Vector3(0f, 1.0f, 5.52f), new Vector3(1.0f, 0.5f, 0.05f), Col(36, 36, 40));
            LBoxAll(b, o, 0f, new Vector3(-0.9f, 1.0f, 5.52f), new Vector3(0.36f, 0.26f, 0.05f), Col(96, 98, 92));
            LBoxAll(b, o, 0f, new Vector3(0.9f, 1.0f, 5.52f), new Vector3(0.36f, 0.26f, 0.05f), Col(96, 98, 92));
            // зеркала, крыша
            for (int side = -1; side <= 1; side += 2)
            {
                b.Rod(new Vector3(side * 1.35f, 2.5f, 5.2f), new Vector3(side * 1.62f, 2.45f, 5.3f), 0.025f, 4, dark);
                LBoxAll(b, o, 0f, new Vector3(side * 1.64f, 2.4f, 5.3f), new Vector3(0.1f, 0.34f, 0.06f), dark);
            }
            LBoxAll(b, o, 0f, new Vector3(0f, 3.28f, -3.0f), new Vector3(1.5f, 0.3f, 1.7f), Col(60, 62, 66));
            LBoxAll(b, o, 0f, new Vector3(0f, 3.2f, 1.6f), new Vector3(0.8f, 0.14f, 0.8f), Col(70, 72, 76));
            LBoxAll(b, o, 0f, new Vector3(0f, 3.2f, 3.4f), new Vector3(0.8f, 0.14f, 0.8f), Col(70, 72, 76));
            // дверь у центра левого борта (там кольцо для зарядки)
            LBoxAll(b, o, 0f, new Vector3(-1.352f, 1.85f, 1.5f), new Vector3(0.05f, 1.9f, 1.1f), Col(20, 26, 34));
            LBoxAll(b, o, 0f, new Vector3(-1.36f, 1.85f, 1.5f), new Vector3(0.03f, 1.94f, 0.06f), dark);
            LBoxAll(b, o, 0f, new Vector3(-1.36f, 0.95f, 1.5f), new Vector3(0.12f, 0.1f, 1.1f), dark);
        }

        /// <summary>Генератор: рама, двигатель, топливный бак, выхлоп, щиток и провода.</summary>
        public static void GeneratorBody(MeshBatch b)
        {
            Color32 frame = Col(30, 30, 34);
            Color32 engine = Col(132, 74, 28);
            Color32 tank = Col(96, 40, 30);
            Color32 dark = Col(22, 22, 26);
            Vector3 o = Vector3.zero;
            LBoxAll(b, o, 0f, new Vector3(0f, 0.12f, 0f), new Vector3(1.9f, 0.16f, 1.2f), frame);
            LBoxAll(b, o, 0f, new Vector3(-0.1f, 0.68f, 0f), new Vector3(1.3f, 0.92f, 0.9f), engine);
            LBoxAll(b, o, 0f, new Vector3(-0.1f, 1.18f, 0f), new Vector3(1.1f, 0.1f, 0.8f), Dim(engine, 0.7f));
            for (int i = 0; i < 4; i++) LBoxAll(b, o, 0f, new Vector3(-0.55f + i * 0.28f, 0.7f, 0.46f), new Vector3(0.07f, 0.8f, 0.04f), Dim(engine, 0.72f));
            b.Prism(new Vector3(0.75f, 0.95f, 0f), Vector3.forward, 0.3f, 0.95f, 8, tank, true);
            b.Prism(new Vector3(0.75f, 1.25f, 0f), Vector3.up, 0.07f, 0.1f, 6, dark, true);
            b.Rod(new Vector3(-0.6f, 1.2f, 0.25f), new Vector3(-0.6f, 1.95f, 0.25f), 0.07f, 6, dark);
            b.Rod(new Vector3(-0.6f, 1.95f, 0.25f), new Vector3(-0.35f, 2.05f, 0.25f), 0.07f, 6, dark);
            LBoxAll(b, o, 0f, new Vector3(0.05f, 0.75f, -0.5f), new Vector3(0.8f, 0.5f, 0.1f), dark);
            LBoxAll(b, o, 0f, new Vector3(0.05f, 0.78f, -0.56f), new Vector3(0.64f, 0.34f, 0.03f), Col(70, 74, 70));
            b.Rod(new Vector3(0.45f, 0.5f, -0.5f), new Vector3(1.05f, 0.1f, -0.7f), 0.03f, 4, Col(14, 14, 14));
            LBoxAll(b, o, 0f, new Vector3(-0.9f, 0.5f, 0f), new Vector3(0.06f, 0.06f, 0.9f), frame);
            LBoxAll(b, o, 0f, new Vector3(-0.9f, 0.9f, 0f), new Vector3(0.06f, 0.06f, 0.9f), frame);
        }

        // ------------------------------------------------------------------ детали зданий

        /// <summary>Дверь с рамой, ступенькой и козырьком. p — основание на земле у стены; yaw — куда смотрит дверь (наружу).</summary>
        public static void Door(MeshBatch b, Vector3 p, float yaw, Color32 frame)
        {
            LBoxAll(b, p, yaw, new Vector3(0f, 1.25f, 0.12f), new Vector3(1.7f, 2.5f, 0.24f), frame);
            LBoxAll(b, p, yaw, new Vector3(0f, 1.1f, 0.26f), new Vector3(1.2f, 2.2f, 0.06f), Col(20, 18, 18));
            LBoxAll(b, p, yaw, new Vector3(0.38f, 1.05f, 0.31f), new Vector3(0.06f, 0.06f, 0.06f), Col(120, 110, 80));
            LBox(b, p, yaw, new Vector3(0f, 0.07f, 0.55f), new Vector3(2.1f, 0.14f, 0.8f), Dim(frame, 0.9f));
            LBoxAll(b, p, yaw, new Vector3(0f, 2.78f, 0.55f), new Vector3(2.3f, 0.12f, 1.1f), Dim(frame, 0.8f));
            LBox(b, p, yaw, new Vector3(-1.05f, 2.45f, 0.5f), new Vector3(0.05f, 0.6f, 0.05f), Col(36, 36, 40));
            LBox(b, p, yaw, new Vector3(1.05f, 2.45f, 0.5f), new Vector3(0.05f, 0.6f, 0.05f), Col(36, 36, 40));
        }

        /// <summary>Витрина магазина с тентом и опущенной шторой.</summary>
        public static void Shopfront(MeshBatch b, Vector3 p, float yaw, float width, Color32 awning)
        {
            LBoxAll(b, p, yaw, new Vector3(0f, 1.2f, 0.08f), new Vector3(width, 2.2f, 0.14f), Col(18, 22, 26));
            LBox(b, p, yaw, new Vector3(0f, 0.14f, 0.1f), new Vector3(width + 0.3f, 0.28f, 0.24f), Col(64, 60, 56));
            LBoxAll(b, p, yaw, new Vector3(0f, 2.55f, 0.55f), new Vector3(width + 0.4f, 0.14f, 1.2f), awning);
            LBoxAll(b, p, yaw, new Vector3(0f, 2.42f, 1.12f), new Vector3(width + 0.4f, 0.28f, 0.05f), Dim(awning, 0.75f));
            for (int i = 0; i < 4; i++) LBoxAll(b, p, yaw, new Vector3(0f, 0.5f + i * 0.2f, 0.17f), new Vector3(width * 0.8f, 0.03f, 0.04f), Col(70, 72, 74));
        }

        public static void Balcony(MeshBatch b, Vector3 p, float yaw, float width)
        {
            Color32 conc = Col(78, 76, 72);
            LBoxAll(b, p, yaw, new Vector3(0f, 0f, 0.55f), new Vector3(width, 0.14f, 1.1f), conc);
            LBox(b, p, yaw, new Vector3(0f, 0.55f, 1.07f), new Vector3(width, 0.9f, 0.06f), Dim(conc, 0.9f));
            LBox(b, p, yaw, new Vector3(-width * 0.5f + 0.03f, 0.55f, 0.55f), new Vector3(0.06f, 0.9f, 1.04f), Dim(conc, 0.9f));
            LBox(b, p, yaw, new Vector3(width * 0.5f - 0.03f, 0.55f, 0.55f), new Vector3(0.06f, 0.9f, 1.04f), Dim(conc, 0.9f));
        }

        public static void RoofAC(MeshBatch b, Vector3 p, float yaw)
        {
            LBoxAll(b, p, yaw, new Vector3(0f, 0.6f, 0f), new Vector3(2.2f, 1.2f, 1.6f), Col(64, 66, 68));
            LPrism(b, p, yaw, new Vector3(0f, 1.25f, 0f), Vector3.up, 0.5f, 0.06f, 8, Col(26, 26, 28), true);
            LBoxAll(b, p, yaw, new Vector3(0f, 0.1f, 0f), new Vector3(2.3f, 0.2f, 1.7f), Col(40, 40, 44));
        }

        public static void WaterTank(MeshBatch b, Vector3 p)
        {
            Color32 wood = Col(82, 66, 52);
            b.Frustum(p + new Vector3(0f, 3.4f, 0f), Vector3.up, 1.5f, 1.5f, 2.4f, 12, wood, true, true);
            b.Prism(p + new Vector3(0f, 2.6f, 0f), Vector3.up, 1.56f, 0.12f, 12, Col(40, 38, 36), false);
            b.Prism(p + new Vector3(0f, 3.4f, 0f), Vector3.up, 1.56f, 0.12f, 12, Col(40, 38, 36), false);
            b.Frustum(p + new Vector3(0f, 4.9f, 0f), Vector3.up, 1.6f, 0.2f, 0.8f, 12, Col(60, 56, 54), false, false);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.5708f + 0.785f;
                b.Rod(p + new Vector3(Mathf.Cos(a) * 1.2f, 0f, Mathf.Sin(a) * 1.2f), p + new Vector3(Mathf.Cos(a) * 1.2f, 2.2f, Mathf.Sin(a) * 1.2f), 0.09f, 5, Col(46, 42, 40));
            }
        }

        public static void Antenna(MeshBatch b, Vector3 p, float height)
        {
            Color32 m = Col(40, 40, 44);
            b.Rod(p, p + new Vector3(0f, height, 0f), 0.07f, 5, m);
            for (int i = 0; i < 3; i++)
            {
                float y = height * (0.45f + i * 0.2f);
                float w = 1.4f - i * 0.35f;
                b.Rod(p + new Vector3(-w * 0.5f, y, 0f), p + new Vector3(w * 0.5f, y, 0f), 0.03f, 4, m);
            }
        }

        public static void RoofHatch(MeshBatch b, Vector3 p, float yaw)
        {
            LBoxAll(b, p, yaw, new Vector3(0f, 1.2f, 0f), new Vector3(2.6f, 2.4f, 2.2f), Col(70, 68, 64));
            LBoxAll(b, p, yaw, new Vector3(0f, 2.45f, 0f), new Vector3(2.8f, 0.12f, 2.4f), Col(50, 50, 52));
            LBoxAll(b, p, yaw, new Vector3(0f, 1.0f, 1.12f), new Vector3(1.0f, 2.0f, 0.06f), Col(30, 30, 32));
        }

        public static void Downpipe(MeshBatch b, Vector3 p, float height)
        {
            b.Prism(p + new Vector3(0f, height * 0.5f, 0f), Vector3.up, 0.07f, height, 6, Col(44, 46, 48), false);
            for (int i = 1; i < 5; i++) b.Prism(p + new Vector3(0f, height * i / 5f, 0f), Vector3.up, 0.095f, 0.06f, 6, Col(30, 30, 32), false);
        }

        /// <summary>Горизонтальная труба вдоль стены (промзона) с кронштейнами.</summary>
        public static void WallPipe(MeshBatch b, Vector3 a, Vector3 c, float radius)
        {
            b.Rod(a, c, radius, 8, Col(66, 52, 44));
            Vector3 d = c - a;
            float len = d.magnitude;
            int n = Mathf.Max(1, (int)(len / 3f));
            for (int i = 0; i <= n; i++)
            {
                Vector3 q = a + d * (i / (float)n);
                b.Prism(q, d / len, radius * 1.3f, 0.08f, 8, Col(36, 34, 34), false);
            }
        }
    }
}
