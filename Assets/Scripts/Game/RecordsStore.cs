using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Таблица рекордов на диске: records.txt в папке данных игры (рядом с отчётами света). Ничего не отправляется наружу.
    /// Тесты (GameSmoke) подменяют путь, настоящие рекорды игрока не трогают.
    /// </summary>
    public static class RecordsStore
    {
        /// <summary>Если задан — файл рекордов лежит здесь (для автотестов).</summary>
        public static string PathOverride;

        static RunRecords table;

        public static string FilePath
        {
            get { return !string.IsNullOrEmpty(PathOverride) ? PathOverride : Path.Combine(Application.persistentDataPath, "records.txt"); }
        }

        public static RunRecords Table
        {
            get
            {
                if (table == null) Load();
                return table;
            }
        }

        public static void Load()
        {
            table = new RunRecords();
            try
            {
                string path = FilePath;
                if (File.Exists(path)) table = RunRecords.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PokaGoryu] Could not read records: " + e.GetType().Name);
                table = new RunRecords();
            }
        }

        /// <summary>Записать забег и сохранить файл. Возвращает место в таблице (1 — рекорд) или 0, если в десятку не вошёл.</summary>
        public static int Submit(RunRecord record)
        {
            int rank = Table.Submit(record);
            Save();
            return rank;
        }

        static void Save()
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Table.Serialize(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PokaGoryu] Could not save records: " + e.GetType().Name);
            }
        }

        /// <summary>Забыть таблицу в памяти: при следующем обращении её перечитают из файла (для тестов).</summary>
        public static void Forget()
        {
            table = null;
        }
    }
}
