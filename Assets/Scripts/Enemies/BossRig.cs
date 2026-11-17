using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Великий Гаситель: парящий шар-броня в шесть створок. Закрытый, он сходится когтями створок к дулу на переднем полюсе (сквозь щель
    /// виден тлеющий глаз); в нападении дуло заряжается фиолетовым, и из него бьёт копьё Тьмы. Раскрытый, он показывает чашу с глазом-ядром:
    /// створки расходятся лепестками, глаз следит за героем, ореол разгорается. Сзади щупальца, вокруг медленно кружат обломки брони и
    /// дымка. Он же и источник света (LightField): тускло-красный в броне, тёплый в открытом виде, фиолетовый, когда целится.
    /// Умирая, раскидывает створки, вспыхивает и проваливается (3.4 с).
    /// </summary>
    sealed class BossRig : EnemyRig, ILightSource
    {
        const int Petals = EnemyModels.BossPetals;
        const int Tentacles = 4;
        const int Links = 4;
        const int Wisps = 9;
        const float EyeScale = 6.2f;
        const float EyeZ = 0.7f;

        readonly Transform body;
        readonly Transform spin;
        readonly Transform[] seats = new Transform[Petals];
        readonly Transform[] hinges = new Transform[Petals];
        readonly Vector3[] seatBase = new Vector3[Petals];
        readonly Transform eye;
        readonly Transform gaze;
        readonly Transform[,] chain = new Transform[Tentacles, Links];
        readonly Transform debris;
        readonly Transform[] wisps = new Transform[Wisps];
        readonly Transform halo;
        readonly Transform corona;
        readonly Transform flare;
        readonly Transform lanceOuter;
        readonly Transform lanceCore;
        readonly Material eyeMat;
        readonly Material haloMat;
        readonly Material coronaMat;
        readonly Material flareMat;
        readonly Material lanceOuterMat;
        readonly Material lanceCoreMat;

        float spinT;
        float eyeYaw;
        float eyePitch;

        // свет босса (LightField читает эти поля)
        Vector3 glowPos;
        float glowRadius = 10f;
        float glowIntensity = 0.2f;
        Color glowColor = new Color(1f, 0.32f, 0.18f, 1f);

        public BossRig(Transform parent, float seed) : base(parent, "BossBody", seed)
        {
            Material lit = Gfx.Lit(Color.white);
            body = Pivot(root, "Body", Vector3.zero);
            spin = Pivot(body, "Spin", Vector3.zero);
            Part(spin, "Hull", EnemyModels.BossHull(), lit, Vector3.zero);
            for (int k = 0; k < Petals; k++)
            {
                float phi = k * 60f;
                seatBase[k] = EnemyModels.BossDir(90f, phi) * EnemyModels.BossR;
                seats[k] = Pivot(body, "Seat" + k, seatBase[k]);
                seats[k].localRotation = Quaternion.Euler(0f, 0f, phi);
                hinges[k] = Pivot(seats[k], "Hinge" + k, Vector3.zero);
                Part(hinges[k], "Petal", EnemyModels.BossPetal(), lit, Vector3.zero);
            }

            eyeMat = Glow(new Color(1f, 0.70f, 0.42f, 1f), 0.3f);
            eye = Part(body, "Eyeball", EnemyModels.EaterEye(), eyeMat, new Vector3(0f, 0f, EyeZ));
            eye.localScale = Vector3.one * EyeScale;
            gaze = Part(eye, "Gaze", EnemyModels.EaterGaze(), eyeMat, Vector3.zero);

            // щупальца свисают из нижней задней части шара
            Vector3[] tAt = { new Vector3(-1.7f, -2.2f, -1.4f), new Vector3(1.7f, -2.2f, -1.4f), new Vector3(-0.8f, -2.8f, -1.9f), new Vector3(0.8f, -2.8f, -1.9f) };
            float[] tYaw = { 200f, 160f, 188f, 172f };
            for (int i = 0; i < Tentacles; i++)
            {
                Transform p = Pivot(body, "Tentacle" + i, tAt[i]);
                p.localRotation = Quaternion.Euler(62f, tYaw[i], 0f);
                for (int k = 0; k < Links; k++)
                {
                    chain[i, k] = Pivot(p, "L" + k, k == 0 ? Vector3.zero : new Vector3(0f, 0f, LinkLen(k - 1)));
                    if (k == 0) chain[i, k].localScale = Vector3.one * 1.9f;
                    Part(chain[i, k], "LinkMesh", EnemyModels.EaterTentacle(k), lit, Vector3.zero);
                    p = chain[i, k];
                }
            }

            debris = Part(body, "Debris", EnemyModels.BossDebris(), lit, Vector3.zero);
            for (int k = 0; k < Wisps; k++)
                wisps[k] = Gfx.Sprite(body, Vector3.zero, new Vector2(7f, 7f), SmokeMat(k), false).transform;

            // корона за шаром: тёмное тело на красном свечении читается силуэтом даже в чёрном небе; ореол спереди разгорается, когда ядро открыто
            coronaMat = Halo(new Color(1f, 0.28f, 0.14f, 0.45f), 0.0f);
            corona = Gfx.Sprite(body, new Vector3(0f, 0f, -1.4f), new Vector2(26f, 26f), coronaMat, false).transform;
            haloMat = Halo(new Color(1f, 0.4f, 0.2f, 0.25f), 0.3f);
            halo = Gfx.Sprite(body, new Vector3(0f, 0f, 1.2f), new Vector2(14f, 14f), haloMat, false).transform;
            flareMat = Halo(new Color(0.8f, 0.5f, 1f, 0f), 0.2f);
            flare = Gfx.Sprite(body, new Vector3(0f, 0f, EnemyModels.BossR + 0.4f), new Vector2(3f, 3f), flareMat, false).transform;

            // копьё Тьмы: два луча в мире (не вращаются вместе с телом): широкий фиолетовый и узкий светлый
            Transform host = root.parent;
            lanceOuterMat = Additive(new Color(0.62f, 0.28f, 1f, 0f));
            lanceCoreMat = Additive(new Color(0.95f, 0.85f, 1f, 0f));
            lanceOuter = Gfx.Prim(PrimitiveType.Cube, host, Vector3.zero, Vector3.one, lanceOuterMat, false).transform;
            lanceCore = Gfx.Prim(PrimitiveType.Cube, host, Vector3.zero, Vector3.one, lanceCoreMat, false).transform;
            lanceOuter.name = "LanceOuter";
            lanceCore.name = "LanceCore";
            lanceOuter.gameObject.SetActive(false);
            lanceCore.gameObject.SetActive(false);
            owned.Add(lanceOuter.gameObject);
            owned.Add(lanceCore.gameObject);

            glowPos = body.TransformPoint(new Vector3(0f, 0f, 1.6f));
            LightField.Register(this);
        }

        Material Additive(Color c)
        {
            Material m = Gfx.Unlit(c, null, true, false, 0f);
            owned.Add(m);
            return m;
        }

        static float LinkLen(int k)
        {
            return k == 0 ? 0.95f : k == 1 ? 0.85f : k == 2 ? 0.75f : 0.65f;
        }

        public override void Dispose()
        {
            LightField.Unregister(this);
            base.Dispose();
        }

        // ------------------------------------------------------------------ анимация

        public override void Animate(EnemyPose s, float dt)
        {
            float t = s.Age + seed;
            float open = s.Open;
            float charge = s.Charge;
            root.localScale = Vector3.one * EnemyModels.BossScale;
            root.localPosition = Vector3.zero;

            spinT += dt * (1f + 1.6f * charge + 0.5f * s.Hit);
            body.localScale = Vector3.one * (1f + 0.018f * Mathf.Sin(t * 1.3f) + 0.05f * charge);
            spin.localRotation = Quaternion.Euler(0f, 0f, spinT * 6f);

            // створки: закрытые чуть дышат и дрожат при зарядке копья, открытые расходятся лепестками
            for (int k = 0; k < Petals; k++)
            {
                float a = open * 72f + 2.2f * Mathf.Sin(t * 2f + k * 1.3f) * (0.25f + open) + 1.4f * charge * Mathf.Sin(t * 31f + k * 2.1f);
                hinges[k].localRotation = Quaternion.AngleAxis(a, Vector3.up);
                seats[k].localPosition = seatBase[k];
            }

            // глаз следит за героем
            GameManager gm = GameManager.Instance;
            Vector3 aim = s.LookAt;
            bool haveAim = s.HasLook;
            if (!haveAim && gm != null && gm.Player != null && gm.Player.Cam != null)
            {
                aim = gm.Player.Cam.transform.position;
                haveAim = true;
            }
            float wantYaw = 0f;
            float wantPitch = 0f;
            if (haveAim)
            {
                Vector3 dn = body.InverseTransformPoint(aim) - new Vector3(0f, 0f, EyeZ);
                if (dn.sqrMagnitude > 0.01f)
                {
                    dn.Normalize();
                    wantYaw = Mathf.Clamp(Mathf.Atan2(dn.x, dn.z) * Mathf.Rad2Deg, -32f, 32f);
                    wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dn.y, -1f, 1f)) * Mathf.Rad2Deg, -24f, 24f);
                }
            }
            float follow = 1f - Mathf.Exp(-6f * dt);
            eyeYaw = Mathf.LerpAngle(eyeYaw, wantYaw, follow);
            eyePitch = Mathf.LerpAngle(eyePitch, wantPitch, follow);
            eye.localRotation = Quaternion.Euler(eyePitch + 0.5f * Mathf.Sin(t * 19f), eyeYaw + 0.5f * Mathf.Sin(t * 13f + 1f), 0f);
            gaze.localScale = new Vector3(Mathf.Lerp(1f, 0.55f, charge), Mathf.Lerp(1f, 1.15f, charge), 1f);
            float glare = 0.50f + 0.45f * open + 0.55f * s.Hit + 0.30f * charge + 0.08f * Mathf.Sin(t * 3f);
            eyeMat.SetColor("_Color", new Color(1f * glare, 0.70f * glare, 0.42f * glare, 1f));

            // щупальца качаются, как у Пожирателя, но медленнее: он парит
            for (int i = 0; i < Tentacles; i++)
            {
                for (int k = 0; k < Links; k++)
                {
                    float w = t * 0.9f + i * 1.4f + k * 0.8f;
                    float amp = 5f + 5f * k;
                    chain[i, k].localRotation = Quaternion.Euler((k == 0 ? 0f : 10f) + amp * Mathf.Sin(w), amp * 0.8f * Mathf.Sin(w * 0.8f + 1.3f), 0f);
                }
            }

            // обломки брони кружат медленно, дымка вокруг (сзади и по бокам: спереди она закрыла бы глаз)
            debris.localRotation = Quaternion.Euler(0f, 0f, t * 9f);
            for (int k = 0; k < Wisps; k++)
            {
                float a = t * (0.10f + 0.025f * k) + k * 2.4f;
                float rad = 4.6f + 0.7f * Mathf.Sin(t * 0.4f + k);
                float z = -Mathf.Abs(Mathf.Sin(a)) * rad * 0.9f - 0.5f;
                wisps[k].localPosition = new Vector3(Mathf.Cos(a) * rad, 1.6f * Mathf.Sin(a * 0.7f + k), z);
                float size = 5.5f + 2f * Mathf.Sin(t * 0.5f + k * 1.7f);
                wisps[k].localScale = new Vector3(size, size, 1f);
            }

            // ореол и вспышка дула
            float haloA = 0.10f + 0.04f * Mathf.Sin(t * 2.4f) + 0.55f * open + 0.25f * charge + 0.4f * s.Hit;
            Color calm = new Color(1f, 0.30f, 0.16f, 1f);
            Color warm = new Color(1f, 0.62f, 0.28f, 1f);
            Color violet = new Color(0.72f, 0.42f, 1f, 1f);
            Color haloC = Color.Lerp(Color.Lerp(calm, warm, open), violet, Mathf.Max(charge * 0.8f, s.Fire));
            haloMat.SetColor("_Color", new Color(haloC.r, haloC.g, haloC.b, Mathf.Clamp01(haloA)));
            float haloSize = 13f + 4f * open + 3f * charge + 5f * s.Fire;
            halo.localScale = new Vector3(haloSize, haloSize, 1f);
            // корона: тёмно-красная в броне, оранжевая в открытом виде, фиолетовая, когда целится; растёт со здоровьем, которое он потерял
            float coronaA = 0.46f + 0.07f * Mathf.Sin(t * 1.7f) + 0.10f * s.Hurt + 0.30f * open + 0.22f * charge + 0.3f * s.Hit;
            coronaMat.SetColor("_Color", new Color(haloC.r, haloC.g * 0.9f, haloC.b, Mathf.Clamp01(coronaA)));
            float coronaSize = 26f + 5f * open + 4f * charge;
            corona.localScale = new Vector3(coronaSize, coronaSize, 1f);
            float flareA = 0.30f * (1f - open) + 0.75f * charge + 0.9f * s.Fire;
            flareMat.SetColor("_Color", new Color(0.82f, 0.55f, 1f, Mathf.Clamp01(flareA)));
            float flareSize = 2.2f + 5f * charge + 9f * s.Fire;
            flare.localScale = new Vector3(flareSize, flareSize, 1f);

            // копьё Тьмы
            if (s.LanceStage > 0)
            {
                Vector3 d = s.LanceTo - s.LanceFrom;
                float len = d.magnitude;
                if (len > 0.5f)
                {
                    float w;
                    float aOuter;
                    float aCore;
                    if (s.LanceStage == 1)
                    {
                        w = 0.05f + 0.07f * charge;
                        aOuter = 0.10f + 0.45f * charge;
                        aCore = 0.20f + 0.70f * charge;
                    }
                    else
                    {
                        w = 1.5f * (0.35f + 0.65f * s.Fire);
                        aOuter = 0.9f * s.Fire;
                        aCore = s.Fire;
                    }
                    Quaternion rot = Quaternion.LookRotation(d / len);
                    lanceOuter.gameObject.SetActive(true);
                    lanceCore.gameObject.SetActive(true);
                    lanceOuter.SetPositionAndRotation(s.LanceFrom + d * 0.5f, rot);
                    lanceCore.SetPositionAndRotation(s.LanceFrom + d * 0.5f, rot);
                    lanceOuter.localScale = new Vector3(w, w, len);
                    lanceCore.localScale = new Vector3(w * 0.38f, w * 0.38f, len);
                    lanceOuterMat.SetColor("_Color", new Color(0.62f, 0.28f, 1f, Mathf.Clamp01(aOuter)));
                    lanceCoreMat.SetColor("_Color", new Color(0.95f, 0.85f, 1f, Mathf.Clamp01(aCore)));
                }
            }
            else if (lanceOuter.gameObject.activeSelf)
            {
                lanceOuter.gameObject.SetActive(false);
                lanceCore.gameObject.SetActive(false);
            }

            // свет
            glowPos = body.TransformPoint(new Vector3(0f, 0f, 1.6f));
            glowRadius = 14f + 16f * open + 6f * charge + 12f * s.Fire;
            glowIntensity = 0.32f + 0.95f * open + 0.35f * charge + 1.5f * s.Fire + 0.4f * s.Hit;
            glowColor = Color.Lerp(Color.Lerp(calm, warm, open), violet, Mathf.Max(charge * 0.8f, s.Fire));
        }

        // ------------------------------------------------------------------ гибель

        public override float CollapseSeconds { get { return 3.4f; } }

        public override void Collapse(float u)
        {
            float k = u * u;
            root.localScale = Vector3.one * EnemyModels.BossScale;
            // створки раскрываются шире и разлетаются, глаз вспыхивает, шар проваливается и гаснет
            for (int i = 0; i < Petals; i++)
            {
                hinges[i].localRotation = Quaternion.AngleAxis(72f + 70f * Mathf.Min(1f, u * 2f), Vector3.up);
                seats[i].localPosition = seatBase[i] + seatBase[i].normalized * (7f * k);
            }
            float shrink = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, u));
            body.localScale = Vector3.one * Mathf.Max(0.01f, shrink);
            root.localPosition = new Vector3(0f, -3.5f * k, 0f);
            float flash = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / 0.65f));
            float glare = 1f + 2.5f * flash;
            eyeMat.SetColor("_Color", new Color(glare, 0.9f * glare, 0.7f * glare, 1f));
            haloMat.SetColor("_Color", new Color(1f, 0.8f, 0.5f, Mathf.Clamp01(0.95f * flash)));
            float haloSize = 14f + 30f * u;
            halo.localScale = new Vector3(haloSize, haloSize, 1f);
            coronaMat.SetColor("_Color", new Color(1f, 0.5f, 0.25f, Mathf.Clamp01(0.5f * flash + 0.3f * (1f - u))));
            corona.localScale = new Vector3(26f + 20f * u, 26f + 20f * u, 1f);
            flareMat.SetColor("_Color", new Color(1f, 0.9f, 0.8f, 0f));
            if (lanceOuter.gameObject.activeSelf)
            {
                lanceOuter.gameObject.SetActive(false);
                lanceCore.gameObject.SetActive(false);
            }
            glowPos = body.TransformPoint(new Vector3(0f, 0f, 1.6f));
            glowRadius = 34f;
            glowIntensity = 0.2f + 2.0f * flash;
            glowColor = Color.Lerp(new Color(1f, 0.62f, 0.28f, 1f), new Color(1f, 0.95f, 0.85f, 1f), flash);
        }

        // ------------------------------------------------------------------ ILightSource

        public Vector3 LightPosition { get { return glowPos; } }
        public float LightRadius { get { return glowRadius; } }
        public float LightIntensity { get { return glowIntensity; } }
        public Color LightColor { get { return glowColor; } }
    }
}
