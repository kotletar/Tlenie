using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Объёмный дым: сотни мягких «клубов» (спрайтов с шумовой текстурой) в ОДНОМ динамическом меше, один вызов отрисовки.
    /// Каждый кадр квадраты разворачиваются к камере, сортируются от дальних к ближним (альфа-смешивание без записи глубины) и гаснут,
    /// когда камера оказывается внутри клуба: иначе близкий спрайт обрезался бы ближней плоскостью. Положение, размер и прозрачность
    /// клубов задаёт сценарий (Puff в массиве P), поэтому дым, как и вся концовка, — чистая функция времени.
    /// Клуб выглядит объёмным из-за текстуры: плотность из шума с искажением координат, «нормаль» из её градиента и свет сверху-справа,
    /// тонкие края светлее, ядро темнее. Цвет и прозрачность каждого клуба — вершинные (Tint).
    /// </summary>
    public sealed class SmokeField
    {
        public struct Puff
        {
            public Vector3 Pos;     // центр клуба в мире
            public float Size;      // поперечник, м
            public float Roll;      // поворот картинки вокруг оси взгляда, градусы
            public int Tile;        // какая из четырёх картинок атласа (0..3)
            public Color Tint;      // rgb — цвет, a — прозрачность (0 — клуба нет)
        }

        public readonly Puff[] P;

        /// <summary>Расстояния до камеры (м): ближе NearA клуб невидим (иначе ближняя плоскость срезала бы его), дальше NearB — непрозрачен.</summary>
        public float NearA = 1.2f;
        public float NearB = 5.0f;

        readonly GameObject go;
        readonly Mesh mesh;
        readonly Material mat;
        readonly Vector3[] verts;
        readonly Vector2[] uvs;
        readonly Color32[] cols;
        readonly float[] keys;
        readonly int[] order;
        const int TileSize = 128;

        public SmokeField(Transform parent, Texture2D atlas, int count, int renderQueue, string name)
        {
            P = new Puff[count];
            verts = new Vector3[count * 4];
            uvs = new Vector2[count * 4];
            cols = new Color32[count * 4];
            keys = new float[count];
            order = new int[count];
            int[] tris = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = i * 4;
                int t = i * 6;
                tris[t] = v;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }
            mesh = new Mesh();
            mesh.name = "PG_" + name;
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(20000f, 20000f, 20000f));

            mat = Gfx.Unlit(Color.white, atlas, false, true, 0f);
            mat.renderQueue = renderQueue;
            go = Gfx.MeshObject(parent, mesh, mat, name);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
        }

        public void SetActive(bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        public void Destroy()
        {
            if (go != null) Object.Destroy(go);
            if (mesh != null) Object.Destroy(mesh);
            if (mat != null) Object.Destroy(mat);
        }

        /// <summary>
        /// Собрать меш для камеры: Pos каждого клуба — в мировых координатах. Невидимые клубы (прозрачность 0 или вплотную к глазу) схлопываются.
        /// Возвращает, сколько клубов реально нарисовано.
        /// </summary>
        public int Rebuild(Vector3 camPos, Quaternion camRot)
        {
            Vector3 right = camRot * Vector3.right;
            Vector3 up = camRot * Vector3.up;
            int n = 0;
            for (int i = 0; i < P.Length; i++)
            {
                if (P[i].Tint.a <= 0.002f || P[i].Size <= 0.01f) continue;
                keys[n] = -(P[i].Pos - camPos).sqrMagnitude;   // сначала самые дальние
                order[n] = i;
                n++;
            }
            System.Array.Sort(keys, order, 0, n);

            int w = 0;
            for (int k = 0; k < n; k++)
            {
                Puff p = P[order[k]];
                float d = Mathf.Sqrt(-keys[k]);
                float fade = Mathf.Clamp01((d - NearA) / (NearB - NearA));
                fade = fade * fade * (3f - 2f * fade);
                float alpha = p.Tint.a * fade;
                if (alpha <= 0.002f) continue;

                float rad = p.Roll * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rad);
                float sn = Mathf.Sin(rad);
                Vector3 r = (right * cs + up * sn) * (p.Size * 0.5f);
                Vector3 u = (-right * sn + up * cs) * (p.Size * 0.5f);
                int v = w * 4;
                verts[v] = p.Pos - r - u;
                verts[v + 1] = p.Pos - r + u;
                verts[v + 2] = p.Pos + r + u;
                verts[v + 3] = p.Pos + r - u;
                float tx = (p.Tile & 1) * 0.5f;
                float ty = ((p.Tile >> 1) & 1) * 0.5f;
                const float inset = 0.004f;
                uvs[v] = new Vector2(tx + inset, ty + inset);
                uvs[v + 1] = new Vector2(tx + inset, ty + 0.5f - inset);
                uvs[v + 2] = new Vector2(tx + 0.5f - inset, ty + 0.5f - inset);
                uvs[v + 3] = new Vector2(tx + 0.5f - inset, ty + inset);
                Color32 c = new Color32(
                    (byte)Mathf.Clamp(p.Tint.r * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(p.Tint.g * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(p.Tint.b * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(alpha * 255f, 0f, 255f));
                cols[v] = c;
                cols[v + 1] = c;
                cols[v + 2] = c;
                cols[v + 3] = c;
                w++;
            }
            // остаток буфера: нулевая площадь, не рисуется
            for (int v = w * 4; v < verts.Length; v++) verts[v] = camPos;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.bounds = new Bounds(camPos, new Vector3(20000f, 20000f, 20000f));
            return w;
        }

        // ------------------------------------------------------------------ текстура клубов

        static float Fbm(float x, float y)
        {
            float sum = 0f;
            float amp = 0.5f;
            float f = 1f;
            float norm = 0f;
            for (int o = 0; o < 5; o++)
            {
                sum += amp * Mathf.PerlinNoise(x * f, y * f);
                norm += amp;
                amp *= 0.52f;
                f *= 2.03f;
            }
            return sum / norm;
        }

        static float Smooth(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        /// <summary>
        /// Атлас 2x2 из четырёх разных клубов (по 128 px). Белый цвет с лепкой света в rgb (тень внизу-слева, светлее сверху-справа и
        /// по тонким краям), форма и мягкий рваный край — в альфе.
        /// </summary>
        public static Texture2D MakeAtlas()
        {
            int T = TileSize;
            Texture2D tex = new Texture2D(T * 2, T * 2, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[T * 2 * T * 2];
            float[] h = new float[(T + 2) * (T + 2)];
            Vector3 light = new Vector3(0.45f, 0.55f, 0.70f).normalized;
            for (int tile = 0; tile < 4; tile++)
            {
                float ox = 3.7f + tile * 13.1f;
                float oy = 9.2f + tile * 7.7f;
                // поле плотности с запасом в один пиксель по краям (для градиента)
                for (int y = -1; y <= T; y++)
                {
                    for (int x = -1; x <= T; x++)
                    {
                        float u = ((x + 0.5f) / T) * 2f - 1f;
                        float v = ((y + 0.5f) / T) * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float wx = Fbm(u * 1.3f + ox + 5.2f, v * 1.3f + oy + 1.3f) - 0.5f;
                        float wy = Fbm(u * 1.3f + ox + 8.1f, v * 1.3f + oy + 3.7f) - 0.5f;
                        float n = Fbm(u * 2.1f + wx * 1.8f + ox, v * 2.1f + wy * 1.8f + oy);
                        h[(y + 1) * (T + 2) + (x + 1)] = (0.95f - r) * 1.25f + (n - 0.5f) * 1.4f;
                    }
                }
                for (int y = 0; y < T; y++)
                {
                    for (int x = 0; x < T; x++)
                    {
                        float u = ((x + 0.5f) / T) * 2f - 1f;
                        float v = ((y + 0.5f) / T) * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float d = h[(y + 1) * (T + 2) + (x + 1)];
                        float a = Smooth(0f, 0.42f, d) * (1f - Smooth(0.84f, 1.0f, r));
                        float gx = (h[(y + 1) * (T + 2) + (x + 2)] - h[(y + 1) * (T + 2) + x]) * T * 0.25f;
                        float gy = (h[(y + 2) * (T + 2) + (x + 1)] - h[y * (T + 2) + (x + 1)]) * T * 0.25f;
                        Vector3 nrm = new Vector3(-gx * 0.35f, -gy * 0.35f, 1f).normalized;
                        float lit = Mathf.Clamp01(Vector3.Dot(nrm, light));
                        float thin = 1f - Smooth(0.1f, 0.9f, a);
                        float lum = Mathf.Clamp01(0.25f + 0.6f * lit * (1f - 0.55f * a) + 0.25f * thin);
                        byte l = (byte)Mathf.RoundToInt(lum * 255f);
                        int px0 = (tile & 1) * T + x;
                        int py0 = ((tile >> 1) & 1) * T + y;
                        px[py0 * T * 2 + px0] = new Color32(l, l, l, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
            }
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(false);
            return tex;
        }
    }
}
