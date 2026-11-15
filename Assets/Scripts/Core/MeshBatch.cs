using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PokaGoryu
{
    /// <summary>
    /// Копилка коробок, призм и четырёхугольников: сотни зданий и деталей собираются в один меш (один draw call).
    /// Цвет вершины — оттенок объекта (шейдер умножает на него), UV не нужны: шейдер мира использует мировые координаты.
    /// Грани можно отбрасывать (низ коробки на земле никто не видит) — это экономит вершины.
    /// </summary>
    public class MeshBatch
    {
        public const int AllFaces = 63;                 // +X -X +Y -Y +Z -Z
        public const int NoBottom = 63 & ~8;            // без нижней грани
        public const int NoBottomBack = 63 & ~8 & ~2;   // без низа и задней (-X) грани

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Color32> cols = new List<Color32>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public int Count { get { return verts.Count; } }

        /// <summary>
        /// Единичная нормаль грани (a, b, c): Cross(b-a, c-a) смотрит наружу. У миллиметровых граней (пальцы, ногти, торцы) векторное
        /// произведение меньше 1e-5, и Vector3.normalized возвращает для него нуль: нулевая нормаль даёт в шейдере NaN и чёрное пятно.
        /// Поэтому рёбра растянуты в 256 раз (на нормаль больших граней это не влияет).
        /// </summary>
        static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross((b - a) * 256f, (c - a) * 256f);
            float m = n.magnitude;
            return m > 1e-9f ? n / m : Vector3.up;
        }

        /// <summary>Четырёхугольник: порядок вершин такой, что Cross(b-a, c-a) смотрит наружу.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
        {
            Vector3 n = FaceNormal(a, b, c);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++)
            {
                norms.Add(n);
                cols.Add(col);
                uvs.Add(Vector2.zero);
            }
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        /// <summary>Четырёхугольник с текстурными координатами (вывески, плакаты): ua..ud — UV вершин a..d.</summary>
        public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color32 col)
        {
            Vector3 n = FaceNormal(a, b, c);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++)
            {
                norms.Add(n);
                cols.Add(col);
            }
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        /// <summary>Треугольник: Cross(b-a, c-a) смотрит наружу.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col)
        {
            Vector3 n = FaceNormal(a, b, c);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            for (int k = 0; k < 3; k++)
            {
                norms.Add(n);
                cols.Add(col);
                uvs.Add(Vector2.zero);
            }
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Коробка по центру и размеру.</summary>
        public void Box(Vector3 center, Vector3 size, Color32 col)
        {
            BoxYaw(center, size, 0f, col, AllFaces);
        }

        /// <summary>Коробка, повёрнутая вокруг вертикальной оси.</summary>
        public void BoxYaw(Vector3 center, Vector3 size, float yawDeg, Color32 col)
        {
            BoxYaw(center, size, yawDeg, col, AllFaces);
        }

        /// <summary>Коробка с выбором граней (маска: +X -X +Y -Y +Z -Z).</summary>
        public void BoxYaw(Vector3 center, Vector3 size, float yawDeg, Color32 col, int faces)
        {
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;
            float rad = yawDeg * 0.0174532925f;
            float cs = Mathf.Cos(rad);
            float sn = Mathf.Sin(rad);

            if ((faces & 1) != 0)   // +X
                Face(center, cs, sn, new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(hx, -hy, hz), col);
            if ((faces & 2) != 0)   // -X
                Face(center, cs, sn, new Vector3(-hx, -hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, -hy, -hz), col);
            if ((faces & 4) != 0)   // +Y
                Face(center, cs, sn, new Vector3(-hx, hy, -hz), new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, hy, -hz), col);
            if ((faces & 8) != 0)   // -Y
                Face(center, cs, sn, new Vector3(-hx, -hy, hz), new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), col);
            if ((faces & 16) != 0)  // +Z
                Face(center, cs, sn, new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, -hy, hz), col);
            if ((faces & 32) != 0)  // -Z
                Face(center, cs, sn, new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, -hy, -hz), col);
        }

        void Face(Vector3 center, float cs, float sn, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
        {
            Quad(Rot(center, cs, sn, a), Rot(center, cs, sn, b), Rot(center, cs, sn, c), Rot(center, cs, sn, d), col);
        }

        static Vector3 Rot(Vector3 center, float cs, float sn, Vector3 p)
        {
            return new Vector3(center.x + p.x * cs + p.z * sn, center.y + p.y, center.z - p.x * sn + p.z * cs);
        }

        // ------------------------------------------------------------------ призмы и конусы

        static void Frame(Vector3 axis, out Vector3 u, out Vector3 w)
        {
            Vector3 h = Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right;
            u = Vector3.Cross(axis, h).normalized;
            w = Vector3.Cross(axis, u);   // (u, w, axis) — правая тройка: u x w = axis
        }

        /// <summary>
        /// Усечённый конус / призма вдоль оси axis (единичный вектор) с центром в center: радиус r0 у начала, r1 у конца.
        /// sides — число граней (8 — «круглый» столб); capStart/capEnd — торцы.
        /// </summary>
        public void Frustum(Vector3 center, Vector3 axis, float r0, float r1, float length, int sides, Color32 col, bool capStart, bool capEnd)
        {
            axis.Normalize();
            Vector3 u, w;
            Frame(axis, out u, out w);
            Vector3 c0 = center - axis * (length * 0.5f);
            Vector3 c1 = center + axis * (length * 0.5f);
            float step = 6.2831853f / sides;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0);
                Vector3 d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                Vector3 b0 = c0 + d0 * r0;
                Vector3 b1 = c0 + d1 * r0;
                Vector3 t0 = c1 + d0 * r1;
                Vector3 t1 = c1 + d1 * r1;
                Quad(b1, t1, t0, b0, col);              // наружу
                if (capEnd && r1 > 0.001f) Tri(c1, t0, t1, col);
                if (capStart && r0 > 0.001f) Tri(c0, b1, b0, col);
            }
        }

        /// <summary>Призма (цилиндр) вдоль оси.</summary>
        public void Prism(Vector3 center, Vector3 axis, float radius, float length, int sides, Color32 col, bool caps)
        {
            Frustum(center, axis, radius, radius, length, sides, col, caps, caps);
        }

        /// <summary>Стержень между двумя точками (труба, ветка, перекладина).</summary>
        public void Rod(Vector3 p0, Vector3 p1, float radius, int sides, Color32 col)
        {
            Vector3 d = p1 - p0;
            float len = d.magnitude;
            if (len < 0.001f) return;
            Prism((p0 + p1) * 0.5f, d / len, radius, len, sides, col, true);
        }

        // ------------------------------------------------------------------ сборка

        public Mesh Build(string name)
        {
            Mesh m = new Mesh();
            m.name = name;
            if (verts.Count > 60000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = verts.ToArray();
            m.normals = norms.ToArray();
            m.colors32 = cols.ToArray();
            m.uv = uvs.ToArray();
            m.triangles = tris.ToArray();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// Набор батчей по клеткам пространства: каждая клетка — свой меш. Камера отсекает невидимые клетки,
    /// а ChunkCuller выключает дальние (за туманом): город рисуется не целиком, а только то, что рядом.
    /// </summary>
    public class BatchSet
    {
        readonly Dictionary<int, MeshBatch> map = new Dictionary<int, MeshBatch>();
        readonly float chunk;

        public BatchSet(float chunkSize)
        {
            chunk = chunkSize;
        }

        public MeshBatch At(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / chunk);
            int cz = Mathf.FloorToInt(z / chunk);
            int key = (cx + 1000) * 4096 + (cz + 1000);
            MeshBatch b;
            if (!map.TryGetValue(key, out b))
            {
                b = new MeshBatch();
                map[key] = b;
            }
            return b;
        }

        public MeshBatch At(Vector3 p)
        {
            return At(p.x, p.z);
        }

        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<int, MeshBatch> kv in map) n += kv.Value.Count;
                return n;
            }
        }

        /// <summary>Собрать меши по клеткам и повесить на объекты с одним материалом. Возвращает число мешей.</summary>
        public int Flush(Transform root, Material mat, string name, List<Renderer> outRenderers)
        {
            int n = 0;
            foreach (KeyValuePair<int, MeshBatch> kv in map)
            {
                if (kv.Value.Count == 0) continue;
                Mesh mesh = kv.Value.Build(name + "_" + kv.Key);
                GameObject go = Gfx.MeshObject(root, mesh, mat, name);
                if (outRenderers != null)
                {
                    Renderer r = go.GetComponent<Renderer>();
                    if (r != null) outRenderers.Add(r);
                }
                n++;
            }
            return n;
        }
    }
}
