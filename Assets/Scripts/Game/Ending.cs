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
    /// (PlayerController/HeroBody). Начинается концовка с посадки: из чёрного герой бежит по площади к двери автобуса, перешагивает порог
    /// (кузов снаружи прячется под тёмным провалом двери), идёт по проходу и садится на переднее правое сиденье; только после этого автобус
    /// трогается. Поездка идёт в настоящем городе (камера внутри автобуса, салон достроен RideStage), кухня — та же,
    /// что в предыстории (Kitchen), только утром. Пропуск — Пробел, Enter, Esc или клик (после секунды): затемнение и экран итога.
    /// </summary>
    public class EndingDirector : MonoBehaviour
    {
        // ------------------------------------------------------------------ время сценария (секунды от начала)

        public const float TBoard = 5.6f;         // посадка: бег к двери, порог, проход, сиденье; потом автобус трогается (время поездки rt = t - TBoard)
        public const float TFadeIn = 0.55f;       // из чёрного к бегу
        public const float TDoor = 2.9f;          // камера пересекает борт: кузов снаружи прячется
        public const float TOpen = 10.4f;         // (время поездки) Тьма за «рассветом» начинает раскрываться
        public const float TEngulf = 15.0f;       // (время поездки) автобус въезжает в Тьму
        public const float RideLen = 18.4f;       // сколько длится поездка до чёрного экрана
        public const float TKitchen = TBoard + RideLen;   // чёрный экран кончился: кухня (кухонное время k = t - TKitchen)
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

        // Камера в салоне: время, поворот вправо, наклон вниз (градусы). Сначала вперёд, потом на водителя (он оборачивается), на людей
        // сзади и снова вперёд. Плавность на каждом отрезке (RideStage.Ease): в ключах скорость нулевая, поэтому остановка на водителе настоящая.
        static readonly float[] RT = { 0f, 1.4f, 2.7f, 3.6f, 5.0f, 6.0f, 7.1f, 8.2f, 12.0f, 13.6f, 15.0f, 18.4f };
        static readonly float[] RYaw = { 0f, 1f, -62f, -64f, -150f, -150f, -10f, 0f, 0f, -7f, 3f, 0f };
        static readonly float[] RPit = { 2f, 2f, 2f, 1f, 3f, 3f, 3f, 4f, 2f, 0f, 0f, 0f };

        // Посадка: путь глаз героя в системе автобуса (м; x вправо от оси автобуса, z вперёд), курс тела (градусы) и наклон головы.
        // Бег к двери — кривая Безье из точки на площади к кольцу у двери (RunA -> RunK -> RunB), курс к двери на подходе ровно +x (90 градусов).
        // Дальше сплайн: ступенька (глаза поднимаются на 0.62 м пола салона), порог (борт на x = -1.35), поворот налево к носу, проход,
        // поворот направо к сиденью и посадка. Скорость на стыке бега и сплайна не прыгает: начальная касательная сплайна — скорость конца бега.
        const float TRun = 2.5f;
        static readonly Vector2 RunA = new Vector2(-13.0f, 4.6f);
        static readonly Vector2 RunK = new Vector2(-7.0f, 1.65f);
        static readonly Vector2 RunB = new Vector2(-2.3f, 1.65f);
        static readonly float[] BT = { 2.50f, 3.00f, 3.40f, 3.90f, 4.40f, 4.85f, 5.25f, 5.60f };
        static readonly float[] BX = { -2.30f, -1.25f, -0.70f, -0.40f, -0.28f, 0.30f, 0.80f, 0.80f };
        static readonly float[] BZ = { 1.65f, 1.66f, 1.72f, 2.15f, 2.78f, 3.12f, 3.20f, 3.20f };
        static readonly float[] BY = { 1.62f, 2.00f, 2.24f, 2.24f, 2.24f, 2.14f, 1.88f, 1.88f };
        static readonly float[] BYaw = { 90f, 90f, 76f, 30f, 3f, 38f, 0f, 0f };
        static readonly float[] BPit = { 2.5f, 1.0f, 0.0f, 2.0f, 3.0f, 9.0f, 4.0f, 2.0f };

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
            new HKey(9.8f, new Vector3(-0.17f, 0.62f, -0.10f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f),
            new HKey(40.0f, new Vector3(-0.17f, 0.62f, -0.10f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f)
        };

        static readonly HKey[] KeysR =
        {
            new HKey(0.0f, new Vector3(-0.28f, 0.80f, 0.42f), new Vector3(0.5f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.30f),
            new HKey(1.2f, new Vector3(-0.28f, 0.80f, 0.42f), new Vector3(0.5f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.30f),
            new HKey(1.7f, new Vector3(0.17f, 0.93f, 0.26f), new Vector3(0f, 0.1f, 1f), new Vector3(0f, 1f, 0f), 0.80f),   // правая рука идёт на стол ЗА кружкой и справа от ручки, а не над самой кружкой
            new HKey(2.6f, new Vector3(0.15f, 0.81f, 0.20f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.60f),
            new HKey(5.6f, new Vector3(0.15f, 0.81f, 0.20f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.60f),
            new HKey(6.8f, new Vector3(0.15f, 1.08f, 0.06f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(8.6f, new Vector3(0.15f, 1.08f, 0.06f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(9.8f, new Vector3(0.17f, 0.62f, -0.10f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f),
            new HKey(40.0f, new Vector3(0.17f, 0.62f, -0.10f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), 0.40f)
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
        int stage = -1;            // -1 ещё не начато, 2 посадка, 0 поездка, 1 кухня
        int boarded;
        int total;
        int lastStep = -1;         // номер последнего шага при посадке (по нему звучат шаги)
        bool insideSky;            // небо и туман уже салонные (после порога)
        Cap[] boardCaps;
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
        /// <summary>Время поездки (от момента, когда герой сел и автобус тронулся); во время посадки 0.</summary>
        public float RideTime { get { return Mathf.Max(0f, t - TBoard); } }
        /// <summary>Идёт посадка: герой ещё бежит к автобусу, идёт по проходу или садится.</summary>
        public bool Boarding { get { return active && t < TBoard; } }
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
            LightOrb.ClearAll();                // осколки света, летевшие к герою в последние секунды, в кадре бега не нужны
            Survivor.IntroHideAll(true);        // не севшие в автобус (разбуженные в последний момент) не стоят на пути героя к двери: камера пройдёт сквозь кольцо

            // автобус у вокзала, дальше он едет под управлением сценария; салон достроен. Кузов снаружи виден, пока герой бежит к двери
            bus.BeginCinematic(true);
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
            lastStep = -1;
            insideSky = false;
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
            ResetPostLook();
            if (body != null)
            {
                body.OverrideW = 0f;
                body.ShoulderShift = Vector3.zero;
                body.HandObstacle = null;
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
            ResetPostLook();
        }

        /// <summary>Вернуть обычную виньетку и убрать помехи камкордера (в конце поездки их разгоняла Тьма).</summary>
        static void ResetPostLook()
        {
            LowRes lr = LowRes.Instance;
            if (lr == null) return;
            lr.Vignette = LowRes.DefaultVignette;
            lr.ExtraGlitch = 0f;
            lr.ClearFinalFx();   // эффекты последней минуты (каёмка, угольки, рябь) в концовку не переходят
        }

        // ------------------------------------------------------------------ субтитры

        void BuildCaps()
        {
            // как поездка звучит, зависит от того, кто с героем в автобусе
            string withMe = boarded >= 3 ? "Все со мной. Все живы." : (boarded >= 1 ? "Нас мало, но мы живы." : "Со мной только водитель. Но я выбрался.");
            string line = boarded >= 1 ? "— Смотрите, как светло!" : "Как светло...";
            float b = TBoard;   // подписи поездки заданы во времени поездки, считаем их от посадки
            boardCaps = new Cap[]
            {
                new Cap(0.45f, 2.3f, "Скорее! Сейчас уедут без меня!"),
                new Cap(3.15f, 4.3f, "Успел!")
            };
            rideCaps = new Cap[]
            {
                new Cap(b + 0.9f, b + 3.2f, "Мы едем. Получилось."),
                new Cap(b + 3.5f, b + 6.2f, withMe),
                new Cap(b + 6.7f, b + 9.0f, "Впереди рассвет. Настоящий."),
                new Cap(b + 9.3f, b + 11.3f, line),
                new Cap(b + 11.6f, b + 13.2f, "Почему он... мигает?"),
                new Cap(b + 13.4f, b + 14.7f, "Это не рассвет."),
                new Cap(b + 14.8f, b + 16.0f, "Это ловушка.")
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

        /// <summary>Дым собирается под камеру, когда она уже стоит там, где её увидит кадр (как и спрайты Billboard).</summary>
        void LateUpdate()
        {
            if (!active || ride == null || stage != 0) return;
            Transform ct = Gfx.CamTransform != null ? Gfx.CamTransform : (pc != null && pc.Cam != null ? pc.Cam.transform : null);
            if (ct == null) return;
            ride.DrawDarkness(ct.position, ct.rotation);
        }

        /// <summary>Для автотестов: сколько клубов Тьмы нарисовано снаружи и в салоне в последнем кадре поездки.</summary>
        public int DarkPuffsDrawn { get { return ride != null ? ride.DarkPuffsDrawn : 0; } }
        public int CabinPuffsDrawn { get { return ride != null ? ride.CabinPuffsDrawn : 0; } }
        public bool HasDriver { get { return ride != null && ride.HasDriver; } }
        /// <summary>Для автотестов: расстояние от водителя до ближайшего жителя в салоне, м.</summary>
        public float DriverClearance { get { return ride != null ? ride.DriverClearance() : 9f; } }

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
            lastStep = -1;
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

        static void SampleHand(HKey[] k, float x, float side, out Vector3 p, out Quaternion r, out float o)
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
            r = HeroBody.HandSlerp(Quaternion.LookRotation(k[i].F, k[i].U), Quaternion.LookRotation(k[i + 1].F, k[i + 1].U), u, side);
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

        /// <summary>Когда гаснет свет у i-го в салоне: водитель (он первый в списке) раньше всех, потом пассажиры по очереди.</summary>
        static float DieTime(int i)
        {
            return i == 0 ? 13.55f : 13.9f + (i - 1) * 0.30f;
        }

        void Evaluate(float real)
        {
            if (ride == null || pc == null) return;
            if (real < TBoard)
            {
                if (stage != 2) EnterBoarding();
                EvaluateBoarding(real);
            }
            else if (real < TKitchen)
            {
                if (stage != 0) EnterRide();
                EvaluateRide(real - TBoard);
            }
            else
            {
                if (stage != 1) EnterKitchen();
                EvaluateKitchen(real - TKitchen, real);
            }
            lastT = real;
        }

        void EnterBoarding()
        {
            stage = 2;
            body.SnapPose();   // руки сразу в позе бега, а не летят к ней из прежней
            body.HandObstacle = null;
            body.ArmScale = 0.8f;
            body.SetSkinTone(1f);
            if (ride != null) ride.SetDarknessVisible(false);
            if (kitchen != null) kitchen.Root.gameObject.SetActive(false);
            if (dayLight != null) LightField.Unregister(dayLight);
            if (faceLight != null) LightField.Unregister(faceLight);
            dayLight = null;
            faceLight = null;
            // возврат к началу (перемотка в автотестах): автобус снаружи снова виден, небо и туман обычные
            if (bus != null) bus.ShowExterior();
            if (insideSky) Gfx.ApplyGlobals();
            insideSky = false;
            ResetPostLook();
            lastStep = -1;
            if (engineSrc != null && !engineSrc.isPlaying) engineSrc.Play();
        }

        void EnterRide()
        {
            stage = 0;
            body.SnapPose();
            body.HandObstacle = null;   // в автобусе кружки нет
            if (ride != null) ride.SetDarknessVisible(true);
            if (kitchen != null) kitchen.Root.gameObject.SetActive(false);
            body.ArmScale = 0.8f;
            body.SetSkinTone(1f);
            pc.Cam.fieldOfView = 74f;
            if (bus != null) bus.HideExterior();   // перемотка из посадки прямо в поездку
            insideSky = true;
            if (dayLight != null) LightField.Unregister(dayLight);
            if (faceLight != null) LightField.Unregister(faceLight);
            dayLight = null;
            faceLight = null;
            if (engineSrc != null && !engineSrc.isPlaying) engineSrc.Play();
        }

        // ------------------------------------------------------------------ посадка

        static float Herm(float p0, float v0, float p1, float v1, float h, float u)
        {
            float u2 = u * u;
            float u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * p0 + (u3 - 2f * u2 + u) * h * v0 + (-2f * u3 + 3f * u2) * p1 + (u3 - u2) * h * v1;
        }

        /// <summary>Сплайн по ключам с заданной скоростью в первом ключе (остальные касательные — по соседним ключам, как в Spline).</summary>
        static float SplineV(float[] ts, float[] vs, float x, float v0)
        {
            int n = ts.Length;
            if (x <= ts[0]) return vs[0];
            if (x >= ts[n - 1]) return vs[n - 1];
            int i = 0;
            while (i < n - 2 && x >= ts[i + 1]) i++;
            float h = ts[i + 1] - ts[i];
            float u = (x - ts[i]) / h;
            float m0 = i > 0 ? (vs[i + 1] - vs[i - 1]) / (ts[i + 1] - ts[i - 1]) : v0;
            float m1 = i + 2 < n ? (vs[i + 2] - vs[i]) / (ts[i + 2] - ts[i]) : 0f;   // у сиденья скорость затухает в ноль
            return Herm(vs[i], m0, vs[i + 1], m1, h, u);
        }

        /// <summary>Бег к двери: точка на площади (x, z в системе автобуса) и курс тела. Темп спадает: на старте на треть быстрее среднего, у двери вдвое медленнее.</summary>
        static Vector2 RunPoint(float time, out float yawDeg)
        {
            float u = Mathf.Clamp01(time / TRun);
            float s = 1.3f * u - 0.3f * u * u;
            float m = 1f - s;
            Vector2 p = m * m * RunA + 2f * m * s * RunK + s * s * RunB;
            Vector2 d = 2f * m * (RunK - RunA) + 2f * s * (RunB - RunK);
            yawDeg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            return p;
        }

        /// <summary>
        /// Глаза героя при посадке в момент time: положение в системе автобуса, курс тела и наклон головы (градусы).
        /// Чистая функция времени, как и всё в сценарии.
        /// </summary>
        public static void BoardPose(float time, out Vector3 local, out float yawDeg, out float pitchDeg)
        {
            float y;
            if (time <= TRun)
            {
                Vector2 q = RunPoint(time, out y);
                local = new Vector3(q.x, BY[0], q.y);
                yawDeg = y;
                pitchDeg = Mathf.Lerp(4f, BPit[0], S(0f, TRun, time));
                return;
            }
            // скорость в конце бега: сплайн продолжает её, а не начинает заново
            float yEnd, yPrev;
            Vector2 qEnd = RunPoint(TRun, out yEnd);
            Vector2 qPrev = RunPoint(TRun - 0.02f, out yPrev);
            Vector2 v = (qEnd - qPrev) / 0.02f;
            float vYaw = (yEnd - yPrev) / 0.02f;
            local = new Vector3(SplineV(BT, BX, time, v.x), SplineV(BT, BY, time, 0f), SplineV(BT, BZ, time, v.y));
            yawDeg = SplineV(BT, BYaw, time, vYaw);
            pitchDeg = SplineV(BT, BPit, time, 0f);
        }

        /// <summary>Сколько шагов герой сделал к моменту time: бег (темп спадает с 3 до 2.5 шагов в секунду), потом шаг по салону; у сиденья шаги кончаются.</summary>
        static float StepCount(float time)
        {
            float run = Mathf.Min(time, TRun);
            float steps = 3.0f * run - 0.1f * run * run;
            if (time > TRun) steps += 2.0f * (Mathf.Min(time, 5.0f) - TRun);
            return steps;
        }

        /// <summary>Сила качки от шагов: 1 — бег, поменьше при шаге по салону, 0 — сидит.</summary>
        static float BobEnvelope(float time)
        {
            return Mathf.Lerp(1f, 0.55f, S(2.6f, 3.4f, time)) * (1f - S(4.6f, 5.2f, time));
        }

        /// <summary>Для автотестов: самое близкое расстояние (по полу, м) от пути героя внутри салона (после порога) до людей в салоне и водителя.</summary>
        public float BoardingClearance()
        {
            if (ride == null) return 9f;
            float best = 9f;
            for (float time = TDoor; time <= TBoard; time += 0.05f)
            {
                Vector3 p;
                float yaw, pit;
                BoardPose(time, out p, out yaw, out pit);
                for (int i = 0; i < ride.Passengers.Count; i++)
                {
                    Vector3 d = ride.Passengers[i].Root.localPosition - p;
                    d.y = 0f;
                    best = Mathf.Min(best, d.magnitude);
                }
            }
            return best;
        }

        void EvaluateBoarding(float t)
        {
            Vector3 bp = bus.StartPos;
            bus.transform.position = bp;

            Vector3 local;
            float yaw, pit;
            BoardPose(t, out local, out yaw, out pit);

            // качка от шагов: голова ныряет на каждый шаг, чуть уходит в стороны и кренится; чем ближе к сиденью, тем спокойнее
            float steps = StepCount(t);
            float env = BobEnvelope(t);
            float sw = Mathf.Sin(Mathf.PI * steps);
            float dive = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * steps);
            Quaternion hq = Quaternion.Euler(0f, yaw, 0f);
            Vector3 eye = bp + local + hq * Vector3.right * (0.03f * env * sw) + Vector3.up * (-0.055f * env * dive);
            // корень игрока стоит под головой и повёрнут по курсу тела: руки сценария заданы в его системе, значит, идут перед камерой
            pc.TeleportTo(eye - hq * headBase, yaw);
            float breath = 0.4f * Mathf.Sin(t * 0.9f);
            pc.Head.localRotation = Quaternion.Euler(pit + 1.6f * env * Mathf.Cos(2f * Mathf.PI * steps) + breath, 1.2f * env * Mathf.Sin(Mathf.PI * steps + 0.6f), 1.8f * env * sw);
            pc.Cam.fieldOfView = Mathf.Lerp(82f, 74f, S(2.6f, 4.8f, t));   // на бегу шире (скорость), в салоне как в поездке

            // руки: бегут (кулаки, ладонями внутрь, в противофазе), в салоне опускаются, при посадке ложатся на колени
            body.ShoulderShift = Vector3.zero;
            float lap = S(4.7f, 5.3f, t);
            for (int a = 0; a < 2; a++)
            {
                float side = a == 0 ? -1f : 1f;
                float ph = Mathf.PI * steps + a * Mathf.PI;
                float swing = Mathf.Sin(ph) * env;
                Vector3 runP = headBase + new Vector3(side * 0.24f, -0.24f + 0.04f * Mathf.Abs(Mathf.Cos(ph)) * env, 0.30f + 0.20f * swing);   // у нижних углов кадра, как «готовая» поза в игре (y 1.38, z 0.45)
                Quaternion runR = Quaternion.LookRotation(new Vector3(-side * 0.12f, 0.15f + 0.25f * swing, 1f), new Vector3(side, 0.35f, 0f));
                Vector3 lapP = headBase + new Vector3(a == 0 ? -0.22f : 0.20f, -0.66f, 0.40f);
                Quaternion lapR = Quaternion.LookRotation(new Vector3(0f, 0.05f, 1f), new Vector3(0f, 1f, 0f));
                body.OverridePos[a] = Vector3.Lerp(runP, lapP, lap);
                body.OverrideRot[a] = Quaternion.Slerp(runR, lapR, lap);
                body.OverrideOpen[a] = Mathf.Lerp(0.15f, 0.35f, lap);
            }

            // кузов снаружи прячется под самым тёмным моментом провала двери, небо и туман — салонные с этого же момента
            bool outside = t < TDoor;
            if (outside) bus.ShowExterior();
            else bus.HideExterior();
            if (!outside)
            {
                ApplyRideSky(0f, bp.z);
                insideSky = true;
            }
            else if (insideSky)
            {
                Gfx.ApplyGlobals();
                insideSky = false;
            }
            bus.CineLight = 1f - S(TDoor, TDoor + 1.0f, t);

            // свет: у героя на улице он ярче, в салоне как в поездке; лампы салона и люди уже горят
            hero.GlowScale = Mathf.Lerp(0.9f, 0.30f, S(2.6f, 4.2f, t));
            float rt = t - TBoard;   // время поездки до её начала отрицательно: водитель и пассажиры в позе начала поездки
            CabinLights(bp, rt, 1f, 1f);

            // затемнение: из чёрного, и короткий провал в тёмный дверной проём
            if (!skipping) fadeA = Mathf.Max(1f - S(0f, TFadeIn, t), 0.92f * S(TDoor - 0.28f, TDoor, t) * (1f - S(TDoor, TDoor + 0.38f, t)));
            flashA = 0f;

            // звуки: двигатель на холостом ходу, дыхание, шаги по земле (звонкие) и по салону (глухие), посадка
            if (engineSrc != null)
            {
                AudioMix.SetVolume(engineSrc, 0.30f + 0.05f * S(2.6f, 3.6f, t));
                engineSrc.pitch = 0.85f;
            }
            int si = Mathf.FloorToInt(steps);
            if (si != lastStep)
            {
                if (lastStep >= 0 && si > lastStep && t < 5.1f)
                {
                    bool ground = t < TDoor;
                    ProcAudio.OneShot(sfx, (si & 1) == 0 ? "step" : "step2", ground ? 0.55f : 0.38f, ground ? 1.2f : 0.78f);
                }
                lastStep = si;
            }
            if (Crossed(0.15f)) ProcAudio.OneShot(sfx, "inhale", 0.25f, 1.25f);
            if (Crossed(TDoor)) ProcAudio.OneShot(sfx, "rumble", 0.30f, 1.3f);   // порог: гулкая подножка
            if (Crossed(5.0f)) ProcAudio.OneShot(sfx, "rustle", 0.45f, 0.9f);    // сел
            if (Crossed(5.25f)) ProcAudio.OneShot(sfx, "exhale", 0.30f, 0.95f);
        }

        /// <summary>
        /// Свет салона: лампы под потолком и огоньки людей. anim — время для их анимации (время поездки), flicker — мигание перед
        /// Тьмой (1 — ровно), dead — сколько света осталось (0 — погас совсем).
        /// </summary>
        void CabinLights(Vector3 bp, float anim, float flicker, float dead)
        {
            for (int i = 0; i < ride.Ceiling.Length; i++)
            {
                ride.Ceiling[i].Pos = bp + ride.CeilingLocal(i);
                ride.Ceiling[i].Intensity = 0.34f * flicker * dead;
            }
            for (int i = 0; i < ride.Passengers.Count; i++)
            {
                float die = DieTime(i);
                float glow = (1f - S(die - 0.15f, die + 0.25f, anim)) * flicker;
                ride.SetPassenger(i, glow, anim);
                ride.Passengers[i].Light.Pos = bp + ride.Passengers[i].Root.localPosition + new Vector3(0f, 0.3f, 0f);
            }
        }

        void EnterKitchen()
        {
            stage = 1;
            // автобус и поездка остаются позади: герой за кухонным столом, как в самом начале
            if (ride != null) ride.SetDarknessVisible(false);
            ResetPostLook();
            body.SnapPose();   // склейка: герой уже за столом, руки в позе сна, а не летят к ней от колен в автобусе
            body.HandObstacle = kitchen.MugPush;   // кисти не проходят сквозь кружку и её ручку
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

            // камера: голова героя в салоне, чуть трясёт на неровностях; перед Тьмой тряска сильнее, а когда фронт Тьмы бьёт в автобус, швыряет
            float shakeK = 1f + 3.0f * S(11.0f, 14.0f, t) + 3.5f * S(14.0f, 15.2f, t);
            Vector3 shake = new Vector3(0.004f * Mathf.Sin(t * 21f), 0.008f * Mathf.Sin(t * 17f + 1f), 0f) * shakeK;
            Vector3 eye = bp + RideStage.SeatEye + shake;
            pc.TeleportTo(eye - headBase, 0f);
            float yaw = RideStage.Ease(RT, RYaw, t) + 0.4f * Mathf.Sin(t * 0.7f);
            float pit = RideStage.Ease(RT, RPit, t) + 0.3f * Mathf.Sin(t * 1.1f);
            float roll = 0.5f * Mathf.Sin(t * 0.8f) * shakeK;
            pc.Head.localRotation = Quaternion.Euler(pit, yaw, roll);

            ApplyRideSky(t, bp.z);

            // руки лежат на коленях (сидит откинувшись: плечи на месте)
            body.ShoulderShift = Vector3.zero;
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
            bus.CineLight = 0f;
            CabinLights(bp, t, flicker, dead);

            // дорога, рассвет-приманка, Тьма (объёмный дым надвигается на автобус и накрывает его)
            ride.SetRoadLamps(ride.FrontZ(t));
            float dawnK = S(2.8f, 8.0f, t) * (1f - 0.6f * S(9.6f, 11.8f, t) * (Mathf.PerlinNoise(t * 9f, 5f) > 0.5f ? 1f : 0f));
            ride.SetDawn(dawnK * (1f - S(13.9f, 15.0f, t)), S(11.2f, 13.5f, t));   // Тьма накрывает зарево, а не светится сквозь себя
            ride.SetDarkness(t, eye);

            // камкордер умирает вместе со светом: края кадра смыкаются, картинку рвёт помехами
            LowRes lr = LowRes.Instance;
            if (lr != null)
            {
                lr.Vignette = Mathf.Lerp(LowRes.DefaultVignette, 6.5f, S(13.6f, 15.4f, t));
                lr.ExtraGlitch = 0.5f * S(14.0f, 15.0f, t) * (Mathf.PerlinNoise(t * 17f, 1.3f) > 0.4f ? 1f : 0.3f);
            }

            // затемнение: чёрного в начале нет (поездка продолжает посадку), потом тьма накрывает экран и он гаснет (чёрный до самой кухни)
            if (!skipping) fadeA = S(15.0f, 16.3f, t);
            flashA = 0f;

            // звуки (времена поездки: Crossed считает от начала всего сценария, поэтому + TBoard)
            if (engineSrc != null)
            {
                float ev = 0.50f * (0.7f + 0.3f * S(0f, 2.0f, t)) * (1f - S(14.6f, 15.6f, t));   // на холостом ходу при посадке было 0.35
                AudioMix.SetVolume(engineSrc, ev);
                engineSrc.pitch = 0.85f + 0.35f * S(0f, 7f, t);
            }
            if (Crossed(TBoard + 0.3f)) ProcAudio.OneShot(sfx, "rumble", 0.25f, 1.1f);   // тронулись
            if (Crossed(TBoard + 6.3f)) ProcAudio.OneShot(sfx, "win", 0.30f, 1.15f);
            if (Crossed(TBoard + 9.7f)) ProcAudio.OneShot(sfx, "swell", 0.40f, 0.8f);
            float[] beats = { 11.0f, 11.9f, 12.7f, 13.4f, 14.0f, 14.5f };
            for (int i = 0; i < beats.Length; i++)
            {
                if (Crossed(TBoard + beats[i])) ProcAudio.OneShot(sfx, "heart", 0.55f + 0.05f * i, 1f);
            }
            if (Crossed(TBoard + 12.4f)) ProcAudio.OneShot(sfx, "rumble", 0.65f, 0.8f);
            if (Crossed(TBoard + 13.2f)) ProcAudio.OneShot(sfx, "tide", 0.55f, 0.9f);
            for (int i = 0; i < ride.Passengers.Count; i++)
            {
                if (Crossed(TBoard + DieTime(i))) ProcAudio.OneShot(sfx, "lampdie", 0.55f, 0.85f + 0.07f * i);
            }
            if (Crossed(TBoard + 14.15f)) ProcAudio.OneShot(sfx, "eater", 0.6f, 0.6f);   // фронт Тьмы бьёт в автобус
            if (Crossed(TBoard + TEngulf - 0.1f))
            {
                ProcAudio.OneShot(sfx, "tide", 1.0f, 0.7f);
                ProcAudio.OneShot(sfx, "powerdown", 0.85f, 0.9f);
            }
            if (Crossed(TBoard + 16.6f)) ProcAudio.OneShot(sfx, "tick", 0.25f, 0.8f);
            if (Crossed(TBoard + 17.6f)) ProcAudio.OneShot(sfx, "tick", 0.28f, 0.8f);
        }

        void ApplyRideSky(float t, float busZ)
        {
            float b = Gfx.Brightness;
            float day = S(2.5f, 9.0f, t) * (1f - S(11.5f, 13.2f, t));   // сколько в небе «рассвета» (золото)
            float vio = S(11.0f, 14.5f, t);                             // сколько Тьмы (фиолет)
            Vector4 horizon = Vector4.Lerp(Vector4.Lerp(new Vector4(0.050f, 0.064f, 0.104f, 0f), new Vector4(0.95f, 0.62f, 0.40f, 0f), day), new Vector4(0.50f, 0.15f, 0.65f, 0f), vio);
            Vector4 zenith = Vector4.Lerp(Vector4.Lerp(new Vector4(0.006f, 0.010f, 0.026f, 0f), new Vector4(0.20f, 0.28f, 0.55f, 0f), day), new Vector4(0.07f, 0.02f, 0.14f, 0f), vio);
            Vector4 glow = Vector4.Lerp(Vector4.Lerp(new Vector4(0.95f, 0.62f, 0.32f, 0f) * 0.35f, new Vector4(1.0f, 0.78f, 0.45f, 0f), day), new Vector4(0.60f, 0.22f, 1.0f, 0f), vio);
            // Тьма накрыла автобус: небо гаснет, туман сгущается до черноты, и всё, что дальше пары метров, тонет в нём
            float eng = S(13.9f, 15.3f, t);
            horizon = Vector4.Lerp(horizon, new Vector4(0.030f, 0.010f, 0.055f, 0f), eng);
            zenith = Vector4.Lerp(zenith, new Vector4(0.004f, 0.002f, 0.010f, 0f), eng);
            glow = Vector4.Lerp(glow, new Vector4(0.25f, 0.08f, 0.45f, 0f), eng);
            Shader.SetGlobalVector("_PG_SkyHorizon", horizon);
            Shader.SetGlobalVector("_PG_SkyZenith", zenith);
            Shader.SetGlobalVector("_PG_SkyGlow", glow);
            Shader.SetGlobalFloat("_PG_Fog", Mathf.Lerp(0.034f, 0.011f, S(3.5f, 6.5f, t)) * (1f + 1.2f * vio) + 0.22f * eng);
            Shader.SetGlobalVector("_PG_FogColor", Vector4.Lerp(new Vector4(0.040f * b, 0.054f * b, 0.088f * b, 0f), new Vector4(0.016f, 0.006f, 0.030f, 0f), eng));
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
            // корпус идёт за головой: пока щека лежит на руках (голова далеко вперёд и вниз), плечи тоже подались к столу;
            // когда руки легли на колени, он выпрямился (иначе плечи слишком близко к кистям и рука складывается вдвое)
            body.ShoulderShift = new Vector3(0f, -0.02f, 0.10f) * (1f - S(8.0f, 10.0f, k)) + off * 0.7f;
            for (int a = 0; a < 2; a++)
            {
                Vector3 p;
                Quaternion r;
                float o;
                SampleHand(a == 0 ? KeysL : KeysR, k, a == 0 ? -1f : 1f, out p, out r, out o);
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
            Shader.SetGlobalVector("_PG_FogColor", new Vector4(0.040f * b, 0.054f * b, 0.088f * b, 0f));   // поездка красила туман в чёрное
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

            DrawCaps(boardCaps, w, h, bar);
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
