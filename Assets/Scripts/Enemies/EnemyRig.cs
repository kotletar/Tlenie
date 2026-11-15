using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Состояние врага для анимации: Enemy.Update собирает его каждый кадр.</summary>
    public struct EnemyPose
    {
        public float Age;        // секунд с появления
        public float Speed;      // скорость по земле, м/с
        public bool Attacking;   // кусает, гасит лампу, ест
        public float Stun;       // секунд оглушения (0 — нет)
        public float Hit;        // вспышка попадания, 0..1
        public float Bite;       // 0..1: укус только что случился (Пожиратель; спадает)
    }

    /// <summary>
    /// Тело врага из частей (меши EnemyModels): иерархия трансформов, которую двигает Animate. Корень тела — дочерний объект Enemy;
    /// Enemy поворачивает себя лицом к цели, а тело анимируется в своих осях (+Z — вперёд). Светящееся (глаза, ядро, ореолы) —
    /// отдельными материалами без освещения, их яркость меняется кадр за кадром; материалы принадлежат телу и освобождаются в Dispose.
    /// </summary>
    public abstract class EnemyRig
    {
        protected readonly Transform root;
        protected readonly float seed;
        protected readonly List<Object> owned = new List<Object>();

        protected EnemyRig(Transform parent, string name, float seed)
        {
            GameObject g = new GameObject(name);
            root = g.transform;
            root.SetParent(parent, false);
            this.seed = seed;
        }

        public static EnemyRig Create(EnemyKind kind, Transform parent, float seed)
        {
            switch (kind)
            {
                case EnemyKind.Shade: return new ShadeRig(parent, seed);
                case EnemyKind.Snuffer: return new SnufferRig(parent, seed);
                default: return new EaterRig(parent, seed);
            }
        }

        public abstract void Animate(EnemyPose s, float dt);

        /// <summary>Убитый враг: тело оседает и растекается (u от 0 до 1), потом объект исчезает (EnemyCorpse).</summary>
        public void Collapse(float u)
        {
            float k = 1f - u * u;
            root.localScale = new Vector3(1f + 0.30f * u, Mathf.Max(0.02f, k), 1f + 0.30f * u);
        }

        public int RendererCount
        {
            get { return root.GetComponentsInChildren<Renderer>(true).Length; }
        }

        public void Dispose()
        {
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] != null) Object.Destroy(owned[i]);
            owned.Clear();
        }

        // ------------------------------------------------------------------ помощники

        protected static readonly Color32 White32 = new Color32(255, 255, 255, 255);

        protected Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos)
        {
            GameObject g = Gfx.MeshObject(parent, mesh, mat, name);
            g.transform.localPosition = localPos;
            return g.transform;
        }

        protected Transform Pivot(Transform parent, string name, Vector3 localPos)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            return g.transform;
        }

        /// <summary>Светящийся материал без освещения (глаза, ядро): цвет меняет Animate.</summary>
        protected Material Glow(Color c, float fog)
        {
            Material m = Gfx.Unlit(c, null, false, false, fog);
            owned.Add(m);
            return m;
        }

        /// <summary>Ореол: аддитивный круглый спрайт.</summary>
        protected Material Halo(Color c, float fog)
        {
            Material m = Gfx.Unlit(c, Gfx.TexHalo, true, false, fog);
            owned.Add(m);
            return m;
        }

        static Texture2D smokeAtlas;
        static Material[] smokeMats;

        /// <summary>Клуб дыма для дымки у плаща и хвоста: общий атлас шума из SmokeField, четыре клуба по материалам (плитка 2x2).</summary>
        protected static Material SmokeMat(int tile)
        {
            if (smokeAtlas == null) smokeAtlas = SmokeField.MakeAtlas();
            if (smokeMats == null || smokeMats[0] == null)
            {
                smokeMats = new Material[4];
                for (int i = 0; i < 4; i++)
                {
                    Material m = Gfx.Unlit(new Color(0.10f, 0.06f, 0.17f, 0.55f), smokeAtlas, false, true, 0.6f);
                    m.mainTextureScale = new Vector2(0.5f, 0.5f);
                    m.mainTextureOffset = new Vector2((i & 1) * 0.5f, ((i >> 1) & 1) * 0.5f);
                    smokeMats[i] = m;
                }
            }
            return smokeMats[tile & 3];
        }

        protected static float Spawn(float age)
        {
            float u = Mathf.Clamp01(age / 0.9f);
            return u * u * (3f - 2f * u);
        }

        protected static float Smooth(float current, float target, float rate, float dt)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
        }
    }

    // ====================================================================== Тень

    /// <summary>
    /// Тень: парящая ссутулившаяся фигура в рваном плаще с высоким капюшоном и бледными глазами в чёрной пустоте, очень длинные руки с
    /// когтями (качаются при движении, поднимаются к цели при нападении), лоскуты плаща сзади относит ходом, у подола и плеч поднимается дымка.
    /// </summary>
    sealed class ShadeRig : EnemyRig
    {
        readonly Transform body;
        readonly Transform head;
        readonly Transform[] upper = new Transform[2];
        readonly Transform[] fore = new Transform[2];
        readonly Transform[] tails = new Transform[3];
        readonly Transform[] wisps = new Transform[5];
        readonly Material eyeMat;
        readonly Material haloMat;
        readonly Transform halo;
        float phase;
        float reach;

        public ShadeRig(Transform parent, float seed) : base(parent, "ShadeBody", seed)
        {
            Material lit = Gfx.Lit(Color.white);
            body = Pivot(root, "Body", Vector3.zero);
            Part(body, "Cloak", EnemyModels.ShadeCloak(), lit, Vector3.zero);
            head = Pivot(body, "Head", new Vector3(0f, 2.1f, 0.26f));
            Part(head, "Hood", EnemyModels.ShadeHood(), lit, new Vector3(0f, -2.1f, -0.26f));
            eyeMat = Glow(new Color(0.80f, 0.96f, 1f, 1f), 0.5f);
            Part(head, "Eyes", EnemyModels.ShadeEyes(), eyeMat, new Vector3(0f, -2.1f, -0.26f));
            haloMat = Halo(new Color(0.45f, 0.75f, 1f, 0.55f), 0.5f);
            halo = Gfx.Sprite(head, new Vector3(0f, 0.30f, 0.45f), new Vector2(0.9f, 0.55f), haloMat, false).transform;
            for (int a = 0; a < 2; a++)
            {
                int side = a == 0 ? -1 : 1;
                upper[a] = Pivot(body, "Upper" + a, new Vector3(side * 0.52f, 1.95f, 0.18f));
                Part(upper[a], "UpperMesh", EnemyModels.ShadeUpperArm(side), lit, Vector3.zero);
                fore[a] = Pivot(upper[a], "Fore" + a, new Vector3(0f, -0.68f, 0f));
                Part(fore[a], "ForeMesh", EnemyModels.ShadeForeArm(side), lit, Vector3.zero);
            }
            Vector3[] tailAt = { new Vector3(-0.32f, 0.42f, -0.40f), new Vector3(0.32f, 0.42f, -0.40f), new Vector3(0f, 0.50f, -0.52f) };
            for (int k = 0; k < 3; k++)
            {
                tails[k] = Pivot(body, "Tail" + k, tailAt[k]);
                Part(tails[k], "TailMesh", EnemyModels.ShadeTail(k), lit, Vector3.zero);
            }
            for (int k = 0; k < 5; k++)
                wisps[k] = Gfx.Sprite(body, new Vector3(0f, 1f, 0f), new Vector2(0.6f, 0.6f), SmokeMat(k), false).transform;
        }

        public override void Animate(EnemyPose s, float dt)
        {
            float t = s.Age + seed;
            float spawn = Spawn(s.Age);
            float calm = s.Stun > 0f ? 0.25f : 1f;
            float move = Mathf.Clamp01(s.Speed / 3.4f);
            root.localScale = new Vector3(Mathf.Lerp(0.6f, 1f, spawn), Mathf.Lerp(0.25f, 1f, spawn), Mathf.Lerp(0.6f, 1f, spawn)) * (1f + 0.18f * s.Hit);
            root.localPosition = new Vector3(0f, 0.10f + 0.07f * Mathf.Sin(t * 2f) * calm, 0f);

            phase += dt * (2.2f + 3.4f * move) * calm;
            reach = Smooth(reach, s.Attacking ? 1f : 0f, 7f, dt);
            body.localRotation = Quaternion.Euler(5f + 9f * move + 8f * reach, 3f * Mathf.Sin(t * 0.9f) * calm, 3f * Mathf.Sin(t * 1.3f) * calm);
            head.localRotation = Quaternion.Euler(4f * Mathf.Sin(t * 1.1f) - 6f * reach, 8f * Mathf.Sin(t * 0.7f + 1f) * calm, 0f);

            for (int a = 0; a < 2; a++)
            {
                float swing = Mathf.Sin(phase + (a == 0 ? 0f : Mathf.PI));
                float idle = Mathf.Sin(t * 1.1f + a * 2.1f);
                // висят чуть вперёд и наружу; при ходьбе качаются; при нападении тянутся вперёд и вверх, когти дрожат
                float pitchUp = Mathf.Lerp(-8f + 16f * swing * move + 5f * idle, -78f, reach);
                float pitchFore = Mathf.Lerp(-22f + 10f * Mathf.Sin(phase + 1f + a) * move + 6f * idle, -34f + 8f * Mathf.Sin(t * 14f + a), reach);
                float roll = (a == 0 ? 1f : -1f) * Mathf.Lerp(12f, 5f, reach);
                upper[a].localRotation = Quaternion.Euler(pitchUp, 0f, roll);
                fore[a].localRotation = Quaternion.Euler(pitchFore, 0f, (a == 0 ? -1f : 1f) * 4f);
            }
            for (int k = 0; k < 3; k++)
            {
                float back = 8f + 30f * move + 7f * Mathf.Sin(t * 2.3f + k * 1.7f);
                tails[k].localRotation = Quaternion.Euler(back, 0f, 7f * Mathf.Sin(t * 1.7f + k * 2.1f) + (k - 1) * 6f);
            }
            for (int k = 0; k < 5; k++)
            {
                float u = Mathf.Repeat(t * 0.33f + k * 0.2f, 1f);
                float a = k * 1.3f + t * 0.4f;
                float rad = Mathf.Lerp(0.50f, 0.22f, u);
                wisps[k].localPosition = new Vector3(Mathf.Cos(a) * rad, Mathf.Lerp(0.5f, 2.3f, u), Mathf.Sin(a) * rad * 0.8f);
                float size = 0.75f * Mathf.Sin(u * Mathf.PI);
                wisps[k].localScale = new Vector3(size, size, 1f);
            }
            float flick = 0.78f + 0.22f * Mathf.Sin(t * 9f) + 0.6f * s.Hit;
            eyeMat.SetColor("_Color", new Color(0.80f * flick, 0.96f * flick, 1f * flick, 1f));
            haloMat.SetColor("_Color", new Color(0.45f, 0.75f, 1f, 0.35f * flick * spawn));
        }
    }

    // ====================================================================== Гаситель

    /// <summary>
    /// Гаситель: парящий колючий шар. Чёрные шипы по сфере вращаются вокруг пустоты, в которой жарко светится ядро (пульсирует, с ореолом),
    /// вокруг летают искры, сзади тянется дымок. При нападении шипы раскручиваются быстрее, ядро вспыхивает ярче.
    /// </summary>
    sealed class SnufferRig : EnemyRig
    {
        readonly Transform spin;
        readonly Transform core;
        readonly Transform halo;
        readonly Transform[] sparks = new Transform[7];
        readonly Transform[] smoke = new Transform[3];
        readonly Material coreMat;
        readonly Material haloMat;
        readonly Material sparkMat;
        float spinT;
        float fury;

        public SnufferRig(Transform parent, float seed) : base(parent, "SnufferBody", seed)
        {
            Material lit = Gfx.Lit(Color.white);
            spin = Pivot(root, "Spin", Vector3.zero);
            Part(spin, "Spikes", EnemyModels.SnufferSpikes(), lit, Vector3.zero);
            coreMat = Glow(new Color(1f, 0.55f, 0.45f, 1f), 0.4f);
            core = Part(root, "Core", EnemyModels.SnufferCore(), coreMat, Vector3.zero);
            haloMat = Halo(new Color(1f, 0.22f, 0.16f, 0.6f), 0.4f);
            halo = Gfx.Sprite(root, Vector3.zero, new Vector2(1.7f, 1.7f), haloMat, false).transform;
            sparkMat = Glow(new Color(1f, 0.62f, 0.30f, 1f), 0.4f);
            for (int k = 0; k < sparks.Length; k++) sparks[k] = Part(root, "Spark" + k, EnemyModels.Spark(), sparkMat, Vector3.zero);
            for (int k = 0; k < smoke.Length; k++) smoke[k] = Gfx.Sprite(root, new Vector3(0f, 0f, -0.4f), new Vector2(0.5f, 0.5f), SmokeMat(k + 1), false).transform;
        }

        public override void Animate(EnemyPose s, float dt)
        {
            float t = s.Age + seed;
            float spawn = Spawn(s.Age);
            float calm = s.Stun > 0f ? 0.2f : 1f;
            fury = Smooth(fury, s.Attacking ? 1f : 0f, 6f, dt);
            root.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, spawn) * (1f + 0.3f * s.Hit);
            root.localPosition = new Vector3(0f, 1.6f + 0.25f * Mathf.Sin(t * 5f), 0f);

            spinT += dt * (1f + 2.4f * fury) * calm;
            spin.localRotation = Quaternion.Euler(spinT * 80f, spinT * 150f, spinT * 38f);
            float spikeK = 1f + 0.10f * Mathf.Sin(t * 9f) + 0.25f * fury;
            spin.localScale = new Vector3(spikeK, spikeK, spikeK);

            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 11f);
            float coreK = 1f + 0.22f * pulse + 0.45f * fury + 0.4f * s.Hit;
            core.localScale = new Vector3(coreK, coreK, coreK);
            float hot = Mathf.Clamp01(0.35f + 0.4f * pulse + 0.5f * fury);
            coreMat.SetColor("_Color", Color.Lerp(new Color(1f, 0.32f, 0.26f, 1f), new Color(1f, 0.95f, 0.88f, 1f), hot));
            float haloSize = 1.45f + 0.5f * pulse + 0.7f * fury;
            halo.localScale = new Vector3(haloSize, haloSize, 1f);
            haloMat.SetColor("_Color", new Color(1f, 0.22f, 0.16f, (0.45f + 0.25f * pulse + 0.2f * fury) * spawn));

            for (int k = 0; k < sparks.Length; k++)
            {
                float w = 1.6f + 0.55f * k;
                float a = t * w * (1f + fury) + k * 2.1f;
                float rad = 0.46f + 0.16f * Mathf.Sin(t * 1.7f + k * 3.1f);
                sparks[k].localPosition = new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a * 0.7f + k) * 0.30f, Mathf.Sin(a) * rad);
                sparks[k].localRotation = Quaternion.Euler(a * 90f, a * 50f, 0f);
                float flick = 0.5f + 0.5f * Mathf.Sin(t * 14f + k * 5f);
                sparks[k].localScale = Vector3.one * Mathf.Lerp(0.4f, 1.3f, flick);
            }
            for (int k = 0; k < smoke.Length; k++)
            {
                float u = Mathf.Repeat(t * 0.9f + k / 3f, 1f);
                smoke[k].localPosition = new Vector3(0.06f * Mathf.Sin(t * 3f + k), 0.04f * Mathf.Sin(t * 2f + k * 2f), -0.30f - 0.9f * u);
                float size = 0.55f * Mathf.Sin(u * Mathf.PI) + 0.05f;
                smoke[k].localScale = new Vector3(size, size, 1f);
            }
        }
    }

    // ====================================================================== Пожиратель

    /// <summary>
    /// Пожиратель: огромная бугристая туша на щупальцах. Пасть с тремя рядами зубов, без языка (нижняя челюсть ходит: жуёт, а при укусе
    /// захлопывается), две пары больших бледных глаз со щелевым зрачком и россыпь мелких, шесть щупалец из четырёх звеньев с бегущей волной,
    /// дыхание и тяжёлая качка при ходьбе.
    /// </summary>
    sealed class EaterRig : EnemyRig
    {
        const int Tentacles = 6;
        const int Links = 4;

        readonly Transform body;
        readonly Transform jaw;
        readonly Transform[,] chain = new Transform[Tentacles, Links];
        readonly Transform[] bigEyes = new Transform[2];
        readonly Material eyeMat;
        readonly Material pupilMat;
        readonly Material throatMat;
        float open;
        float phase;

        public EaterRig(Transform parent, float seed) : base(parent, "EaterBody", seed)
        {
            Material lit = Gfx.Lit(Color.white);
            body = Pivot(root, "Body", Vector3.zero);
            Part(body, "Flesh", EnemyModels.EaterBody(), lit, Vector3.zero);
            throatMat = Glow(new Color(0.42f, 0.05f, 0.09f, 1f), 0.5f);
            Part(body, "Throat", EnemyModels.EaterThroat(), throatMat, Vector3.zero);
            jaw = Pivot(body, "Jaw", new Vector3(0f, 1.62f, 1.06f));
            Part(jaw, "JawMesh", EnemyModels.EaterJaw(), lit, Vector3.zero);
            eyeMat = Glow(new Color(0.95f, 0.93f, 0.62f, 1f), 0.45f);
            pupilMat = Glow(new Color(0.07f, 0.03f, 0.02f, 1f), 0.45f);
            Vector3[] eyeAt = { new Vector3(-0.80f, 3.20f, 1.60f), new Vector3(0.80f, 3.20f, 1.60f) };
            for (int k = 0; k < 2; k++)
            {
                bigEyes[k] = Part(body, "Eye" + k, EnemyModels.EaterEye(false), eyeMat, eyeAt[k]);
                Part(bigEyes[k], "Pupil", EnemyModels.EaterEye(true), pupilMat, Vector3.zero);
            }
            // россыпь мелких глаз: на лбу и по бокам, у каждого свой наклон
            Vector3[] smallAt =
            {
                new Vector3(-1.50f, 2.80f, 1.15f), new Vector3(1.50f, 2.80f, 1.15f), new Vector3(-0.35f, 3.85f, 1.12f), new Vector3(0.42f, 3.78f, 1.14f),
                new Vector3(-1.85f, 2.20f, 0.65f), new Vector3(1.88f, 2.35f, 0.60f), new Vector3(-1.05f, 3.62f, 1.00f), new Vector3(1.12f, 3.50f, 1.05f)
            };
            float[] smallYaw = { -35f, 35f, -8f, 8f, -62f, 62f, -22f, 20f };
            for (int k = 0; k < smallAt.Length; k++)
            {
                Transform e = Part(body, "SmallEye" + k, EnemyModels.EaterEye(false), eyeMat, smallAt[k]);
                e.localScale = Vector3.one * (0.36f + 0.09f * (k % 3));
                e.localRotation = Quaternion.Euler(0f, smallYaw[k], 0f);
            }
            float[] yaw = { -66f, -26f, 26f, 66f, 148f, -148f };
            for (int i = 0; i < Tentacles; i++)
            {
                float a = yaw[i] * Mathf.Deg2Rad;
                Vector3 baseAt = new Vector3(Mathf.Sin(a) * 1.55f, 0.90f, Mathf.Cos(a) * 1.35f);
                Transform p = Pivot(body, "Tentacle" + i, baseAt);
                p.localRotation = Quaternion.Euler(0f, yaw[i], 0f);
                for (int k = 0; k < Links; k++)
                {
                    chain[i, k] = Pivot(p, "L" + k, k == 0 ? Vector3.zero : new Vector3(0f, 0f, LinkLen(k - 1)));
                    Part(chain[i, k], "LinkMesh", EnemyModels.EaterTentacle(k), lit, Vector3.zero);
                    p = chain[i, k];
                }
            }
        }

        static float LinkLen(int k)
        {
            return k == 0 ? 0.95f : k == 1 ? 0.85f : k == 2 ? 0.75f : 0.65f;
        }

        public override void Animate(EnemyPose s, float dt)
        {
            float t = s.Age + seed;
            float spawn = Spawn(s.Age);
            float calm = s.Stun > 0f ? 0.3f : 1f;
            float move = Mathf.Clamp01(s.Speed / 1.7f);
            phase += dt * (1.3f + 1.4f * move) * calm;
            float breath = Mathf.Sin(t * 1.4f);
            root.localScale = new Vector3(Mathf.Lerp(0.7f, 1f, spawn), Mathf.Lerp(0.3f, 1f, spawn), Mathf.Lerp(0.7f, 1f, spawn)) * (1f + 0.07f * s.Hit);
            root.localPosition = new Vector3(0f, 0.04f * Mathf.Sin(phase * 2f) * (0.3f + move), 0f);
            body.localScale = new Vector3(1f + 0.025f * breath, 1f - 0.03f * breath, 1f + 0.025f * breath);
            body.localRotation = Quaternion.Euler(2f + 3f * move * Mathf.Sin(phase * 2f), 2f * Mathf.Sin(phase), 2.5f * Mathf.Sin(phase));

            // челюсть: покой — вялая зевота, нападение — жуёт, укус — захлопывается
            float openTarget = s.Attacking ? 0.55f + 0.45f * Mathf.Sin(t * 5.5f) : 0.18f + 0.14f * Mathf.Sin(t * 0.9f);
            open = Smooth(open, openTarget, 10f, dt);
            float jawOpen = Mathf.Max(0f, open) * (1f - s.Bite);
            jaw.localRotation = Quaternion.Euler(8f + jawOpen * 34f, 0f, 0f);

            for (int i = 0; i < Tentacles; i++)
            {
                float lead = i * 1.1f;
                for (int k = 0; k < Links; k++)
                {
                    float w = phase * 1.7f + lead + k * 0.9f;
                    float amp = 4f + 5f * k;
                    // щупальце выходит почти горизонтально, плавно загибается вниз и касается земли кончиком (накопленный изгиб 0, 12, 28, 40 градусов)
                    float basePitch = k == 0 ? 2f : (k == 1 ? 12f : (k == 2 ? 16f : 12f));
                    chain[i, k].localRotation = Quaternion.Euler(basePitch + amp * Mathf.Sin(w) * calm, amp * 0.9f * Mathf.Sin(w * 0.8f + 1.3f) * calm, 0f);
                }
            }
            float glare = 0.82f + 0.18f * Mathf.Sin(t * 3f) + 0.5f * s.Hit;
            eyeMat.SetColor("_Color", new Color(0.95f * glare, 0.93f * glare, 0.62f * glare, 1f));
            float throat = 0.7f + 0.3f * Mathf.Sin(t * 2.4f) + 0.5f * jawOpen;
            throatMat.SetColor("_Color", new Color(0.42f * throat, 0.05f * throat, 0.09f * throat, 1f));
            for (int k = 0; k < 2; k++) bigEyes[k].localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 2.2f + k));
        }
    }
}
