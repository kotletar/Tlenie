using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Режиссёр: спавнит врагов в темноте вокруг героя (только на дорогах), запускает Приливы Тьмы,
    /// ведёт очаги-контрольные точки и финальную оборону у автобуса.
    /// Всё привязано не к координате, а к прогрессу по пути до автобуса (CityMap.Progress).
    /// </summary>
    public class Director : MonoBehaviour
    {
        public bool InHearth;
        public Lamp Checkpoint;
        public Vector3 CheckpointPos;
        public bool FinalStand;
        public bool TideActive;
        public float TideLeft;
        public float HeroProgress;

        /// <summary>Для автотестов: режиссёр никого не порождает (таймеры, взведённые до теста, иначе сработали бы в случайный момент).</summary>
        public bool Quiet;

        const float TideTotal = 28f;
        static readonly float[] TideAt = { 0.30f, 0.55f, 0.80f };

        readonly List<Survivor> checkpointFollowers = new List<Survivor>();
        readonly List<Lamp> hearths = new List<Lamp>();
        readonly bool[] tideDone = new bool[3];
        float spawnTimer = 6f;
        float snufferTimer = 12f;
        float finalEaterTimer;
        bool eaterSpawned;
        float bossTimer;
        bool bossSpawned;
        bool bossDefeated;

        /// <summary>Великий Гаситель побеждён: до конца минуты город затихает (новых врагов нет).</summary>
        public bool BossDefeated { get { return bossDefeated; } }
        public bool BossSpawned { get { return bossSpawned; } }

        /// <summary>Босс погиб (зовёт Enemy.BossDie).</summary>
        public void OnBossDefeated()
        {
            bossDefeated = true;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.Notify("Великий Гаситель рассеялся");
        }

        /// <summary>Новая игра: найти очаги, самый ранний — контрольная точка.</summary>
        public void ResetAll()
        {
            hearths.Clear();
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                if (Lamp.All[i].IsHearth) hearths.Add(Lamp.All[i]);
            }
            Checkpoint = null;
            for (int i = 0; i < hearths.Count; i++)
            {
                if (Checkpoint == null || hearths[i].Progress < Checkpoint.Progress) Checkpoint = hearths[i];
            }
            CheckpointPos = LevelBuilder.StartPos;
            checkpointFollowers.Clear();
            for (int i = 0; i < tideDone.Length; i++) tideDone[i] = false;
            TideActive = false;
            TideLeft = 0f;
            FinalStand = false;
            InHearth = false;
            HeroProgress = 0f;
            eaterSpawned = false;
            spawnTimer = 6f;
            snufferTimer = 12f;
            bossSpawned = false;
            bossDefeated = false;
        }

        public bool IsInHearth(Vector3 p)
        {
            for (int i = 0; i < hearths.Count; i++)
            {
                Lamp h = hearths[i];
                float dx = h.BasePos.x - p.x;
                float dz = h.BasePos.z - p.z;
                if (dx * dx + dz * dz < GameConfig.HearthRadius * GameConfig.HearthRadius) return true;
            }
            // Заведённый автобус — последний очаг: пока идёт последняя минута, рядом с дверью свет не тлеет и понемногу
            // возвращается (на целую минуту осады без этого не хватило бы света даже тому, кто почти не стреляет).
            Bus bus = Bus.Instance;
            if (bus != null && bus.State == Bus.Mode.Running)
            {
                float bx = bus.DoorPos.x - p.x;
                float bz = bus.DoorPos.z - p.z;
                if (bx * bx + bz * bz < GameConfig.HearthRadius * GameConfig.HearthRadius) return true;
            }
            return false;
        }

        public void StartFinal()
        {
            FinalStand = true;
            finalEaterTimer = 5f;
            spawnTimer = 1f;
            bossTimer = GameConfig.BossArriveDelay;
            bossSpawned = false;
            bossDefeated = false;
            FinalScreenFx.Kick(1.0f, 1.2f, 0.7f);   // двигатель заведён: волна от центра кадра, всплеск хроматики и вспышка каёмки
        }

        /// <summary>
        /// Где парит босс и как он прилетает. Парит над площадью вокзала, в 24 м от двери автобуса, на высоте 8.5 м (первое из направлений запад,
        /// юг, восток, север, где шар с шипами целиком над площадью: над домами он бы в них врезался). Путь — две прямые: из точки from в 100 м за
        /// площадью, на высоте, с которой он проходит выше самого высокого дома на этом отрезке (BuildingHeight с соседними клетками и запасом 10 м),
        /// в точку entry над краем площади, оттуда крутой спуск к якорю. Так он выходит из-за крыш, не проходя сквозь дома.
        /// </summary>
        public static void BossStation(Vector3 door, out Vector3 anchor, out Vector3 entry, out Vector3 from)
        {
            Vector3[] dirs = { new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f) };
            CellRect st = CityMap.Station;
            float margin = (EnemyModels.BossR + 1.5f) * EnemyModels.BossScale + 2f;   // шар с шипами не должен задеть дома вокруг площади
            float x0 = st.X0 * CityMap.Cell + margin;
            float x1 = (st.X1 + 1) * CityMap.Cell - margin;
            float z0 = st.Z0 * CityMap.Cell + margin;
            float z1 = (st.Z1 + 1) * CityMap.Cell - margin;
            Vector3 dir = dirs[0];
            anchor = door + dir * 24f;
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 c = door + dirs[i] * 24f;
                if (c.x >= x0 && c.x <= x1 && c.z >= z0 && c.z <= z1)
                {
                    dir = dirs[i];
                    anchor = c;
                    break;
                }
            }
            anchor.y = 8.5f;
            // сколько метров от якоря до края площади по ходу dir
            float toEdge = dir.x < -0.5f ? anchor.x - st.X0 * CityMap.Cell
                : dir.x > 0.5f ? (st.X1 + 1) * CityMap.Cell - anchor.x
                : dir.z < -0.5f ? anchor.z - st.Z0 * CityMap.Cell
                : (st.Z1 + 1) * CityMap.Cell - anchor.z;
            entry = anchor + dir * Mathf.Max(4f, toEdge - 6f);
            from = anchor + dir * 100f;
            float top = 0f;
            for (int i = 0; i <= 48; i++)
            {
                Vector3 q = Vector3.Lerp(from, entry, i / 48f);
                int cx = Mathf.FloorToInt(q.x / CityMap.Cell);
                int cz = Mathf.FloorToInt(q.z / CityMap.Cell);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int x = cx + dx;
                        int z = cz + dz;
                        if (x >= 0 && x < CityMap.W && z >= 0 && z < CityMap.H) top = Mathf.Max(top, LevelBuilder.BuildingHeight[x, z]);
                    }
                }
            }
            entry.y = Mathf.Max(anchor.y + 14f, top + 10f);
            from.y = entry.y + 8f;
        }

        void SpawnBoss()
        {
            Bus bus = Bus.Instance;
            if (bus == null) return;
            Vector3 anchor, entry, from;
            BossStation(bus.DoorPos, out anchor, out entry, out from);
            bossSpawned = true;
            Enemy.SpawnBoss(anchor, entry, from);
        }
        public void Tick(float dt)
        {
            PlayerController pl = PlayerController.Instance;
            GameManager gm = GameManager.Instance;
            if (pl == null || gm == null) return;
            Vector3 hp = pl.transform.position;
            HeroProgress = CityMap.Progress(hp.x, hp.z);

            UpdateHearths(hp, gm);
            UpdateTide(dt, gm, pl);
            UpdateSpawning(dt, hp, pl);
        }

        // ------------------------------------------------------------------ очаги

        void UpdateHearths(Vector3 hp, GameManager gm)
        {
            InHearth = IsInHearth(hp);
            if (!InHearth) return;
            for (int i = 0; i < hearths.Count; i++)
            {
                Lamp h = hearths[i];
                float dx = h.BasePos.x - hp.x;
                float dz = h.BasePos.z - hp.z;
                if (dx * dx + dz * dz > GameConfig.HearthRadius * GameConfig.HearthRadius) continue;
                if (Checkpoint == null || h.Progress > Checkpoint.Progress + 0.01f)
                {
                    SetCheckpoint(h);
                    gm.ControlHintsVisible = false;   // дошли до первого очага после старта: памятка по кнопкам внизу экрана больше не нужна
                    gm.Notify("Очаг: сюда вы вернётесь, если свет погаснет");
                }
            }
        }

        /// <summary>Точка рядом с очагом, где можно стоять (не в стене).</summary>
        static Vector3 SpotNear(Lamp h)
        {
            Vector3 b = h.BasePos;
            float[] ox = { 2.8f, -2.8f, 0f, 0f };
            float[] oz = { 0f, 0f, 2.8f, -2.8f };
            for (int i = 0; i < 4; i++)
            {
                if (CityMap.IsRoad(b.x + ox[i], b.z + oz[i], false)
                    && CityMap.LineClear(b.x, b.z, b.x + ox[i], b.z + oz[i], 0.4f, false))
                    return new Vector3(b.x + ox[i], 0.1f, b.z + oz[i]);
            }
            return new Vector3(b.x, 0.1f, b.z);
        }

        void SetCheckpoint(Lamp h)
        {
            Checkpoint = h;
            CheckpointPos = SpotNear(h);
            checkpointFollowers.Clear();
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                if (Survivor.All[i].IsAwake) checkpointFollowers.Add(Survivor.All[i]);
            }
        }

        // ------------------------------------------------------------------ прилив

        void UpdateTide(float dt, GameManager gm, PlayerController pl)
        {
            if (!TideActive)
            {
                for (int i = 0; i < TideAt.Length; i++)
                {
                    if (!tideDone[i] && HeroProgress >= TideAt[i])
                    {
                        tideDone[i] = true;
                        TideActive = true;
                        TideLeft = TideTotal;
                        gm.Notify("ПРИЛИВ ТЬМЫ: огни тускнеют");
                        ProcAudio.OneShot(pl.Sfx, "tide", 0.9f, 1f);
                        break;
                    }
                }
                return;
            }

            TideLeft -= dt;
            float k = Mathf.Clamp01(Mathf.Min(TideLeft, TideTotal - TideLeft) / 3f);
            gm.TideFactor = Mathf.Lerp(1f, 0.6f, k);
            if (TideLeft <= 0f)
            {
                TideActive = false;
                gm.TideFactor = 1f;
                gm.Notify("Прилив отступил");
            }
        }

        // ------------------------------------------------------------------ спавн

        void UpdateSpawning(float dt, Vector3 hp, PlayerController pl)
        {
            if (Quiet) return;
            if (FinalStand && GameConfig.BossEnabled)
            {
                if (bossDefeated) return;   // босс побеждён: до конца минуты тихо
                if (!bossSpawned)
                {
                    bossTimer -= dt;
                    if (bossTimer <= 0f) SpawnBoss();
                }
            }
            int district = Mathf.Clamp((int)(HeroProgress * 4f), 0, 3);
            float pressure = 1f + district * GameConfig.DistrictPressure + (TideActive ? GameConfig.TidePressure : 0f) + (FinalStand ? GameConfig.FinalPressure : 0f);
            int maxShades = GameConfig.ShadesBase + district * GameConfig.ShadesPerDistrict + (TideActive ? GameConfig.ShadesTide : 0) + (FinalStand ? GameConfig.ShadesFinal : 0);
            pressure *= GameDifficulty.PressureScale;
            maxShades += GameDifficulty.ExtraShades;   // бесконечный город: дальше город — больше Теней одновременно
            if (InHearth && !FinalStand) pressure *= GameConfig.HearthPressure;

            spawnTimer -= dt;
            if (spawnTimer <= 0f)
            {
                spawnTimer = Random.Range(GameConfig.SpawnIntervalMin, GameConfig.SpawnIntervalMax) / Mathf.Max(0.1f, pressure);
                if (Enemy.CountOf(EnemyKind.Shade) < maxShades)
                {
                    Vector3 p;
                    if (FindDarkSpot(hp, 20f, 36f, EnemyKind.Shade, out p)) Enemy.Spawn(EnemyKind.Shade, p);
                }
            }

            snufferTimer -= dt;
            if (snufferTimer <= 0f)
            {
                snufferTimer = Random.Range(GameConfig.SnufferIntervalMin, GameConfig.SnufferIntervalMax) / Mathf.Max(0.1f, pressure);
                TrySpawnSnuffer(hp);
            }

            if (!eaterSpawned && !FinalStand && HeroProgress > GameConfig.EaterAtProgress)
            {
                Vector3 p;
                if (FindDarkSpot(hp, 28f, 40f, EnemyKind.Eater, out p))
                {
                    eaterSpawned = true;
                    Enemy.Spawn(EnemyKind.Eater, p);
                    GameManager.Instance.Notify("Что-то огромное просыпается впереди…");
                }
            }

            if (FinalStand && !GameConfig.BossEnabled)   // с боссом Пожирателей в последней минуте нет: босс и есть финальный враг
            {
                finalEaterTimer -= dt;
                if (finalEaterTimer <= 0f)
                {
                    finalEaterTimer = GameConfig.FinalEaterInterval;
                    if (Enemy.CountOf(EnemyKind.Eater) < 2)
                    {
                        Vector3 p;
                        if (FindDarkSpot(hp, 24f, 38f, EnemyKind.Eater, out p)) Enemy.Spawn(EnemyKind.Eater, p);
                    }
                }
            }
        }

        void TrySpawnSnuffer(Vector3 hp)
        {
            int cap = TideActive ? 3 : 2;
            if (Enemy.CountOf(EnemyKind.Snuffer) >= cap) return;

            Lamp target = null;
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                if (l.Dead || l.IsDecoy || l.IsHearth) continue;
                float dx = l.BasePos.x - hp.x;
                float dz = l.BasePos.z - hp.z;
                if (dx * dx + dz * dz > 50f * 50f) continue;
                // Тьма тянется к слабому огню; в прилив — к любому.
                if (l.Charge < 0.45f || (TideActive && Random.value < 0.3f))
                {
                    target = l;
                    break;
                }
            }
            if (target == null) return;
            Vector3 p;
            if (FindDarkSpot(target.BasePos, 14f, 26f, EnemyKind.Snuffer, out p)) Enemy.Spawn(EnemyKind.Snuffer, p);
        }

        /// <summary>
        /// Тёмная точка на дороге на заданном расстоянии: не в здании, не у самой стены и с проходом к точке around
        /// (за наростом Тьмы враг бы застрял, поэтому там он не рождается).
        /// </summary>
        internal static bool FindDarkSpot(Vector3 around, float minD, float maxD, EnemyKind kind, out Vector3 pos)
        {
            int pathChecks = 0;
            for (int tries = 0; tries < 28; tries++)
            {
                float ang = Random.Range(0f, 6.283f);
                float dist = Random.Range(minD, maxD);
                Vector3 p = around + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!CityMap.CanStand(p.x, p.z, 1.6f, true)) continue;
                if (!Enemy.SpawnSpotClear(new Vector3(p.x, 0f, p.z), kind)) continue;   // не внутри фонтана, машины, ствола
                if (LightField.Sample(p + Vector3.up * 1.2f) > 0.06f) continue;
                if (pathChecks >= 5) break;
                pathChecks++;
                if (!CityMap.HasPath(around.x, around.z, p.x, p.z)) continue;
                p.y = 0f;
                pos = p;
                return true;
            }
            pos = around;
            return false;
        }

        // ------------------------------------------------------------------ возрождение

        /// <summary>
        /// Возврат к последнему очагу: мир за ним сбрасывается, спасённые до него возвращаются. voluntary — игрок сам выбрал возврат из паузы
        /// (застрял): света ему не убавляют, он получает не меньше, чем при обычном возрождении.
        /// </summary>
        public void Respawn(bool voluntary = false)
        {
            GameManager gm = GameManager.Instance;
            PlayerController pl = PlayerController.Instance;
            if (gm == null || pl == null) return;

            Enemy.ClearAll();
            LightOrb.ClearAll();
            float cp = Checkpoint != null ? Checkpoint.Progress : 0f;

            for (int i = 0; i < Lamp.All.Count; i++)
            {
                if (Lamp.All[i].Progress > cp + 0.01f) Lamp.All[i].ResetToInitial();
            }
            for (int i = 0; i < Growth.All.Count; i++)
            {
                if (Growth.All[i].Progress > cp + 0.01f) Growth.All[i].ResetToInitial();
            }
            for (int i = 0; i < Generator.All.Count; i++)
            {
                if (Generator.All[i].Progress > cp + 0.01f) Generator.All[i].ResetToInitial();
            }
            int k = 0;
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Survivor s = Survivor.All[i];
                if (checkpointFollowers.Contains(s))
                {
                    Vector3 off = new Vector3(Mathf.Sin(k * 1.7f) * 1.6f, 0f, Mathf.Cos(k * 1.7f) * 1.6f);
                    Vector3 target = CheckpointPos + off;
                    if (!CityMap.IsRoad(target.x, target.z, false)) target = CheckpointPos;
                    s.ReviveAt(target);
                    k++;
                }
                else if (s.Progress > cp + 0.01f)
                {
                    s.ResetToInitial();
                }
            }
            if (Bus.Instance != null) Bus.Instance.ResetToInitial();

            for (int i = 0; i < TideAt.Length; i++)
            {
                if (TideAt[i] > cp) tideDone[i] = false;
            }
            TideActive = false;
            TideLeft = 0f;
            gm.TideFactor = 1f;
            FinalStand = false;
            bossSpawned = false;
            bossDefeated = false;
            if (cp < GameConfig.EaterAtProgress) eaterSpawned = false;
            spawnTimer = 8f;
            snufferTimer = 14f;

            pl.Hero.ResetFor(voluntary ? Mathf.Max(pl.Hero.Life, GameConfig.RespawnLight) : GameConfig.RespawnLight, true);
            pl.TeleportTo(CheckpointPos, 0f);
            GuideWisp.CancelActive();
        }
    }
}
