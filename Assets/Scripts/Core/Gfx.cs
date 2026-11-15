using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PokaGoryu
{
    /// <summary>
    /// Графические помощники: шейдеры, глобальные параметры света, материалы, примитивы, меши и процедурные текстуры.
    /// Арта в проекте нет — всё рисуется кодом (заменить на свои текстуры можно в BuildTextures).
    /// </summary>
    public static class Gfx
    {
        public static Shader WorldShader;
        public static Shader UnlitShader;
        public static Shader SkyShader;
        public static Shader PostShader;
        public static Transform CamTransform;

        /// <summary>Фасады по районам: 0 жилой, 1 рынок, 2 больница, 3 промзона.</summary>
        public static readonly Texture2D[] TexFacade = new Texture2D[4];
        public static Texture2D TexAsphalt;
        public static Texture2D TexPavement;
        public static Texture2D TexGrass;
        public static Texture2D TexHalo;
        public static Texture2D TexVignette;
        public static Texture2D TexHorizon;
        public static Texture2D TexRing;

        /// <summary>Яркость картинки (клавиши - и =). 1 — по умолчанию.</summary>
        public static float Brightness = 1f;

        static bool inited;
        static Mesh coneMesh;
        static readonly Dictionary<int, Material> litCache = new Dictionary<int, Material>();
        static readonly Dictionary<string, Material> worldCache = new Dictionary<string, Material>();
        static readonly Dictionary<Texture2D, int> texIds = new Dictionary<Texture2D, int>();
        static int nextTexId;
        static readonly List<string> retiredKeys = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            inited = false;
            coneMesh = null;
            sphereMesh = null;
            litCache.Clear();
            worldCache.Clear();
            texIds.Clear();
            nextTexId = 0;
            CamTransform = null;
        }

        public static void Init()
        {
            if (inited) return;
            inited = true;

            WorldShader = Shader.Find("Pokagoryu/World");
            UnlitShader = Shader.Find("Pokagoryu/Unlit");
            SkyShader = Shader.Find("Pokagoryu/Sky");
            PostShader = Shader.Find("Pokagoryu/Post");
            if (WorldShader == null || UnlitShader == null)
            {
                Debug.LogError("[Тлеющая тьма] Не найдены шейдеры Pokagoryu/World или Pokagoryu/Unlit. Проверьте Assets/Resources/Shaders.");
                Shader fallback = Shader.Find("Sprites/Default");
                if (WorldShader == null) WorldShader = fallback;
                if (UnlitShader == null) UnlitShader = fallback;
            }

            ApplyGlobals();
            BuildTextures();
        }

        /// <summary>Общие параметры освещения для всех шейдеров (небо, луна, туман, яркость).</summary>
        public static void ApplyGlobals()
        {
            float b = Brightness;
            // Мрачный город: свет почти только от ламп и героя, остальное тонет в сыром сумраке.
            // Светлее/темнее — клавиши - и = в игре или Яркость в настройках; общий тон — числа ниже.
            Shader.SetGlobalFloat("_PG_Fog", 0.034f);
            Shader.SetGlobalFloat("_PG_Exposure", 2.7f * b);
            Shader.SetGlobalVector("_PG_AmbSky", new Vector4(0.18f * b, 0.22f * b, 0.35f * b, 0f));
            Shader.SetGlobalVector("_PG_AmbGround", new Vector4(0.090f * b, 0.085f * b, 0.095f * b, 0f));
            Vector3 moon = new Vector3(0.35f, 0.8f, -0.5f).normalized;
            Shader.SetGlobalVector("_PG_MoonDir", new Vector4(moon.x, moon.y, moon.z, 0f));
            Shader.SetGlobalVector("_PG_MoonColor", new Vector4(0.17f * b, 0.21f * b, 0.32f * b, 0f));
            Shader.SetGlobalVector("_PG_FogColor", new Vector4(0.040f * b, 0.054f * b, 0.088f * b, 0f));
            Shader.SetGlobalVector("_PG_SkyZenith", new Vector4(0.006f, 0.010f, 0.026f, 0f));
            Shader.SetGlobalVector("_PG_SkyHorizon", new Vector4(0.050f, 0.064f, 0.104f, 0f));
            Shader.SetGlobalVector("_PG_SkyGlow", new Vector4(0.95f, 0.62f, 0.32f, 0f));
            Shader.SetGlobalVector("_PG_GlowDir", new Vector4(0f, 0f, 1f, 0f));
        }

        public static void SetBrightness(float b)
        {
            Brightness = Mathf.Clamp(b, 0.5f, 2.2f);
            ApplyGlobals();
        }

        // ---------------------------------------------------------------- материалы

        /// <summary>Освещаемый материал без текстуры; цвет берётся из цвета вершин и tint (кешируется).</summary>
        public static Material Lit(Color c)
        {
            int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
            Material m;
            if (litCache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(WorldShader);
            m.SetColor("_Color", c);
            m.SetFloat("_Gloss", 0.08f);
            m.enableInstancing = true;   // одинаковые кубики (пальцы, детали) рисуются одним вызовом
            litCache[key] = m;
            return m;
        }

        /// <summary>
        /// Номер текстуры для ключа кеша материалов. Вместо Object.GetInstanceID(): в Unity 6000.6 он помечен как устаревший
        /// с ошибкой компиляции (нужен GetEntityId), а в старых версиях GetEntityId нет. Так код собирается везде.
        /// </summary>
        static int TexId(Texture2D tex)
        {
            if (tex == null) return 0;
            int id;
            if (!texIds.TryGetValue(tex, out id))
            {
                id = ++nextTexId;
                texIds[tex] = id;
            }
            return id;
        }

        public static void ReleaseWorldTexture(Texture2D texture)
        {
            if (texture == null) return;
            retiredKeys.Clear();
            foreach (KeyValuePair<string, Material> entry in worldCache)
                if (entry.Value != null && entry.Value.mainTexture == texture)
                {
                    Object.Destroy(entry.Value);
                    retiredKeys.Add(entry.Key);
                }
            for (int i = 0; i < retiredKeys.Count; i++) worldCache.Remove(retiredKeys[i]);
            texIds.Remove(texture);
        }

        /// <summary>
        /// Материал мира с текстурой в мировых координатах (tile метров на повтор): окна одного размера на любой стене.
        /// emission — сила светящихся окон (альфа текстуры), gloss — блеск (мокрый асфальт), roofFlat — крыши без текстуры.
        /// </summary>
        public static Material LitWorld(Texture2D tex, float tile, float emission, float gloss, bool roofFlat)
        {
            return LitWorld(tex, tile, emission, gloss, roofFlat, false);
        }

        /// <summary>glossFromAlpha: блеск берётся из альфа-канала текстуры (мокрый асфальт: лужи блестят, сухое — нет).</summary>
        public static Material LitWorld(Texture2D tex, float tile, float emission, float gloss, bool roofFlat, bool glossFromAlpha)
        {
            string key = TexId(tex) + "|" + tile + "|" + emission + "|" + gloss + "|" + roofFlat + "|" + glossFromAlpha;
            Material m;
            if (worldCache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(WorldShader);
            m.mainTexture = tex;
            m.SetColor("_Color", Color.white);
            m.SetFloat("_WorldTile", tile);
            m.SetFloat("_Emission", emission);
            m.SetFloat("_Gloss", gloss);
            m.SetFloat("_RoofFlat", roofFlat ? 1f : 0f);
            m.SetFloat("_GlossAlpha", glossFromAlpha ? 1f : 0f);
            worldCache[key] = m;
            return m;
        }

        /// <summary>Светящийся материал. additive — ореолы; alpha — спрайты с прозрачностью; иначе непрозрачный.</summary>
        public static Material Unlit(Color c, Texture2D tex, bool additive, bool alpha, float fog)
        {
            Material m = new Material(UnlitShader);
            m.SetColor("_Color", c);
            if (tex != null) m.mainTexture = tex;
            m.SetFloat("_FogAmount", fog);
            m.SetFloat("_Cull", 0f);
            if (additive)
            {
                m.SetFloat("_SrcBlend", 5f);   // SrcAlpha
                m.SetFloat("_DstBlend", 1f);   // One
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3100;
            }
            else if (alpha)
            {
                m.SetFloat("_SrcBlend", 5f);   // SrcAlpha
                m.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
            }
            else
            {
                m.SetFloat("_SrcBlend", 1f);   // One
                m.SetFloat("_DstBlend", 0f);   // Zero
                m.SetFloat("_ZWrite", 1f);
            }
            return m;
        }

        public static Material SkyMaterial()
        {
            if (SkyShader == null) return null;
            Material m = new Material(SkyShader);
            m.renderQueue = 1000;
            return m;
        }

        public static Material PostMaterial()
        {
            if (PostShader == null) return null;
            return new Material(PostShader);
        }

        // ---------------------------------------------------------------- примитивы и меши

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
        {
            GameObject go;
            if (type == PrimitiveType.Sphere && !keepCollider)
            {
                // Шар Unity — 515 вершин; наш — 77. Лампы, огоньки и огни на крышах не нуждаются в большем.
                go = new GameObject("Sphere");
                go.AddComponent<MeshFilter>().sharedMesh = SphereMesh();
                go.AddComponent<MeshRenderer>();
            }
            else
            {
                go = GameObject.CreatePrimitive(type);
                if (!keepCollider)
                {
                    Collider col = go.GetComponent<Collider>();
                    if (col != null) Object.Destroy(col);
                }
            }
            Transform t = go.transform;
            if (parent != null) t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>Плоский 2D-спрайт (квадрат), всегда повёрнутый к камере.</summary>
        public static GameObject Sprite(Transform parent, Vector3 localPos, Vector2 size, Material mat, bool yawOnly)
        {
            GameObject go = Prim(PrimitiveType.Quad, parent, localPos, new Vector3(size.x, size.y, 1f), mat, false);
            Billboard b = go.AddComponent<Billboard>();
            b.YawOnly = yawOnly;
            return go;
        }

        /// <summary>Объект из готового меша (например, собранного MeshBatch).</summary>
        public static GameObject MeshObject(Transform parent, Mesh mesh, Material mat, string name)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>Невидимый коллайдер-коробка (стены, препятствия).</summary>
        public static GameObject StaticBox(Transform parent, Vector3 center, Vector3 size, string name, float yawDeg)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = center;
            if (yawDeg != 0f) go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            return go;
        }

        public static GameObject StaticBox(Transform parent, Vector3 center, Vector3 size, string name)
        {
            return StaticBox(parent, center, size, name, 0f);
        }

        static Mesh sphereMesh;

        /// <summary>Лёгкий шар диаметром 1 (10 сегментов x 6 колец, 77 вершин, 120 треугольников).</summary>
        public static Mesh SphereMesh()
        {
            if (sphereMesh != null) return sphereMesh;
            const int seg = 10;
            const int rings = 6;
            Vector3[] v = new Vector3[(rings + 1) * (seg + 1)];
            Vector3[] n = new Vector3[v.Length];
            Vector2[] uv = new Vector2[v.Length];
            for (int i = 0; i <= rings; i++)
            {
                float th = Mathf.PI * i / rings;
                for (int j = 0; j <= seg; j++)
                {
                    float ph = 6.2831853f * j / seg;
                    Vector3 p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    int k = i * (seg + 1) + j;
                    v[k] = p * 0.5f;
                    n[k] = p;
                    uv[k] = new Vector2((float)j / seg, 1f - (float)i / rings);
                }
            }
            List<int> t = new List<int>();
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int a = i * (seg + 1) + j;
                    int b = (i + 1) * (seg + 1) + j;
                    int c = (i + 1) * (seg + 1) + j + 1;
                    int d = i * (seg + 1) + j + 1;
                    // порядок вершин — наружу (как у MeshBatch); у полюсов один из двух треугольников вырожден
                    if (i < rings - 1) { t.Add(a); t.Add(c); t.Add(b); }
                    if (i > 0) { t.Add(a); t.Add(d); t.Add(c); }
                }
            }
            sphereMesh = new Mesh();
            sphereMesh.name = "PG_Sphere";
            sphereMesh.vertices = v;
            sphereMesh.normals = n;
            sphereMesh.uv = uv;
            sphereMesh.triangles = t.ToArray();
            sphereMesh.RecalculateBounds();
            return sphereMesh;
        }

        /// <summary>
        /// Единичный конус: вершина в (0,0,0), основание радиуса 1 на y = -1. Альфа вершин падает к основанию.
        /// Нормали смотрят наружу и вверх — по ним шейдер (_EdgeFade) гасит альфу у силуэта, и край конуса мягкий.
        /// Вершина у каждой грани своя (своя нормаль), поэтому вершин 2 * seg.
        /// </summary>
        public static Mesh ConeMesh()
        {
            if (coneMesh != null) return coneMesh;
            int seg = 20;
            Vector3[] v = new Vector3[seg * 2];
            Vector3[] n = new Vector3[seg * 2];
            Color32[] c = new Color32[seg * 2];
            for (int i = 0; i < seg; i++)
            {
                float a = i * 6.2831853f / seg;
                Vector3 nn = new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a)).normalized;
                v[i] = Vector3.zero;
                n[i] = nn;
                c[i] = new Color32(255, 255, 255, 150);
                v[seg + i] = new Vector3(Mathf.Cos(a), -1f, Mathf.Sin(a));
                n[seg + i] = nn;
                c[seg + i] = new Color32(255, 255, 255, 0);
            }
            int[] t = new int[seg * 3];
            for (int i = 0; i < seg; i++)
            {
                t[i * 3] = i;
                t[i * 3 + 1] = seg + i;
                t[i * 3 + 2] = seg + (i + 1) % seg;
            }
            Mesh m = new Mesh();
            m.name = "PG_LightCone";
            m.vertices = v;
            m.normals = n;
            m.colors32 = c;
            m.triangles = t;
            m.RecalculateBounds();
            coneMesh = m;
            return coneMesh;
        }

        // ---------------------------------------------------------------- текстуры

        static Color32 C32(float r, float g, float b, float a)
        {
            return new Color32(
                (byte)Mathf.Clamp(r, 0f, 255f),
                (byte)Mathf.Clamp(g, 0f, 255f),
                (byte)Mathf.Clamp(b, 0f, 255f),
                (byte)Mathf.Clamp(a, 0f, 255f));
        }

        static Texture2D MakeTex(int w, int h, bool mips, TextureWrapMode wrap, FilterMode filter, System.Func<int, int, Color32> f)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    px[y * w + x] = f(x, y);
                }
            }
            t.SetPixels32(px);
            t.wrapMode = wrap;
            t.filterMode = filter;
            t.Apply(mips);
            return t;
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float len2 = abx * abx + aby * aby;
            float t = 0f;
            if (len2 > 0.0001f) t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / len2);
            float cx = ax + abx * t;
            float cy = ay + aby * t;
            float dx = px - cx;
            float dy = py - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static void BuildTextures()
        {
            TexGen.BuildAll();   // фасады, асфальт, плитка — в TexGen
            TexHalo = MakeHalo();
            TexVignette = MakeVignette();
            TexHorizon = MakeHorizon();
            TexRing = MakeRing();
        }

        /// <summary>Мягкий круглый ореол (белый, альфа спадает от центра).</summary>
        static Texture2D MakeHalo()
        {
            return MakeTex(64, 64, false, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f;
                float dy = (y + 0.5f) / 32f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a;
                return C32(255f, 255f, 255f, a * 255f);
            });
        }

        /// <summary>Кольцо с четырьмя засечками: метка цели взаимодействия.</summary>
        static Texture2D MakeRing()
        {
            return MakeTex(64, 64, false, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f;
                float dy = (y + 0.5f) / 32f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                if (r > 0.70f && r < 0.84f) a = 1f;
                bool tick = (Mathf.Abs(dx) < 0.06f || Mathf.Abs(dy) < 0.06f) && r > 0.46f && r < 0.70f;
                if (tick) a = 1f;
                if (r > 0.84f && r < 0.92f) a = Mathf.Max(a, (0.92f - r) / 0.08f * 0.5f);
                return C32(255f, 255f, 255f, a * 255f);
            });
        }

        /// <summary>Виньетка для экрана: прозрачно в центре, чёрно по краям.</summary>
        static Texture2D MakeVignette()
        {
            return MakeTex(128, 128, false, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 64f - 1f;
                float dy = (y + 0.5f) / 64f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.35f) / 0.8f));
                return C32(0f, 0f, 0f, a * 255f);
            });
        }

        /// <summary>Полоса дневного света на горизонте: ярко внизу, растворяется вверх.</summary>
        static Texture2D MakeHorizon()
        {
            return MakeTex(4, 64, false, TextureWrapMode.Clamp, FilterMode.Bilinear, (x, y) =>
            {
                float v = (y + 0.5f) / 64f;
                float a = Mathf.Pow(1f - v, 1.7f);
                return C32(255f, 238f, 190f, a * 255f);
            });
        }

    }
}
