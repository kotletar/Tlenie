using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Интерфейс почти отсутствует: здоровье героя читается по его телу (смотри вниз, Tab).
    /// Здесь только то, без чего не обойтись: подсказка у прицела (только для цели в фокусе), метки над важными местами
    /// (стена Тьмы, автобус, очаг, потухшие люди, генератор), одна строка «что делать», короткие сообщения,
    /// виньетка, белая вспышка и титульный/финальный экраны. Всё на IMGUI (без пакетов).
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        class Note
        {
            public string Text;
            public float Age;
        }

        struct WorldLabel
        {
            public Vector3 Pos;       // где рисовать текст
            public Vector3 Gaze;      // куда должен смотреть игрок (центр объекта)
            public string Text;
            public Color Col;
            public float MaxDist;     // расстояние взаимодействия
            public float Cone;        // косинус допустимого угла между взглядом и объектом
        }

        static readonly List<WorldLabel> labels = new List<WorldLabel>(48);
        static int labelsFrame = -1;

        readonly List<Note> notes = new List<Note>();
        readonly List<Note> thoughts = new List<Note>();   // мысли героя: показываются по одной
        const float ThoughtTime = 5.8f;
        GUIStyle thoughtStyle;
        GUIStyle small;
        GUIStyle tiny;
        GUIStyle mid;
        GUIStyle big;
        GUIStyle left;
        int lastFs = -1;
        float controlHintsAlpha = 1f;   // строка с кнопками внизу экрана гаснет плавно, а не обрывается

        /// <summary>
        /// Метка в мире (вызывать каждый кадр, пока она нужна). Рисуется ТОЛЬКО когда игрок смотрит на объект (gaze — его центр,
        /// cone — косинус допустимого угла от центра экрана) и стоит ближе maxDist — расстояния взаимодействия с ним.
        /// </summary>
        public static void Label(Vector3 pos, string text, Color col, float maxDist, Vector3 gaze, float cone)
        {
            if (labelsFrame != Time.frameCount) { labels.Clear(); labelsFrame = Time.frameCount; }
            if (labels.Count >= 48) return;
            WorldLabel l = new WorldLabel();
            l.Pos = pos;
            l.Gaze = gaze;
            l.Text = text;
            l.Col = col;
            l.MaxDist = maxDist;
            l.Cone = cone;
            labels.Add(l);
        }

        public static void Label(Vector3 pos, string text, Color col, float maxDist)
        {
            Label(pos, text, col, maxDist, pos, 0.93f);
        }

        public void Notify(string text)
        {
            Note n = new Note();
            n.Text = text;
            n.Age = 0f;
            notes.Add(n);
            if (notes.Count > 3) notes.RemoveAt(0);
        }

        /// <summary>Мысль героя (из GameManager.Think): очередь, на экране одна.</summary>
        public void Think(string text)
        {
            Note n = new Note();
            n.Text = text;
            n.Age = 0f;
            thoughts.Add(n);
            if (thoughts.Count > 4) thoughts.RemoveAt(1);
        }

        public void ResetRun()
        {
            labels.Clear();
            labelsFrame = -1;
            notes.Clear();
            thoughts.Clear();
        }

        void Update()
        {
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                notes[i].Age += Time.unscaledDeltaTime;
                if (notes[i].Age > 4.5f) notes.RemoveAt(i);
            }
            // мысль «идёт» только пока игра идёт (не в меню, не на паузе)
            GameManager gm = GameManager.Instance;
            if (thoughts.Count > 0 && gm != null && gm.State == GameState.Playing && !gm.Paused)
            {
                thoughts[0].Age += Time.unscaledDeltaTime;
                if (thoughts[0].Age > ThoughtTime) thoughts.RemoveAt(0);
            }
            // памятка по кнопкам гаснет за ~1,3 с, когда герой дошёл до первого очага; в новом забеге возвращается
            float hintTarget = (gm == null || gm.ControlHintsVisible) ? 1f : 0f;
            controlHintsAlpha = Mathf.MoveTowards(controlHintsAlpha, hintTarget, Time.unscaledDeltaTime * 0.8f);
        }

        void EnsureStyles()
        {
            int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.026f * GameSettings.HintScale));
            if (small != null && lastFs == fs) return;
            lastFs = fs;

            small = new GUIStyle(GUI.skin.label);
            small.fontSize = fs;
            small.alignment = TextAnchor.MiddleCenter;
            small.wordWrap = true;
            small.normal.textColor = Color.white;

            tiny = new GUIStyle(small);
            tiny.fontSize = Mathf.Max(10, Mathf.RoundToInt(fs * 0.8f));
            tiny.wordWrap = false;

            mid = new GUIStyle(small);
            mid.fontSize = Mathf.RoundToInt(fs * 1.6f);

            big = new GUIStyle(small);
            big.fontSize = Mathf.RoundToInt(fs * 3.4f);
            big.fontStyle = FontStyle.Bold;

            left = new GUIStyle(small);
            left.alignment = TextAnchor.UpperLeft;

            thoughtStyle = new GUIStyle(small);
            thoughtStyle.fontSize = Mathf.RoundToInt(fs * 1.12f);
            thoughtStyle.fontStyle = FontStyle.Italic;
            thoughtStyle.alignment = TextAnchor.MiddleCenter;
        }

        static bool textShadow = true;

        /// <summary>Цвет текста во всех состояниях стиля: иначе под курсором (hover) IMGUI красит текст в цвет скина.</summary>
        static void SetTextColor(GUIStyle st, Color c)
        {
            st.normal.textColor = c;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
        }

        static void Text(Rect r, string s, GUIStyle st, Color c)
        {
            if (textShadow)
            {
                SetTextColor(st, new Color(0f, 0f, 0f, c.a * 0.85f));
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, st);
            }
            SetTextColor(st, c);
            GUI.Label(r, s, st);
        }

        static string Clock(float t)
        {
            int m = (int)(t / 60f);
            int s = (int)(t % 60f);
            return m + ":" + (s < 10 ? "0" : "") + s;
        }

        void DrawWorldLabels(PlayerController pl, float w, float h)
        {
            Camera cam = pl.Cam;
            float cw = Mathf.Max(1f, cam.pixelWidth);
            float ch = Mathf.Max(1f, cam.pixelHeight);
            Vector3 cp = cam.transform.position;
            Vector3 cf = cam.transform.forward;
            for (int i = 0; i < labels.Count; i++)
            {
                WorldLabel l = labels[i];
                // только то, на что смотрит игрок, и только на расстоянии взаимодействия
                Vector3 toG = l.Gaze - cp;
                float dist = toG.magnitude;
                if (dist > l.MaxDist || dist < 0.01f) continue;
                float cos = Vector3.Dot(cf, toG / dist);
                if (cos < l.Cone) continue;
                Vector3 sp = cam.WorldToScreenPoint(l.Pos);
                if (sp.z <= 0.5f) continue;
                float sx = sp.x / cw * w;
                float sy = h - sp.y / ch * h;
                if (sx < -200f || sx > w + 200f || sy < -50f || sy > h + 50f) continue;
                // плавное появление у границы конуса и у границы расстояния
                float a = Mathf.Clamp01((cos - l.Cone) / 0.03f) * Mathf.Clamp01((l.MaxDist - dist) / 0.8f) * l.Col.a;
                float width = Mathf.Max(360f, l.Text.Length * tiny.fontSize * 0.7f);
                float height = Mathf.Max(28f, tiny.lineHeight * 1.25f);
                Text(new Rect(sx - width * 0.5f, sy - height * 0.5f, width, height), l.Text, tiny, new Color(l.Col.r, l.Col.g, l.Col.b, a));
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Player == null || gm.Hero == null)
            {
                labels.Clear();
                return;
            }
            GUI.depth = 0;
            EnsureStyles();

            float w = Screen.width;
            float h = Screen.height;
            Rect full = new Rect(0f, 0f, w, h);
            HeroLight hero = gm.Hero;
            PlayerController pl = gm.Player;
            float life = hero.Life01;
            Color warm = new Color(1f, 0.92f, 0.75f, 1f);

            // --- Виньетка: чем слабее свет, тем уже зрение; в прилив Тьма давит с краёв
            float vig = 0.10f + (1f - life) * 0.85f;
            if (hero.InLastBreath) vig = 1f;
            if (gm.Dir != null && gm.Dir.TideActive) vig = Mathf.Max(vig, 0.5f + 0.12f * Mathf.Sin(Time.unscaledTime * 2f));
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(vig));
            GUI.DrawTexture(full, Gfx.TexVignette, ScaleMode.StretchToFill, true);
            GUI.color = new Color(0f, 0f, 0f, (1f - life) * 0.20f);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = new Color(0.7f, 0.05f, 0.03f, hero.DamageFlash * 0.5f);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.95f, 0.8f, hero.FlashPulse * 0.18f);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            if (pl.Combat != null && pl.Combat.ScreenFlash > 0.01f)
            {
                GUI.color = new Color(1f, 0.97f, 0.84f, pl.Combat.ScreenFlash);
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            if (gm.State == GameState.Title || gm.State == GameState.Intro || gm.State == GameState.Ending || (gm.State == GameState.Playing && gm.Paused))
            {
                labels.Clear();   // главное меню и паузу рисует MenuUI, предысторию и концовку — IntroDirector и EndingDirector; подсказки и сообщения под ними не нужны
                return;
            }

            if (gm.State == GameState.Playing)
            {
                if (GameSettings.Hints) DrawWorldLabels(pl, w, h);

                // Прицел
                GUI.color = new Color(1f, 0.95f, 0.8f, 0.55f);
                GUI.DrawTexture(new Rect(w * 0.5f - 2f, h * 0.5f - 2f, 4f, 4f), Texture2D.whiteTexture);
                if (pl.WakeProgress > 0.01f)
                {
                    GUI.DrawTexture(new Rect(w * 0.5f - 40f, h * 0.5f + 22f, 80f * Mathf.Clamp01(pl.WakeProgress), 4f), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;

                // Подсказки: только для цели в фокусе (на ней светится кольцо)
                float py = h * 0.60f;
                if (!string.IsNullOrEmpty(pl.PromptE))
                {
                    Text(new Rect(w * 0.1f, py, w * 0.8f, h * 0.05f), pl.PromptE, small, new Color(warm.r, warm.g, warm.b, 0.95f));
                    py += h * 0.045f;
                }
                if (!string.IsNullOrEmpty(pl.PromptF))
                {
                    Text(new Rect(w * 0.1f, py, w * 0.8f, h * 0.05f), pl.PromptF, small, new Color(warm.r, warm.g, warm.b, 0.95f));
                }

                // Мысль героя: внизу, курсивом, голубоватым — это не подсказка, а он сам себе
                if (thoughts.Count > 0 && !gm.Paused)
                {
                    Note th = thoughts[0];
                    float ta = Mathf.Clamp01(Mathf.Min(th.Age / 0.8f, (ThoughtTime - th.Age) / 0.9f));
                    GUI.color = new Color(0f, 0f, 0f, 0.30f * ta);
                    GUI.DrawTexture(new Rect(w * 0.14f, h * 0.745f, w * 0.72f, h * 0.085f * GameSettings.HintScale), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    Text(new Rect(w * 0.17f, h * 0.75f, w * 0.66f, h * 0.075f * GameSettings.HintScale), th.Text, thoughtStyle, new Color(0.84f, 0.91f, 1f, ta));
                }

                // Цель: что делать прямо сейчас
                Text(new Rect(w * 0.12f, h * 0.012f, w * 0.76f, h * 0.075f), gm.ObjectiveText(), small, new Color(warm.r, warm.g, warm.b, 0.85f));

                // Скромная статистика в углу (полоска здоровья — на груди героя, смотри вниз)
                int total = Survivor.All.Count;
                string stats = "Со мной: " + gm.AwakeSurvivors() + "    Разбужено: " + gm.SurvivorsWoken + " из " + total;
                if (GuideWisp.CooldownLeft > 0.5f) stats += "\nПроводник: " + Mathf.CeilToInt(GuideWisp.CooldownLeft) + " с";
                if (gm.Endless) stats = "Город " + gm.City + "    Возвращений к очагу: " + gm.ReturnsLeft + "\n" + stats;
                Text(new Rect(w * 0.02f, h * 0.012f, w * 0.3f, h * 0.1f), stats, left, new Color(warm.r, warm.g, warm.b, 0.5f));

                if (gm.Dir != null && gm.Dir.TideActive)
                {
                    Text(new Rect(0f, h * 0.10f, w, h * 0.08f), "ПРИЛИВ ТЬМЫ", mid, new Color(0.75f, 0.6f, 1f, 0.9f));
                }

                // Память о кнопках внизу экрана: только до первого очага (GameManager.ControlHintsVisible), потом гаснет
                if (controlHintsAlpha > 0.01f)
                    Text(new Rect(w * 0.05f, h * 0.955f, w * 0.9f, h * 0.04f),
                        "ЛКМ выстрел/луч   ПКМ вспышка   E вдох   F выдох   G Проводник   Q прикрыть   Tab посмотреть на себя   - / = яркость",
                        tiny, new Color(warm.r, warm.g, warm.b, 0.4f * controlHintsAlpha));

                if (gm.DebugOverlay)
                {
                    string dbg = "FPS " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)) + "\n" +
                        "Свет " + hero.Life.ToString("0.0") + "  аура " + hero.LightRadius.ToString("0.0") + "\n" +
                        "прогресс " + (gm.Dir != null ? gm.Dir.HeroProgress.ToString("0.00") : "-") + "  врагов " + Enemy.All.Count + "\n" +
                        "ламп живых " + gm.LampsLeft() + "  выживших рядом " + gm.AwakeSurvivors() + "\n" +
                        "Свет за забег: +" + gm.Economy.TotalIncome.ToString("0.0") + " / −" + gm.Economy.TotalExpense.ToString("0.0") + "\n" +
                        "За минуту, расход:\n" + gm.Economy.Breakdown(false, true) + "\n" +
                        "F3 свет  F4 очаг  F5 вокзал  F6 Тень  F7 баланс";
                    Text(new Rect(w * 0.62f, h * 0.10f, w * 0.36f, h * 0.65f), dbg, left, new Color(0.6f, 1f, 0.7f, 0.85f));
                }

                if (hero.InLastBreath)
                {
                    Text(new Rect(0f, h * 0.30f, w, h * 0.12f), "ПОСЛЕДНИЙ ВЗДОХ", big, new Color(1f, 0.55f, 0.3f, 1f));
                    Text(new Rect(w * 0.15f, h * 0.43f, w * 0.7f, h * 0.1f),
                        "Спасённые рядом могут раздуть тебя обратно... " + Mathf.CeilToInt(Mathf.Max(0f, hero.LastBreathLeft)),
                        small, warm);
                }

                // пауза: меню рисует MenuUI
            }
            labels.Clear();

            // Сообщения
            float ny = h * 0.17f;
            for (int i = 0; i < notes.Count; i++)
            {
                Note n = notes[i];
                float a = Mathf.Clamp01(Mathf.Min(n.Age * 3f, (4.5f - n.Age) * 2f));
                Text(new Rect(0f, ny, w, h * 0.05f), n.Text, small, new Color(warm.r, warm.g, warm.b, a));
                ny += h * 0.045f;
            }

            if (gm.State == GameState.Dead)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(full, Texture2D.whiteTexture);
                GUI.color = Color.white;
                Text(new Rect(0f, h * 0.34f, w, h * 0.14f), gm.RunOver ? "СВЕТ ПОГАС НАВСЕГДА" : "СВЕТ ПОГАС", big, new Color(1f, 0.5f, 0.3f, 1f));
                if (gm.Endless && gm.RunOver)
                {
                    int cleared = gm.CitiesCleared;
                    Text(new Rect(0f, h * 0.50f, w, h * 0.06f), "Бесконечный город: пройдено городов — " + cleared + ", время " + RunRecords.Clock(gm.PlayTime), mid, warm);
                    Text(new Rect(0f, h * 0.57f, w, h * 0.05f), RankLine(gm), small, warm);
                }
                else if (gm.Endless)
                    Text(new Rect(0f, h * 0.52f, w, h * 0.06f), "Тьма взяла своё. Очаг ещё горит. Возвращений осталось: " + gm.ReturnsLeft, mid, warm);
                else
                    Text(new Rect(0f, h * 0.52f, w, h * 0.06f), "Тьма взяла своё. Очаг ещё горит: вернись к нему.", mid, warm);
            }
            else if (gm.State == GameState.Victory)
            {
                DrawVictory(gm, w, h, full, warm);
            }
        }

        void DrawVictory(GameManager gm, float w, float h, Rect full, Color warm)
        {
            GUI.color = new Color(1f, 0.97f, 0.88f, Mathf.Clamp01(gm.WinFade));
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (gm.WinFade < 0.55f) return;

            textShadow = false;   // тёмный текст на светлом фоне: без тени
            float a = Mathf.Clamp01((gm.WinFade - 0.55f) / 0.35f);
            Color ink = new Color(0.16f, 0.1f, 0.05f, a);
            int boarded = Bus.Instance != null ? Bus.Instance.BoardedCount : 0;
            int total = Survivor.All.Count;
            string verdict;
            if (gm.Endless)
            {
                if (boarded == 0) verdict = "Ты уехал один. Свет остался только в тебе.";
                else if (boarded * 2 < total) verdict = "Автобус уехал. Не все огоньки дошли.";
                else if (boarded < total) verdict = "Почти все добрались. За городом уже рассвет.";
                else verdict = "Все огоньки спасены. За городом уже рассвет.";
            }
            else
            {
                // классика: после ловушки герой просыпается за кухонным столом, и всё случилось только во сне
                if (boarded == 0) verdict = "Во сне ты выбрался один. Наяву — только мокрые штаны.";
                else if (boarded * 2 < total) verdict = "Во сне ты спас не всех. Наяву — только мокрые штаны.";
                else if (boarded < total) verdict = "Во сне ты спас почти всех. Наяву — только мокрые штаны.";
                else verdict = "Во сне ты спас всех. Наяву — только мокрые штаны.";
            }
            int lamps = gm.LampsLeft();
            string city = lamps >= 14 ? "В городе ещё горят огни: он может загореться снова." : "Город погас. Огней почти не осталось.";

            string title = gm.Endless ? "ГОРОД " + gm.City + " ПРОЙДЕН" : "ВСЁ БЫЛО СНОМ";
            string tail = gm.Endless
                ? "Время забега: " + Clock(gm.PlayTime) + "    Возвращений к очагу: " + gm.Deaths + " (осталось " + gm.ReturnsLeft + ")\n"
                  + "Следующий город темнее: больше Теней, быстрее тает свет, больше погасших ламп."
                : "Время: " + Clock(gm.PlayTime) + "    Возвращений к очагу: " + gm.Deaths + "\n" + RankLine(gm);
            // босс последней минуты: был ли он в этом забеге и добили ли его до отъезда автобуса
            string bossLine = gm.Dir != null && gm.Dir.BossSpawned ? (gm.BossesKilled > 0 ? "Великий Гаситель повержен\n" : "Великий Гаситель остался за спиной\n") : "";
            string stats = gm.Endless
                ? "Спасено: " + boarded + " из " + total + "\n" + bossLine + "Огней осталось городу: " + lamps + "\n" + city + "\n" + tail
                : "Спасено во сне: " + boarded + " из " + total + "\n" + bossLine + tail;
            Text(new Rect(0f, h * 0.18f, w, h * 0.14f), title, big, ink);
            Text(new Rect(w * 0.1f, h * 0.36f, w * 0.8f, h * 0.08f), verdict, mid, ink);
            Text(new Rect(w * 0.1f, h * 0.46f, w * 0.8f, h * 0.2f), stats, small, ink);
            textShadow = true;
        }

        /// <summary>Строка про таблицу рекордов под итогом забега: место, «новый рекорд» или почему не засчитано.</summary>
        string RankLine(GameManager gm)
        {
            if (gm.Cheated) return "Рекорд не засчитан: использовались отладочные клавиши.";
            if (!gm.RunCounts) return "Рекорд не засчитан: сложность меняли во время забега.";
            if (gm.LastRecord == null) return "";
            if (gm.LastRank == 1) return "НОВЫЙ РЕКОРД! Первое место в таблице.";
            if (gm.LastRank > 1) return "Таблица рекордов: место " + gm.LastRank + " из " + RunRecords.Keep + ".";
            if (gm.Endless && gm.CitiesCleared < 1) return "Ни одного города не пройдено: в таблицу не попало.";
            return "В десятку лучших не вошло.";
        }
    }
}
