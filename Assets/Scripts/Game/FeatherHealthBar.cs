using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Бар здоровья «Перья» 3D — порт скрипта пользователя (three.js -> Unity).
    /// Сцена строится из кода и рендерится отдельной камерой в маленькую RenderTexture (на тёмной подложке, как в оригинале).
    /// В игре картинка висит на груди героя на месте прежней шкалы из 10 огоньков: смотришь вниз (Tab) — видишь свой свет
    /// (HeroBody просит рендер через Request и кладёт Texture на панель). Draw рисует ту же картинку в прямоугольнике экрана
    /// (IMGUI) — на случай, если полоска понадобится в углу HUD. Всё, как в оригинале: 20 перьев-«лезвий», которые
    /// загораются по мере здоровья, туман, световая полоса, покачивание, три тона (Sun, Pale, Gold) и пиксельный режим.
    /// Отличия от оригинала: нет uGUI (в проекте этого пакета нет), поэтому вместо RawImage — IMGUI и панель в мире; свет
    /// перьев считает собственный шейдер Pokagoryu/Feather (в сборке не нужны Standard и Legacy Shaders); перетаскивание
    /// мышью убрано; камера сама подбирает угол обзора под форму прямоугольника; рендер идёт 30 раз в секунду и только
    /// пока полоска видна.
    /// </summary>
    public class FeatherHealthBar : MonoBehaviour
    {
        public enum Tone { Sun, Pale, Gold }

        [Header("Состояние")]
        public Tone tone = Tone.Sun;
        [Range(0, 100)] public float target = 70f;
        [Tooltip("Пиксельный режим оригинала: рисует в 40% размера и без сглаживания (для угла HUD). На груди не нужен: кадр и так низкого разрешения")]
        public bool pixelMode = false;
        public bool auto = true;

        [Header("Прочее")]
        [Tooltip("Слой для объектов бара: основная камера его не рисует")]
        public int featherLayer = 30;
        [Tooltip("Смещение сцены, чтобы не пересекаться с остальным миром")]
        public Vector3 stageOffset = new Vector3(0f, -1000f, 0f);
        public float renderFps = 30f;

        // ---------- константы (как в JS) ----------
        const int N = 20;
        const float LEN = 5.7f;
        const float X0 = -LEN / 2f;
        const float STEP = LEN / (N - 1);

        struct ToneSet
        {
            public Color core, mid, edge;
        }

        static Color H(int h)
        {
            return new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, 1f);
        }

        static ToneSet[] tones;
        static Color dark;

        static void InitConstants()
        {
            if (tones != null) return;
            tones = new ToneSet[3];
            tones[0] = new ToneSet { core = H(0xfff7be), mid = H(0xffe26e), edge = H(0xf0b232) };   // sun
            tones[1] = new ToneSet { core = H(0xfffce4), mid = H(0xfff1b0), edge = H(0xeccb69) };   // pale
            tones[2] = new ToneSet { core = H(0xffec91), mid = H(0xffc846), edge = H(0xd78c1e) };   // gold
            dark = H(0x2a2418);
        }

        class Feather
        {
            public Transform t;
            public Material m;
            public float hh, x, lit, ph, haloRot;
            public SpriteRenderer halo;
        }

        class Puff
        {
            public SpriteRenderer sr;
            public float u, ph, size, y, z, op, rot;
            public bool edge;
        }

        // ---------- поля ----------
        RenderTexture rt;
        int rtW, rtH;
        float rtScale = -1f;
        int wantW = 144, wantH = 60;
        int lastDrawFrame = -100;
        float renderT;
        Camera cam;
        Transform stage, root;
        Material additiveMat;
        Sprite grain;
        SpriteRenderer streak;
        readonly List<Feather> feathers = new List<Feather>();
        readonly List<Puff> mist = new List<Puff>();

        float shown = 70f, shimT, rx = 0.2f, ry = -0.4f;
        bool built;

        float Scale { get { return pixelMode ? 0.4f : 1f; } }

        /// <summary>Картинка полоски (для своих экранов).</summary>
        public RenderTexture Texture { get { return rt; } }

        // ---------- публичное API ----------
        public void SetTone(int i) { tone = (Tone)Mathf.Clamp(i, 0, 2); }
        public void SetHealth(float v) { target = Mathf.Clamp(v, 0f, 100f); }
        public void SetPixel(bool v) { pixelMode = v; }
        public void SetAuto(bool v) { auto = v; }

        /// <summary>
        /// Попросить рендер полоски в RenderTexture w x h (панель на груди). Звать каждый кадр, пока полоска видна:
        /// 3 кадра без запроса — и сцена полоски спит.
        /// </summary>
        public void Request(int w, int h)
        {
            wantW = Mathf.Max(8, w);
            wantH = Mathf.Max(8, h);
            lastDrawFrame = Time.frameCount;
        }

        /// <summary>Нарисовать полоску в прямоугольнике экрана (из OnGUI). alpha — общая прозрачность.</summary>
        public void Draw(Rect r, float alpha)
        {
            wantW = Mathf.Max(8, Mathf.RoundToInt(r.width * Scale));
            wantH = Mathf.Max(8, Mathf.RoundToInt(r.height * Scale));
            lastDrawFrame = Time.frameCount;
            if (rt == null) return;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, rt, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;
        }

        // ---------- инициализация ----------
        void Awake()
        {
            InitConstants();
            shown = target;
            BuildStage();
        }

        void OnDestroy()
        {
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
            if (stage != null) Destroy(stage.gameObject);
        }

        void BuildStage()
        {
            Shader lit = Shader.Find("Pokagoryu/Feather");
            if (lit == null)
            {
                Debug.LogError("[Тлеющая тьма] Не найден шейдер Pokagoryu/Feather (Assets/Resources/Shaders). Полоска здоровья отключена.");
                enabled = false;
                return;
            }

            stage = new GameObject("FeatherBarStage").transform;
            stage.position = stageOffset;

            // камера: three (0,.2,9.5) смотрит в -Z; в Unity ось Z зеркальна -> (0,.2,-9.5) смотрит в +Z
            GameObject camGo = new GameObject("Cam");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = new Vector3(0f, 0.2f, -9.5f);
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // прозрачный чёрный: полоска лежит на предплечье светом (additive), подложки нет
            cam.cullingMask = 1 << featherLayer;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false;   // рендерим сами (Camera.Render), 30 раз в секунду и только когда полоска на экране

            GameObject rootGo = new GameObject("Root");
            rootGo.transform.SetParent(stage, false);
            root = rootGo.transform;

            // материалы
            additiveMat = Gfx.Unlit(Color.white, null, true, false, 0f);
            grain = MakeGrainSprite();

            // перья
            for (int i = 0; i < N; i++)
            {
                float hh = 0.28f + 1.0f * Mathf.Pow(i / (float)(N - 1), 0.8f);
                const float w = 0.11f;

                GameObject go = new GameObject("Feather" + i);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(X0 + i * STEP, 0f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = Lathe(hh, w, 6);
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                Material m = new Material(lit);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                Feather f = new Feather();
                f.t = go.transform;
                f.m = m;
                f.hh = hh;
                f.x = X0 + i * STEP;
                f.ph = Random.value * 6.28f;
                f.haloRot = Random.value * 6.28f;
                f.halo = MakeSprite();
                f.halo.transform.localPosition = new Vector3(f.x, 0f, 0.15f);   // three z=-.15 -> unity +.15
                f.halo.transform.localScale = new Vector3(1.5f + hh, 1.5f + hh, 1f);
                feathers.Add(f);
            }

            // туман
            for (int i = 0; i < 90; i++)
            {
                Puff p = new Puff();
                p.sr = MakeSprite();
                p.u = (i + Random.value) / 90f;
                p.ph = Random.value * 6.28f;
                p.size = 1.1f + Random.value * 1f;
                p.y = (Random.value - .5f) * 1.9f;
                p.z = (Random.value - .5f) * .9f;
                p.edge = Random.value < .4f;
                p.op = .022f + Random.value * .025f;
                p.rot = Random.value * 6.28f;
                mist.Add(p);
            }
            for (int i = 0; i < 6; i++)
            {
                Puff p = new Puff();
                p.sr = MakeSprite();
                p.u = (i + .5f) / 6f;
                p.ph = Random.value * 6.28f;
                p.size = 3.6f;
                p.y = 0f;
                p.z = 0f;
                p.edge = false;
                p.op = .028f;
                p.rot = Random.value * 6.28f;
                mist.Add(p);
            }

            // световая полоса
            streak = MakeSprite();

            SetLayerRecursive(stage.gameObject, featherLayer);
            built = true;
        }

        SpriteRenderer MakeSprite()
        {
            GameObject go = new GameObject("S");
            go.transform.SetParent(root, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = grain;
            sr.sharedMaterial = additiveMat;
            sr.color = new Color(1f, 1f, 1f, 0f);
            return sr;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        // ---------- геометрия: аналог THREE.LatheGeometry с flatShading ----------
        static Vector3 LathePoint(Vector2 p, int i, int seg)
        {
            float a = i * Mathf.PI * 2f / seg;
            return new Vector3(p.x * Mathf.Sin(a), p.y, p.x * Mathf.Cos(a));
        }

        static void LatheTri(List<Vector3> verts, List<int> idx, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-10f) return;   // вырожденный
            idx.Add(verts.Count); verts.Add(a);
            idx.Add(verts.Count); verts.Add(b);
            idx.Add(verts.Count); verts.Add(c);
        }

        static Mesh Lathe(float hh, float w, int seg)
        {
            Vector2[] prof =
            {
                new Vector2(0f, -hh), new Vector2(w, -hh * .55f),
                new Vector2(w, hh * .55f), new Vector2(0f, hh)
            };
            List<Vector3> verts = new List<Vector3>();
            List<int> idx = new List<int>();

            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < seg; i++)
                {
                    Vector3 a = LathePoint(prof[j], i, seg);
                    Vector3 b = LathePoint(prof[j], i + 1, seg);
                    Vector3 c = LathePoint(prof[j + 1], i, seg);
                    Vector3 d = LathePoint(prof[j + 1], i + 1, seg);
                    LatheTri(verts, idx, a, b, c);
                    LatheTri(verts, idx, b, d, c);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "FeatherLathe";
            mesh.SetVertices(verts);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();   // вершины не общие -> плоское затенение
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- текстура «зерна» ----------
        static Sprite MakeGrainSprite()
        {
            const int S = 128;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    float a;
                    if (d >= 1f) a = 0f;
                    else if (d <= .35f) a = Mathf.Lerp(1f, .45f, d / .35f);
                    else a = Mathf.Lerp(.45f, 0f, (d - .35f) / .65f);
                    a *= .4f + .6f * Random.value;
                    px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255f, 0f, 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S, 0, SpriteMeshType.FullRect);
        }

        // ---------- RenderTexture / пиксельный режим ----------
        void EnsureRT()
        {
            int w = wantW;
            int h = wantH;
            if (rt != null && w == rtW && h == rtH && Mathf.Approximately(rtScale, Scale)) return;

            cam.targetTexture = null;
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.filterMode = Scale < 1f ? FilterMode.Point : FilterMode.Bilinear;
            rt.antiAliasing = 1;
            rt.name = "FeatherBar";
            rtW = w;
            rtH = h;
            rtScale = Scale;
            cam.targetTexture = rt;

            // угол обзора под форму прямоугольника: полоска (5.7 в ширину) занимает почти всю ширину, перья помещаются по высоте
            float aspect = (float)w / h;
            float visH = Mathf.Max(LEN * 1.18f / aspect, 3.15f);
            cam.fieldOfView = 2f * Mathf.Atan(visH * 0.5f / 9.5f) * Mathf.Rad2Deg;
        }

        // ---------- главный цикл ----------
        void LateUpdate()
        {
            if (!built) return;
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            float now = Time.time * 1000f;   // мс, как performance.now()

            // плавное приближение shown -> target (40 ед/сек)
            float diff = target - shown;
            shown = Mathf.Abs(diff) < .01f ? target : shown + Mathf.Sign(diff) * Mathf.Min(Mathf.Abs(diff), 40f * dt);

            // пока полоска не на экране (меню, пауза), сцену не обновляем и не рисуем
            if (Time.frameCount - lastDrawFrame > 3) return;
            EnsureRT();

            renderT += Time.unscaledDeltaTime;
            bool doRender = renderT >= 1f / Mathf.Max(1f, renderFps);

            float hf = Mathf.Clamp01(shown / 100f);
            float b = .1f + .9f * hf;
            float sz = .6f + .4f * hf;
            ToneSet T = tones[(int)tone];
            float pulse = (.85f + .2f * Mathf.Sin(now / 300f)) * b * 1.3f;

            float dirSign = target < shown - .01f ? -1f : 1f;
            shimT += dirSign * dt / 1.4f;
            if (shimT > 1f) shimT = 0f;
            if (shimT < 0f) shimT = 1f;
            float sx = X0 + shimT * LEN * hf;

            // перья
            for (int i = 0; i < feathers.Count; i++)
            {
                Feather f = feathers[i];
                float t = (i + .5f) / N;
                float tg2 = hf >= t ? 1f : 0f;
                f.lit += (tg2 - f.lit) * Mathf.Min(1f, dt * 12f);

                Color cc = Color.Lerp(T.mid, T.core, i / (float)(N - 1) * .6f);
                if (tg2 > 0f && hf < t + 1f / N) cc = Color.Lerp(cc, Color.white, .45f);

                float boost = Mathf.Exp(-Mathf.Pow((f.x - sx) / .4f, 2f)) * f.lit * (hf > .05f ? 1f : 0f);
                if (boost > .02f) cc = Color.Lerp(cc, Color.white, boost * .6f);

                Color baseCol = Color.Lerp(dark, cc, f.lit);
                float ei = f.lit * pulse * (1f + boost) * 0.72f;   // 0.72: на полном здоровье перья не выгорают в белое пятно
                f.m.SetColor("_Color", baseCol);
                f.m.SetColor("_Emit", new Color(cc.r * ei, cc.g * ei, cc.b * ei, 1f));

                Vector3 ls = f.t.localScale;
                ls.y = .9f + .1f * f.lit;
                f.t.localScale = ls;

                float ha = .2f * f.lit * b * 1.4f * (.8f + .25f * Mathf.Sin(now / 500f + f.ph)) * (1f + boost * .6f);
                f.halo.color = new Color(cc.r, cc.g, cc.b, Mathf.Clamp01(ha));
                f.haloRot += dt * .1f;
            }

            // туман
            for (int i = 0; i < mist.Count; i++)
            {
                Puff p = mist[i];
                float w = Mathf.Clamp01((hf * 1.08f - p.u) / .08f);
                p.sr.transform.localPosition = new Vector3(
                    X0 + p.u * LEN,
                    p.y * sz + .07f * Mathf.Sin(now / 900f + p.ph),
                    p.z);
                float k = sz * (1f + .15f * Mathf.Sin(now / 700f + p.ph));
                p.sr.transform.localScale = new Vector3(p.size * k, p.size * k, 1f);
                p.rot += dt * .12f;
                float a = p.op * w * b * 1.4f * (.8f + .25f * Mathf.Sin(now / 500f + p.ph * 2f));
                Color c = p.edge ? T.edge : T.mid;
                p.sr.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
            }

            // световая полоса
            int ix = Mathf.Clamp(Mathf.RoundToInt((sx - X0) / STEP), 0, N - 1);
            float shh = feathers[ix].hh;
            streak.enabled = hf > .05f;
            streak.transform.localPosition = new Vector3(sx, 0f, -0.15f);
            streak.transform.localScale = new Vector3(.4f, 2f * shh * 1.1f, 1f);
            streak.color = new Color(1f, 1f, 1f, Mathf.Clamp01(.5f * b));

            // свет перьев: направленный и тёплый точечный (параметры шейдера Pokagoryu/Feather)
            Vector3 toLight = new Vector3(2f, 4f, -5f).normalized;
            Shader.SetGlobalVector("_FB_DirToLight", new Vector4(toLight.x, toLight.y, toLight.z, 0f));
            Shader.SetGlobalVector("_FB_DirColor", new Vector4(0.6f, 0.6f, 0.6f, 0f));
            Vector3 pp = stage.position + new Vector3(X0 + LEN * hf / 2f, 0f, -1.8f);
            Shader.SetGlobalVector("_FB_PointPos", new Vector4(pp.x, pp.y, pp.z, 12f));
            float pi = (1f + .4f * Mathf.Sin(now / 300f)) * b * 1.6f;
            Color pc = H(0xffe28a);
            Shader.SetGlobalVector("_FB_PointColor", new Vector4(pc.r * pi, pc.g * pi, pc.b * pi, 0f));
            Shader.SetGlobalVector("_FB_Ambient", new Vector4(0.12f, 0.11f, 0.08f, 0f));

            // покачивание / поворот
            if (auto)
            {
                ry = .6f * Mathf.Sin(now / 1800f);
                rx = .2f + .08f * Mathf.Sin(now / 2600f);
            }
            // three: R = Rx * Ry; ось Z зеркальна, поэтому углы меняют знак
            root.localRotation =
                Quaternion.AngleAxis(-rx * Mathf.Rad2Deg, Vector3.right) *
                Quaternion.AngleAxis(-ry * Mathf.Rad2Deg, Vector3.up);

            // билборды: спрайты всегда смотрят в камеру
            Quaternion camRot = cam.transform.rotation;
            for (int i = 0; i < feathers.Count; i++)
                feathers[i].halo.transform.rotation = camRot * Quaternion.Euler(0f, 0f, -feathers[i].haloRot * Mathf.Rad2Deg);
            for (int i = 0; i < mist.Count; i++)
                mist[i].sr.transform.rotation = camRot * Quaternion.Euler(0f, 0f, -mist[i].rot * Mathf.Rad2Deg);
            streak.transform.rotation = camRot;

            if (doRender)
            {
                renderT = 0f;
                cam.Render();
            }
        }
    }
}
