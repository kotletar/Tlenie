using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Концовка классической игры. Автобус уезжает с героем и людьми, впереди рассвет, но рассвет — приманка: за ним Тьма, и она
    /// проглатывает автобус. Чёрный экран, резкий толчок — и герой просыпается за своим кухонным столом (тем же, где всё началось):
    /// остывший кофе, догоревшая свеча, руки как руки. «Сила» объяснилась: под ним тёплая лужа, он описался во сне.
    ///
    /// Всё считается функцией времени (Evaluate), как в предыстории (IntroDirector): камеру и руки ведёт сценарий, герой остаётся тем же
    /// (PlayerController/HeroBody). Поездка идёт в настоящем городе (камера внутри автобуса, салон достроен RideStage), кухня — та же,
    /// что в предыстории (Kitchen), только утром. Пропуск — Пробел, Enter, Esc или клик (после секунды): затемнение и экран итога.
    /// </summary>
    public class EndingDirector : MonoBehaviour
    {
        // ------------------------------------------------------------------ время сценария (секунды от начала)

        public const float TFadeIn = 1.3f;
        public const float TOpen = 10.4f;         // Тьма за «рассветом» начинает раскрываться
        public const float TEngulf = 15.0f;       // автобус въезжает в Тьму
        public const float TKitchen = 18.4f;      // чёрный экран кончился: кухня (кухонное время k = t - TKitchen)
        public const float KJolt = 1.25f;         // толчок: герой просыпается
        const float KFadeOut = 25.4f;
        const float KTitle = 26.6f;
        const float KEnd = 31.0f;
        const float GateDist = 150f;              // сколько автобус проезжает от вокзала до Тьмы

        struct Cap
        {
            public float A;
            public float B;
            public string Text;
            public Cap(float a, float b, string text)
            {
                A = a;
                B = b;
                Text = text;
            }
        }

        // Камера в салоне: время, поворот вправо, наклон вниз (градусы). Оглядывается на людей и снова вперёд.
        static readonly float[] RT = { 0f, 1.8f, 2.8f, 4.0f, 5.6f, 6.6f, 8.0f, 12.0f, 13.6f, 15.0f, 18.4f };
        static readonly float[] RYaw = { 0f, 1f, -48f, -150f, -150f, -10f, 0f, 0f, -7f, 3f, 0f };
        static readonly float[] RPit = { 2f, 2f, 2f, 3f, 3f, 3f, 4f, 2f, 0f, 0f, 0f };

        // Камера на кухне: время, поворот, наклон, крен (градусы), смещение головы от обычного положения за столом (м).
        // k < KJolt: щека на сложенных руках, лицо повёрнуто к кружке (она на расстоянии 0.3 м, голова рядом с ней, не в ней).
        static readonly float[] KT = { 0f, 1.20f, 1.40f, 1.90f, 2.80f, 3.80f, 4.80f, 6.20f, 7.60f, 8.80f, 10.00f, 12.80f, 14.60f, 16.20f, 20.0f, 31.0f };
        static readonly float[] KYaw = { 78f, 78f, 22f, -14f, -36f, 18f, 2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        static readonly float[] KPit = { 8f, 8f, -16f, -9f, 0f, 4f, 30f, 26f, 18f, 38f, 74f, 74f, 10f, 6f, 2f, 0f };
        static readonly float[] KRoll = { -80f, -80f, -28f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        static readonly float[] KOffX = { -0.30f, -0.30f, -0.12f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        static readonly float[] KOffY = { -0.25f, -0.25f, 0.0f, 0.03f, 0f, 0f, 0f, 0f, 0f, 0f, -0.04f, -0.04f, 0f, 0f, 0f, 0f };
        static readonly float[] KOffZ = { 0.58f, 0.58f, 0.20f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.02f, 0.02f, 0f, 0f, 0f, 0f };

        struct HKey
        {
            public float T;
            public Vector3 P;
            public Vector3 F;
            public Vector3 U;
            public float O;
            public HKey(float t, Vector3 p, Vector3 f, Vector3 u, float o)
            {
                T = t;
                P = p;
                F = f;
                U = u;
                O = o;
            }
        }

        // Руки на кухне (комната): спит на сложенных руках, толкается от стола, кладёт ладони на стол, разглядывает их, опускает на колени.
        static readonly HKey[] KeysL =
        {
            new HKey(0.0f, new Vector3(-0.42f, 0.80f, 0.30f), new Vector3(0.3f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.30f),
            new HKey(1.2f, new Vector3(-0.42f, 0.80f, 0.30f), new Vector3(0.3f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.30f),
            new HKey(1.7f, new Vector3(-0.32f, 0.92f, 0.16f), new Vector3(0f, 0.1f, 1f), new Vector3(0f, 1f, 0f), 0.80f),
            new HKey(2.6f, new Vector3(-0.16f, 0.81f, 0.22f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.60f),
            new HKey(5.6f, new Vector3(-0.16f, 0.81f, 0.22f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.60f),
            new HKey(6.8f, new Vector3(-0.17f, 1.06f, 0.05f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(8.6f, new Vector3(-0.17f, 1.06f, 0.05f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(9.8f, new Vector3(-0.15f, 0.68f, -0.30f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f),
            new HKey(40.0f, new Vector3(-0.15f, 0.68f, -0.30f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f)
        };

        static readonly HKey[] KeysR =
        {
            new HKey(0.0f, new Vector3(-0.28f, 0.80f, 0.42f), new Vector3(0.5f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.30f),
            new HKey(1.2f, new Vector3(-0.28f, 0.80f, 0.42f), new Vector3(0.5f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.30f),
            new HKey(1.7f, new Vector3(0.00f, 0.92f, 0.20f), new Vector3(0f, 0.1f, 1f), new Vector3(0f, 1f, 0f), 0.80f),
            new HKey(2.6f, new Vector3(0.12f, 0.81f, 0.20f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.60f),
            new HKey(5.6f, new Vector3(0.12f, 0.81f, 0.20f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.60f),
            new HKey(6.8f, new Vector3(0.15f, 1.08f, 0.06f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(8.6f, new Vector3(0.15f, 1.08f, 0.06f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(9.8f, new Vector3(0.15f, 0.68f, -0.30f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f),
            new HKey(40.0f, new Vector3(0.15f, 0.68f, -0.30f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f)
        };

        // ------------------------------------------------------------------ состояние

        PlayerController pc;
        HeroLight hero;
        HeroBody body;
        Bus bus;
        Kitchen kitchen;
        RideStage ride;
        EndingLight dayLight;
        EndingLight faceLight;
        AudioSource sfx;
        AudioSource engineSrc;
        Vector3 headBase;
        float prevFov = 76f;
        int stage = -1;            // -1 ещё не начато, 0 поездка, 1 кухня
        int boarded;
        int total;
        Cap[] rideCaps;
        Cap[] kitchenCaps;

        bool active;
        bool skipping;
        float skipT;
        float t;
        float lastT;
        float fadeA;
        float flashA;
        float lastTick = -1f;
        float gateZ;

        GUIStyle capStyle;
        GUIStyle hintStyle;
        GUIStyle titleStyle;
        GUIStyle subStyle;
        int lastFs = -1;

        public bool Active { get { return active; } }
        public float Elapsed { get { return t; } }
        public float Duration { get { return TKitchen + KEnd; } }
        /// <summary>Тьма за «рассветом» уже раскрылась (для автотестов).</summary>
        public bool DarknessVisible { get { return ride != null && ride.GateOpen > 0.1f; } }

        // ------------------------------------------------------------------ запуск и завершение

        /// <summary>Начать концовку. false — не удалось (тогда сразу экран итога).</summary>
        public bool Begin()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Player == null || gm.Hero == null || gm.Intro == null) return false;
            if (Gfx.UnlitShader == null || Gfx.WorldShader == null) return false;
            bus = Bus.Instance;
            if (bus == null) return false;
            pc = gm.Player;
            hero = gm.Hero;
            body = pc.Body;
            if (body == null) return false;
            kitchen = gm.Intro.AcquireKitchen();
            if (kitchen == null) return false;

            boarded = bus.BoardedCount;
            total = Survivor.All.Count;
            BuildCaps();
            Enemy.ClearAll();

            // автобус у вокзала, дальше он едет под управлением сценария; салон достроен
            bus.BeginCinematic();
            gateZ = bus.StartPos.z + GateDist + RideStage.SeatEye.z;
            ride = RideStage.Build(bus, gateZ, boarded);
            for (int i = 0; i < ride.Ceiling.Length; i++) LightField.Register(ride.Ceiling[i]);
            for (int i = 0; i < ride.Passengers.Count; i++) LightField.Register(ride.Passengers[i].Light);
            kitchen.Root.gameObject.SetActive(false);

            pc.ResetHead();
            headBase = pc.Head.localPosition;
            body.HideBody = true;
            body.OverrideW = 1f;
            body.ArmScale = 0.8f;
            hero.DamageFlash = 0f;
            hero.GlowScale = 0.55f;
            prevFov = pc.Cam.fieldOfView;
            pc.Cam.fieldOfView = 74f;
            if (pc.DroneSrc != null) AudioMix.SetVolume(pc.DroneSrc, 0f);

            if (sfx == null) sfx = ProcAudio.Source(gameObject, "tick", false, 1f, false);
            if (engineSrc == null) engineSrc = ProcAudio.Source(gameObject, "engine", true, 0f, false);
            else AudioMix.SetVolume(engineSrc, 0f);
            if (!engineSrc.isPlaying) engineSrc.Play();

            t = 0f;
            lastT = -1f;
            lastTick = -1f;
            skipping = false;
            skipT = 0f;
            fadeA = 1f;
            flashA = 0f;
            stage = -1;
            active = true;
            Evaluate(0f);
            return true;
        }

        /// <summary>Остановить концовку без экрана итога (выход в меню).</summary>
        public void Abort()
        {
            if (!active) return;
            active = false;
            End();
        }

        void Finish()
        {
            if (!active) return;
            active = false;
            End();
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.FinishEnding();
        }

        void End()
        {
            if (ride != null)
            {
                for (int i = 0; i < ride.Ceiling.Length; i++) LightField.Unregister(ride.Ceiling[i]);
                for (int i = 0; i < ride.Passengers.Count; i++) LightField.Unregister(ride.Passengers[i].Light);
                ride.Destroy();
                ride = null;
            }
            if (dayLight != null) LightField.Unregister(dayLight);
            if (faceLight != null) LightField.Unregister(faceLight);
            dayLight = null;
            faceLight = null;
            if (kitchen != null)
            {
                kitchen.SetWet(0f, 0f);
                kitchen.Root.gameObject.SetActive(false);
            }
            if (engineSrc != null) engineSrc.Stop();
            Gfx.ApplyGlobals();
            if (body != null)
            {
                body.OverrideW = 0f;
                body.HideBody = false;
                body.ArmScale = 1f;
                body.SetSkinTone(1f);
            }
            if (hero != null)
            {
                hero.GlowScale = 1f;
                hero.DamageFlash = 0f;
            }
            if (pc != null)
            {
                pc.ResetHead();
                pc.TeleportTo(LevelBuilder.StartPos, LevelBuilder.StartYaw);
                if (pc.Cam != null) pc.Cam.fieldOfView = prevFov;
                if (pc.DroneSrc != null) AudioMix.SetVolume(pc.DroneSrc, 0.35f);
            }
            stage = -1;
        }

        void OnDestroy()
        {
            if (ride != null) ride.Destroy();
        }

        // ------------------------------------------------------------------ субтитры

        void BuildCaps()
        {
            // как поездка звучит, зависит от того, кто с героем в автобусе
            string withMe = boarded >= 3 ? "Все со мной. Все живы." : (boarded >= 1 ? "Нас мало, но мы живы." : "Я один. Зато я выбрался.");
            string line = boarded >= 1 ? "— Смотрите, как светло!" : "Как светло...";
            rideCaps = new Cap[]
            {
                new Cap(0.9f, 3.2f, "Мы едем. Получилось."),
                new Cap(3.5f, 6.2f, withMe),
                new Cap(6.7f, 9.0f, "Впереди рассвет. Настоящий."),
                new Cap(9.3f, 11.3f, line),
                new Cap(11.6f, 13.2f, "Почему он... мигает?"),
                new Cap(13.4f, 14.7f, "Это не рассвет."),
                new Cap(14.8f, 16.0f, "Это ловушка.")
            };
            string saved = "Зато во сне я спас " + boarded + " из " + total + "!";
            if (boarded == 0) saved = "Зато во сне я выбрался из города!";
            kitchenCaps = new Cap[]
            {
                new Cap(TKitchen + 1.6f, TKitchen + 2.9f, "...А?!"),
                new Cap(TKitchen + 3.2f, TKitchen + 5.8f, "Кухня. Стол. Остывший кофе."),
                new Cap(TKitchen + 6.0f, TKitchen + 8.6f, "Автобус... Тьма... Это всё был сон?"),
                new Cap(TKitchen + 9.0f, TKitchen + 11.6f, "Руки — просто руки. Не светятся."),
                new Cap(TKitchen + 12.2f, TKitchen + 14.6f, "Зато... тепло. Очень тепло. И мокро."),
                new Cap(TKitchen + 15.2f, TKitchen + 16.8f, "...Ага."),
                new Cap(TKitchen + 17.2f, TKitchen + 21.2f, "Вот и вся моя «суперсила»: я описался во сне."),
                new Cap(TKitchen + 21.8f, TKitchen + 25.0f, saved)
            };
        }

        // ------------------------------------------------------------------ кадр

        void Update()
        {
            if (!active) return;
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Ending)
            {
                Abort();
                return;
            }
            float dt = Time.deltaTime;
            t += dt;
            if (t > 1.0f && SkipPressed()) Skip();
            if (skipping)
            {
                skipT += dt;
                Evaluate(t);
                fadeA = Mathf.Max(fadeA, Mathf.Clamp01(skipT / 0.45f));
                if (skipT >= 0.45f) Finish();
                return;
            }
            Evaluate(t);
            if (t >= TKitchen + KEnd) Finish();
        }

        /// <summary>Пропустить концовку: короткое затемнение и экран итога.</summary>
        public void Skip()
        {
            if (!active || skipping) return;
            skipping = true;
            skipT = 0f;
        }

        static bool SkipPressed()
        {
            return GameInput.Down(GKey.Space) || GameInput.Down(GKey.Enter) || GameInput.Down(GKey.Esc) || GameInput.MouseDown(0);
        }

        /// <summary>Для автотестов: перейти к моменту сценария (звуки до него не проигрываются).</summary>
        public void DebugSeek(float newT)
        {
            t = newT;
            lastT = newT - 0.001f;
            lastTick = Mathf.Floor(newT) - 1f;
        }

        // ------------------------------------------------------------------ сценарий

        static float S(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        /// <summary>Кубический сплайн Эрмита по ключам (касательные — по соседним ключам).</summary>
        static float Spline(float[] ts, float[] vs, float x)
        {
            int n = ts.Length;
            if (x <= ts[0]) return vs[0];
            if (x >= ts[n - 1]) return vs[n - 1];
            int i = 0;
            while (i < n - 2 && x >= ts[i + 1]) i++;
            float h = ts[i + 1] - ts[i];
            float u = (x - ts[i]) / h;
            float m0 = i > 0 ? (vs[i + 1] - vs[i - 1]) / (ts[i + 1] - ts[i - 1]) : (vs[i + 1] - vs[i]) / h;
            float m1 = i + 2 < n ? (vs[i + 2] - vs[i]) / (ts[i + 2] - ts[i]) : (vs[i + 1] - vs[i]) / h;
            float u2 = u * u;
            float u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * vs[i] + (u3 - 2f * u2 + u) * h * m0 + (-2f * u3 + 3f * u2) * vs[i + 1] + (u3 - u2) * h * m1;
        }

        static void SampleHand(HKey[] k, float x, out Vector3 p, out Quaternion r, out float o)
        {
            if (x <= k[0].T)
            {
                p = k[0].P;
                r = Quaternion.LookRotation(k[0].F, k[0].U);
                o = k[0].O;
                return;
            }
            int i = 0;
            while (i < k.Length - 2 && x >= k[i + 1].T) i++;
            float u = S(k[i].T, k[i + 1].T, x);
            p = Vector3.Lerp(k[i].P, k[i + 1].P, u);
            r = Quaternion.Slerp(Quaternion.LookRotation(k[i].F, k[i].U), Quaternion.LookRotation(k[i + 1].F, k[i + 1].U), u);
            o = Mathf.Lerp(k[i].O, k[i + 1].O, u);
        }

        bool Crossed(float at)
        {
            return lastT < at && t >= at;
        }

        /// <summary>Расстояние, которое автобус проехал от вокзала: плавный разгон до 12 м/с (за 15 секунд — ровно GateDist).</summary>
        public static float RideDistance(float x)
        {
            return 12f * (x - 2.5f * (1f - Mathf.Exp(-x / 2.5f)));
        }

        void Evaluate(float real)
        {
            if (ride == null || pc == null) return;
            if (real < TKitchen)
            {
                if (stage != 0) EnterRide();
                EvaluateRide(real);
            }
            else
            {
                if (stage != 1) EnterKitchen();
                EvaluateKitchen(real - TKitchen, real);
            }
            lastT = real;
        }

        void EnterRide()
        {
            stage = 0;
            if (kitchen != null) kitchen.Root.gameObject.SetActive(false);
            body.ArmScale = 0.8f;
            body.SetSkinTone(1f);
            pc.Cam.fieldOfView = 74f;
            if (dayLight != null) LightField.Unregister(dayLight);
            if (faceLight != null) LightField.Unregister(faceLight);
            dayLight = null;
            faceLight = null;
            if (engineSrc != null && !engineSrc.isPlaying) engineSrc.Play();
        }

        void EnterKitchen()
        {
            stage = 1;
            // автобус и поездка остаются позади: герой за кухонным столом, как в самом начале
            if (engineSrc != null) engineSrc.Stop();
            kitchen.Root.gameObject.SetActive(true);
            kitchen.SetCity(-9999f, 0f);   // утро: окна города не светятся
            kitchen.SetHaze(0f, 0f);
            pc.TeleportTo(kitchen.World(Kitchen.SeatRoot), 0f);
            pc.ResetHead();
            headBase = pc.Head.localPosition;
            body.ArmScale = 0.72f;
            body.SetSkinTone(1.0f);
            hero.GlowScale = 0f;
            hero.DamageFlash = 0f;
            pc.Cam.fieldOfView = 76f;
            dayLight = new EndingLight { Pos = kitchen.World(new Vector3(0f, 1.7f, 1.9f)), Radius = 8.5f, Intensity = 0.9f, Col = new Color(1f, 0.93f, 0.80f, 1f) };
            faceLight = new EndingLight { Pos = kitchen.World(Kitchen.SeatRoot + new Vector3(0f, 1.2f, 0.3f)), Radius = 2.2f, Intensity = 0.40f, Col = new Color(1f, 0.92f, 0.78f, 1f) };
            LightField.Register(dayLight);
            LightField.Register(faceLight);
        }

        // ------------------------------------------------------------------ поездка

        void EvaluateRide(float t)
        {
            // автобус: разгон от вокзала, плавно уходит на ось дороги из города
            float s = RideDistance(t);
            Vector3 start = bus.StartPos;
            Vector3 bp = new Vector3(Mathf.Lerp(start.x, RideStage.RoadX + 1.0f, S(1.0f, 6.0f, t)), start.y, start.z + s);
            bus.transform.position = bp;

            // камера: голова героя в салоне, чуть трясёт на неровностях; перед Тьмой тряска сильнее
            float shakeK = 1f + 3.0f * S(11.0f, 15.0f, t);
            Vector3 shake = new Vector3(0.004f * Mathf.Sin(t * 21f), 0.008f * Mathf.Sin(t * 17f + 1f), 0f) * shakeK;
            Vector3 eye = bp + RideStage.SeatEye + shake;
            pc.TeleportTo(eye - headBase, 0f);
            float yaw = Spline(RT, RYaw, t) + 0.4f * Mathf.Sin(t * 0.7f);
            float pit = Spline(RT, RPit, t) + 0.3f * Mathf.Sin(t * 1.1f);
            float roll = 0.5f * Mathf.Sin(t * 0.8f) * shakeK;
            pc.Head.localRotation = Quaternion.Euler(pit, yaw, roll);

            ApplyRideSky(t, bp.z);

            // руки лежат на коленях
            for (int a = 0; a < 2; a++)
            {
                Vector3 p = headBase + new Vector3(a == 0 ? -0.22f : 0.20f, -0.66f, 0.40f);
                body.OverridePos[a] = p;
                body.OverrideRot[a] = Quaternion.LookRotation(new Vector3(0f, 0.05f, 1f), new Vector3(0f, 1f, 0f));
                body.OverrideOpen[a] = 0.35f;
            }

            // свет героя и салона: тёплый, потом мигает и гаснет вместе с людьми
            float flicker = Mathf.Lerp(1f, Mathf.PerlinNoise(t * 13f, 2.7f) > 0.45f ? 1f : 0.25f, S(11.0f, 13.6f, t));
            float dead = 1f - S(14.4f, 15.2f, t);
            hero.GlowScale = 0.30f * flicker * dead;
            for (int i = 0; i < ride.Ceiling.Length; i++)
            {
                ride.Ceiling[i].Pos = bp + ride.CeilingLocal(i);
                ride.Ceiling[i].Intensity = 0.34f * Mathf.Min(1f, S(0f, 0.8f, t)) * flicker * dead;
            }
            for (int i = 0; i < ride.Passengers.Count; i++)
            {
                float die = 13.8f + i * 0.30f;
                float glow = (1f - S(die - 0.15f, die + 0.25f, t)) * flicker;
                ride.SetPassenger(i, glow, t);
                ride.Passengers[i].Light.Pos = bp + ride.Passengers[i].Root.localPosition + new Vector3(0f, 0.3f, 0f);
            }

            // дорога, рассвет-приманка, Тьма
            ride.SetRoadLamps(bp.z, Mathf.Lerp(400f, 0f, S(9.8f, 14.6f, t)));
            float dawnK = S(2.8f, 8.0f, t) * (1f - 0.6f * S(9.6f, 11.8f, t) * (Mathf.PerlinNoise(t * 9f, 5f) > 0.5f ? 1f : 0f));
            ride.SetDawn(dawnK, S(11.2f, 13.5f, t));
            ride.SetGate(S(TOpen, 13.6f, t), S(13.9f, TEngulf, t), t);

            // затемнение: из чёрного, перед поглощением в чёрное (до самой кухни)
            if (!skipping) fadeA = Mathf.Max(1f - S(0f, TFadeIn, t), S(14.5f, 15.9f, t));
            flashA = 0f;

            // звуки
            if (engineSrc != null)
            {
                float ev = 0.50f * S(0f, 2.0f, t) * (1f - S(14.6f, 15.6f, t));
                AudioMix.SetVolume(engineSrc, ev);
                engineSrc.pitch = 0.85f + 0.35f * S(0f, 7f, t);
            }
            if (Crossed(0.3f)) ProcAudio.OneShot(sfx, "rumble", 0.25f, 1.1f);
            if (Crossed(6.3f)) ProcAudio.OneShot(sfx, "win", 0.30f, 1.15f);
            if (Crossed(9.7f)) ProcAudio.OneShot(sfx, "swell", 0.40f, 0.8f);
            float[] beats = { 11.0f, 11.9f, 12.7f, 13.4f, 14.0f, 14.5f };
            for (int i = 0; i < beats.Length; i++)
            {
                if (Crossed(beats[i])) ProcAudio.OneShot(sfx, "heart", 0.55f + 0.05f * i, 1f);
            }
            if (Crossed(12.4f)) ProcAudio.OneShot(sfx, "rumble", 0.65f, 0.8f);
            if (Crossed(13.2f)) ProcAudio.OneShot(sfx, "tide", 0.55f, 0.9f);
            for (int i = 0; i < 6; i++)
            {
                if (Crossed(13.8f + i * 0.30f)) ProcAudio.OneShot(sfx, "lampdie", 0.55f, 0.85f + 0.07f * i);
            }
            if (Crossed(TEngulf - 0.1f))
            {
                ProcAudio.OneShot(sfx, "tide", 1.0f, 0.7f);
                ProcAudio.OneShot(sfx, "powerdown", 0.85f, 0.9f);
            }
            if (Crossed(16.6f)) ProcAudio.OneShot(sfx, "tick", 0.25f, 0.8f);
            if (Crossed(17.6f)) ProcAudio.OneShot(sfx, "tick", 0.28f, 0.8f);
        }

        void ApplyRideSky(float t, float busZ)
        {
            float b = Gfx.Brightness;
            float day = S(2.5f, 9.0f, t) * (1f - S(11.5f, 13.2f, t));   // сколько в небе «рассвета» (золото)
            float vio = S(11.0f, 14.5f, t);                             // сколько Тьмы (фиолет)
            Vector4 horizon = Vector4.Lerp(Vector4.Lerp(new Vector4(0.050f, 0.064f, 0.104f, 0f), new Vector4(0.95f, 0.62f, 0.40f, 0f), day), new Vector4(0.50f, 0.15f, 0.65f, 0f), vio);
            Vector4 zenith = Vector4.Lerp(Vector4.Lerp(new Vector4(0.006f, 0.010f, 0.026f, 0f), new Vector4(0.20f, 0.28f, 0.55f, 0f), day), new Vector4(0.07f, 0.02f, 0.14f, 0f), vio);
            Vector4 glow = Vector4.Lerp(Vector4.Lerp(new Vector4(0.95f, 0.62f, 0.32f, 0f) * 0.35f, new Vector4(1.0f, 0.78f, 0.45f, 0f), day), new Vector4(0.60f, 0.22f, 1.0f, 0f), vio);
            Shader.SetGlobalVector("_PG_SkyHorizon", horizon);
            Shader.SetGlobalVector("_PG_SkyZenith", zenith);
            Shader.SetGlobalVector("_PG_SkyGlow", glow);
            Shader.SetGlobalFloat("_PG_Fog", Mathf.Lerp(0.034f, 0.011f, S(3.5f, 6.5f, t)) * (1f + 1.2f * vio));
            Shader.SetGlobalFloat("_PG_Exposure", 1.7f * b);   // в тесном салоне ближние лампы не должны выжигать стены (в предыстории так же)
        }

        // ------------------------------------------------------------------ кухня, утро

        void KitchenPose(float k, out float yaw, out float pit, out float roll, out Vector3 off)
        {
            yaw = Spline(KT, KYaw, k);
            pit = Spline(KT, KPit, k);
            roll = Spline(KT, KRoll, k);
            off = new Vector3(Spline(KT, KOffX, k), Spline(KT, KOffY, k), Spline(KT, KOffZ, k));
        }

        /// <summary>Глаза героя на кухне (в системе комнаты) в момент k.</summary>
        Vector3 KitchenEye(float k)
        {
            float yaw, pit, roll;
            Vector3 off;
            KitchenPose(k, out yaw, out pit, out roll, out off);
            return Kitchen.SeatRoot + headBase + off;
        }

        /// <summary>Расстояние от точки до кружки (её стенок): отрицательное — точка внутри.</summary>
        static float DistToMug(Vector3 p)
        {
            Vector2 d = new Vector2(p.x - Kitchen.MugRest.x, p.z - Kitchen.MugRest.z);
            float radial = d.magnitude - 0.057f;
            float dy = p.y - Mathf.Clamp(p.y, Kitchen.MugRest.y, Kitchen.MugRest.y + Kitchen.MugHeight);
            if (radial <= 0f && dy == 0f) return Mathf.Max(radial, -0.001f);
            return Mathf.Sqrt(Mathf.Max(radial, 0f) * Mathf.Max(radial, 0f) + dy * dy);
        }

        /// <summary>
        /// Для автотестов: самое близкое расстояние от камеры героя до кружки за всё время на кухне. Голова спящего лежит рядом с
        /// кружкой, но никогда не в ней (и камера не ближе ближней плоскости 0.1 м).
        /// </summary>
        public float MugClearance()
        {
            Vector3 hb = headBase == Vector3.zero ? new Vector3(0f, 1.62f, 0f) : headBase;
            float best = 9f;
            for (float k = 0f; k <= KFadeOut; k += 0.02f)
            {
                float yaw, pit, roll;
                Vector3 off;
                KitchenPose(k, out yaw, out pit, out roll, out off);
                best = Mathf.Min(best, DistToMug(Kitchen.SeatRoot + hb + off));
            }
            return best;
        }

        /// <summary>Для автотестов: самая низкая высота камеры над столешницей, пока голова над столом (иначе скатерть режется ближней плоскостью).</summary>
        public float TableClearance()
        {
            Vector3 hb = headBase == Vector3.zero ? new Vector3(0f, 1.62f, 0f) : headBase;
            float best = 9f;
            for (float k = 0f; k <= KFadeOut; k += 0.02f)
            {
                float yaw, pit, roll;
                Vector3 off;
                KitchenPose(k, out yaw, out pit, out roll, out off);
                Vector3 e = Kitchen.SeatRoot + hb + off;
                if (e.z > 0.03f && Mathf.Abs(e.x) < 0.65f && e.z < 0.88f) best = Mathf.Min(best, e.y - Kitchen.TableTop);
            }
            return best;
        }

        void EvaluateKitchen(float k, float real)
        {
            Vector3 org = kitchen.Origin;
            ApplyMorningSky();

            // ---- камера
            float yaw, pit, roll;
            Vector3 off;
            KitchenPose(k, out yaw, out pit, out roll, out off);
            float breath = k < KJolt ? 1f : 0.3f;
            pit += 0.5f * breath * Mathf.Sin(k * 1.2f);
            if (k >= KJolt)
            {
                // после толчка голову ещё чуть трясёт
                float jolt = Mathf.Exp(-(k - KJolt) * 5f);
                pit -= 7f * jolt;
                yaw += 4f * jolt * Mathf.Sin((k - KJolt) * 22f);
            }
            pc.Head.localPosition = headBase + off;
            pc.Head.localRotation = Quaternion.Euler(pit, yaw, roll);

            // ---- руки
            for (int a = 0; a < 2; a++)
            {
                Vector3 p;
                Quaternion r;
                float o;
                SampleHand(a == 0 ? KeysL : KeysR, k, out p, out r, out o);
                body.OverridePos[a] = p - Kitchen.SeatRoot;
                body.OverrideRot[a] = r;
                body.OverrideOpen[a] = o;
            }

            // ---- комната утром: лампа и свеча погашены, кофе остыл, часы — раннее утро
            kitchen.BulbMat.SetColor("_Color", new Color(0.10f, 0.09f, 0.08f, 1f));
            kitchen.LampHaloMat.SetColor("_Color", new Color(1f, 0.78f, 0.42f, 0f));
            kitchen.Mug.localPosition = Kitchen.MugRest;
            for (int i = 0; i < kitchen.Steam.Length; i++) kitchen.Steam[i].localScale = Vector3.zero;
            kitchen.Flame.localScale = Vector3.zero;
            kitchen.FlameCore.localScale = Vector3.zero;
            kitchen.FlameHalo.localScale = Vector3.zero;
            kitchen.SmokeMat.SetColor("_Color", new Color(0.55f, 0.55f, 0.6f, 0f));
            for (int i = 0; i < kitchen.Smoke.Length; i++) kitchen.Smoke[i].gameObject.SetActive(false);
            kitchen.SetClock(6f, 56f + k / 60f, k);

            // ---- мокрое: пятно проявляется, когда он замечает тепло, лужа растекается
            kitchen.SetWet(S(8.8f, 15.0f, k), k);

            // ---- вспышка толчка и затемнение
            flashA = k >= KJolt ? 0.85f * Mathf.Exp(-(k - KJolt) * 4.0f) : 0f;
            if (!skipping) fadeA = S(KFadeOut, KFadeOut + 0.9f, k);

            // ---- звуки
            float sec = Mathf.Floor(k);
            if (sec != lastTick && k >= 0.5f && k < 9f)
            {
                lastTick = sec;
                ProcAudio.OneShot(sfx, "tick", 0.16f, 1f);
            }
            if (Crossed(TKitchen + 0.2f)) ProcAudio.OneShot(sfx, "exhale", 0.12f, 0.8f);
            if (Crossed(TKitchen + KJolt))
            {
                ProcAudio.OneShot(sfx, "hit", 0.9f, 0.6f);
                ProcAudio.OneShot(sfx, "inhale", 0.85f, 1.2f);
            }
            float[] beats = { 1.5f, 1.95f, 2.45f, 3.05f, 3.8f };
            for (int i = 0; i < beats.Length; i++)
            {
                if (Crossed(TKitchen + beats[i])) ProcAudio.OneShot(sfx, "heart", 0.6f - 0.06f * i, 1f);
            }
            float[] drips = { 11.7f, 12.6f, 13.5f, 14.6f };
            for (int i = 0; i < drips.Length; i++)
            {
                if (Crossed(TKitchen + drips[i])) ProcAudio.OneShot(sfx, "drip", 0.75f - 0.1f * i, 1f + 0.08f * i);
            }
            if (Crossed(TKitchen + KTitle)) ProcAudio.OneShot(sfx, "orb", 0.5f, 0.7f);
        }

        void ApplyMorningSky()
        {
            float b = Gfx.Brightness;
            // дневное небо: яркое, чтобы за окном было видно утро, а не ночь со звёздами
            Shader.SetGlobalVector("_PG_SkyHorizon", new Vector4(1.10f, 0.92f, 0.72f, 0f));
            Shader.SetGlobalVector("_PG_SkyZenith", new Vector4(0.50f, 0.70f, 1.05f, 0f));
            Shader.SetGlobalVector("_PG_SkyGlow", new Vector4(1.0f, 0.84f, 0.58f, 0f));
            Shader.SetGlobalFloat("_PG_Fog", 0.004f);
            Shader.SetGlobalFloat("_PG_Exposure", 1.7f * b);
        }

        // ------------------------------------------------------------------ экран: субтитры, полосы, затемнение

        void EnsureStyles()
        {
            int fs = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.036f));
            if (capStyle != null && lastFs == fs) return;
            lastFs = fs;
            capStyle = new GUIStyle(GUI.skin.label);
            capStyle.fontSize = fs;
            capStyle.alignment = TextAnchor.UpperCenter;
            capStyle.wordWrap = true;
            hintStyle = new GUIStyle(capStyle);
            hintStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(fs * 0.58f));
            hintStyle.alignment = TextAnchor.LowerRight;
            hintStyle.wordWrap = false;
            titleStyle = new GUIStyle(capStyle);
            titleStyle.fontSize = Mathf.RoundToInt(fs * 3.3f);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            subStyle = new GUIStyle(capStyle);
            subStyle.fontSize = Mathf.RoundToInt(fs * 1.1f);
            subStyle.alignment = TextAnchor.MiddleCenter;
        }

        static void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            GUI.color = Color.white;   // GUI.color красит и текст: после чёрных полос и затемнения он остался бы чёрным
            st.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.85f);
            st.hover.textColor = st.normal.textColor;
            st.active.textColor = st.normal.textColor;
            st.focused.textColor = st.normal.textColor;
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, st);
            st.normal.textColor = c;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
            GUI.Label(r, s, st);
        }

        void DrawCaps(Cap[] caps, float w, float h, float bar)
        {
            if (caps == null) return;
            for (int i = 0; i < caps.Length; i++)
            {
                Cap c = caps[i];
                if (t < c.A || t > c.B) continue;
                float a = Mathf.Clamp01(Mathf.Min((t - c.A) / 0.4f, (c.B - t) / 0.4f));
                GUI.color = new Color(0f, 0f, 0f, 0.38f * a);
                GUI.DrawTexture(new Rect(0f, h - bar - h * 0.13f, w, h * 0.13f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                Shadowed(new Rect(w * 0.12f, h - bar - h * 0.115f, w * 0.76f, h * 0.10f), c.Text, capStyle, new Color(1f, 0.95f, 0.82f, a));
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!active) return;
            EnsureStyles();
            GUI.depth = -10;   // поверх остального интерфейса

            float w = Screen.width;
            float h = Screen.height;

            // затемнение — под полосами, субтитрами и названием: они остаются видны на чёрном
            if (fadeA > 0.001f)
            {
                GUI.color = new Color(0f, 0f, 0f, fadeA);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            }

            // чёрные полосы, как в кино
            float bar = h * 0.095f;
            GUI.color = new Color(0f, 0f, 0f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, w, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h - bar, w, bar), Texture2D.whiteTexture);

            DrawCaps(rideCaps, w, h, bar);
            DrawCaps(kitchenCaps, w, h, bar);

            // название на чёрном экране в конце
            float k = t - TKitchen;
            if (k >= KTitle)
            {
                float a = Mathf.Clamp01(Mathf.Min((k - KTitle) / 0.8f, (KEnd - k) / 0.8f));
                Shadowed(new Rect(0f, h * 0.36f, w, h * 0.16f), "ТЛЕЮЩАЯ ТЬМА", titleStyle, new Color(1f, 0.82f, 0.48f, a));
                Shadowed(new Rect(0f, h * 0.53f, w, h * 0.06f), "конец", subStyle, new Color(1f, 0.92f, 0.75f, a * 0.8f));
            }

            // подсказка про пропуск
            if (t > 1.5f && k < KFadeOut)
            {
                Shadowed(new Rect(w * 0.5f, h - bar - h * 0.045f, w * 0.48f, h * 0.04f), "Пробел — пропустить", hintStyle, new Color(1f, 0.92f, 0.75f, 0.5f));
            }

            // белая вспышка толчка — поверх всего
            if (flashA > 0.01f)
            {
                GUI.color = new Color(1f, 0.97f, 0.88f, flashA);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
