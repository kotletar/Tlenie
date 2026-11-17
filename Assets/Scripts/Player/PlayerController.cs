using UnityEngine;

namespace PokaGoryu
{
    public enum FocusKind { None, Lamp, Sleeper, Hurt, Orb, Bus, Generator, Pizza, Optic }

    /// <summary>
    /// Герой от первого лица: движение, обзор, вдох/выдох, прикрыть огонь, Проводник (G).
    /// Бой (ЛКМ/ПКМ) — в PalmShot (код из ссылки). Взаимодействие идёт по ФОКУСУ: цель выбирается по тому, куда герой смотрит
    /// и как близко она стоит; на ней появляется кольцо, и подсказки E/F относятся только к ней.
    /// Поэтому рядом могут быть лампа, человек и автобус — и ничего не путается.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance;

        public Camera Cam;
        public Transform Head;
        public CharacterController Cc;
        public HeroLight Hero;
        public HeroBody Body;
        public PalmShot Combat;

        // Состояние для других систем (тело, интерфейс, враги)
        public bool Inhaling;
        public bool Exhaling;
        float walkedMeters;           // сколько герой прошёл (для первой мысли про тёмный город)
        public float LookDownT;       // 0..1: насколько герой смотрит на себя
        public float MoveAmount;      // 0..1
        public float WalkPhase;
        public float WakeProgress;    // 0..1 (для полоски у прицела)
        public string PromptE;
        public string PromptF;
        public FocusKind FocusType;

        public float MouseSens = 2.0f;

        float yaw;
        float pitch;
        bool adoptYaw;
        float vy;
        float bowDeg;
        float tabT;
        const float StepVolume = 0.32f;   // громкость шага до общей громкости «Эффекты»
        int lastFoot;                     // номер последнего шага: звук шага привязан к качанию головы (WalkPhase)
        float heartTimer;
        Vector3 headBase;
        Vector3 pushVel;

        AudioSource sfx;
        AudioSource stepSrc;
        AudioSource heartSrc;
        AudioSource loopInhale;
        AudioSource loopExhale;
        AudioSource loopCrackle;
        AudioLowPassFilter lowpass;

        // фокус взаимодействия
        Object focusObj;
        Vector3 focusPoint;
        Lamp fLamp;
        Survivor fSurv;
        LightOrb fOrb;
        Generator fGen;
        Pizza fPizza;
        LightOptic fOptic;
        GameObject focusRing;
        Material focusMat;
        FocusKind candKind;
        Object candObj;
        Vector3 candPoint;
        float candScore;
        FocusKind prevKind;
        Vector3 prevPoint;
        float prevScore;
        bool prevFound;

        public Vector3 EyePos { get { return Cam.transform.position; } }

        /// <summary>Новый забег: счёт пройденного (для первой мысли про тёмный город) начинается заново.</summary>
        public void ResetWalked()
        {
            walkedMeters = 0f;
        }

        /// <summary>Общий источник одноразовых звуков героя (2D).</summary>
        public AudioSource Sfx { get { return sfx; } }

        /// <summary>Низкий гул города (петля): в предыстории на кухне его нет, он приходит вместе с Тьмой.</summary>
        public AudioSource DroneSrc;

        /// <summary>Вернуть голову в обычное положение (после предыстории, где камеру водил сценарий).</summary>
        public void ResetHead()
        {
            Head.localPosition = headBase;
            Head.localRotation = Quaternion.identity;
            LookDownT = 0f;
            Inhaling = false;
            Exhaling = false;
        }

        // ------------------------------------------------------------------ создание

        public void Init()
        {
            Instance = this;

            Cc = gameObject.AddComponent<CharacterController>();
            Cc.height = 1.8f;
            Cc.radius = 0.35f;
            Cc.center = new Vector3(0f, 0.9f, 0f);
            Cc.stepOffset = 0.32f;
            Cc.slopeLimit = 50f;
            Cc.skinWidth = 0.04f;
            // Люди не загораживают героя: в тесных местах (хранилище света) они обступали его и не давали выйти. Слой людей исключён из его
            // столкновений, поэтому герой проходит сквозь них; они его по-прежнему обходят и друг друга не пропускают.
            Cc.excludeLayers = 1 << Survivor.Layer;

            GameObject headGo = new GameObject("Head");
            Head = headGo.transform;
            Head.SetParent(transform, false);
            headBase = new Vector3(0f, 1.62f, 0.16f);
            Head.localPosition = headBase;

            GameObject camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(Head, false);
            Cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            Cam.fieldOfView = 76f;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 600f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.03f, 0.04f, 0.08f, 1f);
            Cam.cullingMask = ~((1 << 30) | (1 << 31));   // слой 30 — сцена полоски здоровья, 31 — кадр камкордера
            Gfx.CamTransform = camGo.transform;

            Hero = gameObject.AddComponent<HeroLight>();
            Hero.Init(Head);
            Body = gameObject.AddComponent<HeroBody>();
            Body.Init(this);
            Combat = gameObject.AddComponent<PalmShot>();
            Combat.Init(this, Body);
            Body.Combat = Combat;

            // Звук: источники на отдельном объекте (без фильтра), фильтр "глушит мир" на объекте со слушателем.
            GameObject audioGo = new GameObject("PlayerAudio");
            audioGo.transform.SetParent(Head, false);
            DroneSrc = ProcAudio.Source(audioGo, "drone", true, 0.35f, false);
            sfx = ProcAudio.Source(audioGo, "flash", false, 1f, false);
            stepSrc = ProcAudio.Source(audioGo, "step", false, 1f, false);
            heartSrc = ProcAudio.Source(audioGo, "heart", false, 1f, false);
            loopInhale = ProcAudio.Source(audioGo, "inhale", true, 0f, false);
            loopExhale = ProcAudio.Source(audioGo, "exhale", true, 0f, false);
            loopCrackle = ProcAudio.Source(audioGo, "crackle", true, 0.1f, false);
            lowpass = camGo.AddComponent<AudioLowPassFilter>();

            // кольцо фокуса
            focusMat = Gfx.Unlit(new Color(1f, 0.92f, 0.6f, 0.9f), Gfx.TexRing, true, false, 0.05f);
            focusRing = Gfx.Sprite(null, Vector3.zero, new Vector2(0.6f, 0.6f), focusMat, false);
            focusRing.name = "FocusRing";
            focusRing.SetActive(false);

            RegisterLights();
        }

        public void RegisterLights()
        {
            if (Hero != null) Hero.Register();
            if (Combat != null) Combat.RegisterLights();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void TeleportTo(Vector3 pos, float yawDeg)
        {
            Cc.enabled = false;
            transform.position = pos;
            Cc.enabled = true;
            yaw = yawDeg;
            pitch = 0f;
            adoptYaw = false;
            vy = 0f;
            pushVel = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Combat != null) Combat.CancelAll();
        }

        public void Push(Vector3 v)
        {
            pushVel += v;
        }

        float rumble;

        /// <summary>Тряска камеры от тяжёлого события (босс подлетает, копьё бьёт рядом, броня захлопнулась): сила 0..2, спадает за ~1 с.</summary>
        public void Rumble(float amount)
        {
            rumble = Mathf.Max(rumble, Mathf.Clamp(amount, 0f, 2f));
        }

        // ------------------------------------------------------------------ кадр

        void Update()
        {
            GameManager gm = GameManager.Instance;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);   // защита от скачков кадра (долгая загрузка, пауза)
            bool playing = gm != null && gm.State == GameState.Playing && !gm.Paused;
            if (gm != null && gm.State == GameState.Title) MenuSway();
            if (!playing)
            {
                SilenceLoops(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
                return;
            }

            UpdateLook(dt);
            UpdateMove(dt);
            UpdateFocus();
            UpdateActions(dt);
            UpdateAudioAndCamera(dt);
        }

        void SilenceLoops(float dt)
        {
            if (loopInhale == null) return;
            AudioMix.SetVolume(loopInhale, Mathf.MoveTowards(AudioMix.Volume(loopInhale), 0f, dt * 6f));
            AudioMix.SetVolume(loopExhale, Mathf.MoveTowards(AudioMix.Volume(loopExhale), 0f, dt * 6f));
            Inhaling = false;
            Exhaling = false;
            PromptE = null;
            PromptF = null;
            WakeProgress = 0f;
            FocusType = FocusKind.None;
            focusObj = null;
            if (focusRing != null && focusRing.activeSelf) focusRing.SetActive(false);
        }

        /// <summary>Главное меню: камера медленно покачивается и смотрит на улицу (мир живёт за меню).</summary>
        void MenuSway()
        {
            float t = Time.unscaledTime;
            transform.rotation = Quaternion.Euler(0f, yaw + Mathf.Sin(t * 0.11f) * 16f, 0f);
            Head.localRotation = Quaternion.Euler(-2f + Mathf.Sin(t * 0.15f) * 1.5f, 0f, 0f);
            adoptYaw = true;
        }

        void UpdateLook(float dt)
        {
            // первый кадр после меню: продолжаем с того направления, куда камера смотрела в меню (без рывка)
            if (adoptYaw)
            {
                yaw = transform.eulerAngles.y;
                adoptYaw = false;
            }
            Vector2 d = GameInput.LookDelta();
            yaw += d.x * MouseSens;
            pitch -= d.y * MouseSens * (GameSettings.InvertY ? -1f : 1f);
            pitch = Mathf.Clamp(pitch, -80f, 89f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // При вдохе герой сам склоняет голову (видно, как наполняется грудь).
            float bowTarget = Inhaling ? 24f : 0f;
            bowDeg = Mathf.Lerp(bowDeg, bowTarget, 1f - Mathf.Exp(-6f * dt));
            // Tab (удерживать) — быстро посмотреть на себя, не уводя мышь.
            float tabTarget = GameInput.Held(GKey.Tab) ? 1f : 0f;
            tabT = Mathf.Lerp(tabT, tabTarget, 1f - Mathf.Exp(-8f * dt));

            float userPitch = Mathf.Clamp(pitch + bowDeg, -80f, 89f);
            float finalPitch = Mathf.Lerp(userPitch, 78f, tabT);
            Head.localRotation = Quaternion.Euler(finalPitch, 0f, 0f);
            LookDownT = Mathf.Clamp01(Mathf.InverseLerp(28f, 68f, finalPitch));
        }

        void UpdateMove(float dt)
        {
            float ix = (GameInput.Held(GKey.D) ? 1f : 0f) - (GameInput.Held(GKey.A) ? 1f : 0f);
            float iz = (GameInput.Held(GKey.W) ? 1f : 0f) - (GameInput.Held(GKey.S) ? 1f : 0f);
            Vector3 input = new Vector3(ix, 0f, iz);
            if (input.sqrMagnitude > 1f) input.Normalize();

            bool busy = Combat != null && Combat.Busy;
            bool sprint = GameInput.Held(GKey.Shift) && iz > 0.1f && !Inhaling && !Exhaling && !busy;
            float speed = sprint ? 6.4f : 4.2f;
            if (Inhaling || Exhaling) speed *= 0.4f;
            if (busy) speed *= 0.7f;
            if (Hero.InLastBreath) speed *= 0.25f;
            Vector3 move = transform.TransformDirection(input) * speed;

            if (Cc.isGrounded && vy < 0f) vy = -2f;
            vy -= 22f * dt;
            move.y = vy;
            move += pushVel;
            pushVel = Vector3.MoveTowards(pushVel, Vector3.zero, dt * 14f);
            Cc.Move(move * dt);

            // Страховка: если героя вынесло за карту, вернуть к очагу.
            if (transform.position.y < -8f)
            {
                GameManager gmSafe = GameManager.Instance;
                Vector3 safe = (gmSafe != null && gmSafe.Dir != null) ? gmSafe.Dir.CheckpointPos : LevelBuilder.StartPos;
                TeleportTo(safe, yaw);
                return;
            }

            Vector3 v = Cc.velocity;
            float horiz = new Vector3(v.x, 0f, v.z).magnitude;
            MoveAmount = Mathf.Clamp01(horiz / 6f);
            WalkPhase += horiz * dt * 1.9f;

            // первые шаги по тёмному городу: герой осматривается (мысль один раз за забег)
            walkedMeters += horiz * dt;
            if (walkedMeters > 24f && GameManager.Instance != null) GameManager.Instance.Think("city", "Весь город погас. А я — нет. Почему?..");

            // Шаг звучит, когда голова в нижней точке качания (WalkPhase = 3π/4 + kπ): звук совпадает с картинкой и со скоростью
            // ходьбы, ноги чередуются (два варианта звука), громкость растёт со скоростью (бег тяжелее, вдох и выдох тише).
            int foot = Mathf.FloorToInt((WalkPhase - 0.75f * Mathf.PI) / Mathf.PI);
            if (foot != lastFoot && Cc.isGrounded && horiz > 0.5f)
            {
                float speedK = Mathf.Clamp(horiz / 4.2f, 0.55f, 1.35f);
                ProcAudio.OneShot(stepSrc, (foot & 1) == 0 ? "step" : "step2",
                    StepVolume * Mathf.Pow(speedK, 0.7f) * Random.Range(0.85f, 1f), Random.Range(0.94f, 1.06f));
            }
            lastFoot = foot;
        }

        // ------------------------------------------------------------------ фокус

        static float Horiz(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Чем меньше, тем лучше цель: угол от направления взгляда + расстояние. -1 — не подходит.</summary>
        static float Score(Vector3 eye, Vector3 fwd, Vector3 pt, float dist)
        {
            Vector3 to = pt - eye;
            float ang = Vector3.Angle(fwd, to);
            if (ang > 50f && dist > 1.8f) return -1f;
            return ang + dist * 5f;
        }

        void Offer(FocusKind kind, Object obj, Vector3 point, float score)
        {
            if (score < 0f) return;
            if (focusObj != null && obj == focusObj && kind == FocusType)
            {
                prevKind = kind;
                prevPoint = point;
                prevScore = score;
                prevFound = true;
            }
            if (score < candScore)
            {
                candScore = score;
                candKind = kind;
                candObj = obj;
                candPoint = point;
            }
        }

        void UpdateFocus()
        {
            Transform ct = Cam.transform;
            Vector3 eye = ct.position;
            Vector3 fwd = ct.forward;
            Vector3 p = transform.position;

            candScore = 1e9f;
            candKind = FocusKind.None;
            candObj = null;
            prevFound = false;

            // лампы и очаги
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                float d = Horiz(l.BasePos, p);
                if (l.IsHearth) d -= 1.0f;
                if (d > GameConfig.InhaleReach) continue;
                float s1 = Score(eye, fwd, l.FocusPoint, d);
                float s2 = Score(eye, fwd, l.LightPosition, d);
                float s = s1 < 0f ? s2 : (s2 < 0f ? s1 : Mathf.Min(s1, s2));
                Offer(FocusKind.Lamp, l, l.FocusPoint, s);
            }

            // люди: потухшие (будить) и раненые (лечить)
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Survivor s = Survivor.All[i];
                bool asleep = s.IsAsleep;
                bool hurt = s.IsAwake && s.Health < GameConfig.SurvivorMaxHealth - 8f;
                if (!asleep && !hurt) continue;
                float d = Horiz(s.transform.position, p);
                if (d > 3.0f) continue;
                float sc = Score(eye, fwd, s.FocusPoint, d);
                Offer(asleep ? FocusKind.Sleeper : FocusKind.Hurt, s, s.FocusPoint, sc);
            }

            // осколки света
            for (int i = 0; i < LightOrb.All.Count; i++)
            {
                LightOrb o = LightOrb.All[i];
                if (o.Collected) continue;
                float d = (o.transform.position - p).magnitude;
                if (d > GameConfig.OrbReach) continue;
                Offer(FocusKind.Orb, o, o.transform.position, Score(eye, fwd, o.transform.position, d));
            }

            // пицца (лежит на земле: подходить надо ближе)
            for (int i = 0; i < Pizza.All.Count; i++)
            {
                Pizza z = Pizza.All[i];
                if (z.Taken) continue;
                float d = Horiz(z.transform.position, p);
                if (d > GameConfig.PizzaReach) continue;
                Offer(FocusKind.Pizza, z, z.FocusPoint, Score(eye, fwd, z.FocusPoint, d));
            }

            // генераторы
            for (int i = 0; i < Generator.All.Count; i++)
            {
                Generator g = Generator.All[i];
                if (g.Running) continue;
                float d = Horiz(g.BasePos, p);
                if (d > 3.2f) continue;
                Offer(FocusKind.Generator, g, g.FocusPoint, Score(eye, fwd, g.FocusPoint, d));
            }

            // зеркала хранилищ света: повернуть на E (линзы стоят неподвижно)
            for (int i = 0; i < LightOptic.All.Count; i++)
            {
                LightOptic o = LightOptic.All[i];
                if (o.Kind != OpticKind.Mirror) continue;
                float d = Horiz(o.transform.position, p);
                if (d > LightOptic.ReachE) continue;
                Offer(FocusKind.Optic, o, o.FocusPoint, Score(eye, fwd, o.FocusPoint, d));
            }

            // автобус: встать в кольцо у двери
            Bus bus = Bus.Instance;
            if (bus != null && bus.CanFeed)
            {
                float d = bus.DistanceTo(p);
                if (d < 3.6f) Offer(FocusKind.Bus, bus, bus.FocusPoint, 4f + d * 3f);
            }

            // гистерезис: не дёргаем фокус из-за мелкого перевеса
            if (prevFound && prevScore <= candScore + 7f)
            {
                candKind = prevKind;
                candObj = focusObj;
                candPoint = prevPoint;
            }

            FocusType = candKind;
            focusObj = candObj;
            focusPoint = candPoint;
            fLamp = candObj as Lamp;
            fSurv = candObj as Survivor;
            fOrb = candObj as LightOrb;
            fGen = candObj as Generator;
            fPizza = candObj as Pizza;
            fOptic = candObj as LightOptic;

            if (FocusType != FocusKind.None)
            {
                if (!focusRing.activeSelf) focusRing.SetActive(true);
                focusRing.transform.position = focusPoint;
                float pulse = 0.55f + 0.08f * Mathf.Sin(Time.time * 6f);
                focusRing.transform.localScale = new Vector3(pulse, pulse, 1f);
            }
            else if (focusRing.activeSelf)
            {
                focusRing.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ действия

        void UpdateActions(float dt)
        {
            bool canAct = !Hero.InLastBreath;
            bool cover = canAct && GameInput.Held(GKey.Q);
            Hero.Covered = cover;

            // Колесо мыши — яркость (радиус) ауры. Это и есть "тратить на освещение пути".
            float sc = GameInput.Scroll();
            if (sc != 0f && !cover)
            {
                Hero.AuraTarget = Mathf.Clamp(Hero.AuraTarget + sc * 0.6f, GameConfig.AuraMin, GameConfig.AuraMax);
            }

            // Проводник
            if (canAct && GameInput.Down(GKey.G)) GuideWisp.TryCast(true);

            Inhaling = false;
            Exhaling = false;
            PromptE = null;
            PromptF = null;
            WakeProgress = 0f;

            bool attacking = Combat != null && Combat.Busy;
            bool wantE = canAct && !cover && !attacking && GameInput.Held(GKey.E);
            bool wantF = canAct && !cover && !attacking && GameInput.Held(GKey.F);
            if (wantE && wantF) wantF = false;

            switch (FocusType)
            {
                case FocusKind.Orb:
                    if (Hero.Life >= GameConfig.MaxLight - 0.5f)
                    {
                        PromptE = "Свет полон";
                    }
                    else
                    {
                        PromptE = "[E] вдохнуть освобождённый свет";
                        if (wantE)
                        {
                            Inhaling = true;
                            fOrb.Collect();
                        }
                    }
                    break;

                case FocusKind.Pizza:
                    // взял — тут же съел: свет прибавляется тремя укусами (Pizza.Bite)
                    if (Hero.Life >= GameConfig.MaxLight - 0.5f)
                    {
                        PromptE = "Свет полон: пицца подождёт";
                    }
                    else
                    {
                        PromptE = "[E] съесть пиццу";
                        if (wantE) fPizza.Take();
                    }
                    break;

                case FocusKind.Lamp:
                    ActOnLamp(fLamp, wantE, wantF, dt);
                    break;

                case FocusKind.Optic:
                    // одно нажатие — поворот на 45°: смотри, как уходит отражённый луч
                    PromptE = "[E] повернуть зеркало";
                    if (canAct && !cover && !attacking && GameInput.Down(GKey.E)) fOptic.Rotate();
                    break;

                case FocusKind.Sleeper:
                    PromptF = "[F] выдох: разбудить (держать)";
                    if (wantF && Hero.Life > 3f)
                    {
                        Exhaling = true;
                        DoWake(fSurv, dt);
                    }
                    else
                    {
                        fSurv.WakeProgress = Mathf.MoveTowards(fSurv.WakeProgress, 0f, dt * 0.5f);
                    }
                    WakeProgress = fSurv.WakeProgress;
                    break;

                case FocusKind.Hurt:
                    PromptF = "[F] выдох: отдать свет раненому (держать)";
                    if (wantF && Hero.Life > 3f)
                    {
                        Exhaling = true;
                        DoHeal(fSurv, dt);
                    }
                    break;

                case FocusKind.Bus:
                    PromptF = "[F] выдох: завести автобус (держать)";
                    if (wantF && Hero.Life > 4f)
                    {
                        Exhaling = true;
                        float busCost = GameConfig.ExhaleRate * dt;
                        Hero.Spend(busCost, LightReason.FeedBus);
                        Bus.Instance.Feed(busCost);
                    }
                    break;

                case FocusKind.Generator:
                    PromptF = "[F] выдох: запустить генератор (держать)";
                    if (wantF && Hero.Life > 4f)
                    {
                        Exhaling = true;
                        float genCost = GameConfig.ExhaleRate * dt;
                        Hero.Spend(genCost, LightReason.FeedGenerator);
                        fGen.Feed(genCost);
                    }
                    break;
            }

            AudioMix.SetVolume(loopInhale, Mathf.MoveTowards(AudioMix.Volume(loopInhale), Inhaling ? 0.55f : 0f, dt * 4f));
            AudioMix.SetVolume(loopExhale, Mathf.MoveTowards(AudioMix.Volume(loopExhale), Exhaling ? 0.5f : 0f, dt * 4f));

            if (Inhaling && GameManager.Instance != null) GameManager.Instance.Think("inhale", "Огонь входит в меня, как воздух. Он мой?.. Тогда и тратить его нужно осторожно.");
        }

        void ActOnLamp(Lamp l, bool wantE, bool wantF, float dt)
        {
            // E — вдох: взять свет
            if (l.IsHearth)
            {
                PromptE = "Очаг общий: его не выпить";
            }
            else if (!l.Dead)
            {
                if (Hero.Life >= GameConfig.MaxLight - 0.5f && !l.IsDecoy)
                {
                    PromptE = "Свет полон";
                }
                else
                {
                    PromptE = "[E] вдох (держать): взять свет, лампа потускнеет";
                    if (wantE)
                    {
                        Inhaling = true;
                        DoInhale(l, dt);
                    }
                }
            }

            // F — выдох: отдать свет лампе
            if (l.Charge < GameConfig.LampMaxCharge - 0.01f)
            {
                if (l.Dead) PromptF = "[F] выдох: зажечь лампу (держать)";
                else if (l.IsHearth) PromptF = "[F] выдох: раздуть очаг (держать)";
                else PromptF = "[F] выдох: раздуть лампу в очаг (держать)";
                if (wantF && Hero.Life > 4f)
                {
                    Exhaling = true;
                    DoFeed(l, dt);
                }
            }
        }

        void DoInhale(Lamp l, float dt)
        {
            if (l.IsDecoy)
            {
                // Ложный огонь: вдох вытягивает свет из героя.
                l.DecoyTick(dt);
                Hero.Damage(GameConfig.DecoyDrain * dt, LightReason.Decoy);
                return;
            }
            float gain = l.Take(GameConfig.InhaleRate * dt, Hero.Life01 < 0.3f);
            Hero.Gain(gain, LightReason.Lamp);
        }

        void DoWake(Survivor s, float dt)
        {
            float step = dt / GameConfig.WakeTime;
            float cost = GameConfig.WakeCost * step;
            Hero.Spend(cost, LightReason.Wake);
            s.Invested += cost;
            s.WakeProgress += step;
            if (s.WakeProgress >= 1f) s.Wake();
        }

        void DoHeal(Survivor s, float dt)
        {
            float cost = GameConfig.HealRate * dt;
            Hero.Spend(cost, LightReason.Heal);
            s.Heal(cost * GameConfig.HealPerLight);
            s.Invested += cost;
        }

        void DoFeed(Lamp l, float dt)
        {
            float cost = GameConfig.ExhaleRate * dt;
            Hero.Spend(cost, LightReason.FeedLamp);
            l.Feed(cost * GameConfig.ExhaleLampEff / GameConfig.LampCapacity);
        }

        // ------------------------------------------------------------------ звук и камера

        void UpdateAudioAndCamera(float dt)
        {
            float danger = 1f - Hero.Life01;

            heartTimer -= dt;
            if (heartTimer <= 0f)
            {
                heartTimer = Mathf.Lerp(1.5f, 0.5f, danger);
                float vol = Mathf.Lerp(0.12f, 0.85f, danger) + LookDownT * 0.2f;
                if (Hero.InLastBreath) vol = 1f;
                ProcAudio.OneShot(heartSrc, "heart", Mathf.Clamp01(vol), 1f);
            }

            AudioMix.SetVolume(loopCrackle, 0.04f + 0.14f * Hero.Life01 + LookDownT * 0.25f);
            if (lowpass != null) lowpass.cutoffFrequency = Mathf.Lerp(22000f, 1600f, LookDownT);

            // Походка: голова подпрыгивает на каждом шаге, покачивается из стороны в сторону и чуть кренится (мягко, чтобы не мутило)
            float walk = MoveAmount;
            float bob = Mathf.Sin(WalkPhase * 2f) * 0.032f * walk;
            float sway = Mathf.Sin(WalkPhase) * 0.018f * walk;
            float shake = Hero.DamageFlash * Hero.DamageFlash * 0.04f;
            // тряска от босса: гасится за секунду; в настройках её можно выключить вместе с тряской от луча
            rumble = Mathf.MoveTowards(rumble, 0f, dt * 1.2f);
            if (rumble > 0f && GameSettings.Shake) shake += rumble * rumble * 0.03f;
            Vector3 jitter = shake > 0.0005f ? Random.insideUnitSphere * shake : Vector3.zero;
            Vector3 combatShake = (Combat != null && GameSettings.Shake) ? Combat.ShakeOffset : Vector3.zero;
            Head.localPosition = headBase + new Vector3(sway, bob, 0f) + jitter + combatShake;
            if (walk > 0.01f)
            {
                float roll = Mathf.Sin(WalkPhase) * 0.9f * walk;
                float nod = Mathf.Cos(WalkPhase * 2f) * 0.4f * walk;
                Head.localRotation = Head.localRotation * Quaternion.Euler(nod, 0f, roll);
            }
        }
    }
}
