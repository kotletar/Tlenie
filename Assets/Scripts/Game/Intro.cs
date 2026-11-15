using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Предыстория (по кнопке «Играть», пропуск — Пробел/Enter/Esc/клик): обычный вечер на кухне, кофе; внезапно гаснет
    /// половина города, наступает Тьма; герой тянется к свече, обжигается — и начинает светиться. Свет теперь внутри него,
    /// и тратить его надо осторожно. Потом чёрный экран с названием и начало игры на улице.
    ///
    /// Всё считается функцией времени (Evaluate): камера, руки, кружка, лампа, свеча, свет в руках, волна темноты над городом.
    /// Игровой герой остаётся тем же (PlayerController/HeroBody): его телепортируют в кухню (она стоит далеко от города),
    /// камеру ведёт сценарий, руки — через HeroBody.Override*. В конце всё возвращается, и GameManager начинает игру.
    /// </summary>
    public class IntroDirector : MonoBehaviour
    {
        // ------------------------------------------------------------------ время сценария (секунды)

        // Время сценария двойное. «Кухонное» время (TBlackout, TLampDies, TBurn, ключи камеры и рук) идёт только на кухне. Реальное время
        // (субтитры, затемнение, название) больше на AerialLen: пока кухня замирает на TAerial, показывают город сверху.
        public const float TBlackout = 10.0f;     // огни дальних домов начинают гаснуть
        public const float TLampDies = 12.0f;     // гаснет лампа на кухне
        public const float TAerial = 12.5f;       // здесь кухня замирает и начинается вид на город сверху
        public const float AerialLen = 8.6f;      // сколько он длится
        public const float TBurn = 19.6f;         // ожог (кухонное время)
        const float TFadeIn = 1.4f;
        const float TFadeOut = 38.6f + AerialLen;
        const float TTitle = 39.9f + AerialLen;
        const float TEnd = 43.2f + AerialLen;

        static readonly Vector3 StageOrigin = new Vector3(-400f, 0f, 200f);

        /// <summary>Кухонное время по реальному: во время вида сверху кухня стоит на TAerial.</summary>
        public static float KitchenTime(float real)
        {
            if (real < TAerial) return real;
            return real < TAerial + AerialLen ? TAerial : real - AerialLen;
        }

        /// <summary>Реальное время по кухонному (для автотестов и расписания).</summary>
        public static float RealTime(float kitchenT)
        {
            return kitchenT < TAerial ? kitchenT : kitchenT + AerialLen;
        }

        /// <summary>Затемнение на склейках: кухня гаснет перед видом сверху, вид сверху — из чёрного и в чёрное, кухня проявляется.</summary>
        static float CutFade(float r)
        {
            float a = TAerial;
            float b = TAerial + AerialLen;
            if (r < a) return S(a - 0.45f, a, r);
            if (r < b) return Mathf.Max(1f - S(a, a + 0.6f, r), S(b - 0.6f, b, r));
            return 1f - S(b, b + 0.5f, r);
        }

        class IntroLight : ILightSource
        {
            public Vector3 Pos;
            public float Radius;
            public float Intensity;
            public Color Col = Color.white;
            public Vector3 LightPosition { get { return Pos; } }
            public float LightRadius { get { return Radius; } }
            public float LightIntensity { get { return Intensity; } }
            public Color LightColor { get { return Col; } }
        }

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

        // Субтитры по РЕАЛЬНОМУ времени. Герой не понимает, что с ним: про силу и цену света он узнаёт уже в игре (мысли героя).
        static readonly Cap[] Caps =
        {
            new Cap(0.7f, 4.0f, "Обычный город. Обычный вечер."),
            new Cap(4.3f, 7.2f, "Кухня, горячий кофе, тишина."),
            new Cap(7.4f, 10.2f, "По радио: «…авария на подстанции, возможны перебои со светом…»"),
            new Cap(10.4f, 12.3f, "И вдруг половина города погасла."),
            new Cap(13.0f, 16.8f, "Тьма шла по улицам и глотала огни."),
            new Cap(17.0f, 20.9f, "Квартал за кварталом. Окно за окном."),
            new Cap(21.6f, 24.6f, "У меня на кухне осталась только свеча."),
            new Cap(25.2f, 28.1f, "Я потянулся к ней — и обжёгся."),
            new Cap(29.2f, 32.6f, "Боли не было. А рука… светилась."),
            new Cap(33.0f, 37.4f, "Я моргнул — свет не погас. Что со мной?.."),
            new Cap(37.8f, 42.6f, "Надо выйти на улицу. Узнать, что стало с городом."),
            new Cap(43.0f, 47.0f, "Может, там ещё остались люди.")
        };

        // Камера (градусы): время, поворот вправо, наклон вниз. Между ключами — сплайн.
        static readonly float[] CamT = { 0f, 1.8f, 3.0f, 4.2f, 5.6f, 7.0f, 8.8f, 9.6f, 10.6f, 11.4f, 12.4f, 14.0f, 16.0f, 17.6f, 19.0f, 19.6f, 20.2f, 21.0f, 23.0f, 26.0f, 29.0f, 32.0f, 37.0f, 45f };
        static readonly float[] CamYaw = { 2f, 2f, 1f, 1f, 0f, -7f, -3f, 0f, 0f, 0f, 0f, 3f, 8f, 18f, 22f, 22f, 20f, 14f, 8f, 4f, 0f, 0f, 0f, 0f };
        static readonly float[] CamPit = { 28f, 27f, 20f, 22f, 12f, 3f, 2f, -6f, -26f, -30f, -2f, 0f, 4f, 14f, 17f, 17f, 10f, 12f, 13f, 10f, 6f, 2f, 0f, 0f };

        // Руки: ключ позы (комната): положение ладони, куда смотрят пальцы (F), куда смотрит тыльная сторона (U), раскрытость.
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

        static readonly HKey[] KeysL =
        {
            new HKey(12.0f, new Vector3(Kitchen.MugRest.x - 0.075f, Kitchen.TableTop + 0.07f, Kitchen.MugRest.z), new Vector3(0f, 0.15f, 1f), new Vector3(-1f, 0f, 0f), 0.42f),
            new HKey(13.4f, new Vector3(-0.16f, 0.81f, 0.22f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.6f),
            new HKey(22.0f, new Vector3(-0.16f, 0.81f, 0.22f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.6f),
            new HKey(23.8f, new Vector3(-0.17f, 1.06f, 0.05f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(60.0f, new Vector3(-0.17f, 1.06f, 0.05f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f)
        };

        static readonly HKey[] KeysR =
        {
            new HKey(12.0f, new Vector3(Kitchen.MugRest.x + 0.080f, Kitchen.TableTop + 0.07f, Kitchen.MugRest.z), new Vector3(0f, 0.15f, 1f), new Vector3(1f, 0f, 0f), 0.42f),
            new HKey(13.4f, new Vector3(0.12f, 0.81f, 0.20f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.6f),
            new HKey(16.0f, new Vector3(0.12f, 0.81f, 0.20f), new Vector3(0f, -0.05f, 1f), new Vector3(0f, 1f, 0f), 0.6f),
            new HKey(19.2f, new Vector3(0.32f, 1.04f, 0.25f), new Vector3(0f, -0.25f, 1f), new Vector3(0f, 1f, 0f), 0.9f),
            new HKey(19.6f, new Vector3(0.32f, 1.04f, 0.25f), new Vector3(0f, -0.25f, 1f), new Vector3(0f, 1f, 0f), 0.9f),
            new HKey(20.3f, new Vector3(0.16f, 1.12f, 0.02f), new Vector3(0f, 1f, -0.15f), new Vector3(0f, 0f, 1f), 1.0f),
            new HKey(24.0f, new Vector3(0.15f, 1.08f, 0.06f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f),
            new HKey(60.0f, new Vector3(0.15f, 1.08f, 0.06f), new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f), 0.95f)
        };

        class Mote
        {
            public Transform T;
            public float Start;
            public float Dur;
            public Vector3 Off;
        }

        // ------------------------------------------------------------------ состояние

        Kitchen kitchen;
        StreetBlackout street;
        bool inAerial;
        float lastU;
        float prevFov = 76f;
        PlayerController pc;
        HeroLight hero;
        HeroBody body;
        IntroLight lampLight;
        IntroLight candleLight;
        IntroLight glowLight;
        IntroLight fillLight;     // мягкая подсветка у лица: руки и рукава не должны тонуть в темноте
        Material[] handHaloMat = new Material[2];
        Transform[] handHalo = new Transform[2];
        Material moteMat;
        readonly List<Mote> motes = new List<Mote>();
        AudioSource sfx;
        AudioSource fridgeSrc;
        AudioSource candleSrc;
        Vector3 headBase;
        readonly Vector3[] hp = new Vector3[2];   // где сейчас кисти (комната)

        bool active;
        bool skipping;
        float skipT;
        float t;
        float lastT;
        float postFade;
        float fadeA;
        float flashA;
        float lastTickSec = -1f;
        bool builtOnce;
        bool cityWasLive;

        GUIStyle capStyle;
        GUIStyle hintStyle;
        GUIStyle titleStyle;
        GUIStyle subStyle;
        int lastFs = -1;

        public bool Active { get { return active; } }
        public float Elapsed { get { return t; } }

        /// <summary>Кухня сцены (строится при первом обращении, пока скрыта): её же использует концовка, когда герой просыпается.</summary>
        public Kitchen AcquireKitchen()
        {
            if (kitchen == null)
            {
                kitchen = Kitchen.Build(StageOrigin);
                kitchen.Root.gameObject.SetActive(false);
            }
            return kitchen;
        }

        // ------------------------------------------------------------------ запуск и завершение

        /// <summary>Начать предысторию. false — не удалось (тогда игра стартует сразу).</summary>
        public bool Begin()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Player == null || gm.Hero == null) return false;
            if (Gfx.UnlitShader == null || Gfx.WorldShader == null) return false;
            pc = gm.Player;
            hero = gm.Hero;
            body = pc.Body;
            if (body == null) return false;

            if (kitchen == null) kitchen = Kitchen.Build(StageOrigin);
            kitchen.Root.gameObject.SetActive(true);
            BuildExtras();

            // игрок садится за стол
            hero.GlowScale = 0f;
            pc.TeleportTo(kitchen.World(Kitchen.SeatRoot), 0f);
            pc.ResetHead();
            headBase = pc.Head.localPosition;
            body.HideBody = true;
            body.OverrideW = 1f;
            body.ArmScale = 0.72f;

            // свет кухни
            lampLight = new IntroLight { Pos = kitchen.World(Kitchen.LampPos), Radius = 8.5f, Intensity = 1.0f, Col = new Color(1f, 0.80f, 0.52f, 1f) };
            candleLight = new IntroLight { Pos = kitchen.World(Kitchen.FlamePos), Radius = 3.6f, Intensity = 0.55f, Col = new Color(1f, 0.68f, 0.34f, 1f) };
            glowLight = new IntroLight { Pos = kitchen.World(Kitchen.FlamePos), Radius = 0f, Intensity = 0f, Col = new Color(1f, 0.78f, 0.42f, 1f) };
            fillLight = new IntroLight { Pos = kitchen.World(Kitchen.SeatRoot), Radius = 1.7f, Intensity = 0.7f, Col = new Color(1f, 0.80f, 0.56f, 1f) };
            LightField.Register(lampLight);
            LightField.Register(candleLight);
            LightField.Register(glowLight);
            LightField.Register(fillLight);

            // звук
            if (sfx == null) sfx = ProcAudio.Source(gameObject, "tick", false, 1f, false);
            if (fridgeSrc == null) fridgeSrc = ProcAudio.Source(gameObject, "fridge", true, 0.12f, false);
            else { AudioMix.SetVolume(fridgeSrc, 0.12f); fridgeSrc.Play(); }
            if (candleSrc == null) candleSrc = ProcAudio.Source(gameObject, "crackle", true, 0.10f, false);
            else { AudioMix.SetVolume(candleSrc, 0.10f); candleSrc.Play(); }
            if (pc.DroneSrc != null) AudioMix.SetVolume(pc.DroneSrc, 0f);

            t = 0f;
            lastT = -1f;
            skipping = false;
            skipT = 0f;
            fadeA = 1f;
            flashA = 0f;
            postFade = 0f;
            lastTickSec = -1f;
            cityWasLive = false;
            inAerial = false;
            lastU = -1f;
            if (street != null) street.Root.gameObject.SetActive(false);
            kitchen.SetCity(300f, 0f);   // после прошлого показа окна могли остаться погашенными
            active = true;
            Evaluate(0f);
            return true;
        }

        /// <summary>Остановить предысторию без начала игры (выход в меню).</summary>
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
            postFade = 1.4f;
            fadeA = 1f;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.FinishIntro();
        }

        void End()
        {
            LeaveAerial();
            if (lampLight != null) LightField.Unregister(lampLight);
            if (candleLight != null) LightField.Unregister(candleLight);
            if (glowLight != null) LightField.Unregister(glowLight);
            if (fillLight != null) LightField.Unregister(fillLight);
            if (kitchen != null) kitchen.Root.gameObject.SetActive(false);
            for (int a = 0; a < 2; a++)
            {
                if (handHalo[a] != null) handHalo[a].gameObject.SetActive(false);
            }
            for (int i = 0; i < motes.Count; i++) motes[i].T.gameObject.SetActive(false);
            if (fridgeSrc != null) fridgeSrc.Stop();
            if (candleSrc != null) candleSrc.Stop();
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
            if (pc != null && pc.DroneSrc != null) AudioMix.SetVolume(pc.DroneSrc, 0.35f);
        }

        void OnDestroy()
        {
            Lamp.IntroMask = null;
            if (kitchen != null) kitchen.Destroy();
            if (street != null) street.Destroy();
        }

        // ------------------------------------------------------------------ вид на улицу: Тьма накрывает проспект

        void EnterAerial()
        {
            inAerial = true;
            lastU = -1f;
            if (street == null) street = StreetBlackout.Build();
            street.Root.gameObject.SetActive(true);
            street.SetWave(9999f, 0f, 0f);
            Lamp.IntroMask = street.LampMask;     // огни улицы ведёт сценарий: горят ровно, пока их не накроет Тьма
            Survivor.IntroHideAll(true);           // на вечерней улице никто не стоит «потухшим»
            LevelBuilder.SetBeaconVisible(false);  // столба света над вокзалом в предыстории нет (в игре он ориентир)
            if (pc != null && pc.Cam != null)
            {
                prevFov = pc.Cam.fieldOfView;
                pc.Cam.fieldOfView = 74f;
            }
            if (LevelBuilder.Culler != null) LevelBuilder.Culler.Refresh();   // камера прыгнула в город: дома рядом должны быть включены сразу
        }

        void LeaveAerial()
        {
            if (!inAerial) return;
            inAerial = false;
            Lamp.IntroMask = null;
            Survivor.IntroHideAll(false);
            LevelBuilder.SetBeaconVisible(true);
            if (street != null) street.Root.gameObject.SetActive(false);
            if (pc != null && pc.Cam != null) pc.Cam.fieldOfView = prevFov;
        }

        /// <summary>
        /// Камера плывёт над проспектом на север, низко, вдоль улицы: по бокам дома, впереди улица тонет в тумане и упирается в
        /// фасад (краёв города не видно). Фронт Тьмы идёт ей навстречу и гасит лампы и окна.
        /// </summary>
        void EvaluateAerial(float u, float real)
        {
            if (!inAerial) EnterAerial();
            Gfx.ApplyGlobals();                                  // обычная ночь города: небо, луна, яркость
            Shader.SetGlobalFloat("_PG_Fog", 0.019f);            // чуть прозрачнее, чем в игре: улица читается, но даль тает

            float k = Mathf.Clamp01(u / AerialLen);
            float e = k * k * (3f - 2f * k);
            Vector3 camPos = Vector3.Lerp(new Vector3(35.2f, 10.5f, 57f), new Vector3(36.6f, 8.4f, 82f), Mathf.Lerp(k, e, 0.5f));
            float pit = Mathf.Lerp(9f, 6f, k) + 0.4f * Mathf.Sin(u * 0.9f);
            float yaw = Mathf.Lerp(-2.5f, 2.0f, k) + 0.5f * Mathf.Sin(u * 0.6f + 1f);
            float roll = 0.7f * Mathf.Sin(u * 0.5f);
            pc.Head.position = camPos;
            pc.Head.rotation = Quaternion.Euler(pit, yaw, roll);

            float front = Mathf.Lerp(188f, 40f, S(0.9f, 6.3f, u));
            float haze = 0.82f * S(0.9f, 2.0f, u) * (1f - S(6.3f, 7.4f, u));
            street.SetWave(front, u, haze);

            if (lastU < 0.35f && u >= 0.35f) ProcAudio.OneShot(sfx, "rumble", 0.6f, 0.9f);
            if (lastU < 3.4f && u >= 3.4f) ProcAudio.OneShot(sfx, "swell", 0.45f, 0.8f);
            if (lastU < 6.0f && u >= 6.0f) ProcAudio.OneShot(sfx, "powerdown", 0.7f, 0.9f);
            lastU = u;
            if (!skipping) fadeA = CutFade(real);
        }

        /// <summary>Свечение рук и огоньки, летящие от свечи в ладонь: создаются один раз.</summary>
        void BuildExtras()
        {
            if (builtOnce)
            {
                for (int a = 0; a < 2; a++) handHalo[a].gameObject.SetActive(true);
                return;
            }
            builtOnce = true;
            for (int a = 0; a < 2; a++)
            {
                handHaloMat[a] = Gfx.Unlit(new Color(1f, 0.78f, 0.42f, 0f), Gfx.TexHalo, true, false, 0f);
                GameObject h = Gfx.Sprite(body.Hands[a].Root, new Vector3(0f, -0.03f, 0.07f), new Vector2(0.34f, 0.34f), handHaloMat[a], false);
                h.name = "IntroHandGlow";
                handHalo[a] = h.transform;
            }
            moteMat = Gfx.Unlit(new Color(1f, 0.74f, 0.36f, 0.9f), Gfx.TexHalo, true, false, 0f);
            System.Random rng = new System.Random(5);
            for (int i = 0; i < 24; i++)
            {
                Mote m = new Mote();
                GameObject g = Gfx.Sprite(transform, Vector3.zero, new Vector2(0.05f, 0.05f), moteMat, false);
                g.name = "IntroMote";
                g.SetActive(false);
                m.T = g.transform;
                m.Start = TBurn + 0.08f + i * 0.055f;
                m.Dur = 0.55f + (float)rng.NextDouble() * 0.35f;
                m.Off = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.3f, (float)rng.NextDouble() - 0.5f) * 0.05f;
                motes.Add(m);
            }
        }

        // ------------------------------------------------------------------ кадр

        void Update()
        {
            float dt = Time.deltaTime;
            if (postFade > 0f)
            {
                postFade -= dt;
                fadeA = Mathf.Clamp01(postFade / 1.4f);
            }
            if (!active) return;

            t += dt;
            if (t > 0.8f && SkipPressed()) Skip();
            if (skipping)
            {
                skipT += dt;
                Evaluate(t);
                fadeA = Mathf.Max(fadeA, Mathf.Clamp01(skipT / 0.45f));
                if (skipT >= 0.45f) Finish();
                return;
            }

            Evaluate(t);
            if (t >= TEnd) Finish();
        }

        /// <summary>Пропустить предысторию: короткое затемнение и игра.</summary>
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

        /// <summary>Для автотестов: перейти к моменту сценария (звуковые события пропускаются).</summary>
        public void DebugSeek(float newT)
        {
            t = newT;
            float kt = KitchenTime(newT);
            lastT = kt - 0.001f;
            lastTickSec = Mathf.Floor(kt) - 1f;
            lastU = Mathf.Max(-1f, newT - TAerial - 0.001f);
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

        /// <summary>Светит ли ещё лампа на кухне: 1 — ровно, меньше — мерцает, 0 — погасла.</summary>
        static float LampLevel(float x)
        {
            if (x >= TLampDies) return 0f;
            if (x < 8.9f) return 1f;
            float n = Mathf.PerlinNoise(x * 17f, 4.2f);
            if (x < TBlackout) return n > 0.60f ? 0.25f : 1f;
            return Mathf.Clamp01(0.30f + 0.6f * n) * (n > 0.7f ? 0.2f : 1f);
        }

        /// <summary>Положение фронта темноты над городом (по X, комнатные метры): окна правее — погашены.</summary>
        static float WaveX(float x)
        {
            if (x < TBlackout) return 300f;
            if (x < 12.4f) return Mathf.Lerp(75f, -8f, S(TBlackout, 12.4f, x));
            return Mathf.Lerp(-8f, -95f, S(12.4f, 31f, x));
        }

        /// <summary>Кадр сценария по реальному времени: кухня или (пока кухня замерла) город сверху.</summary>
        void Evaluate(float real)
        {
            if (kitchen == null || pc == null) return;
            if (real >= TAerial && real < TAerial + AerialLen)
            {
                EvaluateAerial(real - TAerial, real);
                return;
            }
            if (inAerial) LeaveAerial();
            EvaluateKitchen(KitchenTime(real), real);
        }

        /// <summary>Кухня: x — кухонное время (после вида сверху идёт дальше с TAerial), real — реальное (для затемнения).</summary>
        void EvaluateKitchen(float x, float real)
        {
            Vector3 org = kitchen.Origin;

            // ---- небо и воздух: вечер -> ночь
            ApplySky(x);

            // ---- камера
            float yaw = Spline(CamT, CamYaw, x) + 0.35f * Mathf.Sin(x * 0.7f);
            float pit = Spline(CamT, CamPit, x) + 0.30f * Mathf.Sin(x * 1.1f);
            if (x >= TBurn)
            {
                float jolt = Mathf.Exp(-(x - TBurn) * 6f);
                pit -= 9f * jolt;
                yaw -= 3f * jolt;
            }
            pc.Head.localPosition = headBase;
            pc.Head.localRotation = Quaternion.Euler(pit, yaw, 0f);

            // ---- кружка: подносим ко рту и обратно
            float lift = S(2.0f, 3.3f, x) * (1f - S(4.2f, 5.6f, x));
            Vector3 mugBottom = Vector3.Lerp(Kitchen.MugRest, new Vector3(0f, 0.92f, 0.02f), lift);
            kitchen.Mug.localPosition = mugBottom;
            float steamK = 1f - S(12.0f, 14.0f, x);
            for (int i = 0; i < kitchen.Steam.Length; i++)
            {
                float ph = Mathf.Repeat(x * 0.45f + i / 3f, 1f);
                kitchen.Steam[i].localPosition = mugBottom + new Vector3(Mathf.Sin(ph * 6f + i * 2f) * 0.012f, Kitchen.MugHeight + 0.02f + ph * 0.18f, 0f);
                float sz = (0.05f + 0.05f * ph) * Mathf.Sin(Mathf.PI * ph) * steamK;
                kitchen.Steam[i].localScale = new Vector3(sz, sz, 1f);
            }

            // ---- руки
            for (int a = 0; a < 2; a++)
            {
                Vector3 p;
                Quaternion r;
                float o;
                if (x < 12.0f)
                {
                    // держат кружку: ладони по бокам
                    Vector3 mugC = mugBottom + new Vector3(0f, 0.07f, 0f);
                    p = mugC + new Vector3(a == 0 ? -0.075f : 0.080f, 0f, 0f);
                    r = Quaternion.LookRotation(new Vector3(0f, 0.15f, 1f), new Vector3(a == 0 ? -1f : 1f, 0f, 0f));
                    o = 0.42f;
                }
                else
                {
                    SampleHand(a == 0 ? KeysL : KeysR, x, out p, out r, out o);
                }
                hp[a] = p;
                body.OverridePos[a] = p - Kitchen.SeatRoot;
                body.OverrideRot[a] = r;
                body.OverrideOpen[a] = o;
            }
            body.OverrideW = 1f - S(30.5f, 36.5f, x);
            // в тёмной кухне кожа светлее, иначе руки чёрные; когда рука засветилась сама, возвращаем обычный тон
            body.SetSkinTone(Mathf.Lerp(1.7f, 1.0f, S(TBurn + 1.0f, TBurn + 4.5f, x)));

            // ---- лампа
            float lamp = LampLevel(x);
            lampLight.Intensity = 1.0f * lamp;
            kitchen.BulbMat.SetColor("_Color", Color.Lerp(new Color(0.10f, 0.09f, 0.08f, 1f), new Color(1f, 0.92f, 0.72f, 1f), lamp));
            kitchen.LampHaloMat.SetColor("_Color", new Color(1f, 0.78f, 0.42f, 0.30f * lamp));

            // ---- свеча
            float flick = 0.86f + 0.14f * Mathf.PerlinNoise(x * 9f, 1.3f) + 0.05f * Mathf.Sin(x * 23f);
            float candleK = x < TBurn ? 1f : 1f - S(TBurn + 0.3f, TBurn + 1.7f, x);
            float flare = x >= TBurn && x < TBurn + 0.35f ? Mathf.Sin((x - TBurn) / 0.35f * Mathf.PI) : 0f;
            float lean = 0.004f * Mathf.Sin(x * 3.1f) + (x > TLampDies ? 0.007f * (Mathf.PerlinNoise(x * 2.3f, 9f) - 0.4f) : 0f);
            Vector3 flamePos = Kitchen.FlamePos + new Vector3(lean, 0f, 0f);
            float fs = candleK * flick * (1f + 1.1f * flare);
            kitchen.Flame.localPosition = flamePos;
            kitchen.Flame.localScale = new Vector3(0.05f * fs, 0.11f * fs, 1f);
            kitchen.FlameCore.localPosition = flamePos + new Vector3(0f, -0.012f * fs, 0f);
            kitchen.FlameCore.localScale = new Vector3(0.022f * fs, 0.05f * fs, 1f);
            kitchen.FlameHalo.localPosition = flamePos;
            float hs = 0.55f * (0.9f + 0.2f * flick) * candleK * (1f + 0.8f * flare);
            kitchen.FlameHalo.localScale = new Vector3(hs, hs, 1f);
            float candleRise = 0.85f + 0.35f * S(11.8f, 13f, x);
            candleLight.Pos = org + flamePos;
            candleLight.Intensity = candleRise * flick * candleK * (1f + 0.8f * flare);
            candleLight.Radius = 3.6f;

            // дымок после погасшей свечи
            float smokeT0 = TBurn + 1.2f;
            kitchen.SmokeMat.SetColor("_Color", new Color(0.55f, 0.55f, 0.6f, 0.20f * (1f - S(smokeT0 + 2.5f, smokeT0 + 5.5f, x))));
            for (int i = 0; i < kitchen.Smoke.Length; i++)
            {
                float p = x - smokeT0 - i * 0.7f;
                bool on = p > 0f && p < 6f;
                kitchen.Smoke[i].gameObject.SetActive(on);
                if (!on) continue;
                kitchen.Smoke[i].localPosition = Kitchen.FlamePos + new Vector3(0.02f * Mathf.Sin(p * 2f + i), 0.03f + 0.10f * p, 0.01f * Mathf.Cos(p * 1.7f));
                float ss = 0.05f + 0.04f * p;
                kitchen.Smoke[i].localScale = new Vector3(ss, ss, 1f);
            }

            // ---- ожог и свет в руках
            float burn = x >= TBurn ? 1f : 0f;
            flashA = x >= TBurn ? 0.85f * Mathf.Exp(-(x - TBurn) * 3.2f) : 0f;
            if (x >= TBurn) hero.DamageFlash = Mathf.Max(hero.DamageFlash, 0.6f * Mathf.Exp(-(x - TBurn) * 3.5f));
            float glowR = S(TBurn + 0.1f, TBurn + 2.6f, x);
            float glowL = S(23.0f, 25.5f, x);
            hero.GlowScale = 0.5f * S(TBurn + 1.6f, TBurn + 5.5f, x);   // в тесной кухне аура вполсилы; на улице она станет полной
            Vector3 rightWorld = org + hp[1];
            Vector3 leftWorld = org + hp[0];
            // рука из-за отдачи/сглаживания чуть отстаёт от цели: берём реальное положение кисти, если она уже есть
            if (body.Hands[1].Root != null) rightWorld = body.Hands[1].Root.position;
            if (body.Hands[0].Root != null) leftWorld = body.Hands[0].Root.position;
            Vector3 gp = Vector3.Lerp(rightWorld, (rightWorld + leftWorld) * 0.5f, glowL);
            glowLight.Pos = gp + new Vector3(0f, 0.05f, 0f);
            float gk = Mathf.Max(glowR, glowL);
            glowLight.Radius = 3.6f * gk;
            glowLight.Intensity = 1.0f * gk * burn;
            // подсветка рук: от лица камеры чуть вперёд; когда рука сама засветилась, она больше не нужна
            Transform camT = pc.Cam.transform;
            fillLight.Pos = camT.position + camT.forward * 0.22f + Vector3.down * 0.04f;
            fillLight.Intensity = 0.7f * (1f - 0.65f * S(TBurn, TBurn + 3f, x));
            handHaloMat[1].SetColor("_Color", new Color(1f, 0.78f, 0.42f, 0.55f * glowR * (0.9f + 0.1f * flick)));
            handHaloMat[0].SetColor("_Color", new Color(1f, 0.78f, 0.42f, 0.55f * glowL * (0.9f + 0.1f * flick)));

            // огоньки от свечи к ладони
            for (int i = 0; i < motes.Count; i++)
            {
                Mote m = motes[i];
                float u = (x - m.Start) / m.Dur;
                bool on = u > 0f && u < 1f;
                if (m.T.gameObject.activeSelf != on) m.T.gameObject.SetActive(on);
                if (!on) continue;
                Vector3 p0 = org + Kitchen.FlamePos + m.Off;
                Vector3 p2 = rightWorld;
                Vector3 p1 = (p0 + p2) * 0.5f + new Vector3(0f, 0.16f, 0.03f);
                float uu = u * u * (3f - 2f * u);
                Vector3 pos = (1f - uu) * (1f - uu) * p0 + 2f * (1f - uu) * uu * p1 + uu * uu * p2;
                m.T.position = pos;
                float sz = 0.06f * (1f - 0.55f * u) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 0.9f + 0.05f));
                m.T.localScale = new Vector3(sz, sz, 1f);
            }

            // ---- город за окном и тёмная дымка на фронте
            float wave = WaveX(x);
            bool cityLive = x >= TBlackout - 0.5f;
            if (cityLive || cityWasLive) kitchen.SetCity(wave, x);   // до этого момента окна горят, как построены
            cityWasLive = cityLive;
            float hazeA = 0.22f * S(TBlackout + 0.5f, TBlackout + 2.5f, x) * (1f - S(32f, 38f, x) * 0.4f);
            kitchen.SetHaze(wave, hazeA);

            // ---- часы
            kitchen.SetClock(7f, 42f + x / 60f, x);

            // ---- звуки (по пересечению моментов времени)
            if (x < TBurn)
            {
                float sec = Mathf.Floor(x);
                if (sec != lastTickSec && x >= 0.5f)
                {
                    lastTickSec = sec;
                    ProcAudio.OneShot(sfx, "tick", 0.22f, 1f);
                }
            }
            if (Crossed(8.4f, x)) ProcAudio.OneShot(sfx, "rumble", 0.55f, 1f);
            if (Crossed(TLampDies - 0.02f, x))
            {
                ProcAudio.OneShot(sfx, "powerdown", 0.8f, 1f);
                if (fridgeSrc != null) fridgeSrc.Stop();
            }
            if (Crossed(TBurn, x))
            {
                ProcAudio.OneShot(sfx, "sizzle", 0.9f, 1f);
                if (candleSrc != null) candleSrc.Stop();
            }
            if (Crossed(TBurn + 0.35f, x)) ProcAudio.OneShot(sfx, "swell", 0.7f, 1f);
            if (Crossed(TBurn + 0.9f, x)) ProcAudio.OneShot(sfx, "inhale", 0.45f, 0.85f);
            if (Crossed(TBurn + 2.2f, x)) ProcAudio.OneShot(sfx, "heart", 0.8f, 1f);
            if (Crossed(TBurn + 3.2f, x)) ProcAudio.OneShot(sfx, "heart", 0.7f, 1f);
            if (Crossed(TBurn + 4.4f, x)) ProcAudio.OneShot(sfx, "heart", 0.55f, 0.95f);
            if (Crossed(TBurn + 5.6f, x)) ProcAudio.OneShot(sfx, "heart", 0.45f, 0.9f);
            if (Crossed(TTitle - AerialLen, x)) ProcAudio.OneShot(sfx, "orb", 0.5f, 0.7f);   // x — кухонное время
            if (pc.DroneSrc != null) AudioMix.SetVolume(pc.DroneSrc, 0.35f * S(TBlackout, 17f, x));
            if (candleSrc != null && candleSrc.isPlaying) AudioMix.SetVolume(candleSrc, 0.10f * flick);

            // ---- затемнение: из чёрного, перед названием в чёрное
            if (!skipping) fadeA = Mathf.Max(Mathf.Max(1f - S(0f, TFadeIn, real), S(TFadeOut, TFadeOut + 1.1f, real)), CutFade(real));
            lastT = x;
        }

        bool Crossed(float at, float x)
        {
            return lastT < at && x >= at;
        }

        void ApplySky(float x)
        {
            float dusk = 1f - S(TBlackout, TBlackout + 4.5f, x);   // 1 — вечер, 0 — ночь
            Vector4 horizon = Vector4.Lerp(new Vector4(0.050f, 0.064f, 0.104f, 0f), new Vector4(0.24f, 0.115f, 0.12f, 0f), dusk);
            Vector4 zenith = Vector4.Lerp(new Vector4(0.006f, 0.010f, 0.026f, 0f), new Vector4(0.022f, 0.032f, 0.085f, 0f), dusk);
            Vector4 glow = Vector4.Lerp(new Vector4(0.95f, 0.62f, 0.32f, 0f) * 0.35f, new Vector4(0.95f, 0.46f, 0.22f, 0f), dusk);
            Shader.SetGlobalVector("_PG_SkyHorizon", horizon);
            Shader.SetGlobalVector("_PG_SkyZenith", zenith);
            Shader.SetGlobalVector("_PG_SkyGlow", glow);
            Shader.SetGlobalFloat("_PG_Fog", 0.009f);   // в комнате воздух чистый; огни города за окном рисует свой туман
            Shader.SetGlobalFloat("_PG_Exposure", 1.5f * Gfx.Brightness);   // в тесной комнате свет не должен выжигать стены в белое
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

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            bool showing = active || postFade > 0f;
            if (!showing) return;
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

            if (active)
            {
                // чёрные полосы, как в кино
                float bar = h * 0.095f;
                GUI.color = new Color(0f, 0f, 0f, 1f);
                GUI.DrawTexture(new Rect(0f, 0f, w, bar), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, h - bar, w, bar), Texture2D.whiteTexture);

                // субтитры
                for (int i = 0; i < Caps.Length; i++)
                {
                    Cap c = Caps[i];
                    if (t < c.A || t > c.B) continue;
                    float a = Mathf.Clamp01(Mathf.Min((t - c.A) / 0.5f, (c.B - t) / 0.5f));
                    GUI.color = new Color(0f, 0f, 0f, 0.38f * a);
                    GUI.DrawTexture(new Rect(0f, h - bar - h * 0.13f, w, h * 0.13f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    Shadowed(new Rect(w * 0.12f, h - bar - h * 0.115f, w * 0.76f, h * 0.10f), c.Text, capStyle, new Color(1f, 0.95f, 0.82f, a));
                }

                // название на чёрном экране
                if (t >= TTitle)
                {
                    float a = Mathf.Clamp01(Mathf.Min((t - TTitle) / 0.8f, (TEnd - t) / 0.8f));
                    Shadowed(new Rect(0f, h * 0.36f, w, h * 0.16f), "ТЛЕЮЩАЯ ТЬМА", titleStyle, new Color(1f, 0.82f, 0.48f, a));
                    Shadowed(new Rect(0f, h * 0.53f, w, h * 0.06f), "не дай погаснуть", subStyle, new Color(1f, 0.92f, 0.75f, a * 0.8f));
                }

                // подсказка про пропуск
                if (t > 1.5f && t < TTitle)
                {
                    Shadowed(new Rect(w * 0.5f, h - bar - h * 0.045f, w * 0.48f, h * 0.04f), "Пробел — пропустить", hintStyle, new Color(1f, 0.92f, 0.75f, 0.5f));
                }
            }

            // белая вспышка ожога — поверх всего
            if (active && flashA > 0.01f)
            {
                GUI.color = new Color(1f, 0.97f, 0.88f, flashA);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
