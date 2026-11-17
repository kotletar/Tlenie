using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Меню на IMGUI (без пакетов и префабов): главное меню, пауза, «Как играть», «Настройки», кнопки на экранах смерти и победы.
    /// Управление: мышь (наведение и клик) или клавиатура (стрелки или W/S — выбор, A/D или стрелки влево-вправо — значение,
    /// Enter или Пробел — подтвердить, Esc — назад). Разметка в виртуальном экране 720 по высоте: масштабируется под любое окно.
    /// Состояние игры (титул, пауза, смерть, победа) определяет GameManager; меню только показывает его и вызывает его методы.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public static MenuUI Instance;

        enum Page { Root, Help, Settings, Report, Records }
        enum Mode { None, Title, Pause, Dead, Victory }

        static readonly Color Gold = new Color(1f, 0.80f, 0.45f, 1f);
        static readonly Color Warm = new Color(1f, 0.92f, 0.75f, 1f);
        static readonly Color Dim = new Color(0.66f, 0.62f, 0.58f, 1f);

        static readonly string[] TitleItems = { "ИГРАТЬ", "БЕСКОНЕЧНЫЙ ГОРОД", "РЕКОРДЫ", "КАК ИГРАТЬ", "НАСТРОЙКИ", "ВЫХОД" };
        static readonly string[] PauseItems = { "ПРОДОЛЖИТЬ", "К ПОСЛЕДНЕМУ ОЧАГУ", "НАЧАТЬ ЗАНОВО", "КАК ИГРАТЬ", "НАСТРОЙКИ", "В ГЛАВНОЕ МЕНЮ", "ВЫЙТИ ИЗ ИГРЫ", "СВЕТ ЗА ЗАБЕГ" };
        static readonly string[] DeadItems = { "К ОЧАГУ  (R)", "В ГЛАВНОЕ МЕНЮ", "СВЕТ ЗА ЗАБЕГ" };
        static readonly string[] WinItems = { "СЫГРАТЬ СНОВА  (R)", "В ГЛАВНОЕ МЕНЮ", "СВЕТ ЗА ЗАБЕГ" };
        static readonly string[] SettingNames =
        {
            "Громкость", "Чувствительность мыши", "Яркость",
            "Эффект камкордера", "Подписи в мире", "Тряска камеры", "Музыка",
            "Эффекты", "Окружение", "Интерфейс", "Угол обзора", "Инверсия мыши Y",
            "Размер подсказок", "Пауза вне окна", "Сложность"
        };

        static readonly int[][] SettingTabs = { new int[] { 14, 1, 10, 11, 13 }, new int[] { 2, 3, 4, 12, 5 }, new int[] { 0, 6, 7, 8, 9 } };
        static readonly string[] TabNames = { "ИГРА", "ВИД", "ЗВУК" };
        int settingsTab;
        int recordsMode;                 // страница рекордов: 0 — классика, 1 — бесконечный город
        int recordsDiff = 1;             // сложность в таблице (Challenge)
        bool recentReport;
        Page page = Page.Root;
        Mode mode = Mode.None;
        int sel;
        Vector2 lastMouse;
        float modeTime;
        GUIStyle sTitle, sSub, sBtn, sHead, sBody, sSmall, sRight, sRow;
        AudioSource sfx;

        public bool Visible { get { return mode != Mode.None; } }

        void Awake()
        {
            Instance = this;
            sfx = ProcAudio.Source(gameObject, "orb", false, 1f, false, AudioBus.Interface);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Вернуться на главную страницу меню (без подстраниц).</summary>
        public void ShowRoot()
        {
            page = Page.Root;
            sel = 0;
        }

        /// <summary>Esc: закрыть подстраницу. Возвращает true, если что-то закрылось.</summary>
        public bool Back()
        {
            if (page == Page.Root) return false;
            if (page == Page.Settings) GameSettings.Save();
            page = Page.Root;
            sel = 0;
            Click(0.9f);
            return true;
        }

        static Mode ModeOf(GameManager gm)
        {
            if (gm == null) return Mode.None;
            switch (gm.State)
            {
                case GameState.Title: return Mode.Title;
                case GameState.Playing: return gm.Paused ? Mode.Pause : Mode.None;
                case GameState.Dead: return Mode.Dead;
                case GameState.Victory: return Mode.Victory;
            }
            return Mode.None;
        }

        static string[] ItemsOf(Mode m)
        {
            switch (m)
            {
                case Mode.Title: return TitleItems;
                case Mode.Pause: return PauseItems;
                case Mode.Dead: return DeadItems;
                case Mode.Victory: return WinItems;
            }
            return TitleItems;
        }

        /// <summary>Пункты текущего меню. В бесконечном режиме экраны победы и конца забега другие.</summary>
        string[] CurrentItems()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.Endless)
            {
                if (mode == Mode.Pause)
                {
                    // в бесконечном городе возврат из паузы тратит одно из возвращений: число видно на кнопке
                    string[] pause = (string[])PauseItems.Clone();
                    pause[1] = "К ОЧАГУ  (возвращений: " + gm.ReturnsLeft + ")";
                    return pause;
                }
                if (mode == Mode.Victory)
                    return new string[] { "ДАЛЬШЕ: ГОРОД " + (gm.City + 1) + "  (R)", "В ГЛАВНОЕ МЕНЮ", "СВЕТ ЗА ЗАБЕГ" };
                if (mode == Mode.Dead)
                    return new string[] { gm.RunOver ? "НОВЫЙ ЗАБЕГ  (R)" : "К ОЧАГУ  (R)", "В ГЛАВНОЕ МЕНЮ", "СВЕТ ЗА ЗАБЕГ" };
            }
            return ItemsOf(mode);
        }

        int ItemCount()
        {
            if (page == Page.Settings) return SettingTabs[settingsTab].Length + 1;
            if (page == Page.Help || page == Page.Report || page == Page.Records) return 1;
            return CurrentItems().Length;
        }

        bool Ready(GameManager gm)
        {
            if (mode == Mode.Victory && gm.WinFade < 0.9f) return false;
            return true;
        }

        // ------------------------------------------------------------------ ввод с клавиатуры

        void Update()
        {
            GameManager gm = GameManager.Instance;
            Mode m = ModeOf(gm);
            if (m != mode)
            {
                mode = m;
                modeTime = 0f;
                page = Page.Root;
                sel = 0;
            }
            if (mode == Mode.None) return;
            modeTime += Time.unscaledDeltaTime;
            if (!Ready(gm)) return;
            if (page == Page.Settings && GameInput.Down(GKey.Tab)) { settingsTab = (settingsTab + 1) % SettingTabs.Length; sel = 0; Click(0.8f); }
            if (page == Page.Report && GameInput.Down(GKey.Tab)) recentReport = !recentReport;
            if (page == Page.Records && GameInput.Down(GKey.Tab)) { recordsMode ^= 1; Click(0.8f); }

            if (GameInput.Down(GKey.Down) || GameInput.Down(GKey.S)) Move(1);
            if (GameInput.Down(GKey.Up) || GameInput.Down(GKey.W)) Move(-1);
            if (GameInput.Down(GKey.Right) || GameInput.Down(GKey.D)) Adjust(1);
            if (GameInput.Down(GKey.Left) || GameInput.Down(GKey.A)) Adjust(-1);
            if (modeTime > 0.25f && (GameInput.Down(GKey.Enter) || GameInput.Down(GKey.Space))) Activate(sel);
        }

        void Move(int dir)
        {
            int n = ItemCount();
            if (n <= 1) return;
            sel = (sel + dir + n) % n;
            Click(0.5f);
        }

        void Adjust(int dir)
        {
            if (page == Page.Records)
            {
                recordsDiff = (recordsDiff + dir + 3) % 3;
                Click(0.7f);
                return;
            }
            if (page != Page.Settings) return;
            if (sel >= SettingTabs[settingsTab].Length) return;
            int id = SettingTabs[settingsTab][sel];
            if (id == 14) GameSettings.Difficulty = (Challenge)(((int)GameSettings.Difficulty + dir + 3) % 3);
            else if (IsToggle(id)) SetSetting(id, GetSetting(id) > 0.5f ? 0f : 1f);
            else
            {
                float min, max, step;
                SettingRange(id, out min, out max, out step);
                SetSetting(id, Mathf.Clamp(GetSetting(id) + dir * step, min, max));
            }
            GameSettings.Apply();
            Click(0.7f);
        }

        void Click(float pitch)
        {
            if (sfx != null) ProcAudio.OneShot(sfx, "orb", 0.35f, pitch);
        }

        void Activate(int i)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            if (page == Page.Help || page == Page.Report || page == Page.Records)
            {
                Back();
                return;
            }

            if (page == Page.Settings)
            {
                if (i == SettingTabs[settingsTab].Length) Back();
                else if (i >= 0 && i < SettingTabs[settingsTab].Length && (IsToggle(SettingTabs[settingsTab][i]) || SettingTabs[settingsTab][i] == 14)) Adjust(1);
                return;
            }

            Click(1.2f);
            switch (mode)
            {
                case Mode.Title:
                    if (i == 0) gm.StartGame();
                    else if (i == 1) gm.StartEndless();
                    else if (i == 2) { page = Page.Records; sel = 0; recordsMode = 0; recordsDiff = (int)GameSettings.Difficulty; }
                    else if (i == 3) { page = Page.Help; sel = 0; }
                    else if (i == 4) { page = Page.Settings; sel = 0; }
                    else gm.QuitGame();
                    break;
                case Mode.Pause:
                    if (i == 0) gm.SetPaused(false);
                    else if (i == 1) gm.ReturnToHearth();   // застрял: вернуться к последнему очагу (в бесконечном городе тратится возвращение)
                    else if (i == 2) gm.Restart();
                    else if (i == 3) { page = Page.Help; sel = 0; }
                    else if (i == 4) { page = Page.Settings; sel = 0; }
                    else if (i == 5) gm.QuitToMenu();
                    else if (i == 6) gm.QuitGame();
                    else { page = Page.Report; sel = 0; }
                    break;
                case Mode.Dead:
                    if (i == 0) { if (gm.RunOver) gm.Restart(); else gm.Respawn(); }
                    else if (i == 1) gm.QuitToMenu();
                    else { page = Page.Report; sel = 0; }
                    break;
                case Mode.Victory:
                    if (i == 0) { if (gm.Endless) gm.NextCity(); else gm.Restart(); }
                    else if (i == 1) gm.QuitToMenu();
                    else { page = Page.Report; sel = 0; }
                    break;
            }
        }

        // ------------------------------------------------------------------ рисование

        void EnsureStyles()
        {
            if (sTitle != null) return;
            sTitle = new GUIStyle(GUI.skin.label);
            sTitle.fontSize = 92;
            sTitle.fontStyle = FontStyle.Bold;
            sTitle.alignment = TextAnchor.MiddleCenter;

            sSub = new GUIStyle(sTitle);
            sSub.fontSize = 28;
            sSub.fontStyle = FontStyle.Normal;

            sBtn = new GUIStyle(sTitle);
            sBtn.fontSize = 26;
            sBtn.fontStyle = FontStyle.Bold;

            sHead = new GUIStyle(sTitle);
            sHead.fontSize = 54;

            sBody = new GUIStyle(sTitle);
            sBody.fontSize = 21;
            sBody.fontStyle = FontStyle.Normal;
            sBody.alignment = TextAnchor.UpperLeft;
            sBody.wordWrap = true;

            sSmall = new GUIStyle(sBody);
            sSmall.fontSize = 16;
            sSmall.alignment = TextAnchor.MiddleCenter;
            sSmall.wordWrap = false;

            sRight = new GUIStyle(sSmall);
            sRight.alignment = TextAnchor.LowerRight;

            sRow = new GUIStyle(sBody);
            sRow.fontSize = 23;
            sRow.alignment = TextAnchor.MiddleLeft;
            sRow.wordWrap = false;
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void Frame(Rect r, Color c, float th)
        {
            Fill(new Rect(r.x, r.y, r.width, th), c);
            Fill(new Rect(r.x, r.yMax - th, r.width, th), c);
            Fill(new Rect(r.x, r.y, th, r.height), c);
            Fill(new Rect(r.xMax - th, r.y, th, r.height), c);
        }

        /// <summary>Цвет текста во всех состояниях стиля: иначе под курсором (hover) IMGUI красит текст в цвет скина.</summary>
        static void SetTextColor(GUIStyle st, Color c)
        {
            st.normal.textColor = c;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
        }

        static void Txt(Rect r, string s, GUIStyle st, Color c)
        {
            SetTextColor(st, new Color(0f, 0f, 0f, c.a * 0.8f));
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, st);
            SetTextColor(st, c);
            GUI.Label(r, s, st);
        }

        /// <summary>Самый крупный размер шрифта (не больше start), при котором многострочный текст помещается в w x h.</summary>
        static int FitBox(GUIStyle st, string text, float w, float h, int start, int min)
        {
            GUIContent c = new GUIContent(text);
            int size = start;
            while (size > min)
            {
                st.fontSize = size;
                if (st.CalcHeight(c, w) <= h) break;
                size--;
            }
            st.fontSize = size;
            return size;
        }

        /// <summary>Самый крупный размер шрифта (не больше start), при котором строка помещается по ширине.</summary>
        static int FitWidth(GUIStyle st, string text, float maxW, int start, int min)
        {
            GUIContent c = new GUIContent(text);
            int size = start;
            while (size > min)
            {
                st.fontSize = size;
                if (st.CalcSize(c).x <= maxW) break;
                size--;
            }
            st.fontSize = size;
            return size;
        }

        void OnGUI()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || mode == Mode.None) return;
            if (!Ready(gm)) return;
            if (page == Page.Settings && GameInput.Down(GKey.Tab)) { settingsTab = (settingsTab + 1) % SettingTabs.Length; sel = 0; Click(0.8f); }
            if (page == Page.Report && GameInput.Down(GKey.Tab)) recentReport = !recentReport;
            EnsureStyles();
            GUI.depth = -10;

            float s = Screen.height / 720f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float vw = Screen.width / s;
            const float vh = 720f;
            Event e = Event.current;
            bool repaint = e.type == EventType.Repaint;
            bool moved = repaint && (e.mousePosition - lastMouse).sqrMagnitude > 1f;

            if (repaint) DrawBackdrop(vw, vh);

            switch (page)
            {
                case Page.Root: DrawRoot(vw, vh, repaint, moved); break;
                case Page.Help: DrawHelp(vw, vh, repaint, moved); break;
                case Page.Settings: DrawSettings(vw, vh, repaint, moved); break;
                case Page.Report: DrawReport(vw, repaint, moved); break;
                case Page.Records: DrawRecords(vw, repaint, moved); break;
            }

            if (repaint) lastMouse = e.mousePosition;
            GUI.matrix = old;
        }

        void DrawBackdrop(float vw, float vh)
        {
            Rect full = new Rect(0f, 0f, vw, vh);
            float a = Mathf.Clamp01(modeTime * 3f);
            if (mode == Mode.Title)
            {
                Fill(full, new Color(0f, 0f, 0f, 0.50f * a));
                GUI.color = new Color(1f, 1f, 1f, 0.85f * a);
                GUI.DrawTexture(full, Gfx.TexVignette, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
            else if (mode == Mode.Pause)
            {
                Fill(full, new Color(0f, 0f, 0f, 0.62f * a));
            }
            else if (mode == Mode.Dead)
            {
                // GameUI уже затемнил экран и написал «СВЕТ ПОГАС»
            }
        }

        /// <summary>Кнопка: рисует себя и возвращает true при клике. Наведение мыши выбирает пункт.</summary>
        bool Button(Rect r, string text, int index, bool repaint, bool moved)
        {
            Event e = Event.current;
            bool hover = r.Contains(e.mousePosition);
            if (moved && hover && sel != index)
            {
                sel = index;
                Click(0.5f);
            }
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (repaint)
            {
                bool on = sel == index;
                if (on)
                {
                    Fill(r, new Color(0.34f, 0.20f, 0.05f, 0.82f));
                    Fill(new Rect(r.x, r.y, 5f, r.height), Gold);
                    Frame(r, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 1f);
                }
                else
                {
                    // на светлом экране победы кнопки темнее, чтобы текст читался
                    Fill(r, new Color(0f, 0f, 0f, mode == Mode.Victory ? 0.74f : 0.38f));
                    Frame(r, new Color(1f, 1f, 1f, 0.07f), 1f);
                }
                FitWidth(sBtn, text, r.width - 32f, 26, 15);
                Txt(r, text, sBtn, on ? Warm : Dim);
            }
            if (clicked)
            {
                sel = index;
                return true;
            }
            return false;
        }

        void DrawRoot(float vw, float vh, bool repaint, bool moved)
        {
            string[] items = CurrentItems();
            float cx = vw * 0.5f;
            float bw = 400f;
            float bh = 56f;
            float gap = 12f;
            float y0 = 300f;
            if (mode == Mode.Title && items.Length > 4) { bh = 48f; gap = 8f; y0 = 262f; }   // шесть пунктов: теснее, чтобы не лезть на подсказку внизу

            if (repaint)
            {
                if (mode == Mode.Title)
                {
                    float t = Time.unscaledTime;
                    float flick = 0.90f + 0.10f * Mathf.PerlinNoise(t * 7f, 1.7f);
                    if (Mathf.PerlinNoise(t * 1.6f, 9.1f) > 0.87f) flick *= 0.5f;   // редкое «моргание» огня
                    GUI.color = new Color(1f, 0.6f, 0.25f, 0.22f * flick);
                    GUI.DrawTexture(new Rect(cx - 480f, 50f, 960f, 220f), Gfx.TexHalo);
                    GUI.color = Color.white;
                    Txt(new Rect(0f, 90f, vw, 110f), "ТЛЕЮЩАЯ ТЬМА", sTitle, new Color(Gold.r, Gold.g, Gold.b, flick));
                    Txt(new Rect(0f, 200f, vw, 40f), "не дай погаснуть", sSub, new Color(Warm.r, Warm.g, Warm.b, 0.85f));
                    Txt(new Rect(0f, 670f, vw, 30f),
                        "Выбор: стрелки или W/S, мышь    Подтвердить: Enter    Назад: Esc",
                        sSmall, new Color(Warm.r, Warm.g, Warm.b, 0.45f));
                    // версия — тихо в правом нижнем углу (сложность видна в «Настройках»)
                    Txt(new Rect(vw - 248f, vh - 40f, 224f, 28f), "v" + GameConfig.Version, sRight, new Color(Dim.r, Dim.g, Dim.b, 0.8f));
                }
                else if (mode == Mode.Pause)
                {
                    Txt(new Rect(0f, 120f, vw, 90f), "ПАУЗА", sHead, Warm);
                    y0 = 250f;
                }
            }
            if (mode == Mode.Pause) { y0 = 214f; bh = 46f; gap = 8f; }   // восемь пунктов: теснее, чтобы поместились в экран

            if (mode == Mode.Dead || mode == Mode.Victory)
            {
                // две кнопки рядом, под текстом экрана смерти или победы (его рисует GameUI)
                float w2 = Mathf.Min(310f, (vw - 80f - 16f * (items.Length - 1)) / items.Length);
                float yy = mode == Mode.Dead ? 470f : 610f;
                for (int i = 0; i < items.Length; i++)
                {
                    Rect r = new Rect(cx - (w2 * items.Length + 16f * (items.Length - 1)) * 0.5f + i * (w2 + 16f), yy, w2, bh);
                    if (Button(r, items[i], i, repaint, moved)) Activate(i);
                }
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                Rect r = new Rect(cx - bw * 0.5f, y0 + i * (bh + gap), bw, bh);
                if (Button(r, items[i], i, repaint, moved)) Activate(i);
            }
        }

        void DrawPanel(float vw, Rect p, string title, bool repaint)
        {
            if (!repaint) return;
            Fill(new Rect(0f, 0f, vw, 720f), new Color(0f, 0f, 0f, 0.35f));
            Fill(p, new Color(0.03f, 0.03f, 0.05f, 0.90f));
            Frame(p, new Color(Gold.r, Gold.g, Gold.b, 0.45f), 2f);
            Txt(new Rect(p.x, p.y + 18f, p.width, 70f), title, sHead, Gold);
        }

        void DrawHelp(float vw, float vh, bool repaint, bool moved)
        {
            float pw = Mathf.Min(vw - 80f, 1120f);
            Rect p = new Rect((vw - pw) * 0.5f, 40f, pw, 640f);
            DrawPanel(vw, p, "КАК ИГРАТЬ", repaint);

            if (repaint)
            {
                float colW = (pw - 100f) * 0.5f;
                Rect left = new Rect(p.x + 36f, p.y + 100f, colW, 440f);
                Rect right = new Rect(p.x + 64f + colW, p.y + 100f, colW, 440f);

                string goal =
                    "ЦЕЛЬ\n" +
                    "Город накрыла Тьма. Твой свет — это и здоровье, и фонарь, и оружие.\n\n" +
                    "Проведи выживших до заброшенного автовокзала: над ним стоит столб света. Широкие улицы — главный путь, " +
                    "узкие переулки — петли и тупики, там люди, лампы и пицца (E — съесть: это свет). Потерялся — нажми G, огонёк покажет дорогу.\n\n" +
                    "Свет можно взять у лампы (она потускнеет) или отдать: разбудить человека, раздуть лампу, завести автобус. " +
                    "Спасённые согревают тебя в ответ. Заведёшь автобус — продержись минуту, пока люди садятся: Тьма пойдёт на свет, а у двери свет не тлеет, как у очага.\n\n" +
                    "Стену Тьмы, перегородившую улицу, выжги лучом (ЛКМ, держать).\n\n" +
                    "На площадях стоят хранилища света: каморки за печатью Тьмы. Прямой свет печать не берёт — направь луч в зеркало (E — повернуть), " +
                    "чтобы он отразился в печать. Линза усиливает луч втрое.\n\n" +
                    "Бесконечный город: за автобусом ждёт следующий город, темнее и опаснее; возвращений к очагу мало. Результаты — в «Рекордах».\n" +
                    "Чтобы увидеть свой свет, посмотри на себя: вниз или Tab — на груди горит полоска из перьев.";
                string keys =
                    "УПРАВЛЕНИЕ\n" +
                    "WASD — идти, Shift — бежать, мышь — смотреть\n" +
                    "ЛКМ — выстрел, держать — слитый луч\n" +
                    "ПКМ — вспышка света вокруг\n" +
                    "E (держать) — вдох: взять свет; у зеркала — повернуть\n" +
                    "F (держать) — выдох: отдать свет\n" +
                    "Q (держать) — прикрыть огонь\n" +
                    "G — Проводник к автобусу\n" +
                    "Колесо мыши — радиус ауры\n" +
                    "Tab — посмотреть на себя\n" +
                    "Esc — пауза    - и = — яркость\n" +
                    "Застряли? Esc, затем «К последнему очагу»\n" +
                    "Пробел — пропустить предысторию и концовку";

                // один размер шрифта на обе колонки: самый крупный, при котором влезают оба текста
                int fs = Mathf.Min(FitBox(sBody, goal, left.width, left.height, 21, 12), FitBox(sBody, keys, right.width, right.height, 21, 12));
                sBody.fontSize = fs;
                Txt(left, goal, sBody, Warm);
                Txt(right, keys, sBody, Warm);
            }

            Rect back = new Rect(vw * 0.5f - 160f, p.yMax - 90f, 320f, 56f);
            if (Button(back, "НАЗАД", 0, repaint, moved)) Activate(0);
        }

        float DoSlider(Rect r, float v, float min, float max, bool on)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            float t = Mathf.InverseLerp(min, max, v);
            if (e.type == EventType.Repaint)
            {
                float cy = r.y + r.height * 0.5f;
                Fill(new Rect(r.x, cy - 3f, r.width, 6f), new Color(1f, 1f, 1f, 0.14f));
                Fill(new Rect(r.x, cy - 3f, r.width * t, 6f), on ? Gold : new Color(0.80f, 0.62f, 0.35f, 0.9f));
                Fill(new Rect(r.x + r.width * t - 7f, cy - 13f, 14f, 26f), on ? Warm : Gold);
            }
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        v = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - r.x) / r.width));
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        v = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - r.x) / r.width));
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
            return v;
        }

        static bool IsToggle(int id) { return id == 3 || id == 4 || id == 5 || id == 11 || id == 13; }
        static float GetSetting(int id)
        {
            switch (id)
            {
                case 0: return GameSettings.Volume;
                case 1: return GameSettings.Sensitivity;
                case 2: return GameSettings.Brightness;
                case 3: return GameSettings.Camcorder ? 1 : 0;
                case 4: return GameSettings.Hints ? 1 : 0;
                case 5: return GameSettings.Shake ? 1 : 0;
                case 6: return GameSettings.MusicVolume;
                case 7: return GameSettings.EffectsVolume;
                case 8: return GameSettings.AmbienceVolume;
                case 9: return GameSettings.InterfaceVolume;
                case 10: return GameSettings.FieldOfView;
                case 11: return GameSettings.InvertY ? 1 : 0;
                case 12: return GameSettings.HintScale;
                case 13: return GameSettings.PauseOnFocusLost ? 1 : 0;
                default: return (int)GameSettings.Difficulty;
            }
        }
        static void SetSetting(int id, float value)
        {
            switch (id)
            {
                case 0: GameSettings.Volume = value; break;
                case 1: GameSettings.Sensitivity = value; break;
                case 2: GameSettings.Brightness = value; break;
                case 3: GameSettings.Camcorder = value > 0.5f; break;
                case 4: GameSettings.Hints = value > 0.5f; break;
                case 5: GameSettings.Shake = value > 0.5f; break;
                case 6: GameSettings.MusicVolume = value; break;
                case 7: GameSettings.EffectsVolume = value; break;
                case 8: GameSettings.AmbienceVolume = value; break;
                case 9: GameSettings.InterfaceVolume = value; break;
                case 10: GameSettings.FieldOfView = value; break;
                case 11: GameSettings.InvertY = value > 0.5f; break;
                case 12: GameSettings.HintScale = value; break;
                case 13: GameSettings.PauseOnFocusLost = value > 0.5f; break;
                case 14: GameSettings.Difficulty = (Challenge)Mathf.Clamp(Mathf.RoundToInt(value), 0, 2); break;
            }
        }
        static void SettingRange(int id, out float min, out float max, out float step)
        {
            min = 0f; max = 1f; step = 0.05f;
            if (id == 1) { min = GameSettings.SensitivityMin; max = GameSettings.SensitivityMax; step = 0.1f; }
            else if (id == 2) { min = GameSettings.BrightnessMin; max = GameSettings.BrightnessMax; }
            else if (id == 10) { min = GameSettings.FovMin; max = GameSettings.FovMax; step = 2f; }
            else if (id == 12) { min = 0.8f; max = 1.5f; }
        }
        static string SettingValue(int id)
        {
            if (IsToggle(id)) return GetSetting(id) > 0.5f ? "ВКЛ" : "ВЫКЛ";
            if (id == 14) return GameDifficulty.Name(GameSettings.Difficulty);
            if (id == 1) return GetSetting(id).ToString("0.0");
            if (id == 10) return Mathf.RoundToInt(GetSetting(id)) + "°";
            return Mathf.RoundToInt(GetSetting(id) * 100f) + "%";
        }
        void DrawSettings(float vw, float vh, bool repaint, bool moved)
        {
            float pw = Mathf.Min(840f, vw - 40f);
            Rect p = new Rect((vw - pw) * 0.5f, 40f, pw, 640f);
            DrawPanel(vw, p, "НАСТРОЙКИ", repaint);
            for (int t = 0; t < TabNames.Length; t++)
            {
                Rect tab = new Rect(p.x + 30f + t * (pw - 60f) / 3f, p.y + 98f, (pw - 60f) / 3f - 8f, 42f);
                if (repaint)
                {
                    Fill(tab, settingsTab == t ? new Color(0.34f, 0.20f, 0.05f, 0.9f) : new Color(1f, 1f, 1f, 0.06f));
                    Txt(tab, TabNames[t], sSmall, settingsTab == t ? Gold : Dim);
                }
                if (GUI.Button(tab, GUIContent.none, GUIStyle.none)) { settingsTab = t; sel = 0; Click(0.8f); }
            }
            int[] rows = SettingTabs[settingsTab];
            for (int i = 0; i < rows.Length; i++)
            {
                int id = rows[i];
                Rect row = new Rect(p.x + 30f, p.y + 166f + i * 62f, pw - 60f, 52f);
                if (moved && row.Contains(Event.current.mousePosition)) sel = i;
                bool on = sel == i;
                if (repaint)
                {
                    if (on) Fill(row, new Color(0.34f, 0.20f, 0.05f, 0.55f));
                    Txt(new Rect(row.x + 14f, row.y, row.width * 0.46f, row.height), SettingNames[id], sRow, on ? Warm : Dim);
                }
                Rect control = new Rect(row.x + row.width * 0.52f, row.y, row.width * 0.27f, row.height);
                if (IsToggle(id) || id == 14)
                {
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { sel = i; Adjust(1); }
                }
                else
                {
                    float min, max, step;
                    SettingRange(id, out min, out max, out step);
                    float old = GetSetting(id);
                    float value = DoSlider(control, old, min, max, on);
                    if (Mathf.Abs(old - value) > 0.0001f)
                    {
                        SetSetting(id, Mathf.Clamp(Mathf.Round(value / step) * step, min, max));
                        sel = i;
                        GameSettings.Apply();
                    }
                }
                if (repaint)
                {
                    Rect value = new Rect(row.x + row.width * 0.80f, row.y, row.width * 0.20f, row.height);
                    Txt(value, SettingValue(id), sRow, on ? Warm : Dim);
                }
            }
            if (repaint) Txt(new Rect(p.x + 20f, p.yMax - 132f, pw - 40f, 30f),
                "Tab — раздел   ↑↓ — выбор   ←→ — значение", sSmall, Dim);
            Rect back = new Rect(vw * 0.5f - 160f, p.yMax - 86f, 320f, 56f);
            if (Button(back, "НАЗАД", rows.Length, repaint, moved)) Back();
        }

        /// <summary>Таблица рекордов: десять лучших забегов для выбранного режима (Tab) и сложности (стрелки влево/вправо).</summary>
        void DrawRecords(float vw, bool repaint, bool moved)
        {
            float pw = Mathf.Min(1000f, vw - 40f);
            Rect p = new Rect((vw - pw) * 0.5f, 30f, pw, 660f);
            DrawPanel(vw, p, "РЕКОРДЫ", repaint);
            Rect head = new Rect(p.x + 30f, p.y + 100f, pw - 60f, 42f);
            if (GUI.Button(head, GUIContent.none, GUIStyle.none)) { recordsMode ^= 1; Click(0.8f); }
            if (repaint)
            {
                System.Collections.Generic.List<RunRecord> top = RecordsStore.Table.Top(recordsMode, recordsDiff);
                Txt(head, (recordsMode == 0 ? "Классика: время до отъезда автобуса" : "Бесконечный город: пройдено городов") + "  ·  "
                    + GameDifficulty.Name((Challenge)recordsDiff) + "   (Tab — режим, ←→ — сложность)", sSmall, Gold);
                float rowH = 38f;
                float x0 = p.x + 40f;
                float w = pw - 80f;
                for (int i = 0; i < RunRecords.Keep; i++)
                {
                    Rect row = new Rect(x0, p.y + 158f + i * rowH, w, rowH);
                    if (i % 2 == 0) Fill(row, new Color(1f, 1f, 1f, 0.035f));
                    RunRecord r = i < top.Count ? top[i] : null;
                    Color col = r == null ? Dim : (i == 0 ? Gold : Warm);
                    Txt(new Rect(row.x + 8f, row.y, 56f, rowH), (i + 1) + ".", sRow, col);
                    if (r == null)
                    {
                        Txt(new Rect(row.x + 70f, row.y, 200f, rowH), "—", sRow, Dim);
                        continue;
                    }
                    bool classic = recordsMode == 0;
                    Txt(new Rect(row.x + 70f, row.y, 210f, rowH), classic ? RunRecords.Clock(r.Seconds) : "городов: " + r.Cities, sRow, col);
                    Txt(new Rect(row.x + 290f, row.y, 230f, rowH), classic ? "возвращений: " + r.Returns : RunRecords.Clock(r.Seconds), sRow, col);
                    Txt(new Rect(row.x + 530f, row.y, 190f, rowH), "спасено: " + r.Saved, sRow, col);
                    Txt(new Rect(row.x + w - 170f, row.y, 160f, rowH), r.Date, sRow, Dim);
                }
            }
            Rect back = new Rect(vw * 0.5f - 160f, p.yMax - 86f, 320f, 56f);
            if (Button(back, "НАЗАД", 0, repaint, moved)) Back();
        }

        void DrawReport(float vw, bool repaint, bool moved)
        {
            GameManager gm = GameManager.Instance;
            float pw = Mathf.Min(1000f, vw - 40f);
            Rect p = new Rect((vw - pw) * 0.5f, 30f, pw, 660f);
            DrawPanel(vw, p, "СВЕТ ЗА ЗАБЕГ", repaint);
            Rect period = new Rect(p.x + 30f, p.y + 100f, pw - 60f, 42f);
            if (GUI.Button(period, GUIContent.none, GUIStyle.none)) recentReport = !recentReport;
            if (repaint)
            {
                Txt(period, recentReport ? "Последние 60 секунд · Tab — весь забег" : "Весь забег · Tab — последние 60 секунд", sSmall, Gold);
                float col = (pw - 100f) * 0.5f;
                string incoming = "ПОЛУЧЕНО\n\n" + gm.Economy.Breakdown(true, recentReport);
                string outgoing = "ПОТРАЧЕНО\n\n" + gm.Economy.Breakdown(false, recentReport);
                sBody.fontSize = Mathf.Min(FitBox(sBody, incoming, col, 340f, 20, 14), FitBox(sBody, outgoing, col, 340f, 20, 14));
                Txt(new Rect(p.x + 36f, p.y + 157f, col, 340f), incoming, sBody, Warm);
                Txt(new Rect(p.x + 64f + col, p.y + 157f, col, 340f), outgoing, sBody, Warm);
                Txt(new Rect(p.x + 30f, p.yMax - 154f, pw - 60f, 52f),
                    "Время: " + Mathf.FloorToInt(gm.PlayTime / 60f) + ":" + ((int)gm.PlayTime % 60).ToString("00") +
                    "   Сейчас света: " + gm.Hero.Life.ToString("0.0") + "   Возвращений: " + gm.Deaths, sSmall, Dim);
            }
            Rect back = new Rect(vw * 0.5f - 160f, p.yMax - 86f, 320f, 56f);
            if (Button(back, "НАЗАД", 0, repaint, moved)) Back();
        }
    }
}
