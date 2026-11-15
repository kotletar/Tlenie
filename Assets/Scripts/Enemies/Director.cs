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

        const float TideTotal = 28f;
        static readonly float[] TideAt = { 0.30f, 0.55f, 0.80f };

        readonly List<Survivor> checkpointFollowers = new List<Survivor>();
        readonly List<Lamp> hearths = new List<Lamp>();
        readonly bool[] tideDone = new bool[3];
        float spawnTimer = 6f;
        float snufferTimer = 12f;
        float finalEaterTimer;
        bool eaterSpawned;

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
            return false;
        }

        public void StartFinal()
        {
            FinalStand = true;
            finalEaterTimer = 5f;
            spawnTimer = 1f;
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
                    if (FindDarkSpot(hp, 20f, 36f, out p)) Enemy.Spawn(EnemyKind.Shade, p);
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
                if (FindDarkSpot(hp, 28f, 40f, out p))
                {
                    eaterSpawned = true;
                    Enemy.Spawn(EnemyKind.Eater, p);
                    GameManager.Instance.Notify("Что-то огромное просыпается впереди…");
                }
            }

            if (FinalStand)
            {
                finalEaterTimer -= dt;
                if (finalEaterTimer <= 0f)
                {
                    finalEaterTimer = GameConfig.FinalEaterInterval;
                    if (Enemy.CountOf(EnemyKind.Eater) < 2)
                    {
                        Vector3 p;
                        if (FindDarkSpot(hp, 24f, 38f, out p)) Enemy.Spawn(EnemyKind.Eater, p);
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
            if (FindDarkSpot(target.BasePos, 14f, 26f, out p)) Enemy.Spawn(EnemyKind.Snuffer, p);
        }

        /// <summary>
        /// Тёмная точка на дороге на заданном расстоянии: не в здании, не у самой стены и с проходом к точке around
        /// (за наростом Тьмы враг бы застрял, поэтому там он не рождается).
        /// </summary>
        static bool FindDarkSpot(Vector3 around, float minD, float maxD, out Vector3 pos)
        {
            int pathChecks = 0;
            for (int tries = 0; tries < 28; tries++)
            {
                float ang = Random.Range(0f, 6.283f);
                float dist = Random.Range(minD, maxD);
                Vector3 p = around + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!CityMap.CanStand(p.x, p.z, 1.6f, true)) continue;
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

        /// <summary>Возврат к последнему очагу: мир за ним сбрасывается, спасённые до него возвращаются.</summary>
        public void Respawn()
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
            if (cp < GameConfig.EaterAtProgress) eaterSpawned = false;
            spawnTimer = 8f;
            snufferTimer = 14f;

            pl.Hero.ResetFor(GameConfig.RespawnLight, true);
            pl.TeleportTo(CheckpointPos, 0f);
            GuideWisp.CancelActive();
        }
    }
}
