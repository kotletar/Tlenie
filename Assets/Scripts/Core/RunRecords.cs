using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PokaGoryu
{
    /// <summary>Один завершённый забег в таблице рекордов.</summary>
    public sealed class RunRecord
    {
        public int Mode;          // RunRecords.Classic или RunRecords.Endless
        public int Difficulty;    // Challenge: 0 — мягкая, 1 — обычная, 2 — суровая
        public float Seconds;     // время забега (в бесконечном городе — время всего забега)
        public int Cities;        // классика: 1; бесконечный город: сколько городов пройдено
        public int Returns;       // сколько раз вернулся к очагу
        public int Saved;         // сколько огоньков уехало на автобусе (в бесконечном городе — за все города)
        public string Date;       // ГГГГ-ММ-ДД
    }

    /// <summary>
    /// Таблица рекордов: по десять лучших забегов на каждый режим и каждую сложность. Чистый C# без UnityEngine: порядок, обрезка и
    /// чтение/запись текста проверяются в docs/verification/CoreChecks.cs. Файл читается терпимо: битые строки пропускаются.
    /// Классика: чем меньше время, тем лучше (при равенстве — меньше возвращений к очагу, потом больше спасённых).
    /// Бесконечный город: чем больше пройдено городов, тем лучше (при равенстве — быстрее).
    /// </summary>
    public sealed class RunRecords
    {
        public const int Classic = 0;
        public const int Endless = 1;
        public const int Keep = 10;
        const string Header = "PGREC1";

        readonly List<RunRecord> all = new List<RunRecord>();

        public int Count { get { return all.Count; } }

        /// <summary>true, если a в таблице выше b (обе записи одного режима и сложности).</summary>
        public static bool Better(RunRecord a, RunRecord b)
        {
            if (a.Mode == Endless)
            {
                if (a.Cities != b.Cities) return a.Cities > b.Cities;
                if (Math.Abs(a.Seconds - b.Seconds) > 0.05f) return a.Seconds < b.Seconds;
                return a.Saved > b.Saved;
            }
            if (Math.Abs(a.Seconds - b.Seconds) > 0.05f) return a.Seconds < b.Seconds;
            if (a.Returns != b.Returns) return a.Returns < b.Returns;
            return a.Saved > b.Saved;
        }

        /// <summary>Лучшие записи режима и сложности, от первой к последней (копия списка).</summary>
        public List<RunRecord> Top(int mode, int difficulty)
        {
            List<RunRecord> list = new List<RunRecord>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Mode == mode && all[i].Difficulty == difficulty) list.Add(all[i]);
            }
            // устойчивая вставка: при равных записях более ранняя остаётся выше
            for (int i = 1; i < list.Count; i++)
            {
                RunRecord x = list[i];
                int j = i - 1;
                while (j >= 0 && Better(x, list[j])) { list[j + 1] = list[j]; j--; }
                list[j + 1] = x;
            }
            if (list.Count > Keep) list.RemoveRange(Keep, list.Count - Keep);
            return list;
        }

        public RunRecord Best(int mode, int difficulty)
        {
            List<RunRecord> top = Top(mode, difficulty);
            return top.Count > 0 ? top[0] : null;
        }

        /// <summary>Добавить забег. Возвращает место в таблице (1 — рекорд) или 0, если в десятку не вошёл.</summary>
        public int Submit(RunRecord r)
        {
            if (r == null) return 0;
            if (r.Mode == Endless && r.Cities < 1) return 0;   // без единого пройденного города записывать нечего
            all.Add(r);
            List<RunRecord> top = Top(r.Mode, r.Difficulty);
            int rank = top.IndexOf(r) + 1;
            // в памяти и в файле держим только десятку каждой категории
            all.RemoveAll(x => x.Mode == r.Mode && x.Difficulty == r.Difficulty && !top.Contains(x));
            return rank;
        }

        // ------------------------------------------------------------------ текст

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            for (int mode = 0; mode < 2; mode++)
            {
                for (int diff = 0; diff < 3; diff++)
                {
                    List<RunRecord> top = Top(mode, diff);
                    for (int i = 0; i < top.Count; i++)
                    {
                        RunRecord r = top[i];
                        sb.Append(r.Mode).Append(';').Append(r.Difficulty).Append(';')
                          .Append(r.Seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(';')
                          .Append(r.Cities).Append(';').Append(r.Returns).Append(';').Append(r.Saved).Append(';')
                          .Append(Clean(r.Date)).Append('\n');
                    }
                }
            }
            return sb.ToString();
        }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(';', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        /// <summary>Прочитать таблицу из текста. Чужое, пустое и битое не бросает исключений: такие строки пропускаются.</summary>
        public static RunRecords Parse(string text)
        {
            RunRecords table = new RunRecords();
            if (string.IsNullOrEmpty(text)) return table;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line == Header) continue;
                string[] f = line.Split(';');
                if (f.Length < 7) continue;
                int mode, diff, cities, returns, saved;
                float seconds;
                if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode)) continue;
                if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out diff)) continue;
                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) continue;
                if (!int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out cities)) continue;
                if (!int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out returns)) continue;
                if (!int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out saved)) continue;
                if (mode < 0 || mode > 1 || diff < 0 || diff > 2) continue;
                if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f || seconds > 1e7f) continue;
                if (cities < 0 || cities > 100000 || returns < 0 || returns > 100000 || saved < 0 || saved > 100000) continue;
                RunRecord r = new RunRecord();
                r.Mode = mode; r.Difficulty = diff; r.Seconds = seconds; r.Cities = cities; r.Returns = returns; r.Saved = saved;
                r.Date = f[6].Trim();
                table.all.Add(r);
            }
            // лишнее (файл правили руками) обрезаем теми же правилами
            RunRecords clean = new RunRecords();
            for (int mode = 0; mode < 2; mode++)
            {
                for (int diff = 0; diff < 3; diff++) clean.all.AddRange(table.Top(mode, diff));
            }
            return clean;
        }

        /// <summary>Время «м:сс» (с часами, если забег дольше часа).</summary>
        public static string Clock(float seconds)
        {
            int s = (int)Math.Max(0f, seconds);
            int h = s / 3600;
            int m = (s / 60) % 60;
            int ss = s % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + ss.ToString("00") : m + ":" + ss.ToString("00");
        }
    }
}
