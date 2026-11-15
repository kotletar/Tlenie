using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Opt-in player regression: -pg-smoke -pg-smoke-out "folder". Uses the real gameplay input path.</summary>
    public sealed class GameSmoke : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string OutputDirectory { get; private set; }
        readonly StringBuilder report = new StringBuilder();
        GameManager gm;
        ReplayInput input;
        string output;
        int failures;
        bool finished;
        float began;

        public static void AttachIfRequested(GameManager manager)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-pg-smoke") < 0) return;
            manager.gameObject.AddComponent<GameSmoke>();
        }
        void Awake()
        {
            Running = true;
            gm = GetComponent<GameManager>();
            input = new ReplayInput();
            GameInput.Provider = input;
            output = Path.Combine(Application.persistentDataPath, "Smoke");
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-pg-smoke-out");
            if (i >= 0 && i + 1 < args.Length) output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(output);
            OutputDirectory = output;
            // Records of this scenario live next to its report: the player's own table is never touched or filled with test runs.
            RecordsStore.PathOverride = Path.Combine(output, "records.txt");
            if (File.Exists(RecordsStore.PathOverride)) File.Delete(RecordsStore.PathOverride);
            RecordsStore.Forget();
            Application.runInBackground = true;
            // Keep the scenario reproducible without changing saved user preferences.
            GameSettings.Difficulty = Challenge.Standard;
            GameSettings.EffectsVolume = GameSettings.AmbienceVolume = GameSettings.InterfaceVolume = 1f;
            GameSettings.PauseOnFocusLost = false;
            GameSettings.Apply();
            began = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
        }
        void OnLog(string message, string stack, LogType kind)
        {
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
            {
                failures++;
                report.AppendLine(kind + ": " + message + "\n" + stack);
            }
        }
        void Update()
        {
            if (!finished && Time.realtimeSinceStartup - began > 170f)
            { Check(false, "timeout"); Finish(); }
        }
        void Check(bool condition, string message)
        {
            if (!condition) failures++;
            string line = (condition ? "PASS " : "FAIL ") + message;
            report.AppendLine(line);
            Debug.Log("[PGSmoke] " + line);
        }
        IEnumerator Key(GKey key)
        {
            input.Key(key, true);
            yield return null;
            yield return null;
            input.Key(key, false);
        }

        void Approach(Vector3 focus, float distance)
        {
            Vector3 chosen = focus + Vector3.back * distance;
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                Vector3 point = new Vector3(focus.x + Mathf.Sin(angle) * distance, 0.1f, focus.z - Mathf.Cos(angle) * distance);
                if (!CityMap.CanStand(point.x, point.z, 0.5f, true)) continue;
                bool blocked = false;
                foreach (Collider c in Physics.OverlapSphere(point + Vector3.up * 0.9f, 0.42f))
                    if (!c.isTrigger && !c.transform.IsChildOf(gm.Player.transform)) { blocked = true; break; }
                if (blocked) continue;
                chosen = point;
                break;
            }
            chosen.y = 0.1f;
            Vector3 toward = focus - chosen;
            float yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            gm.Player.TeleportTo(chosen, yaw);
            Vector3 look = focus - gm.Player.Cam.transform.position;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            input.LookOnce(new Vector2(0f, -pitch / gm.Player.MouseSens * (GameSettings.InvertY ? -1f : 1f)));
        }

        /// <summary>Встать в точку stand (на земле) и смотреть в точку aim: поворот по горизонтали сразу, наклон головы — следующим кадром.</summary>
        void FaceFrom(Vector3 stand, Vector3 aim)
        {
            stand.y = 0.1f;
            Vector3 toward = aim - stand;
            float yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            gm.Player.TeleportTo(stand, yaw);
            Vector3 look = aim - gm.Player.Cam.transform.position;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            input.LookOnce(new Vector2(0f, -pitch / gm.Player.MouseSens * (GameSettings.InvertY ? -1f : 1f)));
        }

        IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pg-ai") >= 0)
            {
                yield return AiChecks();
                Finish();
                yield break;
            }
            // Settings are never saved by this scenario. Store anything temporarily adjusted for a check.
            yield return new WaitForSecondsRealtime(1.5f);
            Check(gm.State == GameState.Title && FindMusic().Length == 4, "title and four music stems");
            Check(LevelBuilder.PizzasPlaced == 8 && LevelBuilder.PizzasMissed == 0 && Survivor.All.Count == 10,
                "golden layout: 8 pizzas, 10 survivors");
            // фонари не лезут в здания и не лежат вдоль фасада в углу; уличная мебель не стоит в стволах деревьев
            Physics.SyncTransforms();
            int lampsInSolids = 0, lampsCramped = 0;
            foreach (Lamp l in Lamp.All)
            {
                if (l.IsHearth) continue;
                if (l.SolidContact(0f) != null) lampsInSolids++;
                else if (!LevelBuilder.LampHasRoom(l.BasePos, l.ArmDir, 0.8f)) lampsCramped++;
            }
            Check(lampsInSolids == 0 && lampsCramped == 0, "street lamps stand clear of buildings: inside " + lampsInSolids + ", cramped " + lampsCramped);
            Check(LevelBuilder.FurnitureInTrees(null) == 0, "street furniture never stands in a tree trunk");
            yield return Key(GKey.Enter);
            yield return new WaitForSecondsRealtime(1.2f);
            Check(gm.State == GameState.Intro, "Enter starts intro through menu input");
            yield return Key(GKey.Space);
            yield return new WaitForSecondsRealtime(0.8f);
            Check(gm.State == GameState.Playing && !gm.Intro.Active, "Space skips intro through input");

            // Кисти детальные: у каждой клиновая ладонь, 14 конусных фаланг и 5 ногтей; контракт PalmShot (14 суставов) цел
            int palms = 0, segments = 0, nails = 0;
            bool jointsOk = true;
            for (int ha = 0; ha < 2; ha++)
            {
                HeroBody.HandRig hand = gm.Player.Body.Hands[ha];
                foreach (MeshRenderer part in hand.Root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (part.name == "PalmMesh") palms++;
                    else if (part.name == "Phalanx") segments++;
                    else if (part.name == "Nail") nails++;
                }
                jointsOk &= hand.Fx.joints.Length == 14 && hand.Palm != null;
            }
            Check(palms == 2 && segments == 28 && nails == 10 && jointsOk,
                "each hand has a wedge palm, 14 tapered finger segments and 5 nails (" + palms + " palms, " + segments + " segments, " + nails + " nails for two hands)");

            // Нулевая нормаль даёт в шейдере NaN и чёрное пятно (так чернели кончики пальцев: у миллиметровых граней векторное
            // произведение меньше порога Vector3.normalized). Ни у одного читаемого меша сцены таких нормалей быть не должно.
            int zeroNormals = 0;
            int checkedMeshes = 0;
            foreach (MeshFilter mf in FindObjectsByType<MeshFilter>())
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] normals = mesh.normals;
                if (normals.Length == 0) continue;
                checkedMeshes++;
                for (int ni = 0; ni < normals.Length; ni++)
                    if (normals[ni].sqrMagnitude < 0.25f) zeroNormals++;
            }
            Check(zeroNormals == 0, "no scene mesh has a zero normal, which would render black (" + checkedMeshes + " meshes checked, " + zeroNormals + " bad)");

            float fov = GameSettings.FieldOfView;
            GameSettings.FieldOfView = 92f; GameSettings.Apply();
            Check(Mathf.Abs(gm.Player.Cam.fieldOfView - 92f) < 0.01f, "FOV applies live");
            GameSettings.FieldOfView = fov; GameSettings.Apply();
            bool inverted = GameSettings.InvertY;
            gm.Player.TeleportTo(LevelBuilder.StartPos, 0f);
            GameSettings.InvertY = false;
            input.LookOnce(new Vector2(0f, 4f));
            yield return new WaitForSecondsRealtime(0.1f);
            float normalY = gm.Player.Cam.transform.forward.y;
            gm.Player.TeleportTo(LevelBuilder.StartPos, 0f);
            GameSettings.InvertY = true;
            input.LookOnce(new Vector2(0f, 4f));
            yield return new WaitForSecondsRealtime(0.1f);
            Check(normalY > 0f && gm.Player.Cam.transform.forward.y < 0f, "mouse Y inversion reverses live look input");
            GameSettings.InvertY = inverted;

            float effect = GameSettings.EffectsVolume, ambience = GameSettings.AmbienceVolume, ui = GameSettings.InterfaceVolume;
            GameSettings.EffectsVolume = 0f; AudioMix.ApplyAll();
            Check(gm.Player.Sfx.volume == 0f && gm.Player.DroneSrc.volume > 0f, "effect bus mutes independently of ambience");
            GameSettings.EffectsVolume = effect; GameSettings.AmbienceVolume = 0f; AudioMix.ApplyAll();
            Check(gm.Player.DroneSrc.volume == 0f && gm.Player.Sfx.volume > 0f, "ambience bus mutes independently of effects");
            GameSettings.AmbienceVolume = ambience; GameSettings.InterfaceVolume = ui; AudioMix.ApplyAll();
            Check(FindMusic()[0].isPlaying, "bus controls preserve scheduled music");

            Lamp lamp = null;
            foreach (Lamp l in Lamp.All) if (!l.IsHearth && !l.IsDecoy && !l.Dead && l.Charge > 0.8f) { lamp = l; break; }
            Check(lamp != null, "full usable lamp exists");
            if (lamp != null)
            {
                gm.Hero.SetLife(35f, LightReason.Debug);
                Approach(lamp.FocusPoint, 2.1f);
                yield return new WaitForSecondsRealtime(0.2f);
                float charge = lamp.Charge;
                Check(gm.Player.FocusType == FocusKind.Lamp, "gaze selects lamp");
                input.Key(GKey.E, true);
                yield return new WaitForSecondsRealtime(0.7f);
                input.Key(GKey.E, false);
                Check(lamp.Charge < charge && gm.Economy.Amount(LightReason.Lamp, true, false) > 2f,
                    "holding E transfers lamp light and records actual income");
                gm.SetPaused(true);
                yield return new WaitForSecondsRealtime(0.6f);
                bool quiet = true;
                foreach (AudioSource source in gm.Player.GetComponentsInChildren<AudioSource>())
                    if (source.clip == ProcAudio.Get("inhale") && source.volume > 0.001f) quiet = false;
                Check(quiet, "pausing during inhale fades its loop with unscaled time");
                gm.SetPaused(false);
            }

            yield return new WaitForSecondsRealtime(0.3f);
            Survivor sleeper = Survivor.All[0];
            gm.Hero.SetLife(90f, LightReason.Debug);
            Approach(sleeper.FocusPoint, 2f);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(gm.Player.FocusType == FocusKind.Sleeper, "gaze selects sleeping survivor");
            input.Key(GKey.F, true);
            yield return new WaitForSecondsRealtime(1.5f);
            input.Key(GKey.F, false);
            Check(sleeper.IsAwake && gm.SurvivorsWoken > 0 && gm.Economy.Amount(LightReason.Wake, false, false) >= 11f,
                "holding F wakes a survivor through focus / actions / economy");

            Pizza pizza = Pizza.All[0];
            gm.Hero.SetLife(40f, LightReason.Debug);
            Approach(pizza.FocusPoint, 2f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(gm.Player.FocusType == FocusKind.Pizza, "gaze selects pizza");
            yield return Key(GKey.E);
            yield return new WaitForSecondsRealtime(2f);
            Check(pizza.Finished && pizza.BitesDone == 3 && Math.Abs(gm.Economy.Amount(LightReason.Pizza, true, false) - 24) < 0.01,
                "E eats pizza: three bites, exactly 24 light");

            gm.Hero.SetLife(100f, LightReason.Debug);
            gm.Player.TeleportTo(LevelBuilder.StartPos, 0f);
            yield return null;
            Vector3 before = gm.Player.transform.position;
            input.Key(GKey.W, true); input.Key(GKey.Q, true);
            yield return new WaitForSecondsRealtime(0.8f);
            input.Key(GKey.W, false); input.Key(GKey.Q, false);
            Check(Vector3.Distance(before, gm.Player.transform.position) > 1f, "W moves through native character controller");
            yield return new WaitForSecondsRealtime(0.2f);

            // Шаги привязаны к качанию головы: при ходьбе 4,2 м/с это ~7,6 шага за 3 с (раньше таймер давал ~5,8).
            System.Reflection.FieldInfo footField = typeof(PlayerController).GetField("lastFoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            gm.Player.TeleportTo(LevelBuilder.StartPos, 0f);
            yield return null;
            int foot0 = (int)footField.GetValue(gm.Player);
            input.Key(GKey.W, true);
            yield return new WaitForSecondsRealtime(3f);
            input.Key(GKey.W, false);
            int footSteps = (int)footField.GetValue(gm.Player) - foot0;
            Check(footSteps >= 6 && footSteps <= 9, "footsteps follow the head bob: " + footSteps + " steps in 3 s of walking");
            yield return new WaitForSecondsRealtime(0.2f);

            Growth wall = Growth.All[0];
            Vector3 target = wall.Center + Vector3.up * 2f;
            Approach(target, 8f);
            gm.Hero.SetLife(100f, LightReason.Debug);
            input.Mouse(0, true);
            yield return new WaitForSecondsRealtime(4.5f);
            input.Mouse(0, false);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(wall.Dead && gm.Economy.Amount(LightReason.Shot, false, false) >= 4.9 && gm.Economy.Amount(LightReason.Beam, false, false) > 15,
                "holding LMB merges beam and burns blocking darkness wall");
            gm.Hero.SetLife(90f, LightReason.Debug);
            input.Mouse(1, true);
            yield return null; yield return null;
            input.Mouse(1, false);
            yield return new WaitForSecondsRealtime(0.6f);
            Check(gm.Economy.Amount(LightReason.Flash, false, false) >= 19.9, "RMB flash uses its own expense category");

            // Хранилища света: печать не берёт прямой свет, зеркало поворачивается на E, отражённый луч сжигает печать, внутри свет.
            bool allSolvable = LevelBuilder.Vaults.Count == 3;
            foreach (LightVault lv in LevelBuilder.Vaults) allSolvable &= LevelBuilder.VaultSolvable(lv);
            Check(allSolvable, "three light vaults exist and every one is solvable by the real beam trace");
            LightVault vault = LevelBuilder.Vaults.Count > 0 ? LevelBuilder.Vaults[0] : null;
            if (vault != null)
            {
                Enemy.ClearAll();
                gm.Hero.SetLife(100f, LightReason.Debug);
                Vector3 outward = vault.Gate - vault.Center;
                outward.y = 0f;
                outward.Normalize();
                FaceFrom(vault.Gate + outward * 6f, vault.Seal.Center + Vector3.up * 1.6f);
                yield return new WaitForSecondsRealtime(0.3f);
                input.Mouse(0, true);
                yield return new WaitForSecondsRealtime(1.5f);
                input.Mouse(0, false);
                yield return new WaitForSecondsRealtime(0.3f);
                Check(!vault.Seal.Dead && vault.Seal.Health >= vault.Seal.MaxHealth - 0.01f, "darkness seal ignores direct light");

                LightOptic mirror = vault.Mirrors[0];
                Vector3 toMirror = mirror.transform.position - vault.Stand;
                toMirror.y = 0f;
                toMirror.Normalize();
                FaceFrom(mirror.transform.position - toMirror * 2f, mirror.FocusPoint);
                yield return new WaitForSecondsRealtime(0.3f);
                Check(gm.Player.FocusType == FocusKind.Optic, "gaze selects the mirror");
                int presses = (vault.Solution[0] - mirror.Step + 4) & 3;
                for (int i = 0; i < presses; i++)
                {
                    yield return Key(GKey.E);
                    yield return null;
                }
                Check(presses > 0 && mirror.Step == vault.Solution[0], "E turns the mirror to the solution (" + presses + " presses)");

                gm.Hero.SetLife(100f, LightReason.Debug);
                FaceFrom(vault.Stand, vault.StandAim);
                yield return new WaitForSecondsRealtime(0.3f);
                input.Mouse(0, true);
                yield return new WaitForSecondsRealtime(4.5f);
                input.Mouse(0, false);
                yield return new WaitForSecondsRealtime(0.4f);
                int freed = 0;
                foreach (LightOrb orb in LightOrb.All)
                    if (!orb.Collected && Vector3.Distance(orb.transform.position, vault.Center) < 3f) freed++;
                Check(vault.Seal.Dead && freed >= 3, "a reflected beam burns the seal and frees the light inside (" + freed + " shards)");

                // Печать хранилища — не стена на дороге: цель дня про неё молчит; открытое хранилище остаётся открытым.
                vault.Seal.ResetToInitial();
                Check(vault.Seal.Dead, "an opened vault stays open when the walls regrow after dying");
                if (LevelBuilder.Vaults.Count > 1)
                {
                    LightVault other = LevelBuilder.Vaults[1];
                    Vector3 away = other.Gate - other.Center;
                    away.y = 0f;
                    gm.Player.TeleportTo(other.Gate + away.normalized * 5f + Vector3.up * 0.1f, 0f);
                    bool nearWall = false;
                    foreach (Growth g in Growth.All)
                        if (!g.Sealed && !g.Dead && Vector3.Distance(g.Center, gm.Player.transform.position) < 30f) nearWall = true;
                    if (!nearWall) Check(!gm.ObjectiveText().Contains("стена Тьмы"), "a vault seal is not announced as a wall blocking the road");
                }
            }

            yield return Key(GKey.Esc);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(gm.Paused && Time.timeScale == 0f, "Escape pauses gameplay");
            double lightAtPause = gm.Economy.ExpectedLight;
            yield return new WaitForSecondsRealtime(0.3f);
            Check(Math.Abs(gm.Economy.ExpectedLight - lightAtPause) < 0.001, "pause does not spend or earn light");
            yield return Key(GKey.Esc);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(!gm.Paused && Time.timeScale == 1f, "Escape resumes gameplay");
            GameSettings.PauseOnFocusLost = true;
            gm.SendMessage("OnApplicationFocus", false);
            Check(gm.Paused && Time.timeScale == 0f, "focus loss pauses an active game");
            gm.SendMessage("OnApplicationFocus", true);
            Check(gm.Paused, "focus return waits for the player to resume");
            gm.SetPaused(false);
            GameSettings.PauseOnFocusLost = false;
            Check(Math.Abs(gm.Economy.ExpectedLight - gm.Hero.Life) < 0.01, "ledger reconciles with actual hero light");

            Enemy.ClearAll();
            Bus bus = Bus.Instance;
            gm.Hero.SetLife(100f, LightReason.Debug);
            Approach(bus.FocusPoint, 1.2f);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(gm.Player.FocusType == FocusKind.Bus, "gaze selects bus door");

            // Музыка последней минуты: настоящий файл Resources/Music/FinalMinute есть в сборке и читается как клип. На время проверок его
            // заменяет тихий клип на 75 с, чтобы настоящая музыка не гремела из колонок (подмена остаётся до конца проверок).
            MusicDirector music = GetComponent<MusicDirector>();
            AudioClip realTrack = music.FinalClip;
            Check(realTrack != null && realTrack.channels == 2 && realTrack.length > 55f && realTrack.length < 75f,
                "the final-minute track from Resources/Music is in the build and loads as a stereo clip" +
                (realTrack != null ? " (" + realTrack.length.ToString("0.0") + " s, " + realTrack.frequency + " Hz)" : " (not found)"));
            AudioClip finalTrack = AudioClip.Create("TestFinalTrack", 8000 * 75, 1, 8000, false);
            music.FinalClip = finalTrack;

            input.Key(GKey.F, true);
            yield return new WaitForSecondsRealtime(5.5f);
            input.Key(GKey.F, false);
            Check(bus.State == Bus.Mode.Running && gm.Dir.FinalStand && bus.Charge >= GameConfig.BusNeed && gm.Economy.Amount(LightReason.FeedBus, false, false) > 0,
                "F charges bus and starts final stand; hero contribution=" + gm.Economy.Amount(LightReason.FeedBus, false, false).ToString("0.0"));
            Check(Math.Abs(bus.StandLength - 60f) < 0.01f && bus.FinalLeft > 50f && bus.FinalLeft < 60f,
                "the final stand lasts a whole minute (" + bus.FinalLeft.ToString("0.0") + " s left, length " + bus.StandLength.ToString("0") + " s)");
            Check(gm.Dir.InHearth && gm.Dir.IsInHearth(bus.DoorPos), "the running bus is the last hearth: light does not smolder at its door");
            yield return new WaitForSecondsRealtime(3f);
            Check(music.TargetIntensity == 1f && music.Intensity > 0.85f, "final stand raises adaptive score");

            // Дорожка на минуту (подставной тихий клип) стартует вместе с обороной и забирает музыку у адаптивных слоёв
            gm.Hero.SetLife(100f, LightReason.Debug);
            yield return new WaitForSecondsRealtime(1.5f);
            AudioSource[] stems = FindMusic();
            Check(music.FinalTrackActive && music.FinalTrackLevel > 0.95f && stems[0].volume < 0.05f && stems[3].volume < 0.05f,
                "the last minute plays its own track and the stems step aside");
            gm.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(music.FinalTrackActive && music.FinalTrackHeld, "the track waits while the game is paused");
            gm.SetPaused(false);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(music.FinalTrackActive && !music.FinalTrackHeld, "the track goes on after the pause");
            // раньше автобус уезжал через 6 с, как только все сели; теперь ждёт всю минуту
            Check(bus.State == Bus.Mode.Running && bus.FinalLeft > 35f && bus.FinalLeft < 56f, "the bus does not leave before the minute is up (" + bus.FinalLeft.ToString("0") + " s left)");

            gm.Hero.SetLife(30f, LightReason.Debug);   // погас с малым светом: возрождение (55) должно записаться доходом
            gm.OnHeroDied();
            yield return Key(GKey.R);
            yield return new WaitForSecondsRealtime(0.4f);
            Check(gm.State == GameState.Playing && gm.Hero.Life > 50f && gm.Economy.Amount(LightReason.Respawn, true, false) > 0,
                "R respawns and accounts for restored light");
            Check(Math.Abs(gm.Economy.ExpectedLight - gm.Hero.Life) < 0.01, "ledger reconciles after respawn");
            yield return new WaitForSecondsRealtime(0.9f);
            Check(!music.FinalTrackActive && music.FinalTrackLevel < 0.001f, "the track ends with the stand: the bus is back to idle and the stems return");
            // Настоящий путь: автобус заведён светом и тронулся (Bus.Leave) — классика начинает концовку: автобус везёт героя и людей,
            // это ловушка, Тьма, герой просыпается за кухонным столом.
            // Настоящая дорожка играет по-настоящему (звук выключен, чтобы не гремела из колонок): её позиция идёт вместе с обороной
            music.FinalClip = realTrack;
            AudioListener.volume = 0f;
            Bus.Instance.Feed(GameConfig.BusNeed + 1f);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(music.FinalTrackActive && music.FinalTrackLevel > 0.9f && music.FinalTrackTime > 1.5f && music.FinalTrackTime < 4.5f,
                "the real final-minute track plays with the final stand and keeps up with it (" + music.FinalTrackTime.ToString("0.0") + " s in)");
            GameSettings.Apply();   // вернуть громкость
            Bus.Instance.BoardedCount = 6;   // в салоне полный набор жителей: иначе проверки водителя и его соседей были бы пустыми
            Bus.Instance.FinalLeft = 0.05f;
            yield return new WaitForSecondsRealtime(1.2f);
            Check(gm.State == GameState.Ending && gm.Ending.Active && Bus.Instance.Scripted, "the bus leaving with the people starts the ending");
            Vector3 busSeat = Bus.Instance.transform.position + RideStage.SeatEye;
            Check(Vector3.Distance(gm.Player.Cam.transform.position, busSeat) < 0.6f && Bus.Instance.transform.position.z > Bus.Instance.StartPos.z,
                "ending: the camera rides inside the moving bus");
            // Обратная кинематика руки: две кости постоянной длины, сгиб в сторону подсказки (вниз и наружу)
            {
                Vector3 s = new Vector3(-0.25f, 1.37f, 0f);
                Vector3 w = new Vector3(-0.2f, 1.2f, 0.5f);
                Vector3 pole = new Vector3(-0.55f, -1f, -0.45f);
                Vector3 e = HeroBody.SolveElbow(s, w, HeroBody.UpperLen, HeroBody.ForeLen, pole);
                bool lengths = Mathf.Abs((e - s).magnitude - HeroBody.UpperLen) < 0.002f && Mathf.Abs((w - e).magnitude - HeroBody.ForeLen) < 0.002f;
                bool bendSide = Vector3.Dot(e - (s + w) * 0.5f, pole) > 0.05f;
                Vector3 far = HeroBody.SolveElbow(s, s + new Vector3(0f, 0f, 1.2f), HeroBody.UpperLen, HeroBody.ForeLen, pole);   // дальше вытянутой руки
                bool straight = Vector3.Angle(s - far, new Vector3(0f, 0f, 1.2f) - far + s) > 160f && !float.IsNaN(far.x);
                Check(lengths && bendSide && straight, "the arm solver keeps the bone lengths, bends towards the pole and stretches straight when the wrist is out of reach");
            }
            // Поворот кистей между «ладонью вниз» и «ладонью к лицу»: правая ладонь идёт влево, левая вправо (тыл смотрит наружу), без выворота.
            {
                Quaternion palmDown = Quaternion.LookRotation(new Vector3(0f, -0.05f, 1f), Vector3.up);
                Quaternion palmRaised = Quaternion.LookRotation(new Vector3(0f, 1f, -0.2f), new Vector3(0f, 0f, 1f));
                Vector3 backRight = HeroBody.HandSlerp(palmDown, palmRaised, 0.25f, 1f) * Vector3.up;
                Vector3 backLeft = HeroBody.HandSlerp(palmDown, palmRaised, 0.25f, -1f) * Vector3.up;
                Check(backRight.x > 0.5f && backLeft.x < -0.5f,
                    "hands turn the logical way: right palm to the left, left palm to the right (back of hand x " + backRight.x.ToString("0.00") + " / " + backLeft.x.ToString("0.00") + ")");
                float handTotal = 0f, handWorst = 0f;
                Quaternion handPrev = palmDown;
                for (int i = 1; i <= 40; i++)
                {
                    Quaternion now = HeroBody.HandSlerp(palmDown, palmRaised, i / 40f, 1f);
                    float step = Quaternion.Angle(handPrev, now);
                    handTotal += step; handWorst = Mathf.Max(handWorst, step); handPrev = now;
                }
                Check(handTotal < 260f && handWorst < handTotal / 40f * 2f && Quaternion.Angle(handPrev, palmRaised) < 0.5f,
                    "the hand turn is smooth and lands on the key (path " + handTotal.ToString("0") + " deg, worst step " + handWorst.ToString("0.0") + ")");
            }
            gm.Ending.DebugSeek(EndingDirector.TOpen + 1.5f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(gm.Ending.DarknessVisible, "ending: the dawn ahead turns out to be the Darkness");
            Check(gm.Ending.HasDriver, "ending: a driver sits at the wheel of the bus");
            Check(gm.Ending.DriverClearance > 1.5f && gm.Ending.DriverClearance < 8f,
                "ending: six residents ride and nobody sits right behind the driver (nearest resident " + gm.Ending.DriverClearance.ToString("0.0") + " m)");
            // Тьма — объёмный дым, который надвигается на автобус и накрывает его, а потом просачивается в салон
            gm.Ending.DebugSeek(14.2f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(gm.Ending.DarkPuffsDrawn > 100, "ending: the Darkness is a wall of smoke that reaches the bus (" + gm.Ending.DarkPuffsDrawn + " puffs drawn)");
            gm.Ending.DebugSeek(15.3f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(gm.Ending.CabinPuffsDrawn > 20, "ending: the Darkness seeps into the cabin (" + gm.Ending.CabinPuffsDrawn + " puffs drawn)");
            gm.Ending.DebugSeek(EndingDirector.TKitchen + 0.5f);
            yield return new WaitForSecondsRealtime(0.4f);
            Vector3 kitchenSeat = gm.Intro.AcquireKitchen().World(Kitchen.SeatRoot);
            Check(Vector3.Distance(gm.Player.transform.position, kitchenSeat) < 0.05f && gm.Intro.AcquireKitchen().Root.gameObject.activeSelf,
                "ending: the hero wakes up at the kitchen table");
            Check(gm.Ending.MugClearance() > 0.12f && gm.Ending.TableClearance() > 0.1f,
                "ending: the sleeping head rests beside the coffee mug, never in it (clearance " + gm.Ending.MugClearance().ToString("0.00") + " m)");
            // Шторы на кухне (общая для предыстории и концовки) висят над подоконником и не проходят сквозь него
            Check(Kitchen.CurtainSillOverlap() < 0.0005f, "kitchen: the curtains hang above the windowsill and never go through it (" + Kitchen.CurtainReport() + ")");
            // Руки в кат-сцене: две кости постоянной длины и согнутый локоть (не палка и не рука, сложенная вдвое): ладони подняты, руки на коленях
            for (int armPose = 0; armPose < 2; armPose++)
            {
                gm.Ending.DebugSeek(EndingDirector.TKitchen + (armPose == 0 ? 7.5f : 11.5f));
                yield return new WaitForSecondsRealtime(0.6f);
                bool bonesOk = true;
                bool bentOk = true;
                string armText = "";
                for (int a = 0; a < 2; a++)
                {
                    Vector3 shoulderPos, elbowPos, wristPos;
                    gm.Player.Body.GetArm(a, out shoulderPos, out elbowPos, out wristPos);
                    float upperLen = (elbowPos - shoulderPos).magnitude;
                    float foreLen = (wristPos - elbowPos).magnitude;
                    float elbowAngle = Vector3.Angle(shoulderPos - elbowPos, wristPos - elbowPos);
                    bonesOk &= Mathf.Abs(upperLen - HeroBody.UpperLen) < 0.01f && Mathf.Abs(foreLen - HeroBody.ForeLen) < 0.01f;
                    bentOk &= elbowAngle > 60f && elbowAngle < 150f;
                    armText += (a == 0 ? " L " : " R ") + upperLen.ToString("0.00") + "+" + foreLen.ToString("0.00") + " m, " + elbowAngle.ToString("0") + " deg;";
                }
                Check(bonesOk && bentOk, "ending: the arms bend at the elbow with constant bone lengths (" + (armPose == 0 ? "palms raised" : "hands on the lap") + "):" + armText);
            }
            // Руки не проходят сквозь кружку и ручку: правая рука идёт на стол мимо них (в моменты, когда она рядом с кружкой)
            {
                float worstInside = 0f;
                float mostPushed = 0f;
                float[] mugMoments = { 1.8f, 2.2f, 2.6f, 4.0f };
                for (int m = 0; m < mugMoments.Length; m++)
                {
                    gm.Ending.DebugSeek(EndingDirector.TKitchen + mugMoments[m]);
                    yield return new WaitForSecondsRealtime(0.45f);
                    for (int a = 0; a < 2; a++)
                    {
                        worstInside = Mathf.Max(worstInside, gm.Player.Body.HandPenetration(a));
                        mostPushed = Mathf.Max(mostPushed, gm.Player.Body.GetHandPush(a).magnitude);
                    }
                }
                Check(worstInside < 0.002f && mostPushed < 0.09f,
                    "ending: hands never go through the mug or its handle (still inside by " + (worstInside * 1000f).ToString("0.0") + " mm, pushed out by at most " + (mostPushed * 100f).ToString("0.0") + " cm)");
            }
            yield return Key(GKey.Space);
            yield return new WaitForSecondsRealtime(1.0f);
            Check(gm.State == GameState.Victory && !gm.Ending.Active && !gm.Intro.AcquireKitchen().Root.gameObject.activeSelf, "Space skips the ending to the result screen");
            yield return new WaitForSecondsRealtime(2.8f);   // экран итога проявляется ~2.6 с, до этого кнопки не принимают ввод
            Check(FindMusic()[0].volume < 0.001f, "victory fades score");
            string csvPath = Path.Combine(output, "last-run-light.csv");
            Check(File.Exists(csvPath) && File.ReadAllText(csvPath).Contains("Beam"), "local report includes per-action light transfers");
            Check(gm.LastRecord != null && gm.LastRank == 1 && File.Exists(RecordsStore.FilePath)
                && RecordsStore.Table.Top(RunRecords.Classic, (int)Challenge.Standard).Count == 1,
                "classic victory goes into the records table (first place, file written)");
            yield return Key(GKey.Down);
            yield return Key(GKey.Down);
            yield return Key(GKey.Enter);
            var page = typeof(MenuUI).GetField("page", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check(page.GetValue(gm.Menu).ToString() == "Report", "victory menu opens light report through keyboard input");
            yield return Key(GKey.Tab);
            var recent = typeof(MenuUI).GetField("recentReport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check((bool)recent.GetValue(gm.Menu), "Tab switches report to rolling minute");

            // Warm caches once, then assert that native resources stabilise across world rebuilds.
            gm.QuitToMenu();
            yield return new WaitForSecondsRealtime(0.8f);
            AudioMix.ApplyAll();
            int materialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
            int voiceCount = AudioMix.VoiceCount;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                gm.QuitToMenu();
                yield return new WaitForSecondsRealtime(0.8f);
            }
            AudioMix.ApplyAll();
            int afterMaterials = Resources.FindObjectsOfTypeAll<Material>().Length;
            Check(afterMaterials <= materialCount + 1, "materials stabilise across restarts: " + materialCount + " -> " + afterMaterials);
            Check(AudioMix.VoiceCount == voiceCount, "audio registry releases destroyed world voices");
            Check(FindMusic().Length == 4 && Math.Abs(gm.Economy.TotalIncome) < 0.001 && gm.Economy.TotalExpense == 0,
                "new run resets economy and reuses music stems");
            CheckEffects();

            int beforeOrbs = Resources.FindObjectsOfTypeAll<Material>().Length;
            for (int i = 0; i < 50; i++) LightOrb.Spawn(LevelBuilder.StartPos, 6f);
            yield return null;
            LightOrb.ClearAll();
            yield return null; yield return null;
            int afterOrbs = Resources.FindObjectsOfTypeAll<Material>().Length;
            Check(afterOrbs <= beforeOrbs, "50 discarded orbs release 100 native materials: " + beforeOrbs + " -> " + afterOrbs);

            // Враги: 3D-тела из частей (не плоские спрайты); погибшие и убранные освобождают свои материалы (первый круг прогревает общие кеши)
            Vector3 foeAt = NearbyFree(LevelBuilder.StartPos, 6f);
            int[] foeParts = new int[3];
            int foeBefore = 0;
            for (int round = 0; round < 2; round++)
            {
                if (round == 1) foeBefore = Resources.FindObjectsOfTypeAll<Material>().Length;
                for (int kind = 0; kind < 3; kind++)
                {
                    for (int n = 0; n < (kind == 0 ? 4 : 1); n++)
                    {
                        Enemy foe = Enemy.Spawn((EnemyKind)kind, foeAt);
                        if (n == 0) foeParts[kind] = foe.BodyParts;
                    }
                }
                yield return null;
                Enemy.ClearAll();
                yield return null; yield return null;
            }
            int foeAfter = Resources.FindObjectsOfTypeAll<Material>().Length;
            Check(foeParts[0] >= 12 && foeParts[1] >= 10 && foeParts[2] >= 30,
                "enemies are 3D bodies made of parts: shade " + foeParts[0] + ", snuffer " + foeParts[1] + ", eater " + foeParts[2]);
            Check(foeAfter <= foeBefore, "enemy bodies release their own materials: " + foeBefore + " -> " + foeAfter);

            // Подсказки: строка с кнопками живёт до первого очага после старта, подсказка про Проводника — до его ручного вызова.
            yield return Key(GKey.Enter);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Key(GKey.Space);
            yield return new WaitForSecondsRealtime(3f);   // автовызов Проводника — через 2 с после старта
            Check(gm.State == GameState.Playing && gm.ControlHintsVisible, "new run shows the control hints strip");
            Check(GuideWisp.CooldownLeft > 0f && !GuideWisp.PlayerActivated, "automatic guide start does not count as the player's own");
            // Враг смотрит, куда идёт: тело поворачивается по движению (оглушённого Тень не ведёт ИИ, двигаем её сами вдоль +X)
            {
                Vector3 shadeAt = NearbyFree(gm.Player.transform.position + new Vector3(0f, 0f, 9f), 3f);
                Enemy shade = Enemy.Spawn(EnemyKind.Shade, shadeAt);
                if (shade != null)
                {
                    shade.Stun(5f);
                    float until = Time.realtimeSinceStartup + 0.9f;
                    Vector3 mark = shade.transform.position;
                    bool marked = false;
                    while (Time.realtimeSinceStartup < until && shade != null)
                    {
                        shade.transform.position += new Vector3(3f * Time.deltaTime, 0f, 0f);
                        // последняя четверть секунды: по ней считаем курс (в свете лампы Тень ещё и пятится сама, поэтому курс не строго +X)
                        if (!marked && Time.realtimeSinceStartup > until - 0.25f) { mark = shade.transform.position; marked = true; }
                        yield return null;
                    }
                    if (shade != null)
                    {
                        Vector3 heading = shade.transform.position - mark;
                        heading.y = 0f;
                        float facing = heading.magnitude > 0.2f ? Vector3.Dot(shade.transform.forward, heading.normalized) : -1f;
                        Check(facing > 0.85f, "a shade turns its face the way it moves (cos " + facing.ToString("0.00") + ", moved " + heading.magnitude.ToString("0.0") + " m)");
                    }
                    else Check(false, "the test shade disappeared while it was moved");
                }
                else Check(false, "the test shade could not be spawned");
                Enemy.ClearAll();
            }
            int wokenBefore = gm.SurvivorsWoken;
            gm.SurvivorsWoken = 1;   // чтобы строка цели дошла до подсказки про Проводника
            Check(gm.ObjectiveText().Contains("G — Проводник"), "objective line mentions the guide until the player uses it");
            yield return Key(GKey.G);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(GuideWisp.PlayerActivated && !gm.ObjectiveText().Contains("G — Проводник"), "pressing G removes the guide hint");
            gm.SurvivorsWoken = wokenBefore;
            Lamp nextHearth = null;
            foreach (Lamp l in Lamp.All)
                if (l.IsHearth && l.Progress > gm.Dir.Checkpoint.Progress + 0.01f && (nextHearth == null || l.Progress < nextHearth.Progress)) nextHearth = l;
            Check(nextHearth != null && gm.ControlHintsVisible, "control hints are still shown before the first hearth after the start");
            if (nextHearth != null)
            {
                gm.Player.TeleportTo(NearbyFree(nextHearth.BasePos, 3f), 0f);
                yield return new WaitForSecondsRealtime(0.4f);
                Check(!gm.ControlHintsVisible, "reaching the first hearth hides the control hints");
            }
            gm.QuitToMenu();
            yield return new WaitForSecondsRealtime(0.8f);
            Check(gm.ControlHintsVisible && !GuideWisp.PlayerActivated, "a new run brings both hints back");

            // Бесконечный город: пункт меню, следующий город темнее, возвращения ограничены, конец забега идёт в таблицу рекордов.
            yield return Key(GKey.Down);
            yield return Key(GKey.Enter);
            yield return new WaitForSecondsRealtime(0.6f);
            Check(gm.Endless && gm.City == 1 && gm.State == GameState.Playing && !gm.Intro.Active && gm.ReturnsLeft == GameManager.EndlessReturnsStart,
                "title menu starts the endless city without the intro");
            int lampsFirst = Lamp.All.Count;
            gm.Win();
            yield return new WaitForSecondsRealtime(0.4f);
            Check(gm.State == GameState.Victory && gm.CitiesCleared == 1 && gm.LastRecord == null, "endless: a finished city is counted, the run goes on");
            float timeFirst = gm.PlayTime;
            yield return Key(GKey.R);
            yield return new WaitForSecondsRealtime(1.0f);
            Check(gm.State == GameState.Playing && gm.City == 2 && GameDifficulty.City == 2 && LevelBuilder.CitySeed == 1
                && gm.ReturnsLeft == GameManager.EndlessReturnsStart + 1 && gm.PlayTime >= timeFirst - 0.01f,
                "endless: R brings the next city, the run clock and returns carry over");
            Check(Survivor.All.Count == 10 && Lamp.All.Count == lampsFirst && GameDifficulty.PressureScale > 1.1f && !gm.ControlHintsVisible,
                "endless: the next city keeps the street map, is more dangerous, and does not repeat the control hints");
            gm.ReturnsLeft = 1;
            gm.OnHeroDied();
            Check(gm.State == GameState.Dead && !gm.RunOver, "endless: dying with a return left is not the end");
            yield return Key(GKey.R);
            yield return new WaitForSecondsRealtime(0.4f);
            Check(gm.State == GameState.Playing && gm.ReturnsLeft == 0, "endless: R spends a return to the hearth");
            gm.OnHeroDied();
            Check(gm.State == GameState.Dead && gm.RunOver && gm.LastRank == 1
                && RecordsStore.Table.Best(RunRecords.Endless, (int)Challenge.Standard) != null
                && RecordsStore.Table.Best(RunRecords.Endless, (int)Challenge.Standard).Cities == 1,
                "endless: dying without returns ends the run and writes it to the records table");
            yield return Key(GKey.R);
            yield return new WaitForSecondsRealtime(1.0f);
            Check(gm.State == GameState.Playing && gm.Endless && gm.City == 1 && !gm.RunOver && GameDifficulty.City == 1
                && gm.ReturnsLeft == GameManager.EndlessReturnsStart, "endless: R after the end starts a fresh run from the first city");
            gm.QuitToMenu();
            yield return new WaitForSecondsRealtime(0.8f);
            Check(!gm.Endless && gm.City == 1 && GameDifficulty.City == 1 && LevelBuilder.CitySeed == 0, "leaving the endless run restores the classic city");

            // Таблица рекордов в меню: пункт 2, Tab переключает режим, Esc возвращает на главную.
            yield return Key(GKey.Down);
            yield return Key(GKey.Down);
            yield return Key(GKey.Enter);
            Check(page.GetValue(gm.Menu).ToString() == "Records", "title menu opens the records table");
            yield return Key(GKey.Tab);
            var recMode = typeof(MenuUI).GetField("recordsMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check((int)recMode.GetValue(gm.Menu) == 1, "Tab switches the records table to the endless city");
            yield return Key(GKey.Esc);
            Check(page.GetValue(gm.Menu).ToString() == "Root", "Esc leaves the records table");

            yield return Key(GKey.Down);
            yield return Key(GKey.Down);
            yield return Key(GKey.Down);
            yield return Key(GKey.Down);
            yield return Key(GKey.Enter);
            Check(page.GetValue(gm.Menu).ToString() == "Settings", "title menu opens settings through keyboard input");
            yield return Key(GKey.Right);
            Check(GameSettings.Difficulty == Challenge.Harsh && GameDifficulty.Mode == Challenge.Harsh, "difficulty selector applies live");
            yield return Key(GKey.Left);
            yield return Key(GKey.Tab);
            yield return Key(GKey.Tab);
            var tab = typeof(MenuUI).GetField("settingsTab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check((int)tab.GetValue(gm.Menu) == 2, "Tab reaches sound settings");
            float oldVolume = GameSettings.Volume;
            yield return Key(oldVolume > 0.5f ? GKey.Left : GKey.Right);
            Check(Mathf.Abs(GameSettings.Volume - oldVolume) > 0.049f, "audio slider responds to keyboard");
            // Do not leave Settings via Back: that would save this synthetic session's values.
            Finish();
        }

        void CheckEffects()
        {
            string[] keys = { "drone", "heart", "hum", "crackle", "inhale", "exhale", "flash", "beam", "wake",
                "lampdie", "shadedie", "hit", "tide", "win", "eater", "shade", "step", "step2", "rustle", "engine", "orb", "munch",
                "fridge", "tick", "powerdown", "rumble", "sizzle", "swell", "drip" };
            float maximum = 0f;
            foreach (string key in keys)
            {
                AudioClip clip = ProcAudio.Get(key);
                float[] samples = new float[clip.samples * clip.channels];
                clip.GetData(samples, 0);
                foreach (float value in samples) maximum = Mathf.Max(maximum, Mathf.Abs(value));
            }
            Check(maximum <= 0.90001f && maximum > 0.1f, keys.Length + " procedural effects have sample headroom; max=" + maximum.ToString("0.000"));
        }

        bool Free(Vector3 point)
        {
            if (!CityMap.CanStand(point.x, point.z, 0.5f, true)) return false;
            foreach (Collider c in Physics.OverlapSphere(point + Vector3.up * 0.9f, 0.3f))
                if (!c.isTrigger && !c.transform.IsChildOf(gm.Player.transform) && c.GetComponentInParent<Enemy>() == null && c.GetComponentInParent<Survivor>() == null)
                    return false;
            return true;
        }
        Vector3 NearbyFree(Vector3 center, float radius)
        {
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI / 16f;
                Vector3 p = new Vector3(center.x + Mathf.Sin(angle) * radius, 0.1f, center.z + Mathf.Cos(angle) * radius);
                if (Free(p)) return p;
            }
            return center;
        }
        IEnumerator AiChecks()
        {
            gm.State = GameState.Playing;
            GameConfig.SmolderPerSec = GameConfig.AuraCostK = 0f;
            GameConfig.ShadesBase = GameConfig.ShadesPerDistrict = GameConfig.ShadesTide = GameConfig.ShadesFinal = 0;
            GameConfig.SpawnIntervalMin = GameConfig.SpawnIntervalMax = GameConfig.SnufferIntervalMin = GameConfig.SnufferIntervalMax = 10000f;
            GameConfig.EaterAtProgress = 2f;
            foreach (Growth wall in Growth.All) wall.Burn(10000f);
            yield return new WaitForSecondsRealtime(0.2f);
            int checkedPositions = 0, violations = 0;
            for (int district = 0; district < CityMap.Hearths.Count; district++)
            {
                Enemy.ClearAll();
                Pt p = CityMap.CenterOf(CityMap.Hearths[district].X, CityMap.Hearths[district].Z);
                Vector3 stand = NearbyFree(new Vector3(p.X, 0.1f, p.Z), 2.5f);
                gm.Player.TeleportTo(stand, 0f);
                gm.Hero.SetLife(100f, LightReason.Debug);
                yield return null;
                for (int kind = 0; kind < 3; kind++)
                {
                    Vector3 spawn = NearbyFree(stand, 7f + kind * 2f);
                    if (Free(spawn)) Enemy.Spawn((EnemyKind)kind, spawn);
                }
                yield return null; yield return null;
                float until = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < until)
                {
                    foreach (Enemy enemy in Enemy.All)
                    {
                        if (enemy == null || enemy.Health <= 0f) continue;
                        checkedPositions++;
                        if (!Free(enemy.transform.position)) violations++;
                    }
                    yield return null;
                }
            }
            Check(checkedPositions > 1000 && violations == 0, "enemy navigation in four districts: positions=" + checkedPositions + ", violations=" + violations);
            Enemy.ClearAll();
            yield return null;
            Pt destination = CityMap.CenterOf(CityMap.BusX, CityMap.BusZ);
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Survivor survivor = Survivor.All[i];
                Vector3 begin = survivor.transform.position;
                survivor.ReviveAt(begin);
                var path = CityMap.FindPath(new Pt(begin.x, begin.z), destination, false, 0f);
                Check(path != null && path.Count > 1, "survivor " + i + " has an open route after walls burn");
                if (path == null || path.Count < 2) continue;
                Vector3 segment = new Vector3(path[1].X, 0.1f, path[1].Z) - begin;
                segment.y = 0;
                Vector3 goal = begin + segment.normalized * Mathf.Min(12f, segment.magnitude);
                goal = NearbyFree(goal, 1.1f);
                gm.Player.TeleportTo(goal, 0f);
                gm.Hero.SetLife(100f, LightReason.Debug);
                float before = Vector3.Distance(begin, goal);
                yield return new WaitForSecondsRealtime(4.5f);
                float after = Vector3.Distance(survivor.transform.position, gm.Player.transform.position);
                Check(after <= 6f || after < before - 2f, "survivor " + i + " follows its route segment: " + before.ToString("0.0") + " -> " + after.ToString("0.0") + "m");
            }
        }

        AudioSource[] FindMusic()
        {
            System.Collections.Generic.List<AudioSource> sources = new System.Collections.Generic.List<AudioSource>();
            foreach (AudioSource s in gm.GetComponents<AudioSource>())
                if (s.clip != null && s.clip.name.StartsWith("Suspense_")) sources.Add(s);
            return sources.ToArray();
        }
        void Finish()
        {
            if (finished) return;
            finished = true;
            GameInput.Provider = null;
            Running = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            report.AppendLine("failures=" + failures);
            report.AppendLine(failures == 0 ? "PG REGRESSION: OK" : "PG REGRESSION: FAILED");
            File.WriteAllText(Path.Combine(output, "regression-result.txt"), report.ToString());
            Application.Quit(failures == 0 ? 0 : 1);
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            GameInput.Provider = null;
            Running = false;
        }

        sealed class ReplayInput : IGameInput
        {
            readonly bool[] held = new bool[64];
            readonly int[] pressed = new int[64];
            readonly bool[] mouseHeld = new bool[2];
            readonly int[] mousePressed = new int[2];
            Vector2 look;
            int lookFrame;
            public ReplayInput()
            {
                for (int i = 0; i < pressed.Length; i++) pressed[i] = -1;
                for (int i = 0; i < mousePressed.Length; i++) mousePressed[i] = -1;
                lookFrame = -1;
            }
            public void Key(GKey key, bool down)
            {
                if (down && !held[(int)key]) pressed[(int)key] = Time.frameCount + 1;
                held[(int)key] = down;
            }
            public void Mouse(int button, bool down)
            {
                if (down && !mouseHeld[button]) mousePressed[button] = Time.frameCount + 1;
                mouseHeld[button] = down;
            }
            public void LookOnce(Vector2 delta) { look = delta; lookFrame = Time.frameCount + 1; }
            public bool Held(GKey key) { return held[(int)key]; }
            public bool Down(GKey key) { return pressed[(int)key] == Time.frameCount; }
            public bool MouseHeld(int button) { return mouseHeld[button]; }
            public bool MouseDown(int button) { return mousePressed[button] == Time.frameCount; }
            public Vector2 LookDelta() { return lookFrame == Time.frameCount ? look : Vector2.zero; }
            public float Scroll() { return 0f; }
        }
    }
}
