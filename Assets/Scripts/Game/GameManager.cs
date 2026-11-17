using UnityEngine;

namespace PokaGoryu
{
    public enum GameState { Title, Intro, Playing, Dead, Victory, Ending }

    /// <summary>
    /// Главный объект игры. Создаётся сам (Bootstrap) в любой сцене, строит мир кодом и ведёт состояния:
    /// титульный экран -> предыстория (кухня, ожог свечой) -> игра -> смерть (возврат к очагу) -> концовка (автобус, ловушка,
    /// пробуждение за кухонным столом) -> итог. В бесконечном городе концовки нет: за автобусом сразу следующий город.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;

        public GameState State = GameState.Title;
        public bool Paused;
        public float TideFactor = 1f;     // множитель яркости ламп (в прилив < 1)

        public PlayerController Player;
        public HeroLight Hero;
        public Director Dir;
        public GameUI UI;
        public MenuUI Menu;
        public FeatherHealthBar Bar;       // полоска здоровья «Перья» (на груди героя)
        public IntroDirector Intro;        // предыстория: кухня, блэкаут, ожог свечой
        public EndingDirector Ending;      // концовка: поездка, ловушка, пробуждение на кухне
        public Transform WorldRoot;

        // Статистика для финала
        public int LampsDied;
        public int SurvivorsWoken;
        public int SurvivorsLost;
        public int EnemiesKilled;
        public int BossesKilled;   // Великий Гаситель побеждён в этом забеге
        public int Deaths;
        public float PlayTime;
        public float WinFade;
        public bool DebugOverlay;
        public readonly LightLedger Economy = new LightLedger();
        /// <summary>
        /// Строка с кнопками внизу экрана: видна с начала забега и пропадает, когда герой дошёл до первого очага после старта
        /// (Director.UpdateHearths). В новом забеге снова видна.
        /// </summary>
        public bool ControlHintsVisible = true;

        // ------------------------------------------------------------------ режим «Бесконечный город» и рекорды

        /// <summary>
        /// Бесконечный город: после каждого города автобус везёт в следующий (та же карта, другие лампы, реквизит и декор, темнее и
        /// опаснее: GameDifficulty.City). Возвращений к очагу ограниченное число (3 в начале, +1 за город, не больше 5); погас без
        /// возвращений — забег окончен, результат идёт в таблицу рекордов. В классике Endless = false и всё как раньше.
        /// </summary>
        public bool Endless;
        public int City = 1;                 // номер города в забеге бесконечного режима
        public int ReturnsLeft;              // сколько раз ещё можно вернуться к очагу (только бесконечный режим)
        public bool RunOver;                 // бесконечный забег закончен
        public bool Cheated;                 // отладочные клавиши или панель баланса: рекорд не засчитывается
        public int LastRank;                 // место последнего забега в таблице (0 — не вошёл или не засчитан)
        [System.NonSerialized] public RunRecord LastRecord;   // запись последнего забега (для экранов победы и конца забега)
        public float CityTime;               // время в текущем городе
        public const int EndlessReturnsStart = 3;
        public const int EndlessReturnsMax = 5;

        /// <summary>Сколько городов уже пройдено в этом забеге бесконечного режима (на экране победы — включая только что пройденный).</summary>
        public int CitiesCleared { get { return Endless ? City - 1 + (State == GameState.Victory ? 1 : 0) : 0; } }

        int endlessSaved;                    // огоньков спасено в прошлых городах
        bool endlessSubmitted;
        bool nextCityRequested;
        bool newEndlessRequested;
        Challenge runDifficulty;
        bool difficultyTouched;

        bool restartRequested;
        float autoGuideT;

        // Мысли героя: короткие реплики про себя, каждая — один раз за забег. Силу и цену света герой осознаёт здесь, в игре
        // (в предыстории он только удивлён: «рука светится, что со мной?»).
        readonly System.Collections.Generic.HashSet<string> thoughtsSeen = new System.Collections.Generic.HashSet<string>();
        float startThoughtT = -1f;

        public static float TideFactorSafe
        {
            get { return Instance != null ? Instance.TideFactor : 1f; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        void Awake()
        {
            Instance = this;
            GameConfig.ResetDefaults();
            gameObject.AddComponent<AudioMix>();
            Gfx.Init();
            Application.targetFrameRate = 60;

            GameObject pgo = new GameObject("Player");
            pgo.transform.SetParent(transform, false);
            Player = pgo.AddComponent<PlayerController>();
            Player.Init();
            Hero = Player.Hero;

            Dir = gameObject.AddComponent<Director>();
            UI = gameObject.AddComponent<GameUI>();
            Menu = gameObject.AddComponent<MenuUI>();
            Bar = gameObject.AddComponent<FeatherHealthBar>();
            Intro = gameObject.AddComponent<IntroDirector>();
            Ending = gameObject.AddComponent<EndingDirector>();
            LowRes lr = gameObject.AddComponent<LowRes>();
            lr.Init(Player.Cam);
            gameObject.AddComponent<FinalScreenFx>();
            GuideWisp.Create(transform);

            DisableOtherCameras();
            BuildWorld();
            State = GameState.Title;

            GameSettings.Load();
            GameSettings.Apply();
            gameObject.AddComponent<MusicDirector>();
            if (Debug.isDebugBuild) gameObject.AddComponent<DevBalancePanel>();
            GameSmoke.AttachIfRequested(this);
        }

        void DisableOtherCameras()
        {
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != Player.Cam && !IsOurCamera(cams[i])) cams[i].gameObject.SetActive(false);
            }
        }

        bool IsOurCamera(Camera c)
        {
            return c.transform.IsChildOf(transform);
        }

        // ------------------------------------------------------------------ мир

        /// <summary>
        /// Собрать мир заново. keepRun — следующий город бесконечного режима: время забега, возвращения и уже показанные мысли
        /// сохраняются, памятка по кнопкам не возвращается. startLife — с каким светом начать (по умолчанию StartLight).
        /// </summary>
        void BuildWorld(bool keepRun = false, float startLife = -1f)
        {
            if (!keepRun) thoughtsSeen.Clear();
            startThoughtT = -1f;
            ControlHintsVisible = !keepRun;
            if (WorldRoot != null)
            {
                WorldRoot.gameObject.SetActive(false);
                Destroy(WorldRoot.gameObject);
            }
            LightField.Clear();
            Lamp.All.Clear();
            Survivor.All.Clear();
            Enemy.All.Clear();
            LightOrb.All.Clear();
            Pizza.All.Clear();
            Growth.All.Clear();
            LightOptic.All.Clear();
            Generator.All.Clear();
            Player.RegisterLights();
            GuideWisp.ResetAll();
            if (GuideWisp.Instance != null) LightField.Register(GuideWisp.Instance);

            GameDifficulty.City = Endless ? City : 1;
            LevelBuilder.CitySeed = Endless ? City - 1 : 0;
            WorldRoot = new GameObject("World").transform;
            LevelBuilder.Build(WorldRoot);
            AudioMix.AddTunnelZone(WorldRoot);
            Dir.ResetAll();

            Hero.ResetFor(startLife > 0f ? startLife : GameConfig.StartLight);
            Player.TeleportTo(LevelBuilder.StartPos, LevelBuilder.StartYaw);
            Player.ResetWalked();
            TideFactor = 1f;
            LampsDied = 0;
            SurvivorsWoken = 0;
            SurvivorsLost = 0;
            EnemiesKilled = 0;
            BossesKilled = 0;
            WinFade = 0f;
            CityTime = 0f;
            if (!keepRun)
            {
                Deaths = 0;
                PlayTime = 0f;
                Cheated = false;
                difficultyTouched = false;
                LastRank = 0;
                LastRecord = null;
            }
            Economy.Reset(Hero.Life);
            if (UI != null) UI.ResetRun();
        }

        /// <summary>Начать заново: в классике — тот же город с начала; в бесконечном режиме — новый забег с первого города.</summary>
        public void Restart()
        {
            if (Endless) newEndlessRequested = true;
            else restartRequested = true;
        }

        /// <summary>Кнопка «Играть» в главном меню: сначала предыстория (её можно пропустить), потом игра.</summary>
        public void StartGame()
        {
            if (State != GameState.Title) return;
            Endless = false;
            City = 1;
            if (Intro != null && Intro.Begin())
            {
                State = GameState.Intro;
                return;
            }
            BeginPlay();
        }

        /// <summary>Кнопка «Бесконечный город»: без предыстории; первый город равен классике, дальше каждый темнее и опаснее.</summary>
        public void StartEndless()
        {
            if (State != GameState.Title) return;
            ResetEndlessRun();
            BeginPlay();
        }

        void ResetEndlessRun()
        {
            Endless = true;
            City = 1;
            ReturnsLeft = EndlessReturnsStart;
            RunOver = false;
            endlessSubmitted = false;
            endlessSaved = 0;
            LastRank = 0;
            LastRecord = null;
        }

        void BeginPlay()
        {
            State = GameState.Playing;
            autoGuideT = 2.0f;
            startThoughtT = 3.5f;
            runDifficulty = GameDifficulty.Mode;
            difficultyTouched = false;
            if (Endless) Notify("Бесконечный город. Автобус увезёт дальше, но следующий город темнее. Возвращений к очагу: " + ReturnsLeft);
            else Notify("Иди к свету на горизонте. У лампы зажми E: вдох. Потухших людей буди выдохом (F)");
        }

        /// <summary>Победа в бесконечном режиме: автобус увозит в следующий город (клавиша R или кнопка на экране победы).</summary>
        public void NextCity()
        {
            if (Endless && State == GameState.Victory) nextCityRequested = true;
        }

        /// <summary>Идёт ли забег, который попадёт в таблицу рекордов (без отладки и без смены сложности на ходу).</summary>
        public bool RunCounts { get { return !Cheated && !difficultyTouched; } }

        RunRecord MakeRecord(int mode, int cities, int saved)
        {
            RunRecord r = new RunRecord();
            r.Mode = mode;
            r.Difficulty = (int)runDifficulty;
            r.Seconds = PlayTime;
            r.Cities = cities;
            r.Returns = Deaths;
            r.Saved = saved;
            r.Date = System.DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return r;
        }

        /// <summary>Классика: автобус уехал — время забега в таблицу рекордов.</summary>
        void SubmitClassicRecord(int boarded)
        {
            LastRecord = MakeRecord(RunRecords.Classic, 1, boarded);
            LastRank = RunCounts ? RecordsStore.Submit(LastRecord) : 0;
        }

        /// <summary>Бесконечный режим: забег закончился (погас без возвращений, ушёл в меню или начал заново) — результат в таблицу.</summary>
        void SubmitEndlessRecord()
        {
            if (!Endless || endlessSubmitted) return;
            endlessSubmitted = true;
            int cleared = CitiesCleared;
            LastRecord = MakeRecord(RunRecords.Endless, cleared, endlessSaved);
            LastRank = RunCounts && cleared >= 1 ? RecordsStore.Submit(LastRecord) : 0;
        }

        /// <summary>Мысль героя (внизу экрана, мягким голубоватым цветом). key — чтобы показать её только один раз за забег.</summary>
        public void Think(string key, string text)
        {
            if (State != GameState.Playing || Paused) return;
            if (!thoughtsSeen.Add(key)) return;
            if (UI != null) UI.Think(text);
        }

        /// <summary>Предыстория закончилась (или её пропустили): герой выходит на улицу, начинается игра.</summary>
        public void FinishIntro()
        {
            if (State != GameState.Intro) return;
            Player.TeleportTo(LevelBuilder.StartPos, LevelBuilder.StartYaw);
            Player.ResetHead();
            if (LevelBuilder.Culler != null) LevelBuilder.Culler.Refresh();
            BeginPlay();
        }

        /// <summary>В главное меню: мир собирается заново, камера снова смотрит на улицу.</summary>
        public void QuitToMenu()
        {
            if (Intro != null) Intro.Abort();
            if (Ending != null) Ending.Abort();
            SubmitEndlessRecord();   // вышел из бесконечного режима: что успел пройти — в таблицу
            Endless = false;
            City = 1;
            Time.timeScale = 1f;
            Paused = false;
            BuildWorld();
            State = GameState.Title;
            if (Menu != null) Menu.ShowRoot();
        }

        /// <summary>Выход из игры (в редакторе — остановка Play).</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// «К последнему очагу» из паузы: выход, если застрял. Всё как после гибели (мир за очагом сбрасывается, враги исчезают, возвращение
        /// засчитывается, в бесконечном городе тратится одно из возвращений), но герой не погибает и света ему не убавляют.
        /// </summary>
        public bool ReturnToHearth()
        {
            if (State != GameState.Playing || Dir == null) return false;
            if (Endless)
            {
                if (ReturnsLeft <= 0)
                {
                    Notify("Возвращений к очагу не осталось");
                    return false;
                }
                ReturnsLeft--;
            }
            Deaths++;
            SetPaused(false);
            Dir.Respawn(true);
            Notify("Вы вернулись к последнему очагу");
            return true;
        }

        public void Respawn()
        {
            if (State != GameState.Dead) return;
            if (Endless)
            {
                // возвращений к очагу ограниченное число; закончились — забег окончен (OnHeroDied)
                if (RunOver || ReturnsLeft <= 0) return;
                ReturnsLeft--;
            }
            Dir.Respawn();
            State = GameState.Playing;
        }

        // ------------------------------------------------------------------ события

        public void Notify(string msg)
        {
            if (UI != null) UI.Notify(msg);
        }

        public void OnHeroDied()
        {
            if (State != GameState.Playing) return;
            State = GameState.Dead;
            Deaths++;
            SaveLightReport();
            ProcAudio.OneShot(Player.Sfx, "tide", 1f, 0.8f);
            if (Endless && ReturnsLeft <= 0)
            {
                RunOver = true;
                SubmitEndlessRecord();
            }
        }

        public void Win()
        {
            if (State != GameState.Playing) return;
            State = GameState.Victory;
            WinFade = 0f;
            SaveLightReport();
            int boarded = Bus.Instance != null ? Bus.Instance.BoardedCount : 0;
            if (Endless)
            {
                ProcAudio.OneShot(Player.Sfx, "win", 1f, 1f);
                endlessSaved += boarded;
                return;
            }
            SubmitClassicRecord(boarded);   // время и место фиксируются сразу, концовка их не меняет
            // классика: автобус уезжает с героем и людьми, и это оказывается ловушкой (EndingDirector); итог — после неё
            if (Ending != null)
            {
                State = GameState.Ending;
                if (!Ending.Begin()) State = GameState.Victory;
            }
            if (State == GameState.Victory) ProcAudio.OneShot(Player.Sfx, "win", 1f, 1f);
        }

        /// <summary>Концовка закончилась (или её пропустили): экран итога.</summary>
        public void FinishEnding()
        {
            if (State != GameState.Ending) return;
            State = GameState.Victory;
            WinFade = 0f;
        }

        void SaveLightReport()
        {
            try
            {
                string dir = GameSmoke.Running ? GameSmoke.OutputDirectory : System.IO.Path.Combine(Application.persistentDataPath, "Reports");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "last-run-light.csv"), Economy.Csv(PlayTime, Hero.Life, Deaths));
            }
            catch (System.Exception e) { Debug.LogWarning("[PokaGoryu] Could not save light report: " + e.GetType().Name); }
        }

        public void OnLampDied(Lamp l)
        {
            LampsDied++;
            if (Player != null && (l.BasePos - Player.transform.position).magnitude < 7f) Notify("Лампа погасла");
        }

        public void OnSurvivorWoken(Survivor s)
        {
            SurvivorsWoken++;
            Notify("Огонёк разгорелся: ещё один спасённый");
            Think("wake", "Он загорелся от моего света. Значит, я могу делиться — пока есть чем.");
        }

        public void OnSurvivorDied(Survivor s)
        {
            SurvivorsLost++;
            Notify("Огонёк погас");
        }

        public void OnEnemyKilled(Enemy e)
        {
            EnemiesKilled++;
            if (e.Kind == EnemyKind.Eater) Notify("Пожиратель рассеялся");
            if (e.Kind == EnemyKind.Boss) BossesKilled++;
        }

        public int LampsLeft()
        {
            int n = 0;
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                if (!l.Dead && !l.IsDecoy) n++;
            }
            return n;
        }

        public int AwakeSurvivors()
        {
            int n = 0;
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                if (Survivor.All[i].IsAwake) n++;
            }
            return n;
        }

        /// <summary>Что сейчас делать: одна строка под верхним краем экрана.</summary>
        public string ObjectiveText()
        {
            Bus bus = Bus.Instance;
            if (bus != null && bus.State == Bus.Mode.Running)
            {
                int left = Mathf.CeilToInt(Mathf.Max(0f, bus.FinalLeft));
                string time = (left / 60) + ":" + (left % 60).ToString("00");
                Enemy boss = Enemy.FindBoss();
                if (boss != null)
                {
                    if (boss.CoreExposed) return "ЯДРО ОТКРЫТО: БЕЙ! До отправления: " + time;
                    if (boss.BossState == BossMode.Arrive) return "Что-то приближается… До отправления: " + time;
                    return "Броня глотает свет: береги его. До отправления: " + time;
                }
                return "Держи круг! До отправления: " + time;
            }

            if (bus != null && bus.State == Bus.Mode.Idle && Player != null && bus.DistanceTo(Player.transform.position) < 34f)
            {
                int pct = Mathf.RoundToInt(bus.ChargeFrac * 100f);
                return "Автобус заглох. Встань в светящееся кольцо у двери и держи F: отдай ему свет (" + pct + "%). Спасённые рядом помогают";
            }

            if (Player != null)
            {
                Vector3 pp = Player.transform.position;
                for (int i = 0; i < Growth.All.Count; i++)
                {
                    Growth g = Growth.All[i];
                    if (g.Dead || g.Sealed) continue;   // печать хранилища путь не закрывает
                    float dx = g.Center.x - pp.x;
                    float dz = g.Center.z - pp.z;
                    if (dx * dx + dz * dz < 30f * 30f) return "Путь закрыла стена Тьмы. Выжги её светом: ЛКМ (держать)";
                }
            }

            if (SurvivorsWoken == 0 && CityTime < 150f)
                return "Найди потухших людей (красные угольки) и разбуди: подойди и держи F";
            // подсказка про Проводника живёт, пока игрок сам его не вызвал (автовызов в начале не считается)
            return GuideWisp.PlayerActivated
                ? "Идите к автовокзалу: столб света над городом"
                : "Идите к автовокзалу: столб света над городом. G — Проводник покажет путь";
        }

        // ------------------------------------------------------------------ кадр

        void Update()
        {
            float dt = Time.deltaTime;

            if (restartRequested)
            {
                restartRequested = false;
                BuildWorld();
                Paused = false;
                Time.timeScale = 1f;
                State = GameState.Playing;
                autoGuideT = 2.0f;
                runDifficulty = GameDifficulty.Mode;
            }
            if (newEndlessRequested)
            {
                // новый забег бесконечного режима (с паузы, с экрана конца забега): прошлый, если успел пройти город, идёт в таблицу
                newEndlessRequested = false;
                SubmitEndlessRecord();
                ResetEndlessRun();
                BuildWorld();
                Paused = false;
                Time.timeScale = 1f;
                BeginPlay();
            }
            if (nextCityRequested)
            {
                // автобус привёз в следующий город: время забега и возвращения идут дальше, свет не меньше стартового
                nextCityRequested = false;
                float carried = Hero.Life;
                bool knewGuide = GuideWisp.PlayerActivated;
                City++;
                ReturnsLeft = Mathf.Min(ReturnsLeft + 1, EndlessReturnsMax);
                BuildWorld(true, Mathf.Clamp(Mathf.Max(carried, GameConfig.StartLight), 1f, GameConfig.MaxLight));
                GuideWisp.KeepKnown(knewGuide);
                Paused = false;
                Time.timeScale = 1f;
                State = GameState.Playing;
                autoGuideT = 0f;
                Notify("Город " + City + ". Темнее прежнего. Возвращений к очагу: " + ReturnsLeft);
            }

            switch (State)
            {
                case GameState.Title:
                    // главное меню (MenuUI): «Играть» вызывает StartGame; Esc закрывает подстраницы
                    if (GameInput.Down(GKey.Esc) && Menu != null) Menu.Back();
                    break;
                case GameState.Intro:
                    break;   // ведёт IntroDirector (пропуск — Пробел, Enter, Esc, клик); по окончании он вызовет FinishIntro
                case GameState.Playing:
                    if (GameInput.Down(GKey.Esc))
                    {
                        // в паузе Esc сначала закрывает «Настройки»/«Как играть», потом продолжает игру
                        if (Paused) { if (Menu == null || !Menu.Back()) SetPaused(false); }
                        else SetPaused(true);
                    }
                    if (!Paused)
                    {
                        PlayTime += dt;
                        CityTime += dt;
                        if (GameDifficulty.Mode != runDifficulty) difficultyTouched = true;   // сложность сменили на ходу: рекорд не засчитывается
                        Economy.Tick(PlayTime);
                        Dir.Tick(dt);
                        if (autoGuideT > 0f)
                        {
                            autoGuideT -= dt;
                            if (autoGuideT <= 0f) GuideWisp.TryCast();   // первый вызов Проводника — бесплатный, показывает систему
                        }
                        if (startThoughtT > 0f)
                        {
                            startThoughtT -= dt;
                            if (startThoughtT <= 0f) Think("start", "Рука всё ещё светится. Почему она не гаснет?..");
                        }
                        // свет на исходе: первая мысль о цене света
                        if (Hero != null && Hero.Life01 < 0.35f && !Hero.InLastBreath) Think("low", "Я гасну… Свет убывает. Нужен очаг или чей-то огонь.");
                    }
                    break;
                case GameState.Dead:
                    if (GameInput.Down(GKey.R))
                    {
                        if (RunOver) newEndlessRequested = true;   // забег бесконечного режима закончен: R — начать новый
                        else Respawn();
                    }
                    break;   // кнопки «К очагу» и «В главное меню» — в MenuUI
                case GameState.Victory:
                    WinFade = Mathf.MoveTowards(WinFade, 1f, dt * 0.35f);
                    if (GameInput.Down(GKey.R))
                    {
                        if (Endless) NextCity();
                        else Restart();
                    }
                    break;
            }

            if (GameInput.Down(GKey.F2))
            {
                GameSettings.Camcorder = !GameSettings.Camcorder;
                GameSettings.Save();
            }
            if (GameInput.Down(GKey.Minus))
            {
                GameSettings.AddBrightness(-0.1f);
                Notify("Яркость: " + Mathf.RoundToInt(Gfx.Brightness * 100f) + "%");
            }
            if (GameInput.Down(GKey.Equals))
            {
                GameSettings.AddBrightness(0.1f);
                Notify("Яркость: " + Mathf.RoundToInt(Gfx.Brightness * 100f) + "%");
            }
            if (LowRes.Instance != null) LowRes.Instance.Glitch = Hero != null ? Mathf.Clamp01(Hero.DamageFlash * 1.6f) : 0f;
            UpdateDebugKeys();

            bool lockCursor = !GameSmoke.Running && ((State == GameState.Playing && !Paused) || State == GameState.Intro || State == GameState.Ending);
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }

        // ------------------------------------------------------------------ отладка (редактор и Development Build)

        void UpdateDebugKeys()
        {
            if (!Debug.isDebugBuild) return;
            if (State != GameState.Playing) return;
            if (GameInput.Down(GKey.F1)) DebugOverlay = !DebugOverlay;
            if (GameInput.Down(GKey.F3))
            {
                Cheated = true;   // отладка: рекорд этого забега не засчитывается
                Hero.SetLife(GameConfig.MaxLight, LightReason.Debug);
                Notify("[debug] свет полон");
            }
            if (GameInput.Down(GKey.F4)) { Cheated = true; DebugNextHearth(); }
            if (GameInput.Down(GKey.F5)) { Cheated = true; DebugGoToStation(); }
            if (GameInput.Down(GKey.F6))
            {
                Cheated = true;
                Enemy.Spawn(EnemyKind.Shade, Player.transform.position + Player.transform.forward * 8f);
            }
        }

        /// <summary>Телепорт к следующему очагу (чтобы быстро проверять районы).</summary>
        void DebugNextHearth()
        {
            Lamp best = null;
            float cur = Dir.HeroProgress;
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                if (!l.IsHearth || l.Progress <= cur + 0.02f) continue;
                if (best == null || l.Progress < best.Progress) best = l;
            }
            if (best == null)
            {
                Notify("[debug] очагов дальше нет");
                return;
            }
            Vector3 b = best.BasePos;
            Vector3 spot = new Vector3(b.x + 2.8f, 0.1f, b.z);
            if (!CityMap.IsRoad(spot.x, spot.z, false)) spot = new Vector3(b.x - 2.8f, 0.1f, b.z);
            if (!CityMap.IsRoad(spot.x, spot.z, false)) spot = new Vector3(b.x, 0.1f, b.z + 2.8f);
            Player.TeleportTo(spot, 0f);
        }

        /// <summary>Телепорт к автовокзалу вместе со всеми выжившими (проверка финала).</summary>
        void DebugGoToStation()
        {
            Vector3 p = LevelBuilder.StationPos;
            Player.TeleportTo(p, 0f);
            int k = 0;
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Vector3 off = new Vector3(Mathf.Sin(k * 1.7f) * 2.5f, 0f, -2f - k * 0.5f);
                Survivor.All[i].ReviveAt(p + off);
                k++;
            }
            Notify("[debug] вокзал, все выжившие с вами");
        }

        public void SetPaused(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0f : 1f;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && GameSettings.PauseOnFocusLost && State == GameState.Playing && !Paused)
                SetPaused(true);
        }

        void LateUpdate()
        {
            if (Player == null || Player.Cam == null) return;
            LightField.PushToShaders(Player.Cam.transform.position, Hero);

            // Полоска здоровья «Перья» показывает свет героя; при слабом свете она золотая, как предупреждение
            if (Bar != null && Hero != null)
            {
                Bar.SetHealth(Hero.Life);
                Bar.SetTone(Hero.InLastBreath || Hero.Life < 25f ? 2 : 0);
            }
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (Instance == this) Instance = null;
        }
    }
}
