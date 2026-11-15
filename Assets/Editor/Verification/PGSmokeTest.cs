#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PokaGoryu.EditorTools
{
    /// <summary>Рисует IMGUI в свою текстуру (чтобы увидеть меню без окна): OnGUI этого объекта идёт первым и переключает цель.</summary>
    public class GuiCapture : MonoBehaviour
    {
        public RenderTexture target;
        public bool capture;
        public int repaintCalls;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            repaintCalls++;
            if (!capture || target == null) return;
            GUI.depth = 1000;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0.10f, 0.12f, 0.18f, 1f));
        }
    }

    /// <summary>
    /// Постоянный редакторский дымовой тест: Unity -batchmode -executeMethod PokaGoryu.EditorTools.PGSmokeTest.Run.
    /// Входит в Play Mode без окна, ведёт игру, телепортируется по городу, снимает кадры в PNG и считает ошибки в логе.
    /// Сценарий выбирается переменной окружения PG_SMOKE_SCENARIO (hands / city / menu).
    /// </summary>
    [InitializeOnLoad]
    public static class PGSmokeTest
    {
        const string Flag = "PG_SMOKE_ACTIVE";
        static bool hooked;
        static int frame;
        static int lastFrameCount = -1;
        static int errors;
        static int editorNoise;   // сбои самого редактора (индексатор Search при первом открытии свежего проекта): к игре не относятся
        static int warnings;
        static bool finished;
        static double startedAt;
        static string outDir;
        static string scenario;
        static readonly StringBuilder report = new StringBuilder();
        static readonly List<string> seenWarn = new List<string>();

        static PGSmokeTest()
        {
            if (SessionState.GetBool(Flag, false)) Hook();
        }

        public static void Run()
        {
            SessionState.SetBool(Flag, true);
            Hook();
            if (scenario == "menushot" || scenario == "introshot" || scenario == "thoughts")
            {
                // окно редактора с видом Game: только так рисуется IMGUI (в batchmode OnGUI не вызывается)
                try { EditorApplication.ExecuteMenuItem("Window/General/Game"); } catch (Exception) { }
            }
            EditorApplication.EnterPlaymode();
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            outDir = Environment.GetEnvironmentVariable("PG_SMOKE_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetTempPath(), "pg_smoke");
            scenario = Environment.GetEnvironmentVariable("PG_SMOKE_SCENARIO");
            if (string.IsNullOrEmpty(scenario)) scenario = "hands";
            Directory.CreateDirectory(outDir);
            startedAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string cond, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // на первом открытии свежего проекта (чистая распаковка архива) индексатор Unity Search может бросить
                // ArgumentOutOfRangeException: это внутренний сбой редактора, не ошибка игры
                if (stack != null && stack.Contains("UnityEditor.Search.")) { editorNoise++; return; }
                errors++;
                if (errors <= 25) report.AppendLine("[" + type + "] " + cond + "\n" + stack);
            }
            else if (type == LogType.Warning)
            {
                warnings++;
                if (seenWarn.Count < 25 && !seenWarn.Contains(cond))
                {
                    seenWarn.Add(cond);
                    report.AppendLine("[Warning] " + cond);
                }
            }
        }

        static void Tick()
        {
            if (finished) return;
            double elapsed = EditorApplication.timeSinceStartup - startedAt;
            if (elapsed > 200.0)
            {
                Finish("TIMEOUT (frame " + frame + ")", 3);
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                if (elapsed > 60.0) Finish("GameManager never appeared", 4);
                return;
            }
            if (Time.frameCount == lastFrameCount) return;   // шаги сценария — по кадрам игры, а не по обновлениям редактора
            lastFrameCount = Time.frameCount;
            frame++;
            if (frame == 1) Time.captureFramerate = 60;   // детерминированное время: каждый кадр ровно 1/60 с
            try
            {
                if (scenario == "city") StepCity(gm);
                else if (scenario == "menu") StepMenu(gm);
                else if (scenario == "menushot") StepMenuShot(gm);
                else if (scenario == "introshot") StepIntroShot(gm);
                else if (scenario == "intro3d") StepIntro3D(gm);
                else if (scenario == "views") StepViews(gm);
                else if (scenario == "introskip") StepIntroSkip(gm);
                else if (scenario == "chest") StepChest(gm);
                else if (scenario == "arm") StepArm(gm);
                else if (scenario == "walk") StepWalk(gm);
                else if (scenario == "shopwalk") StepShopWalk(gm);
                else if (scenario == "aerial") StepAerial(gm);
                else if (scenario == "thoughts") StepThoughts(gm);
                else if (scenario == "aerialskip") StepAerialSkip(gm);
                else if (scenario == "follow") StepFollow(gm);
                else if (scenario == "busfight") StepBusFight(gm);
                else if (scenario == "finalstand") StepFinalStand(gm, true);
                else if (scenario == "finalpassive") StepFinalStand(gm, false);
                else if (scenario == "econ") StepEcon(gm);
                else if (scenario == "reson") StepReson(gm);
                else if (scenario == "pizza") StepPizza(gm);
                else if (scenario == "durable") StepDurable(gm);
                else if (scenario == "layout") StepLayout(gm);
                else if (scenario == "vault") StepVault(gm);
                else if (scenario == "ending") StepEnding(gm, false);
                else if (scenario == "endingshot") StepEnding(gm, true);
                else if (scenario == "stats") StepStats(gm);
                else if (scenario == "enemies") StepEnemies(gm);
                else if (scenario == "close") StepClose(gm);
                else if (scenario == "bar") StepBar(gm);
                else StepHands(gm);
            }
            catch (Exception e)
            {
                errors++;
                report.AppendLine("[EXCEPTION in smoke step " + frame + "] " + e);
            }
        }

        // ------------------------------------------------------------------ сценарии

        static void StepHands(GameManager gm)
        {
            PlayerController pc = gm.Player;
            PalmShot ps = pc.Combat;
            switch (frame)
            {
                case 2:
                    report.AppendLine("lamps=" + Lamp.All.Count + " survivors=" + Survivor.All.Count + " growths=" + Growth.All.Count
                        + " cameras=" + Camera.allCamerasCount + " gfx=" + SystemInfo.graphicsDeviceType);
                    break;
                case 10: gm.State = GameState.Playing; break;
                case 60: Capture("h01_ready"); break;
                case 61: ps.FireShot(); break;
                case 72: Capture("h02_shot"); break;
                case 73: report.AppendLine("beams active: " + CountActive(ps.transform, "Beam") + " orbs active: " + CountActive(ps.transform, "Orb")); break;
                case 100: ps.ForceHold = true; ps.FireShot(); break;
                case 165: Capture("h03_hold"); break;
                case 166: report.AppendLine("hold: beams active " + CountActive(ps.transform, "Beam")); ps.ForceHold = false; break;
                case 200: ps.FireFlash(); break;
                case 218: Capture("h04_flash"); break;
                case 260: pc.Body.DebugPose = 3; break;
                case 285: Capture("h05_inhale"); break;
                case 286: pc.Body.DebugPose = 2; break;
                case 311: Capture("h06_exhale"); break;
                case 312: pc.Body.DebugPose = 1; break;
                case 337: Capture("h07_cover"); break;
                case 338: pc.Body.DebugPose = 0; SetPitch(pc, -40f); break;
                case 365: Capture("h08_lookup"); break;
                case 366: SetPitch(pc, 70f); break;
                case 395: Capture("h09_lookdown"); break;
                case 396: SetPitch(pc, 0f); pc.TeleportTo(new Vector3(105f, 0.1f, 168f), 90f); break;
                case 420:
                    for (int i = 0; i < Growth.All.Count; i++) report.AppendLine("growth " + i + " hp before: " + Growth.All[i].Health.ToString("F0") + " dead=" + Growth.All[i].Dead);
                    ps.ForceHold = true;
                    ps.FireShot();
                    break;
                case 520:
                    for (int i = 0; i < Growth.All.Count; i++) report.AppendLine("growth " + i + " hp after: " + Growth.All[i].Health.ToString("F0") + " dead=" + Growth.All[i].Dead);
                    ps.ForceHold = false;
                    report.AppendLine("hero life now=" + gm.Hero.Life.ToString("F1"));
                    break;
                case 560:
                    Finish("OK", errors > 0 ? 2 : 0);
                    break;
            }
        }

        static void StepCity(GameManager gm)
        {
            PlayerController pc = gm.Player;
            switch (frame)
            {
                case 2:
                    report.AppendLine("lamps=" + Lamp.All.Count + " survivors=" + Survivor.All.Count + " growths=" + Growth.All.Count
                        + " cameras=" + Camera.allCamerasCount + " gfx=" + SystemInfo.graphicsDeviceType);
                    break;
                case 10: gm.State = GameState.Playing; break;
                case 70: Capture("c01_start"); break;
                case 71: pc.TeleportTo(new Vector3(105f, 0.1f, 168f), 90f); break;
                case 100: Capture("c02_wall"); break;
                case 101: pc.TeleportTo(new Vector3(171f, 0.1f, 225f), 0f); break;
                case 130: Capture("c03_alley"); break;
                case 131: pc.TeleportTo(new Vector3(204f, 0.1f, 215f), 0f); break;
                case 160: Capture("c04_tunnel"); break;
                case 161: CallPrivate(gm, "DebugNextHearth"); break;
                case 190: Capture("c05_hearth"); break;
                case 191: pc.TeleportTo(new Vector3(66f, 0.1f, 322f), 0f); break;
                case 220: Capture("c06_approach"); break;
                case 221:
                    {
                        CallPrivate(gm, "DebugGoToStation");
                        Vector3 bp = LevelBuilder.BusPos;
                        Vector3 from = new Vector3(bp.x - 9f, 0.1f, bp.z - 9f);
                        Vector3 d2 = bp - from;
                        pc.TeleportTo(from, Mathf.Atan2(d2.x, d2.z) * Mathf.Rad2Deg);
                    }
                    break;
                case 255: Capture("c07_bus"); break;
                case 256: pc.TeleportTo(new Vector3(33f, 0.1f, 60f), 0f); break;
                case 285: Capture("c08_street2"); break;
                case 320:
                    report.AppendLine("final: state=" + gm.State + " hero.life=" + gm.Hero.Life.ToString("F1"));
                    Finish("OK", errors > 0 ? 2 : 0);
                    break;
            }
        }

        // ------------------------------------------------------------------ крупные планы моделей

        static readonly List<Vector3> closeTargets = new List<Vector3>();
        static readonly List<string> closeNames = new List<string>();
        static readonly List<float> closeDist = new List<float>();
        static readonly List<float> closePitch = new List<float>();
        static readonly List<float> closeAngle = new List<float>();
        static int closeIdx;

        static Vector3 FindObjectPos(string name, int skip)
        {
            int seen = 0;
            Collider[] cols = UnityEngine.Object.FindObjectsByType<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].gameObject.name != name) continue;
                if (seen++ < skip) continue;
                return cols[i].bounds.center;
            }
            return Vector3.zero;
        }

        static void AddClose(string name, Vector3 target, float dist, float pitch, float angleDeg)
        {
            closeNames.Add(name);
            closeTargets.Add(target);
            closeDist.Add(dist);
            closePitch.Add(pitch);
            closeAngle.Add(angleDeg);
        }

        /// <summary>Точка для камеры на дороге на расстоянии dist от цели с чистой линией взгляда: перебираем углы от желаемого.</summary>
        static Vector3 PickView(Vector3 tgt, float dist, float angDeg)
        {
            for (int k = 0; k < 24; k++)
            {
                float off = ((k + 1) / 2) * 15f * (k % 2 == 0 ? 1f : -1f);
                float a = (angDeg + off) * Mathf.Deg2Rad;
                Vector3 p = tgt + new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
                p.y = 0.1f;
                Vector3 e = Vector3.Lerp(p, tgt, 0.85f);
                if (CityMap.CanStand(p.x, p.z, 0.5f, true) && CityMap.LineClear(p.x, p.z, e.x, e.z, 0.3f, true)) return p;
            }
            return new Vector3(tgt.x, 0.1f, tgt.z - dist);
        }

        static void StepClose(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 10)
            {
                gm.State = GameState.Playing;
                gm.Hero.Life = 100f;
                // цели: лампа, очаг, человек, генератор, машина, бак, дерево, скамья, остановка
                Lamp lamp = null, hearth = null;
                for (int i = 0; i < Lamp.All.Count; i++)
                {
                    Lamp l = Lamp.All[i];
                    if (hearth == null && l.IsHearth) hearth = l;
                    if (lamp == null && !l.IsHearth && !l.IsDecoy && l.Charge > 0.9f && l.BasePos.z > 150f && l.BasePos.z < 260f) lamp = l;
                }
                if (lamp != null) AddClose("k01_lamp", lamp.BasePos + new Vector3(0f, 4.2f, 0f), 7f, -18f, 35f);
                if (hearth != null) AddClose("k02_hearth", hearth.BasePos + new Vector3(0f, 0.9f, 0f), 3.2f, 15f, 20f);
                if (Survivor.All.Count > 0) AddClose("k03_survivor", Survivor.All[0].transform.position + new Vector3(0f, 1.0f, 0f), 3.0f, 0f, 25f);
                if (Generator.All.Count > 0) AddClose("k04_generator", Generator.All[0].BasePos + new Vector3(0f, 1.0f, 0f), 4.0f, 10f, 30f);
                Vector3 car = FindObjectPos("Car", 3);
                if (car != Vector3.zero) AddClose("k05_car", car, 6.5f, 8f, 40f);
                Vector3 dump = FindObjectPos("Dumpster", 0);
                if (dump != Vector3.zero) AddClose("k06_dumpster", dump, 4.0f, 8f, 30f);
                Vector3 tree = FindObjectPos("Tree", 0);
                if (tree != Vector3.zero) AddClose("k07_tree", tree + new Vector3(0f, 1.2f, 0f), 7f, -8f, 20f);
                Vector3 bench = FindObjectPos("Bench", 0);
                if (bench != Vector3.zero) AddClose("k08_bench", bench, 4.5f, 10f, 25f);
                Vector3 shelter = FindObjectPos("Shelter", 0);
                if (shelter != Vector3.zero) AddClose("k09_shelter", shelter, 8f, 4f, 30f);
                Vector3 crate = FindObjectPos("Crate", 0);
                if (crate != Vector3.zero) AddClose("k10_crate", crate, 3.5f, 12f, 30f);
                report.AppendLine("close-up targets: " + closeNames.Count);
            }
            // каждые 70 кадров очередной план; враги — отдельно в конце
            int slot = (frame - 11) / 70;
            int phase = (frame - 11) % 70;
            if (frame > 10 && slot < closeNames.Count)
            {
                if (phase == 0)
                {
                    Vector3 tgt = closeTargets[slot];
                    Vector3 pos = PickView(tgt, closeDist[slot], closeAngle[slot]);
                    Vector3 d = tgt - pos;
                    float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    pc.TeleportTo(pos, yaw);
                    SetPitch(pc, closePitch[slot]);
                }
                if (phase == 40) Capture(closeNames[slot]);
            }
            int enemyStart = 11 + closeNames.Count * 70;
            if (frame == enemyStart)
            {
                pc.TeleportTo(new Vector3(171f, 0.1f, 225f), 0f);
                SetPitch(pc, 0f);
                gm.Hero.Life = 100f;
                gm.Hero.AuraTarget = 2f;
                Enemy.Spawn(EnemyKind.Shade, new Vector3(168.5f, 0f, 233f));
                Enemy.Spawn(EnemyKind.Snuffer, new Vector3(173.5f, 0f, 236f));
                Enemy.Spawn(EnemyKind.Eater, new Vector3(171f, 0f, 252f));
            }
            if (frame >= enemyStart && frame < enemyStart + 90)
            {
                for (int i = 0; i < Enemy.All.Count; i++) Enemy.All[i].Stun(5f);   // стоят на месте для кадра
                gm.Hero.Life = 100f;
            }
            if (frame == enemyStart + 80) Capture("k11_enemies");
            if (frame == enemyStart + 120) Finish("OK", errors > 0 ? 2 : 0);
        }

        // ------------------------------------------------------------------ полоска здоровья «Перья»

        static void BarVisible(GameManager gm, int w, int h)
        {
            FeatherHealthBar bar = gm.Bar;
            if (bar == null) return;
            BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FeatherHealthBar).GetField("lastDrawFrame", f).SetValue(bar, Time.frameCount);
            typeof(FeatherHealthBar).GetField("wantW", f).SetValue(bar, w);
            typeof(FeatherHealthBar).GetField("wantH", f).SetValue(bar, h);
            typeof(FeatherHealthBar).GetField("renderT", f).SetValue(bar, 1f);   // рендерить каждый кадр
        }

        static void BarSet(GameManager gm, float life, bool pixel)
        {
            gm.Hero.Life = life;
            gm.Bar.pixelMode = pixel;
            gm.Bar.SetHealth(life);
            gm.Bar.SetTone(life < 25f ? 2 : 0);
            typeof(FeatherHealthBar).GetField("shown", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(gm.Bar, life);
        }

        static void SaveBar(GameManager gm, string name, int scale)
        {
            RenderTexture rt = gm.Bar.Texture;
            if (rt == null) { report.AppendLine("[smoke] bar texture is null"); return; }
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            // поверх тёмно-синего фона, как в игре (HUD рисуется с альфа-смешиванием), увеличено в scale раз
            Color32[] src = tex.GetPixels32();
            Texture2D outTex = new Texture2D(rt.width * scale, rt.height * scale, TextureFormat.RGB24, false);
            Color32[] dst = new Color32[outTex.width * outTex.height];
            Color bg = new Color(0.10f, 0.12f, 0.18f, 1f);
            for (int y = 0; y < outTex.height; y++)
            {
                for (int x = 0; x < outTex.width; x++)
                {
                    Color32 s = src[(y / scale) * rt.width + (x / scale)];
                    float a = s.a / 255f;
                    float r = s.r / 255f * a + bg.r * (1f - a);
                    float g = s.g / 255f * a + bg.g * (1f - a);
                    float b = s.b / 255f * a + bg.b * (1f - a);
                    dst[y * outTex.width + x] = new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), 255);
                }
            }
            outTex.SetPixels32(dst);
            outTex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), outTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(outTex);
            report.AppendLine("bar " + name + " rt=" + rt.width + "x" + rt.height + " life=" + gm.Hero.Life.ToString("F0"));
        }

        static void StepBar(GameManager gm)
        {
            if (frame == 10) gm.State = GameState.Playing;
            if (frame >= 12 && gm.Bar != null && frame < 400) BarVisible(gm, 360, 150);
            switch (frame)
            {
                case 14: BarSet(gm, 100f, true); break;
                case 40: SaveBar(gm, "b01_100_pixel", 4); break;
                case 41: BarSet(gm, 70f, true); break;
                case 70: SaveBar(gm, "b02_70_pixel", 4); break;
                case 71: BarSet(gm, 40f, true); break;
                case 100: SaveBar(gm, "b03_40_pixel", 4); break;
                case 101: BarSet(gm, 15f, true); break;
                case 130: SaveBar(gm, "b04_15_pixel", 4); break;
                case 131: BarSet(gm, 70f, false); break;
                case 170: SaveBar(gm, "b05_70_smooth", 2); break;
                case 171: BarSet(gm, 100f, false); break;
                case 200: SaveBar(gm, "b06_100_smooth", 2); break;
                case 260:
                    {
                        // стоимость: 120 кадров обновления и рендера полоски
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        FeatherHealthBar bar = gm.Bar;
                        MethodInfo lu = typeof(FeatherHealthBar).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                        for (int i = 0; i < 120; i++)
                        {
                            BarVisible(gm, 360, 150);
                            lu.Invoke(bar, null);
                        }
                        sw.Stop();
                        report.AppendLine("bar update+render x120: " + sw.ElapsedMilliseconds + " ms (" + (sw.ElapsedMilliseconds / 120.0).ToString("F2") + " ms each)");
                    }
                    break;
                case 300: Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        // ------------------------------------------------------------------ враги не проходят сквозь стены

        static int navViolations;
        static int navChecks;
        static float navMinHero = 999f;
        static int navSpawned;
        static System.Random navRng;

        static void StepEnemies(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 10)
            {
                gm.State = GameState.Playing;
                pc.TeleportTo(new Vector3(171f, 0.1f, 225f), 0f);
                gm.Hero.Life = 100f;
                navRng = new System.Random(12345);
            }
            if (frame == 20)
            {
                // враги за домами: клетки дорог на расстоянии 20-55 м от героя, до которых нет прямой видимости
                Vector3 hp = pc.transform.position;
                int kind = 0;
                for (int tries = 0; tries < 4000 && navSpawned < 14; tries++)
                {
                    int cx = navRng.Next(CityMap.W);
                    int cz = navRng.Next(CityMap.H);
                    if (!CityMap.IsRoadCell(cx, cz) || CityMap.Blocked[cx, cz]) continue;
                    Pt c = CityMap.CenterOf(cx, cz);
                    float dx = c.X - hp.x, dz = c.Z - hp.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < 20f || d > 55f) continue;
                    if (CityMap.LineClear(hp.x, hp.z, c.X, c.Z, 0f, false)) continue;   // нужен дом между ними
                    EnemyKind k = (kind % 5 == 4) ? EnemyKind.Eater : ((kind % 2 == 0) ? EnemyKind.Shade : EnemyKind.Snuffer);
                    kind++;
                    Enemy.Spawn(k, new Vector3(c.X, 0f, c.Z));
                    navSpawned++;
                }
                report.AppendLine("spawned behind buildings: " + navSpawned + " enemies, hero at (" + hp.x.ToString("F0") + "," + hp.z.ToString("F0") + ")");
            }
            if (frame > 20)
            {
                for (int i = 0; i < Enemy.All.Count; i++)
                {
                    Vector3 p = Enemy.All[i].transform.position;
                    navChecks++;
                    if (!CityMap.IsRoad(p.x, p.z, true))
                    {
                        navViolations++;
                        if (navViolations <= 5) report.AppendLine("VIOLATION frame " + frame + ": " + Enemy.All[i].Kind + " at (" + p.x.ToString("F1") + "," + p.z.ToString("F1") + ")");
                    }
                    float hd = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(pc.transform.position.x, pc.transform.position.z));
                    if (hd < navMinHero) navMinHero = hd;
                }
                if (frame % 400 == 0)
                {
                    report.AppendLine("frame " + frame + ": enemies=" + Enemy.All.Count + " checks=" + navChecks + " violations=" + navViolations
                        + " closestToHero=" + navMinHero.ToString("F1") + " heroLife=" + gm.Hero.Life.ToString("F0"));
                }
                // герой не гибнет: тест про стены, а не про баланс
                if (gm.Hero.Life < 40f) gm.Hero.Life = 100f;
            }
            if (frame == 2400)
            {
                report.AppendLine("RESULT: enemy position checks=" + navChecks + ", inside buildings/walls=" + navViolations);
                Finish(navViolations == 0 ? "OK" : "VIOLATIONS", (navViolations == 0 && errors == 0) ? 0 : 2);
            }
        }

        // ------------------------------------------------------------------ статистика (оптимизация)

        static void StepStats(GameManager gm)
        {
            PlayerController pc = gm.Player;
            switch (frame)
            {
                case 10: gm.State = GameState.Playing; break;
                case 40: Stats("start", gm); break;
                case 41: pc.TeleportTo(new Vector3(105f, 0.1f, 168f), 90f); break;
                case 70: Stats("wall", gm); break;
                case 71: pc.TeleportTo(new Vector3(66f, 0.1f, 322f), 0f); break;
                case 100: Stats("approach", gm); break;
                case 101:
                    {
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        gm.QuitToMenu();
                        sw.Stop();
                        report.AppendLine("world rebuild (BuildWorld): " + sw.ElapsedMilliseconds + " ms");
                    }
                    break;
                case 130:
                    {
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        MethodInfo mi = typeof(Gfx).GetMethod("BuildTextures", BindingFlags.Static | BindingFlags.NonPublic);
                        if (mi != null) mi.Invoke(null, null);
                        sw.Stop();
                        report.AppendLine("texture generation (Gfx.BuildTextures): " + sw.ElapsedMilliseconds + " ms");
                    }
                    break;
                case 160: Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        static void Stats(string label, GameManager gm)
        {
            Camera cam = gm.Player.Cam;
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>();
            int active = 0, meshR = 0, spriteR = 0, otherR = 0;
            long tris = 0, verts = 0;
            HashSet<Material> mats = new HashSet<Material>();
            HashSet<Shader> shaders = new HashSet<Shader>();
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int visible = 0;
            long visTris = 0;
            HashSet<Material> visMats = new HashSet<Material>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.gameObject.layer == 30 || r.gameObject.layer == 31) continue;
                active++;
                long t = 0, v = 0;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (r is MeshRenderer && mf != null && mf.sharedMesh != null)
                {
                    meshR++;
                    t = mf.sharedMesh.GetIndexCount(0) / 3;
                    v = mf.sharedMesh.vertexCount;
                }
                else if (r is SpriteRenderer) spriteR++;
                else otherR++;
                tris += t;
                verts += v;
                Material m = r.sharedMaterial;
                if (m != null)
                {
                    mats.Add(m);
                    if (m.shader != null) shaders.Add(m.shader);
                }
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds))
                {
                    visible++;
                    visTris += t;
                    if (m != null) visMats.Add(m);
                }
            }
            long texBytes = 0;
            int texCount = 0;
            Texture2D[] texs = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < texs.Length; i++)
            {
                if (AssetDatabase.GetAssetPath(texs[i]).Length > 0) continue;
                if (texs[i].hideFlags != HideFlags.None) continue;
                texBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texs[i]);
                texCount++;
            }

            // стоимость кадра: 60 рендеров камеры в 640x360 + синхронизация чтением пикселя
            RenderTexture rt = new RenderTexture(640, 360, 24);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) cam.Render();
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = old;
            sw.Stop();
            cam.targetTexture = prev;
            UnityEngine.Object.DestroyImmediate(px);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            // та же сцена в крошечный кадр 160x90: почти чистая стоимость CPU (подготовка и отправка draw call'ов)
            RenderTexture rtS = new RenderTexture(160, 90, 24);
            cam.targetTexture = rtS;
            cam.Render();
            System.Diagnostics.Stopwatch sw2 = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) cam.Render();
            RenderTexture.active = rtS;
            Texture2D px2 = new Texture2D(1, 1, TextureFormat.RGB24, false);
            px2.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = old;
            sw2.Stop();
            cam.targetTexture = prev;
            UnityEngine.Object.DestroyImmediate(px2);
            rtS.Release();
            UnityEngine.Object.DestroyImmediate(rtS);
            report.AppendLine("[" + label + "] small frame 160x90 x60: " + (sw2.ElapsedMilliseconds / 60.0).ToString("F2") + " ms/frame (CPU-heavy part)");

            report.AppendLine("[" + label + "] renderers active=" + active + " (mesh " + meshR + ", sprite " + spriteR + ", other " + otherR + ")"
                + " materials=" + mats.Count + " shaders=" + shaders.Count
                + " | scene tris=" + tris + " verts=" + verts
                + " | in frustum: renderers=" + visible + " tris=" + visTris + " materials=" + visMats.Count
                + " | runtime textures=" + texCount + " " + (texBytes / 1024) + " KB"
                + " | camera.Render x60: " + sw.ElapsedMilliseconds + " ms (" + (sw.ElapsedMilliseconds / 60.0).ToString("F2") + " ms/frame)");
        }

        static GuiCapture guiCap;

        static void StepMenu(GameManager gm)
        {
            switch (frame)
            {
                case 2:
                    report.AppendLine("state=" + gm.State + " cameras=" + Camera.allCamerasCount + " screen=" + Screen.width + "x" + Screen.height);
                    break;
                case 3:
                    {
                        GameObject go = new GameObject("GuiCapture");
                        guiCap = go.AddComponent<GuiCapture>();
                        guiCap.target = new RenderTexture(Screen.width, Screen.height, 24);
                    }
                    break;
                // главное меню
                case 20: guiCap.capture = true; break;
                case 22: SaveGui("m01_title"); report.AppendLine("repaints so far: " + guiCap.repaintCalls); break;
                // как играть
                case 23: SetMenuPage(gm, 1); break;
                case 40: guiCap.capture = true; break;
                case 42: SaveGui("m02_help"); break;
                // настройки
                case 43: SetMenuPage(gm, 2); break;
                case 44:
                    GameSettings.Volume = 0.3f;
                    GameSettings.Apply();
                    report.AppendLine("volume set 0.3 -> AudioListener.volume=" + AudioListener.volume.ToString("F2"));
                    GameSettings.Volume = 0.8f;
                    GameSettings.Apply();
                    break;
                case 60: guiCap.capture = true; break;
                case 62: SaveGui("m03_settings"); break;
                // пауза
                case 63:
                    SetMenuPage(gm, 0);
                    gm.StartGame();
                    report.AppendLine("StartGame -> state=" + gm.State);
                    break;
                case 70: gm.SetPaused(true); report.AppendLine("paused: timeScale=" + Time.timeScale); break;
                case 85: guiCap.capture = true; break;
                case 87: SaveGui("m04_pause"); break;
                case 88: SetMenuPage(gm, 2); break;
                case 100: guiCap.capture = true; break;
                case 102: SaveGui("m05_pause_settings"); break;
                case 103: SetMenuPage(gm, 0); break;
                case 104: MenuActivate(gm, 4); break;     // «В главное меню»
                case 110: report.AppendLine("after QuitToMenu: state=" + gm.State + " paused=" + gm.Paused + " timeScale=" + Time.timeScale); break;
                case 111: MenuActivate(gm, 0); break;     // «Играть»
                case 115: report.AppendLine("after Play: state=" + gm.State); break;
                // смерть и победа
                case 116: gm.State = GameState.Dead; break;
                case 135: guiCap.capture = true; break;
                case 137: SaveGui("m06_dead"); break;
                case 138: gm.State = GameState.Victory; gm.WinFade = 1f; break;
                case 160: guiCap.capture = true; break;
                case 162: SaveGui("m07_victory"); break;
                case 170:
                    Finish("OK", errors > 0 ? 2 : 0);
                    break;
            }
        }

        /// <summary>Окно редактора (не batchmode): снимки Game view вместе с IMGUI через ScreenCapture.</summary>
        static void StepMenuShot(GameManager gm)
        {
            switch (frame)
            {
                case 2:
                    report.AppendLine("state=" + gm.State + " screen=" + Screen.width + "x" + Screen.height + " focusedWindow=" + (EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().Name : "none"));
                    break;
                case 40: Shoot("w01_title"); break;
                case 55: SetMenuPage(gm, 1); break;
                case 80: Shoot("w02_help"); break;
                case 95: SetMenuPage(gm, 2); break;
                case 120: Shoot("w03_settings"); break;
                case 135: SetMenuPage(gm, 0); gm.State = GameState.Playing; break;   // мимо предыстории: нужна пауза в игре
                case 145: gm.SetPaused(true); break;
                case 175: Shoot("w04_pause"); break;
                case 190: SetMenuPage(gm, 2); break;
                case 215: Shoot("w05_pause_settings"); break;
                case 230: SetMenuPage(gm, 0); gm.SetPaused(false); gm.State = GameState.Dead; break;
                case 260: Shoot("w06_dead"); break;
                case 275: gm.State = GameState.Victory; gm.WinFade = 1f; break;
                case 305: Shoot("w07_victory"); break;

                // таблица рекордов (образцы пишем в отдельный файл: настоящие рекорды не трогаем), экраны бесконечного города
                case 320:
                    RecordsStore.PathOverride = Path.Combine(outDir, "records_sample.txt");
                    if (File.Exists(RecordsStore.PathOverride)) File.Delete(RecordsStore.PathOverride);
                    RecordsStore.Forget();
                    float[] secs = { 1021f, 1184f, 1305f, 1490f, 1722f };
                    for (int i = 0; i < secs.Length; i++) RecordsStore.Submit(SampleRecord(RunRecords.Classic, 1, secs[i], 1, i, 9 - i));
                    int[] cities = { 4, 3, 3, 1 };
                    float[] ctimes = { 4100f, 3200f, 3550f, 900f };
                    for (int i = 0; i < cities.Length; i++) RecordsStore.Submit(SampleRecord(RunRecords.Endless, 1, ctimes[i], cities[i], 2, 14 + i));
                    gm.Endless = false;
                    gm.State = GameState.Title;
                    break;
                case 330: SetMenuPage(gm, 4); break;
                case 360: Shoot("w08_records_classic"); break;
                case 370: typeof(MenuUI).GetField("recordsMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(gm.Menu, 1); break;
                case 395: Shoot("w09_records_endless"); break;
                case 405: SetMenuPage(gm, 0); gm.Endless = true; gm.City = 2; gm.ReturnsLeft = 4; gm.State = GameState.Victory; gm.WinFade = 1f; break;
                case 435: Shoot("w10_endless_victory"); break;
                case 445: gm.State = GameState.Dead; gm.ReturnsLeft = 2; gm.RunOver = false; break;
                case 475: Shoot("w11_endless_dead"); break;
                case 485: gm.ReturnsLeft = 0; gm.RunOver = true; gm.LastRecord = SampleRecord(RunRecords.Endless, 1, 3200f, 1, 2, 7); gm.LastRank = 2; break;
                case 515: Shoot("w12_endless_over"); break;
                case 525: gm.Endless = false; gm.RunOver = false; gm.State = GameState.Victory; gm.LastRecord = SampleRecord(RunRecords.Classic, 1, 990f, 1, 0, 10); gm.LastRank = 1; break;
                case 555: Shoot("w13_victory_record"); break;
                case 570: Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        static RunRecord SampleRecord(int mode, int difficulty, float seconds, int cities, int returns, int saved)
        {
            RunRecord r = new RunRecord();
            r.Mode = mode; r.Difficulty = difficulty; r.Seconds = seconds; r.Cities = cities; r.Returns = returns; r.Saved = saved;
            r.Date = "2026-11-15";
            return r;
        }

        // ------------------------------------------------------------------ предыстория, грудь, расстановка

        static float chestLife = -1f;

        /// <summary>Окно редактора: предыстория как в игре, с субтитрами и затемнением (IMGUI + Game view).</summary>
        static void StepIntroShot(GameManager gm)
        {
            float[] times = { 2.5f, 6.5f, 8.8f, 11.2f, 14.0f, 17.8f, 22.5f, 26.5f, 28.4f, 30.5f, 34.5f, 39.5f, 45.0f, 50.0f };
            switch (frame)
            {
                case 2: report.AppendLine("state=" + gm.State + " screen=" + Screen.width + "x" + Screen.height); break;
                case 10: SetMenuPage(gm, 0); gm.StartGame(); report.AppendLine("after StartGame: state=" + gm.State + " intro active=" + gm.Intro.Active); break;
            }
            if (frame < 14) return;
            int slot = (frame - 14) / 45;
            int phase = (frame - 14) % 45;
            if (slot < times.Length)
            {
                if (phase == 0) gm.Intro.DebugSeek(times[slot]);
                if (phase == 40) Shoot("i" + slot.ToString("00") + "_" + gm.Intro.Elapsed.ToString("F1"));
            }
            else if (frame == 14 + times.Length * 45 + 30)
            {
                report.AppendLine("end state=" + gm.State + " intro active=" + gm.Intro.Active);
                Finish("OK", errors > 0 ? 2 : 0);
            }
        }

        /// <summary>Без окна: кадры предыстории (3D без IMGUI) и переход в игру в конце.</summary>
        static void StepIntro3D(GameManager gm)
        {
            float[] times = { 0.5f, 2.5f, 3.6f, 6.5f, 9.4f, 10.9f, 11.9f, 13.4f, 14.6f, 16.0f, 17.4f, 18.8f, 20.2f, 21.9f, 25.5f, 27.8f, 28.3f, 28.7f, 29.6f, 31.0f, 35.0f, 40.0f, 45.0f };
            switch (frame)
            {
                case 2: report.AppendLine("state=" + gm.State); break;
                case 10: gm.StartGame(); report.AppendLine("after StartGame: state=" + gm.State + " intro active=" + gm.Intro.Active); break;
            }
            if (frame < 14) return;
            int slot = (frame - 14) / 40;
            int phase = (frame - 14) % 40;
            if (slot < times.Length)
            {
                if (phase == 0) gm.Intro.DebugSeek(times[slot]);
                if (phase == 36) Capture("n" + slot.ToString("00") + "_" + gm.Intro.Elapsed.ToString("F1"));
            }
            else if (frame == 14 + times.Length * 40 + 5)
            {
                gm.Intro.DebugSeek(IntroDirector.RealTime(42.7f));   // конец: через несколько кадров интро запускает игру
            }
            else if (frame == 14 + times.Length * 40 + 300)
            {
                report.AppendLine("end state=" + gm.State + " intro active=" + gm.Intro.Active + " pos=" + gm.Player.transform.position + " glowScale=" + gm.Hero.GlowScale.ToString("F2"));
                Capture("n99_game_start");
                Finish("OK", errors > 0 ? 2 : 0);
            }
        }

        static float econDrain;
        static float econLife0;

        /// <summary>Без окна: экономика света — тление за кадр, чистый итог убийства Тени, Гасителя и Пожирателя (выстрел минус осколок).</summary>
        static void StepEcon(GameManager gm)
        {
            PlayerController pc = gm.Player;
            PalmShot ps = pc.Combat;
            HeroLight h = gm.Hero;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame == 5)
            {
                pc.TeleportTo(new Vector3(171f, 0.1f, 225f), 0f);
                SetPitch(pc, 0f);
                h.Life = 60f;
                h.AuraTarget = GameConfig.AuraStart;
                Enemy.ClearAll();
            }
            if (frame == 10) econLife0 = h.Life;
            if (frame == 70)
            {
                econDrain = (econLife0 - h.Life) / 60f;
                report.AppendLine("passive drain (aura 5 m): " + (econDrain * 60f).ToString("F2") + " /s  (smoulder " + GameConfig.SmolderPerSec.ToString("F2") + " + aura " + (GameConfig.AuraCostK * 25f).ToString("F2") + ")");
            }
            // три убийства подряд: Тень, Гаситель (выстрел стоит 5), Пожиратель (слитый луч ~4 с по 8 = 32)
            EnemyKind[] kinds = { EnemyKind.Shade, EnemyKind.Snuffer, EnemyKind.Eater, EnemyKind.Shade, EnemyKind.Shade };
            float[] costs = { 5f, 5f, 32f, 5f, 5f };
            int slot = (frame - 80) / 100;
            int phase = (frame - 80) % 100;
            if (frame < 80) return;
            if (slot >= kinds.Length) { Finish("OK", errors > 0 ? 2 : 0); return; }
            if (phase == 0)
            {
                Vector3 p = pc.transform.position + pc.transform.forward * 1.6f;
                Enemy e = Enemy.Spawn(kinds[slot], p);
                e.Stun(9f);
                econLife0 = h.Life;
                h.Spend(costs[slot]);
                e.ShotHit(500f);
            }
            if (phase == 90)
            {
                float net = h.Life - econLife0 + econDrain * 90f;
                report.AppendLine(kinds[slot] + " kill: shot " + costs[slot].ToString("F0") + ", light " + econLife0.ToString("F1") + " -> " + h.Life.ToString("F1") + ", net with drain removed " + net.ToString("+0.0;-0.0")
                    + "  orbs left=" + LightOrb.All.Count);
            }
        }

        static float resLife0;
        static float resDrain;

        /// <summary>Без окна: доход света от выживших рядом (резонанс) при 0 и 3 проснувшихся, сразу после пробуждения и при полном здоровье.</summary>
        static void StepReson(GameManager gm)
        {
            PlayerController pc = gm.Player;
            HeroLight h = gm.Hero;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame >= 3 && frame % 15 == 0) Enemy.ClearAll();
            if (frame == 5)
            {
                pc.TeleportTo(new Vector3(171f, 0.1f, 225f), 0f);
                SetPitch(pc, 0f);
                h.Life = 60f;
                h.AuraTarget = GameConfig.AuraStart;
            }
            if (frame == 10) resLife0 = h.Life;
            if (frame == 70)
            {
                resDrain = (resLife0 - h.Life) / 1f;
                report.AppendLine("passive drain (aura 5 m, no survivors): " + resDrain.ToString("F2") + " /s");
                for (int i = 0; i < 3; i++)
                {
                    Survivor s = Survivor.All[i];
                    CharacterController cc = s.GetComponent<CharacterController>();
                    cc.enabled = false;
                    s.transform.position = pc.transform.position + new Vector3(-1.6f + i * 1.6f, 0f, 1.8f);
                    cc.enabled = true;
                    s.Wake();
                }
            }
            // окно A: только что проснулись (здоровье = SurvivorWakeHealth)
            if (frame == 100) { h.Life = 60f; resLife0 = h.Life; report.AppendLine("window A start: survivors health " + Survivor.All[0].Health.ToString("F0") + "/" + Survivor.All[1].Health.ToString("F0") + "/" + Survivor.All[2].Health.ToString("F0")); }
            if (frame == 700)
            {
                float gain = (h.Life - resLife0) / 10f + resDrain;
                report.AppendLine("window A (10 s, 3 survivors, health now " + Survivor.All[0].Health.ToString("F0") + "): net life change " + ((h.Life - resLife0) / 10f).ToString("+0.00;-0.00") + " /s => resonance income " + gain.ToString("F2") + " /s");
                for (int i = 0; i < 3; i++) Survivor.All[i].Heal(1000f);
                h.Life = 60f;
                resLife0 = h.Life;
            }
            if (frame == 1300)
            {
                float gain = (h.Life - resLife0) / 10f + resDrain;
                report.AppendLine("window B (10 s, 3 survivors at full health): net life change " + ((h.Life - resLife0) / 10f).ToString("+0.00;-0.00") + " /s => resonance income " + gain.ToString("F2") + " /s");
                Finish("OK", errors > 0 ? 2 : 0);
            }
        }

        static float PzWallDist(Vector3 p)
        {
            float best = 3.2f;
            int[] ddx = { 1, -1, 0, 0 };
            int[] ddz = { 0, 0, 1, -1 };
            for (int d = 0; d < 4; d++)
            {
                for (float s = 0.2f; s < best; s += 0.2f)
                {
                    if (!CityMap.IsRoad(p.x + ddx[d] * s, p.z + ddz[d] * s, false)) { best = s; break; }
                }
            }
            return best;
        }

        static void PzPlaceHero(PlayerController pc, Pizza pz, float dist, float pitch)
        {
            Vector3 tgt = pz.transform.position;
            Vector3 pf = pz.transform.forward;
            float baseYaw = Mathf.Atan2(-pf.x, -pf.z) * Mathf.Rad2Deg;
            for (int k = 0; k < 24; k++)
            {
                float off = ((k + 1) / 2) * 15f * (k % 2 == 0 ? 1f : -1f);
                float y2 = baseYaw + off;
                float a2 = y2 * Mathf.Deg2Rad;
                Vector3 p2 = tgt - new Vector3(Mathf.Sin(a2), 0f, Mathf.Cos(a2)) * dist;
                p2.y = 0.1f;
                if (CityMap.CanStand(p2.x, p2.z, 0.4f, true) && CityMap.LineClear(p2.x, p2.z, tgt.x, tgt.z, 0.2f, true))
                {
                    pc.TeleportTo(p2, y2);
                    SetPitch(pc, pitch);
                    return;
                }
            }
            report.AppendLine("  [no standing spot found near pizza at " + tgt.ToString("F1") + "]");
        }

        static float pzLife0;
        static float pzDrain;

        /// <summary>
        /// Без окна: пицца. Раскладка (места, расстояния до стен, ламп, людей, дверей, хлама) и весь цикл:
        /// подошёл, смотрит -> фокус и подсказка, взял -> три укуса, свет, мысль, пустая коробка; при полном свете взять нельзя.
        /// </summary>
        static void StepPizza(GameManager gm)
        {
            PlayerController pc = gm.Player;
            HeroLight h = gm.Hero;
            BindingFlags bfi = BindingFlags.Instance | BindingFlags.NonPublic;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame >= 3 && frame % 15 == 0) Enemy.ClearAll();
            if (frame == 5)
            {
                report.AppendLine("pizzas: placed=" + LevelBuilder.PizzasPlaced + " missed=" + LevelBuilder.PizzasMissed + " in world=" + Pizza.All.Count + " (map wants " + CityMap.Pizzas.Count + ")");
                for (int i = 0; i < Pizza.All.Count; i++)
                {
                    Pizza pz = Pizza.All[i];
                    Vector3 p = pz.transform.position;
                    int cx = CityMap.CX(p.x);
                    int cz = CityMap.CZ(p.z);
                    float lamp = 999f, surv = 999f, door = 999f, hearth = 999f;
                    for (int k = 0; k < Lamp.All.Count; k++)
                    {
                        float d = Vector3.Distance(new Vector3(Lamp.All[k].BasePos.x, 0f, Lamp.All[k].BasePos.z), p);
                        if (Lamp.All[k].IsHearth) hearth = Mathf.Min(hearth, d); else lamp = Mathf.Min(lamp, d);
                    }
                    for (int k = 0; k < Survivor.All.Count; k++) surv = Mathf.Min(surv, Vector3.Distance(new Vector3(Survivor.All[k].SpawnPos.x, 0f, Survivor.All[k].SpawnPos.z), p));
                    for (int k = 0; k < LevelBuilder.Entrances.Count; k++) door = Mathf.Min(door, Vector3.Distance(new Vector3(LevelBuilder.Entrances[k].x, 0f, LevelBuilder.Entrances[k].z), p));
                    int solid = 0;
                    Collider[] hits = Physics.OverlapSphere(p + Vector3.up * 0.4f, 0.5f);
                    for (int k = 0; k < hits.Length; k++)
                    {
                        if (hits[k].isTrigger || hits[k].gameObject.name == "Floor") continue;
                        solid++;
                    }
                    bool ownCollider = pz.GetComponentInChildren<Collider>() != null;
                    string kind = CityMap.Kind[cx, cz] == CityMap.Alley ? "alley" : "NOT ALLEY (kind " + CityMap.Kind[cx, cz] + ")";
                    report.AppendLine("  #" + i + " " + p.ToString("F1") + " cell " + cx + "," + cz + " " + kind + " district " + CityMap.District[cx, cz]
                        + " progress " + CityMap.Progress(p.x, p.z).ToString("F2") + " wall " + PzWallDist(p).ToString("F1")
                        + " | nearest lamp " + lamp.ToString("F1") + " survivor " + surv.ToString("F1") + " door " + door.ToString("F1") + " hearth " + hearth.ToString("F0")
                        + " | solids in 0.5 m: " + solid + (ownCollider ? " [BAD: own collider]" : "") + " light " + LightField.Sample(p + Vector3.up * 0.3f).ToString("F2"));
                }
            }

            // --- цикл подбора на пицце 0 (света не хватает): фокус, подсказка, взять, три укуса
            if (frame >= 20) h.AuraTarget = GameConfig.AuraStart;
            if (frame == 20)
            {
                h.Life = 50f;
                PzPlaceHero(pc, Pizza.All[0], 2.0f, 0f);   // смотрит прямо перед собой (не вниз): пицца всё равно должна попасть в фокус
            }
            if (frame == 30)
            {
                Pizza pz = Pizza.All[0];
                report.AppendLine("eat test, pizza 0: hero " + pc.transform.position.ToString("F1") + " dist " + Vector3.Distance(pc.transform.position, pz.transform.position).ToString("F1")
                    + ", focus=" + pc.FocusType + ", prompt='" + pc.PromptE + "'");
                Capture("pz_a_before");
                pzLife0 = h.Life;
                pz.Take();
            }
            if (frame > 30 && frame <= 30 + 110)
            {
                Pizza pz = Pizza.All[0];
                int f = frame - 30;
                if (f == 12 || f == 24 || f == 40 || f == 58 || f == 76 || f == 100)
                {
                    report.AppendLine("  t=" + (f / 60f).ToString("F2") + " s: bites " + pz.BitesDone + ", life " + h.Life.ToString("F1") + ", finished " + pz.Finished);
                    Capture("pz_b_t" + f.ToString("000"));
                }
            }
            if (frame == 30 + 112)
            {
                Pizza pz = Pizza.All[0];
                float elapsed = 112f / 60f;
                float gain = h.Life - pzLife0 + (GameConfig.SmolderPerSec + GameConfig.AuraCostK * GameConfig.AuraStart * GameConfig.AuraStart) * elapsed;
                HashSet<string> seen = (HashSet<string>)typeof(GameManager).GetField("thoughtsSeen", bfi).GetValue(gm);
                report.AppendLine("after eating: taken=" + pz.Taken + " finished=" + pz.Finished + " bites=" + pz.BitesDone + " life " + pzLife0.ToString("F1") + " -> " + h.Life.ToString("F1")
                    + " (gain without drain " + gain.ToString("F1") + ", wanted " + GameConfig.PizzaLight.ToString("F0") + "), thought shown=" + seen.Contains("pizza")
                    + ", focus now=" + pc.FocusType + ", slices visible=" + (pz.transform.Find("Slices") != null && pz.transform.Find("Slices").gameObject.activeSelf));
                Capture("pz_c_empty");
            }

            // --- при полном свете пицца ждёт
            if (frame == 160)
            {
                h.Life = 100f;
                PzPlaceHero(pc, Pizza.All[1], 1.6f, 30f);
            }
            if (frame == 172)
            {
                h.Life = 100f;
                report.AppendLine("full-light test, pizza 1: focus=" + pc.FocusType + ", prompt='" + pc.PromptE + "' taken=" + Pizza.All[1].Taken);
            }

            // --- взгляд вбок: пицца в 2.3 м, но смотрим в другую сторону — фокуса на ней быть не должно
            if (frame == 180)
            {
                h.Life = 60f;
                PzPlaceHero(pc, Pizza.All[2], 2.3f, 0f);
            }
            if (frame == 190)
            {
                report.AppendLine("range test, pizza 2 at 2.3 m: focus=" + pc.FocusType + ", prompt='" + pc.PromptE + "'");
                PzPlaceHero(pc, Pizza.All[2], 3.4f, 0f);
            }
            if (frame == 200)
            {
                report.AppendLine("range test, pizza 2 at 3.4 m (beyond reach " + GameConfig.PizzaReach.ToString("F1") + "): focus=" + pc.FocusType);
                Finish("OK", errors > 0 ? 2 : 0);
            }
        }

        /// <summary>Без окна: Пожиратель кусает человека (раньше съедал сразу): сколько укусов выдерживает проснувшийся.</summary>
        static void StepDurable(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame >= 3) gm.Hero.Life = 100f;
            Survivor s = Survivor.All[0];
            if (frame == 5)
            {
                pc.TeleportTo(new Vector3(36f, 0.1f, 70f), 0f);
                s.Wake();
                report.AppendLine("woken: health " + s.Health.ToString("F0") + " of " + GameConfig.SurvivorMaxHealth.ToString("F0"));
            }
            if (frame == 8)
            {
                Vector3 p = s.transform.position + new Vector3(0f, 0f, 3f);
                Enemy e = Enemy.Spawn(EnemyKind.Eater, p);
                report.AppendLine("eater spawned at " + e.transform.position.ToString("F1") + ", survivor at " + s.transform.position.ToString("F1"));
            }
            if (frame > 8 && frame % 60 == 0)
                report.AppendLine("  t=" + ((frame - 8) / 60f).ToString("F1") + " s: survivor state " + s.State + " health " + s.Health.ToString("F0"));
            if (frame == 540) Finish("OK", errors > 0 ? 2 : 0);
        }

        static int busInside;
        static int busChecks;
        static float busMinHero = 999f;

        /// <summary>Без окна: Тени гонятся за героем по ту сторону автобуса и не должны проходить сквозь него.</summary>
        static void StepBusFight(GameManager gm)
        {
            PlayerController pc = gm.Player;
            Vector3 bus = LevelBuilder.BusPos;
            float bx0 = bus.x - 1.35f, bx1 = bus.x + 1.35f, bz0 = bus.z - 5.5f, bz1 = bus.z + 5.5f;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame >= 3)
            {
                gm.Hero.Life = 100f;
                gm.Hero.AuraTarget = GameConfig.AuraMin;
            }
            if (frame == 5)
            {
                pc.TeleportTo(new Vector3(bus.x + 6f, 0.1f, bus.z), 270f);
                for (int i = 0; i < 4; i++) Enemy.Spawn(EnemyKind.Shade, new Vector3(bus.x - 9f, 0f, bus.z - 3f + i * 2f));
                Enemy.Spawn(EnemyKind.Snuffer, new Vector3(bus.x - 8f, 0f, bus.z + 4f));
                report.AppendLine("bus at " + bus + ", footprint x " + bx0.ToString("F1") + ".." + bx1.ToString("F1") + " z " + bz0.ToString("F1") + ".." + bz1.ToString("F1")
                    + ", light at bus centre=" + LightField.Sample(bus + Vector3.up * 1.2f).ToString("F2"));
            }
            if (frame > 8)
            {
                for (int i = 0; i < Enemy.All.Count; i++)
                {
                    Vector3 p = Enemy.All[i].transform.position;
                    busChecks++;
                    if (p.x > bx0 && p.x < bx1 && p.z > bz0 && p.z < bz1) busInside++;
                    float dh = Vector3.Distance(p, pc.transform.position);
                    if (dh < busMinHero) busMinHero = dh;
                }
            }
            if (frame >= 222 && frame <= 238 && Enemy.All.Count > 1)
            {
                BindingFlags bf2 = BindingFlags.Instance | BindingFlags.NonPublic;
                Enemy e1 = Enemy.All[1];
                float dt1 = (float)typeof(Enemy).GetField("detourT", bf2).GetValue(e1);
                report.AppendLine("    f" + frame + " enemy1 " + e1.transform.position.ToString("F3") + " detourT=" + dt1.ToString("F2"));
            }
            if (frame >= 20 && frame <= 300 && frame % 20 == 0 && Enemy.All.Count > 1)
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
                Enemy e0 = Enemy.All[1];
                float dt0 = (float)typeof(Enemy).GetField("detourT", bf).GetValue(e0);
                Vector3 dd0 = (Vector3)typeof(Enemy).GetField("detourDir", bf).GetValue(e0);
                report.AppendLine("  f" + frame + " enemy1 " + e0.transform.position.ToString("F2") + " detourT=" + dt0.ToString("F2") + " dir=" + dd0.ToString("F2"));
            }
            if (frame == 80) Capture("bus_a");
            if (frame == 200) Capture("bus_b");
            if (frame == 720)
            {
                report.AppendLine("enemies=" + Enemy.All.Count + " checks=" + busChecks + " inside the bus=" + busInside + " closest to hero=" + busMinHero.ToString("F1")
                    + (busInside > 0 ? "  [BAD: passed through the bus]" : "  ok"));
                for (int i = 0; i < Enemy.All.Count; i++) report.AppendLine("  enemy " + i + " at " + Enemy.All[i].transform.position.ToString("F1"));
                Finish("OK", errors > 0 ? 2 : 0);
            }
        }

        static int standKills0;
        static float standMinLife = 999f;
        static int standMaxEnemies;

        static void SetYaw(PlayerController pc, float deg)
        {
            FieldInfo fi = typeof(PlayerController).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi != null) fi.SetValue(pc, deg);
        }

        /// <summary>
        /// Без окна: последняя минута у автобуса целиком (3600 кадров по 1/60 с). Герой стоит в кольце у двери, двигатель заведён.
        /// «finalstand» — умелый герой: аура на минимуме, бьёт Пожирателей и Теней выстрелами с идеальной меткостью, осколки в радиусе 22 м
        /// подбирает сразу (потолок мастерства); «finalpassive» — стоит и ничего не делает (низ). Настоящий игрок между ними.
        /// Отчёт каждые 5 с: свет, враги, осколки; в конце: уехал ли автобус, сколько света, сколько ушло на выстрелы и пришло осколками.
        /// </summary>
        static void StepFinalStand(GameManager gm, bool gunner)
        {
            PlayerController pc = gm.Player;
            Bus bus = Bus.Instance;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame < 5) return;
            if (frame == 5)
            {
                Enemy.ClearAll();
                gm.Hero.Life = 100f;
                Vector3 door = bus.DoorPos;
                pc.TeleportTo(new Vector3(door.x, 0.1f, door.z), 270f);
                bus.Feed(GameConfig.BusNeed + 1f);   // двигатель заведён: минута пошла
                standKills0 = gm.EnemiesKilled;
                report.AppendLine("final stand (" + (gunner ? "gunner with perfect aim" : "passive hero") + "): length " + bus.StandLength.ToString("F0")
                    + " s, bus " + bus.State + ", hero " + pc.transform.position.ToString("F1") + ", eater every " + GameConfig.FinalEaterInterval.ToString("F0")
                    + " s, pressure +" + GameConfig.FinalPressure.ToString("F1") + ", extra shades " + GameConfig.ShadesFinal);
                return;
            }
            float t = (frame - 6) / 60f;
            if (gm.Hero.Life < standMinLife) standMinLife = gm.Hero.Life;
            int enemies = Enemy.All.Count;
            if (enemies > standMaxEnemies) standMaxEnemies = enemies;

            if (gunner && bus.State == Bus.Mode.Running && gm.State == GameState.Playing)
            {
                gm.Hero.AuraTarget = GameConfig.AuraMin;   // умелый игрок гасит ауру: она съедает больше всего света
                // осколки подбирает сразу (на самом деле — шаг-два от круга и вдох на E)
                Vector3 chest = pc.transform.position + Vector3.up * 1.3f;
                for (int i = 0; i < LightOrb.All.Count; i++)
                {
                    LightOrb o = LightOrb.All[i];
                    if (o != null && !o.Collected && (o.transform.position - chest).magnitude < 22f) o.Collect();
                }
                // цель: Пожиратель (его свет автобуса не жжёт), Тени — только те, что подобрались вплотную; Гасители героя не трогают.
                // Остальные Тени сгорают в свете автобуса сами: тратить на них выстрелы невыгодно.
                Vector3 eye = pc.EyePos;
                Enemy best = null;
                float bestScore = 1e9f;
                for (int i = 0; i < Enemy.All.Count; i++)
                {
                    Enemy e = Enemy.All[i];
                    if (e == null || e.Health <= 0f || e.Kind == EnemyKind.Snuffer) continue;
                    Vector3 d = e.transform.position - eye;
                    d.y = 0f;
                    float dist = d.magnitude;
                    if (e.Kind == EnemyKind.Shade && dist > 5f) continue;
                    if (dist > GameConfig.CombatRange * 0.9f || !CityMap.LineClear(eye.x, eye.z, e.transform.position.x, e.transform.position.z, 0.15f, false)) continue;
                    // автобус, машины и дома закрывают луч: по такой цели умелый игрок не стреляет
                    if (Physics.Linecast(eye, e.transform.position + Vector3.up * 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float score = dist - (e.Kind == EnemyKind.Eater ? 100f : 0f);
                    if (score < bestScore) { bestScore = score; best = e; }
                }
                if (best != null)
                {
                    Vector3 to = best.transform.position + Vector3.up * 1.2f - eye;
                    float hor = new Vector2(to.x, to.z).magnitude;
                    SetYaw(pc, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                    SetPitch(pc, -Mathf.Atan2(to.y, hor) * Mathf.Rad2Deg);
                    if (frame % 3 == 0 && gm.Hero.Life > 8f) pc.Combat.FireShot();
                }
            }

            if (frame % 300 == 6)
            {
                report.AppendLine("  t=" + t.ToString("F0").PadLeft(2) + " s: left " + bus.FinalLeft.ToString("F0").PadLeft(2) + ", hero light " + gm.Hero.Life.ToString("F0").PadLeft(3)
                    + ", shades " + Enemy.CountOf(EnemyKind.Shade) + " snuffers " + Enemy.CountOf(EnemyKind.Snuffer) + " eaters " + Enemy.CountOf(EnemyKind.Eater)
                    + ", orbs " + LightOrb.All.Count + ", kills " + (gm.EnemiesKilled - standKills0)
                    + ", spent on shots " + gm.Economy.Amount(LightReason.Shot, false, false).ToString("F0") + ", from orbs " + gm.Economy.Amount(LightReason.Orb, true, false).ToString("F0"));
            }
            if (frame == 6 + 600) Capture("fs_10s");
            if (frame == 6 + 3000) Capture("fs_50s");

            if (gm.State == GameState.Dead)
            {
                report.AppendLine("HERO DIED at t=" + t.ToString("F1") + " s (bus had " + bus.FinalLeft.ToString("F0") + " s left), kills " + (gm.EnemiesKilled - standKills0));
                Finish("OK", errors > 0 ? 2 : 0);
                return;
            }
            if (bus.State != Bus.Mode.Running)
            {
                report.AppendLine("BUS LEFT at t=" + t.ToString("F1") + " s, hero light " + gm.Hero.Life.ToString("F0") + " (lowest " + standMinLife.ToString("F0")
                    + "), most enemies at once " + standMaxEnemies + ", kills " + (gm.EnemiesKilled - standKills0)
                    + ", spent on shots " + gm.Economy.Amount(LightReason.Shot, false, false).ToString("F0") + ", from orbs " + gm.Economy.Amount(LightReason.Orb, true, false).ToString("F0"));
                Finish("OK", errors > 0 ? 2 : 0);
                return;
            }
            if (frame > 6 + 4200) Finish("TIMEOUT: the bus did not leave in 70 s", 3);
        }

        /// <summary>Без окна: человек идёт за героем через парки (деревья, скамейки, клумбы): не должен застрять.</summary>
        static void StepFollow(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame >= 3) gm.Hero.Life = 100f;
            // (человек, где стоит герой): сад на старте, детская площадка
            int[] who = { 0, 8 };
            Vector3[] heroAt = { new Vector3(24f, 0.1f, 30f), new Vector3(60f, 0.1f, 306f) };
            int slot = (frame - 5) / 800;
            int phase = (frame - 5) % 800;
            if (frame < 5) return;
            if (slot >= who.Length) { Finish("OK", errors > 0 ? 2 : 0); return; }
            Survivor s = Survivor.All[who[slot]];
            if (phase == 0)
            {
                pc.TeleportTo(heroAt[slot], 0f);
                s.Wake();
                report.AppendLine("follow " + slot + ": survivor " + who[slot] + " starts at " + s.transform.position.ToString("F0") + ", hero at " + heroAt[slot].ToString("F0"));
            }
            if (phase > 0 && phase % 200 == 0)
                report.AppendLine("  t=" + phase + " dist=" + Vector3.Distance(s.transform.position, pc.transform.position).ToString("F1") + " pos=" + s.transform.position.ToString("F0"));
            if (phase == 799)
            {
                float d = Vector3.Distance(s.transform.position, pc.transform.position);
                report.AppendLine("  final distance " + d.ToString("F1") + (d > 6f ? "  [BAD: stuck?]" : "  ok"));
            }
        }

        /// <summary>Без окна: пропуск и выход в меню прямо во время вида сверху (камера и поле зрения должны вернуться).</summary>
        static void StepAerialSkip(GameManager gm)
        {
            Camera cam = gm.Player.Cam;
            switch (frame)
            {
                case 10: gm.StartGame(); break;
                case 14: gm.Intro.DebugSeek(15.0f); break;
                case 40: report.AppendLine("in aerial: cam=" + cam.transform.position.ToString("F0") + " fov=" + cam.fieldOfView.ToString("F0") + " t=" + gm.Intro.Elapsed.ToString("F1") + " beacon visible=" + LevelBuilder.BeaconActive + " (must be False)"); gm.Intro.Skip(); break;
                case 120:
                    {
                        Transform vis0 = Survivor.All[0].transform.Find("Visual");
                        report.AppendLine("after skip: state=" + gm.State + " fov=" + cam.fieldOfView.ToString("F0") + " cam=" + cam.transform.position.ToString("F1") + " life=" + gm.Hero.Life.ToString("F0")
                            + " survivor visible=" + (vis0 != null && vis0.gameObject.activeSelf) + " lampMask=" + (Lamp.IntroMask != null ? "ON(bug)" : "off") + " beacon visible=" + LevelBuilder.BeaconActive + " (must be True)");
                    }
                    break;
                case 130: gm.QuitToMenu(); break;
                case 140: gm.StartGame(); break;
                case 150: gm.Intro.DebugSeek(14.5f); break;
                case 180: report.AppendLine("second aerial: cam=" + cam.transform.position.ToString("F0") + " fov=" + cam.fieldOfView.ToString("F0")); gm.QuitToMenu(); break;
                case 190: report.AppendLine("after quit: state=" + gm.State + " active=" + gm.Intro.Active + " fov=" + cam.fieldOfView.ToString("F0")); Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        /// <summary>Окно редактора: мысли героя в игре (IMGUI) — после пропуска предыстории.</summary>
        static void StepThoughts(GameManager gm)
        {
            switch (frame)
            {
                case 10: SetMenuPage(gm, 0); gm.StartGame(); break;
                case 40: gm.Intro.Skip(); break;
                case 140: report.AppendLine("state=" + gm.State); break;
                case 200: Shoot("t01_start"); break;                       // «Рука всё ещё светится...» (3.5 с после начала игры)
                case 330: gm.Think("arm", "На руке — мой свет. Пока он горит, я жив."); break;
                case 345: Shoot("t02_arm"); break;
                case 480: gm.Think("shot", "Свет слушается руки и бьёт по Тьме. Выходит, это — оружие?.. Но он убавился."); break;
                case 495: Shoot("t03_shot"); break;
                case 560: report.AppendLine("thought queue ok"); Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        /// <summary>Без окна: только вид на город сверху (реальное время предыстории 12.5..21.1).</summary>
        static void StepAerial(GameManager gm)
        {
            float[] times = { 13.0f, 14.2f, 15.4f, 16.6f, 17.8f, 19.0f, 20.0f, 20.9f };
            switch (frame)
            {
                case 10: gm.StartGame(); report.AppendLine("after StartGame: state=" + gm.State); break;
            }
            if (frame < 14) return;
            int slot = (frame - 14) / 30;
            int phase = (frame - 14) % 30;
            if (slot < times.Length)
            {
                if (phase == 0) gm.Intro.DebugSeek(times[slot]);
                if (phase == 26) Capture("a" + slot.ToString("00") + "_" + gm.Intro.Elapsed.ToString("F1"));
            }
            else if (frame == 14 + times.Length * 30 + 3)
            {
                Finish("OK", errors > 0 ? 2 : 0);
            }
        }

        // ------------------------------------------------------------------ произвольные ракурсы из файла

        static readonly List<string[]> viewList = new List<string[]>();

        static float F(string s)
        {
            return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Ракурсы из текстового файла (PG_SMOKE_VIEWS): строка = имя вид аргументы.
        ///   pos X Y Z yaw pitch              — камера в точке
        ///   at X Z dist yaw pitch            — стоять в dist м перед точкой (X, Z), смотреть на неё под углом yaw
        ///   survivor N dist yaw pitch        — перед человеком N
        ///   lamp N dist yaw pitch            — перед лампой N
        ///   bus dist yaw pitch               — вокруг автобуса
        /// Каждый ракурс — телепорт, 18 кадров на установление, снимок.
        /// </summary>
        static void StepViews(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 3)
            {
                string path = Environment.GetEnvironmentVariable("PG_SMOKE_VIEWS");
                viewList.Clear();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        viewList.Add(t.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                }
                report.AppendLine("views: " + viewList.Count);
                gm.State = GameState.Playing;
                gm.Hero.Life = 100f;
            }
            if (frame < 5) return;
            int slot = (frame - 5) / 22;
            int phase = (frame - 5) % 22;
            if (slot >= viewList.Count)
            {
                if (phase == 0) Finish("OK", errors > 0 ? 2 : 0);
                return;
            }
            gm.Hero.Life = 100f;
            string[] v = viewList[slot];
            if (phase == 0)
            {
                Vector3 pos = Vector3.zero;
                float yaw = 0f;
                float pit = 0f;
                string kind = v[1];
                try
                {
                    if (kind == "pos") { pos = new Vector3(F(v[2]), F(v[3]), F(v[4])); yaw = F(v[5]); pit = F(v[6]); }
                    else if (kind == "shopfront")
                    {
                        // перед витриной i в dist м, лицом к стене
                        int si = int.Parse(v[2]);
                        Vector3 sn = LevelBuilder.ShopFrontNormals[si];
                        pos = LevelBuilder.ShopFronts[si] + sn * F(v[3]);
                        pos.y = 0.1f;
                        yaw = Mathf.Atan2(-sn.x, -sn.z) * Mathf.Rad2Deg;
                        pit = F(v[4]);
                    }
                    else if (kind == "ivy")
                    {
                        int ii = int.Parse(v[2]);
                        Vector3 inn = LevelBuilder.IvyNormals[ii];
                        pos = LevelBuilder.IvyPatches[ii] + inn * F(v[3]);
                        pos.y = 0.1f;
                        yaw = Mathf.Atan2(-inn.x, -inn.z) * Mathf.Rad2Deg;
                        pit = F(v[4]);
                    }
                    else if (kind == "tree")
                    {
                        int tix = int.Parse(v[2]);
                        if (tix < 0) tix += LevelBuilder.TreePoints.Count;
                        Vector3 tp = LevelBuilder.TreePoints[tix];
                        float td = F(v[3]);
                        yaw = F(v[4]);
                        pit = F(v[5]);
                        float ta = yaw * Mathf.Deg2Rad;
                        pos = tp - new Vector3(Mathf.Sin(ta), 0f, Mathf.Cos(ta)) * td;
                        pos.y = 0.1f;
                    }
                    else
                    {
                        Vector3 tgt = Vector3.zero;
                        float dist = 4f;
                        if (kind == "at") { tgt = new Vector3(F(v[2]), 0f, F(v[3])); dist = F(v[4]); yaw = F(v[5]); pit = F(v[6]); }
                        else if (kind == "survivor") { tgt = Survivor.All[int.Parse(v[2])].transform.position; dist = F(v[3]); yaw = F(v[4]); pit = F(v[5]); }
                        else if (kind == "lamp") { tgt = Lamp.All[int.Parse(v[2])].BasePos; dist = F(v[3]); yaw = F(v[4]); pit = F(v[5]); }
                        else if (kind == "bus") { tgt = LevelBuilder.BusPos; dist = F(v[2]); yaw = F(v[3]); pit = F(v[4]); }
                        else if (kind == "park") { tgt = LevelBuilder.ParkCenters[int.Parse(v[2])]; dist = F(v[3]); yaw = F(v[4]); pit = F(v[5]); }
                        else if (kind == "pizza")
                        {
                            // перед открытой стороной коробки (yaw — поворот относительно неё), смотреть на пиццу
                            Pizza pzv = Pizza.All[int.Parse(v[2])];
                            tgt = pzv.transform.position;
                            Vector3 pf = pzv.transform.forward;
                            dist = F(v[3]);
                            yaw = Mathf.Atan2(-pf.x, -pf.z) * Mathf.Rad2Deg + F(v[4]);
                            pit = F(v[5]);
                        }
                        // камера на дороге с чистой линией до цели: перебираем углы от заданного
                        bool found = false;
                        for (int k = 0; k < 24 && !found; k++)
                        {
                            float off = ((k + 1) / 2) * 15f * (k % 2 == 0 ? 1f : -1f);
                            float y2 = yaw + off;
                            float a2 = y2 * Mathf.Deg2Rad;
                            Vector3 p2 = tgt - new Vector3(Mathf.Sin(a2), 0f, Mathf.Cos(a2)) * dist;
                            p2.y = 0.1f;
                            Vector3 e = Vector3.Lerp(p2, tgt, 0.85f);
                            if (CityMap.CanStand(p2.x, p2.z, 0.4f, true) && CityMap.LineClear(p2.x, p2.z, e.x, e.z, 0.2f, true))
                            {
                                pos = p2;
                                yaw = y2;
                                found = true;
                            }
                        }
                        if (!found)
                        {
                            float a = yaw * Mathf.Deg2Rad;
                            pos = tgt - new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * dist;
                            pos.y = 0.1f;
                        }
                    }
                }
                catch (Exception e)
                {
                    report.AppendLine("bad view line '" + string.Join(" ", v) + "': " + e.Message);
                }
                pc.TeleportTo(pos, yaw);
                SetPitch(pc, pit);
            }
            if (phase == 18) Capture(v[0]);
        }

        /// <summary>Без окна: пропуск предыстории посреди сцены и повторный запуск из меню.</summary>
        static void StepIntroSkip(GameManager gm)
        {
            switch (frame)
            {
                case 10: gm.StartGame(); report.AppendLine("started: state=" + gm.State); break;
                case 330: report.AppendLine("t=" + gm.Intro.Elapsed.ToString("F1") + " state=" + gm.State); gm.Intro.Skip(); break;
                case 420: report.AppendLine("after skip: state=" + gm.State + " active=" + gm.Intro.Active + " pos=" + gm.Player.transform.position + " glow=" + gm.Hero.GlowScale.ToString("F2") + " exposure ok=" + (Shader.GetGlobalFloat("_PG_Exposure") > 2.5f)); break;
                case 430: gm.QuitToMenu(); break;
                case 440: report.AppendLine("menu: state=" + gm.State); gm.StartGame(); break;
                case 450: report.AppendLine("second run: state=" + gm.State + " active=" + gm.Intro.Active); break;
                case 700: report.AppendLine("t=" + gm.Intro.Elapsed.ToString("F1") + " state=" + gm.State); gm.QuitToMenu(); break;
                case 720: report.AppendLine("quit during intro: state=" + gm.State + " active=" + gm.Intro.Active + " exposure ok=" + (Shader.GetGlobalFloat("_PG_Exposure") > 2.5f)); Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        /// <summary>Без окна: полоска здоровья на груди при взгляде вниз.</summary>
        static void StepChest(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame >= 6 && gm.State == GameState.Playing && chestLife > 0f) gm.Hero.Life = chestLife;
            switch (frame)
            {
                case 3: gm.State = GameState.Playing; gm.Hero.Life = 100f; chestLife = 100f; break;
                case 5: pc.TeleportTo(new Vector3(33f, 0.1f, 45f), 0f); SetPitch(pc, 70f); break;
                case 40: Capture("c01_life100_p70"); break;
                case 41: chestLife = 55f; break;
                case 80: Capture("c02_life55_p70"); break;
                case 81: chestLife = 20f; break;
                case 120: Capture("c03_life20_p70"); break;
                case 121: chestLife = 100f; SetPitch(pc, 50f); break;
                case 160: Capture("c04_life100_p50"); break;
                case 161: SetPitch(pc, 78f); chestLife = 70f; break;
                case 200: Capture("c05_life70_p78"); break;
                case 201: SetPitch(pc, 0f); break;
                case 230: Capture("c06_forward"); break;
                case 231: pc.TeleportTo(new Vector3(171f, 0.1f, 225f), 0f); SetPitch(pc, 70f); chestLife = 100f; break;
                case 270: Capture("c07_alley_life100"); break;
                case 271: chestLife = 45f; break;
                case 330: Capture("c08_alley_life45"); break;
                case 331: Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        /// <summary>Подставить походку герою: фаза шага и 12 прогонов LateUpdate рук, чтобы сглаживание дошло до позы.</summary>
        static void ForceWalk(PlayerController pc, float phase, float amount)
        {
            pc.MoveAmount = amount;
            pc.WalkPhase = phase;
            BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo ucam = typeof(PlayerController).GetMethod("UpdateAudioAndCamera", f);
            if (ucam != null) ucam.Invoke(pc, new object[] { 0.016f });
            MethodInfo lu = typeof(HeroBody).GetMethod("LateUpdate", f);
            for (int i = 0; i < 12; i++) lu.Invoke(pc.Body, null);
        }

        /// <summary>Без окна: рука с полоской здоровья (взгляд вниз под разными углами) и походка героя.</summary>
        static void StepArm(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame >= 6 && gm.State == GameState.Playing && chestLife > 0f) gm.Hero.Life = chestLife;
            switch (frame)
            {
                case 3: gm.State = GameState.Playing; gm.Hero.Life = 100f; chestLife = 100f; break;
                case 5: pc.TeleportTo(new Vector3(33f, 0.1f, 45f), 0f); SetPitch(pc, 0f); break;
                case 30: Capture("a00_forward"); break;
                case 31: SetPitch(pc, 30f); break;
                case 70: Capture("a01_p30"); report.AppendLine("raise p30=" + pc.Body.ArmRaise.ToString("F2")); break;
                case 71: SetPitch(pc, 42f); break;
                case 110: Capture("a02_p42"); report.AppendLine("raise p42=" + pc.Body.ArmRaise.ToString("F2")); break;
                case 111: SetPitch(pc, 55f); chestLife = 50f; break;
                case 150:
                    {
                        Capture("a03_p55_life50");
                        report.AppendLine("raise p55=" + pc.Body.ArmRaise.ToString("F2"));
                        ArmReport(pc);
                        BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
                        Transform[] fore = (Transform[])typeof(HeroBody).GetField("fore", bf).GetValue(pc.Body);
                        Vector3 mid = (fore[0].position + pc.Body.Hands[0].Root.position) * 0.5f;
                        CaptureLook("z01_arm_close", mid, 32f);
                        CaptureLook("z02_arm_close_wide", mid, 55f);
                    }
                    break;
                case 151: SetPitch(pc, 70f); chestLife = 18f; break;
                case 190: Capture("a04_p70_life18"); break;
                case 191: SetPitch(pc, 45f); chestLife = 100f; break;
                case 230: Capture("a05_p45_life100"); break;
                case 231: SetPitch(pc, 0f); break;
                case 260: ForceWalk(pc, 0f, 1f); Capture("w00_walk_phase0"); break;
                case 261: ForceWalk(pc, 1.57f, 1f); Capture("w01_walk_phase1"); break;
                case 262: ForceWalk(pc, 3.14f, 1f); Capture("w02_walk_phase2"); break;
                case 263: ForceWalk(pc, 4.71f, 1f); Capture("w03_walk_phase3"); break;
                case 264: SetPitch(pc, 75f); break;
                case 290: ForceWalk(pc, 1.0f, 1f); Capture("w04_legs_a"); break;
                case 291: ForceWalk(pc, 4.1f, 1f); Capture("w05_legs_b"); break;
                case 300: Finish("OK", errors > 0 ? 2 : 0); break;
            }
        }

        /// <summary>Крупный план без пост-обработки: камера героя смотрит в точку lookAt с узким FOV, кадр 1280x720.</summary>
        static void CaptureLook(string name, Vector3 lookAt, float fov)
        {
            GameManager gm = GameManager.Instance;
            Camera cam = gm.Player.Cam;
            RenderTexture rt = new RenderTexture(1280, 720, 24);
            RenderTexture prevT = cam.targetTexture;
            Quaternion prevR = cam.transform.rotation;
            float prevF = cam.fieldOfView;
            cam.transform.rotation = Quaternion.LookRotation(lookAt - cam.transform.position, cam.transform.up);
            cam.fieldOfView = fov;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevT;
            cam.transform.rotation = prevR;
            cam.fieldOfView = prevF;
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        static void ArmReport(PlayerController pc)
        {
            BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            Transform[] fore = (Transform[])typeof(HeroBody).GetField("fore", f).GetValue(pc.Body);
            Transform wrist = pc.Body.Hands[0].Root;
            Vector3 mid = (fore[0].position + wrist.position) * 0.5f;
            Vector3 toCam = (pc.Cam.transform.position - mid).normalized;
            report.AppendLine("arm: forearm axis=" + fore[0].up.ToString("F2") + " palmSide(+Z)=" + fore[0].forward.ToString("F2") + " toCam=" + toCam.ToString("F2")
                + " cos(palmSide,toCam)=" + Vector3.Dot(fore[0].forward, toCam).ToString("F2") + " len=" + fore[0].localScale.y.ToString("F2")
                + " camPos=" + pc.Cam.transform.position.ToString("F2") + " mid=" + mid.ToString("F2"));
            GameObject disp = fore[0].Find("HealthDisplay") != null ? fore[0].Find("HealthDisplay").gameObject : null;
            report.AppendLine("  display active=" + (disp != null && disp.activeSelf) + " raise=" + pc.Body.ArmRaise.ToString("F2"));
        }

        /// <summary>Без окна: герой идёт на витрины магазинов — они должны остановить его у переплётов (в нишу зайти нельзя).</summary>
        static void StepShopWalk(GameManager gm)
        {
            PlayerController pc = gm.Player;
            int perShop = 70;
            int slot = (frame - 5) / perShop;
            int phase = (frame - 5) % perShop;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame < 5) return;
            if (slot >= 12 || slot >= LevelBuilder.ShopFronts.Count)
            {
                Finish("OK", errors > 0 ? 2 : 0);
                return;
            }
            int si = slot * 8 % LevelBuilder.ShopFronts.Count;
            Vector3 wall = LevelBuilder.ShopFronts[si];
            Vector3 n = LevelBuilder.ShopFrontNormals[si];
            gm.Hero.Life = 100f;
            if (phase == 0)
            {
                Vector3 p = wall + n * 2.2f;
                p.y = 0.1f;
                pc.TeleportTo(p, Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg);
                SetPitch(pc, 0f);
            }
            else if (phase < 60)
            {
                // идти на стену со скоростью бега (как при зажатой W)
                pc.Cc.Move(-n * 4.2f * Time.deltaTime);
            }
            else if (phase == 62)
            {
                float depth = Vector3.Dot(pc.transform.position - wall, n);
                report.AppendLine("shop " + si + ": stopped " + depth.ToString("F2") + " m from the wall (display depth 0.64)");
                if (depth < 0.64f) report.AppendLine("  [BAD] hero got into the display");
            }
        }

        static int walkIdx = 1;

        /// <summary>Без окна: живой человек идёт к герою — снимки в разных фазах шага.</summary>
        static void StepWalk(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 3) gm.State = GameState.Playing;
            if (frame >= 3) gm.Hero.Life = 100f;
            Survivor s = Survivor.All[walkIdx];
            if (frame == 6)
            {
                Vector3 tgt = s.transform.position;
                bool found = false;
                for (int k = 0; k < 24 && !found; k++)
                {
                    float y2 = k * 15f;
                    float a2 = y2 * Mathf.Deg2Rad;
                    Vector3 p2 = tgt - new Vector3(Mathf.Sin(a2), 0f, Mathf.Cos(a2)) * 7f;
                    p2.y = 0.1f;
                    if (CityMap.CanStand(p2.x, p2.z, 0.4f, true) && CityMap.LineClear(p2.x, p2.z, tgt.x, tgt.z, 0.25f, true))
                    {
                        pc.TeleportTo(p2, y2);
                        found = true;
                    }
                }
                report.AppendLine("walk: found spot=" + found + " target=" + tgt + " speed=" + GameConfig.SurvivorSpeed);
                SetPitch(pc, 3f);
                s.Wake();
            }
            if (frame >= 10 && frame % 7 == 0 && frame < 90)
            {
                Capture("p" + frame.ToString("D2"));
                Vector3 v = s.GetComponent<CharacterController>().velocity;
                report.AppendLine("  frame " + frame + " survivor speed=" + new Vector2(v.x, v.z).magnitude.ToString("F2") + " dist=" + Vector3.Distance(s.transform.position, pc.transform.position).ToString("F1"));
            }
            if (frame == 95) Finish("OK", errors > 0 ? 2 : 0);
        }

        /// <summary>Без окна: расстановка — люди не внутри предметов, у фонарей есть подход.</summary>
        static void StepLayout(GameManager gm)
        {
            if (frame != 5)
            {
                if (frame > 8) Finish("OK", errors > 0 ? 2 : 0);
                return;
            }
            Physics.SyncTransforms();
            int bad = 0;
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Vector3 p = Survivor.All[i].transform.position;
                Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.4f, 0.3f);
                for (int k = 0; k < hits.Length; k++)
                {
                    Collider c = hits[k];
                    if (c.GetComponentInParent<Survivor>() != null || c.GetComponentInParent<PlayerController>() != null || c.name == "Floor") continue;
                    bad++;
                    report.AppendLine("survivor " + i + " at " + p + " overlaps " + c.name);
                    break;
                }
            }
            report.AppendLine("survivors=" + Survivor.All.Count + " overlapping solids=" + bad + " moved by builder=" + LevelBuilder.SurvivorsMoved);

            // витрины магазинов твёрдые: в них нельзя зайти и встать между полками
            int shopBad = 0;
            for (int i = 0; i < LevelBuilder.ShopFronts.Count; i++)
            {
                Vector3 pr = LevelBuilder.ShopFronts[i] + LevelBuilder.ShopFrontNormals[i] * 0.3f + Vector3.up * 1.0f;
                bool solid = false;
                Collider[] hs = Physics.OverlapSphere(pr, 0.12f);
                for (int k = 0; k < hs.Length; k++)
                {
                    if (hs[k].name == "Shopfront") { solid = true; break; }
                }
                if (!solid) shopBad++;
            }
            report.AppendLine("shop fronts=" + LevelBuilder.ShopFronts.Count + " not solid=" + shopBad + " park centers=" + LevelBuilder.ParkCenters.Count);

            Collider[] cols = UnityEngine.Object.FindObjectsByType<Collider>();
            int cars = 0;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].gameObject.name == "Car") cars++;
            }
            int lampsChecked = 0;
            int lampsCrowded = 0;
            float closestCar = 999f;
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                Vector3 b = l.BasePos;
                float nearest = 999f;
                string nname = "";
                for (int k = 0; k < cols.Length; k++)
                {
                    Collider c = cols[k];
                    string n = c.gameObject.name;
                    if (n != "Car" && n != "Dumpster" && n != "Crate" && n != "Barrels" && n != "Bench" && n != "Planter" && n != "Tree" && n != "Solid"
                        && n != "Shopfront" && n != "CafeTable" && n != "Mailbox" && n != "PhoneBooth" && n != "BikeRack" && n != "Vending" && n != "Kiosk"
                        && n != "Column" && n != "Board" && n != "TrafficPole" && n != "Fountain" && n != "Carousel" && n != "SlideTower" && n != "Sandbox") continue;
                    Vector3 cp = c.ClosestPoint(new Vector3(b.x, 0.5f, b.z));
                    float d = new Vector2(cp.x - b.x, cp.z - b.z).magnitude;
                    // витрины стоят вдоль стены по бокам от лампы (так задумано): тесно только если столб упёрся в витрину
                    if (n == "Shopfront" && d >= 0.45f) continue;
                    if (d < nearest) { nearest = d; nname = n; }
                    if (n == "Car" && d < closestCar) closestCar = d;
                }
                lampsChecked++;
                if (nearest < 1.2f)
                {
                    lampsCrowded++;
                    report.AppendLine("lamp " + i + " at " + b + ": " + nname + " only " + nearest.ToString("F2") + " m away");
                }
            }
            report.AppendLine("cars=" + cars + " lamps=" + lampsChecked + " crowded(<1.2 m)=" + lampsCrowded + " closest car edge to any lamp=" + closestCar.ToString("F2") + " m");

            // фонарь целиком (основание, мачта, кронштейн, плафон) не должен задевать здания и прочее твёрдое
            int lampInside = 0, lampHugging = 0, lampsTotal = 0, lampCramped = 0, lampTight = 0, lampSnug = 0;
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                if (l.IsHearth) continue;
                lampsTotal++;
                string inside = l.SolidContact(0f);
                if (inside != null)
                {
                    lampInside++;
                    report.AppendLine("lamp " + i + " at " + l.BasePos + " arm " + l.ArmDir + " is inside " + inside);
                    continue;
                }
                string near = l.SolidContact(0.3f);
                if (near != null && near != "TunnelRoof")   // фонари тоннеля висят у потолка: так задумано
                {
                    lampHugging++;
                    report.AppendLine("lamp " + i + " at " + l.BasePos + " arm " + l.ArmDir + " hugs " + near + " (<0.3 m)");
                }
                if (!LevelBuilder.LampHasRoom(l.BasePos, l.ArmDir, 0.8f))
                {
                    lampCramped++;
                    report.AppendLine("lamp " + i + " at " + l.BasePos + " arm " + l.ArmDir + " has less than 0.8 m to a side wall");
                }
                else if (!LevelBuilder.LampHasRoom(l.BasePos, l.ArmDir, 1.5f))
                {
                    lampTight++;
                    report.AppendLine("lamp " + i + " at " + l.BasePos + " arm " + l.ArmDir + " has 0.8-1.5 m to a side wall");
                }
                else if (!LevelBuilder.LampHasRoom(l.BasePos, l.ArmDir, 2.4f))
                {
                    lampSnug++;
                }
            }
            report.AppendLine("street lamps=" + lampsTotal + " inside solids=" + lampInside + " hugging solids(<0.3 m)=" + lampHugging
                + " cramped(<0.8 m to a side wall)=" + lampCramped + " tight(0.8-1.5 m)=" + lampTight + " snug(1.5-2.4 m)=" + lampSnug);
            string lampList = "";
            for (int i = 0; i < Lamp.All.Count && i < 30; i++)
                lampList += i + ":(" + Lamp.All[i].BasePos.x.ToString("F1") + "," + Lamp.All[i].BasePos.z.ToString("F1") + ")" + (Lamp.All[i].IsHearth ? "H" : "") + " ";
            report.AppendLine("first lamps: " + lampList);

            // хранилища света: сколько, где, решаются ли (трассировщик луча из места Stand при правильных зеркалах)
            report.AppendLine("light vaults=" + LevelBuilder.Vaults.Count);
            for (int i = 0; i < LevelBuilder.Vaults.Count; i++)
            {
                LightVault lv = LevelBuilder.Vaults[i];
                string steps = "";
                for (int k = 0; k < lv.Mirrors.Count; k++) steps += lv.Mirrors[k].Step + "->" + lv.Solution[k] + " ";
                report.AppendLine("vault tier " + lv.Tier + " centre " + lv.Center + " gate " + lv.Gate + " stand " + lv.Stand + " mirrors " + steps
                    + (lv.Lens != null ? "lens " : "") + "solvable=" + LevelBuilder.VaultSolvable(lv) + " sealHp=" + lv.Seal.Health);
            }

            System.Text.StringBuilder treeLog = new System.Text.StringBuilder();
            int inTrees = LevelBuilder.FurnitureInTrees(treeLog);
            report.Append(treeLog.ToString());
            report.AppendLine("street furniture standing in tree trunks=" + inTrees);
        }

        /// <summary>
        /// Хранилища света, по 260 кадров на каждое: кадр с места Stand при неверном зеркале, луч мимо печати, зеркала поставлены верно,
        /// луч через зеркала и линзу в печать, печать горит и сгорает, вид на открытую каморку. Читает отчёт: здоровье печати по ходу.
        /// </summary>
        static void StepVault(GameManager gm)
        {
            PlayerController pc = gm.Player;
            if (frame == 3)
            {
                gm.State = GameState.Playing;
                gm.Hero.Life = 100f;
            }
            if (frame < 5) return;
            int vi = (frame - 5) / 260;
            int ph = (frame - 5) % 260;
            if (vi >= LevelBuilder.Vaults.Count)
            {
                if (ph == 0) Finish("OK", errors > 0 ? 2 : 0);
                return;
            }
            LightVault lv = LevelBuilder.Vaults[vi];
            gm.Hero.Life = 100f;
            string tag = "v" + (vi + 1);
            Vector3 toMirror = lv.StandAim - new Vector3(lv.Stand.x, lv.StandAim.y, lv.Stand.z);
            toMirror.y = 0f;
            float yaw = Mathf.Atan2(toMirror.x, toMirror.z) * Mathf.Rad2Deg;
            if (ph == 0)
            {
                for (int k = 0; k < lv.Mirrors.Count; k++) lv.Mirrors[k].SetStep((lv.Solution[k] + 2) & 3);
                pc.TeleportTo(lv.Stand, yaw);
                SetPitch(pc, 0f);
                report.AppendLine(tag + " tier " + lv.Tier + " sealHp=" + lv.Seal.Health + " solvable=" + LevelBuilder.VaultSolvable(lv));
            }
            if (ph == 8) Capture(tag + "_a_unsolved");
            if (ph == 10)
            {
                pc.Combat.ForceHold = true;
                pc.Combat.FireShot();
            }
            if (ph == 40)
            {
                Capture(tag + "_b_wrong_angle");
                report.AppendLine(tag + " wrong mirror angle: sealHp=" + lv.Seal.Health + " bounces=" + BeamTrace.Bounces);
            }
            if (ph == 45) pc.Combat.ForceHold = false;
            if (ph == 60) for (int k = 0; k < lv.Mirrors.Count; k++) lv.Mirrors[k].SetStep(lv.Solution[k]);
            if (ph == 64)
            {
                pc.Combat.ForceHold = true;
                pc.Combat.FireShot();
            }
            if (ph == 100)
            {
                Capture(tag + "_c_solved");
                report.AppendLine(tag + " solved: sealHp=" + lv.Seal.Health.ToString("F0") + " bounces=" + BeamTrace.Bounces + " segs=" + BeamTrace.Count);
            }
            if (ph == 190) Capture(tag + "_d_burning");
            if (ph == 235)
            {
                pc.Combat.ForceHold = false;
                report.AppendLine(tag + " end: seal dead=" + lv.Seal.Dead + " orbs=" + LightOrb.All.Count);
                Vector3 outward = lv.Gate - lv.Center;
                outward.y = 0f;
                outward.Normalize();
                pc.TeleportTo(lv.Gate + outward * 4.5f, Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg);
                SetPitch(pc, 12f);
            }
            if (ph == 250) Capture(tag + "_e_open");
        }

        /// <summary>
        /// Концовка: кадры поездки в автобусе (салон, люди, рассвет, Тьма), пробуждение за кухонным столом (голова рядом с кружкой, а не в ней),
        /// руки, мокрый стул. Времена — в секундах сценария; кухня начинается с EndingDirector.TKitchen. Перемотка DebugSeek, кадр через 8 кадров.
        /// </summary>
        static void StepEnding(GameManager gm, bool windowed)
        {
            float k0 = EndingDirector.TKitchen;
            string[] names;
            float[] times;
            if (windowed)
            {
                // окно редактора: кадры с настоящими субтитрами, полосами и титулом (IMGUI), потом экран итога
                names = new string[] { "s01_ride_caption", "s02_ride_flicker", "s03_ride_trap", "s04_kitchen_wake", "s05_kitchen_coffee", "s06_kitchen_warm", "s07_kitchen_wet", "s08_kitchen_saved", "s09_title" };
                times = new float[] { 1.8f, 12.2f, 14.4f, k0 + 2.0f, k0 + 4.4f, k0 + 13.0f, k0 + 18.5f, k0 + 22.6f, k0 + 28.0f };
            }
            else
            {
                names = new string[]
                {
                    "e01_ride_start", "e02_ride_left", "e03_ride_back", "e04_ride_dawn", "e05_ride_flicker", "e06_ride_gate", "e07_ride_dark", "e08_ride_engulf",
                    "e09_kitchen_sleep", "e10_kitchen_jolt", "e11_kitchen_rise", "e12_kitchen_window", "e13_kitchen_mug", "e14_kitchen_hands", "e15_kitchen_wet",
                    "e16_kitchen_wet2", "e17_kitchen_forward"
                };
                times = new float[]
                {
                    0.9f, 3.4f, 4.8f, 7.4f, 10.2f, 12.4f, 13.7f, 14.6f,
                    k0 + 0.6f, k0 + 1.4f, k0 + 2.0f, k0 + 2.9f, k0 + 4.6f, k0 + 7.6f, k0 + 10.8f,
                    k0 + 13.4f, k0 + 17.5f
                };
            }
            if (frame == 3)
            {
                gm.State = GameState.Playing;
                gm.Hero.Life = 100f;
            }
            if (frame == 6)
            {
                if (Bus.Instance != null) Bus.Instance.BoardedCount = 6;
                gm.Win();
                report.AppendLine("after Win: state=" + gm.State + " ending active=" + gm.Ending.Active + " duration=" + gm.Ending.Duration.ToString("F1"));
            }
            if (frame < 10) return;
            int slot = (frame - 10) / 16;
            int phase = (frame - 10) % 16;
            if (slot >= names.Length)
            {
                int rel = frame - (10 + names.Length * 16);
                if (rel == 0)
                {
                    report.AppendLine("mug clearance=" + gm.Ending.MugClearance().ToString("F3") + " m, table clearance=" + gm.Ending.TableClearance().ToString("F3") + " m");
                    if (!windowed) Finish("OK", errors > 0 ? 2 : 0);
                    else gm.Ending.Skip();
                }
                if (windowed)
                {
                    if (rel == 220) Shoot("s10_result");
                    if (rel == 240)
                    {
                        report.AppendLine("end state=" + gm.State + " winFade=" + gm.WinFade.ToString("F2"));
                        Finish("OK", errors > 0 ? 2 : 0);
                    }
                }
                return;
            }
            gm.Hero.Life = 100f;
            if (phase == 0) gm.Ending.DebugSeek(times[slot]);
            if (phase == 12)
            {
                if (windowed) Shoot(names[slot]);
                else Capture(names[slot]);
            }
        }

        static void Shoot(string name)
        {
            string path = Path.Combine(outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            report.AppendLine("screenshot " + name + " (" + Screen.width + "x" + Screen.height + ")");
        }

        static void SetMenuPage(GameManager gm, int pageIdx)
        {
            FieldInfo fi = typeof(MenuUI).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) { report.AppendLine("[smoke] MenuUI.page not found"); return; }
            fi.SetValue(gm.Menu, Enum.ToObject(fi.FieldType, pageIdx));
            FieldInfo sel = typeof(MenuUI).GetField("sel", BindingFlags.Instance | BindingFlags.NonPublic);
            if (sel != null) sel.SetValue(gm.Menu, 0);
        }

        static void MenuActivate(GameManager gm, int i)
        {
            MethodInfo mi = typeof(MenuUI).GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi == null) { report.AppendLine("[smoke] MenuUI.Activate not found"); return; }
            mi.Invoke(gm.Menu, new object[] { i });
        }

        static void SaveGui(string name)
        {
            guiCap.capture = false;
            RenderTexture rt = guiCap.target;
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            // увеличить вдвое, чтобы текст читался
            RenderTexture big = new RenderTexture(rt.width * 2, rt.height * 2, 0);
            Graphics.Blit(tex, big);
            RenderTexture.active = big;
            Texture2D tex2 = new Texture2D(big.width, big.height, TextureFormat.RGB24, false);
            tex2.ReadPixels(new Rect(0, 0, big.width, big.height), 0, 0);
            tex2.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex2.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(tex2);
            big.Release();
            UnityEngine.Object.DestroyImmediate(big);
            report.AppendLine("gui shot " + name);
        }

        // ------------------------------------------------------------------ утилиты

        static int CountActive(Transform root, string name)
        {
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.name == name && c.gameObject.activeSelf) n++;
            }
            return n;
        }

        static void CallPrivate(object target, string method)
        {
            MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi == null) { report.AppendLine("[smoke] method not found: " + method); return; }
            mi.Invoke(target, null);
        }

        static void SetPitch(PlayerController pc, float deg)
        {
            FieldInfo fi = typeof(PlayerController).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi != null) fi.SetValue(pc, deg);
        }

        static void Capture(string name)
        {
            GameManager gm = GameManager.Instance;
            Camera cam = gm.Player.Cam;
            RenderTexture scene = new RenderTexture(640, 360, 24);
            RenderTexture fin = new RenderTexture(1280, 720, 24);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = scene;
            cam.Render();
            cam.targetTexture = prev;

            GameObject pcGo = GameObject.Find("PostCamera");
            Transform quad = pcGo != null ? pcGo.transform.Find("PostQuad") : null;
            if (pcGo != null && quad != null)
            {
                Camera pcam = pcGo.GetComponent<Camera>();
                Material pm = quad.GetComponent<Renderer>().sharedMaterial;
                pm.mainTexture = scene;
                pm.SetFloat("_Brightness", Gfx.Brightness);
                pm.SetFloat("_Glitch", 0f);
                quad.localScale = new Vector3(2f * 16f / 9f, 2f, 1f);
                RenderTexture pPrev = pcam.targetTexture;
                pcam.targetTexture = fin;
                pcam.Render();
                pcam.targetTexture = pPrev;
            }
            else
            {
                report.AppendLine("[smoke] no PostCamera/PostQuad: saving raw scene");
                Graphics.Blit(scene, fin);
            }

            RenderTexture old = RenderTexture.active;
            RenderTexture.active = fin;
            Texture2D tex = new Texture2D(fin.width, fin.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, fin.width, fin.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            scene.Release();
            fin.Release();
            UnityEngine.Object.DestroyImmediate(scene);
            UnityEngine.Object.DestroyImmediate(fin);
            Vector3 p = gm.Player.transform.position;
            report.AppendLine("shot " + name + " at (" + p.x.ToString("F0") + "," + p.z.ToString("F0") + ") state=" + gm.State
                + " life=" + (gm.Hero != null ? gm.Hero.Life.ToString("F1") : "?"));
        }

        static void Finish(string reason, int code)
        {
            if (finished) return;
            finished = true;
            string text = "SMOKE RESULT: " + reason + "\nframes=" + frame + " errors=" + errors + " warnings=" + warnings
                + (editorNoise > 0 ? " (ignored editor-internal errors: " + editorNoise + ")" : "") + "\n" + report;
            try { File.WriteAllText(Path.Combine(outDir, "smoke_result.txt"), text, Encoding.UTF8); } catch (Exception) { }
            Debug.Log("[SMOKE] " + text);
            SessionState.SetBool(Flag, false);
            EditorApplication.Exit(code);
        }
    }
}
#endif
