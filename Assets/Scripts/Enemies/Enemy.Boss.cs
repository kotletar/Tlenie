using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Что делает Великий Гаситель: летит к вокзалу, под бронёй (бьёт копьями Тьмы), с открытым ядром (уязвим), гибнет.</summary>
    public enum BossMode { Arrive, Armored, Open, Dying }

    /// <summary>
    /// Босс последней минуты (часть Enemy: он тоже враг для лучей, вспышки и подсчёта; тело — BossRig, режиссёр — Director.SpawnBoss).
    /// Парит над площадью вокзала на расстоянии BossStandOff от двери автобуса и качается там. Здоровье — три трети (фазы). Круг боя:
    ///  - ПОД БРОНЁЙ: весь урон по нему глотается (свет героя впустую), он бьёт копьями Тьмы: прицел 1.2-1.0 с (линия следит за героем, а на
    ///    последних 60% времени замирает: можно отойти), удар на 0.22 с толщиной BossLanceRadius, 9 света за попадание. Копьё останавливает
    ///    первое твёрдое (автобус, дом) и проходит сквозь людей. Прикрытый ладонями герой невидим: прицел остаётся там, где его видели в
    ///    последний раз (если он остался на месте, копьё всё равно его заденет). Копий в залпе 2, 3, 4 по фазам.
    ///  - ОТКРЫТОЕ ЯДРО: после залпа броня раскрывается на BossWindow секунд (в каждой следующей трети на 0.5 с меньше). Урон по ядру растёт
    ///    в BossCoreMul раз, а за каждую единицу урона ядро проливает герою BossBleed света (осколки летят прямо к нему): выстрел за 5 света
    ///    возвращает около 2. Когда третья опустошена, броня захлопывается (проливается ещё BossBreakLight), начинается следующая фаза;
    ///    в третьей он выпускает двух Гасителей. Последняя треть — гибель: свет сыплется и летит к герою, остальные враги рассеиваются.
    /// Автобус и дома останавливают копьё: за автобусом можно переждать залп. Хватит ли света на весь бой — главный вопрос.
    /// </summary>
    public partial class Enemy
    {
        const float LanceStrikeTime = 0.22f;
        const float LanceReach = 70f;

        BossMode bossMode;
        float bossT;
        int bossPhase = 1;
        float bossSegMax;
        float bossSegHp;
        Vector3 bossFrom;
        Vector3 bossEntry;
        Vector3 bossAnchor;
        float bossOpen;
        float bossBleedAcc;
        float bossPitch;
        int bossLancesLeft;
        float bossGap;
        int lanceState;        // 0 — нет, 1 — прицел, 2 — удар
        float lanceT;
        float lanceTele;
        Vector3 lanceDir = Vector3.forward;
        Vector3 lanceFrom;
        Vector3 lanceEnd;
        Vector3 aimSeen;
        bool aimSeenSet;
        AudioSource bossSfx;
        float armorFxT;

        /// <summary>Для автотестов и стенда: сколько копий выпущено и сколько попало в героя; урон по ядру; сколько ударов поглотила броня.</summary>
        public int LancesFired;
        public int LancesHit;
        public float CoreDamage;
        public int ArmorHits;

        public bool IsBoss { get { return Kind == EnemyKind.Boss; } }
        public BossMode BossState { get { return bossMode; } }
        public int BossPhase { get { return bossPhase; } }
        public float BossOpenAmount { get { return bossOpen; } }
        /// <summary>Ядро открыто и принимает урон (броня раскрыта больше чем на 60%).</summary>
        public bool CoreExposed { get { return Kind == EnemyKind.Boss && bossMode == BossMode.Open && bossOpen >= 0.6f && !dead; } }
        public float BossSegmentLeft { get { return bossSegHp; } }
        public float BossSegmentMax { get { return bossSegMax; } }
        public int LanceStage { get { return lanceState; } }
        /// <summary>Сколько секунд до удара, пока копьё целится (0, если не целится).</summary>
        public float LanceTimeLeft { get { return lanceState == 1 ? Mathf.Max(0f, lanceTele - lanceT) : 0f; } }
        /// <summary>Как близок удар: 0 — копьё не целится, 1 — вот-вот ударит (для экранных эффектов).</summary>
        public float LanceCharge { get { return lanceState == 1 ? Mathf.Clamp01(lanceT / Mathf.Max(0.1f, lanceTele)) : 0f; } }
        /// <summary>Линия сейчас замерла (последние 60% прицела): отойти с неё ещё можно, но она уже не следит.</summary>
        public bool LanceLocked { get { return lanceState == 1 && lanceT >= lanceTele * 0.4f; } }
        /// <summary>Сколько секунд назад линия замерла (отрицательно, пока она ещё следит или копьё не целится).</summary>
        public float LanceLockAge { get { return lanceState == 1 ? lanceT - lanceTele * 0.4f : -1f; } }
        public Vector3 LanceOrigin { get { return lanceFrom; } }
        public Vector3 LanceTip { get { return lanceEnd; } }
        public Vector3 BossAnchor { get { return bossAnchor; } }
        public Vector3 AimSeen { get { return aimSeen; } }

        /// <summary>Босс в этой игре (или null).</summary>
        public static Enemy FindBoss()
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].Kind == EnemyKind.Boss) return All[i];
            return null;
        }

        static int LancesFor(int phase) { return 1 + phase; }
        static float TelegraphFor(int phase) { return 1.2f - 0.1f * (phase - 1); }
        static float GapFor(int phase) { return phase == 1 ? 0.45f : phase == 2 ? 0.40f : 0.35f; }
        static float WindowFor(int phase) { return Mathf.Max(2.4f, GameConfig.BossWindow - 0.5f * (phase - 1)); }

        /// <summary>Появление босса: он прилетит из точки from через entry (над краем площади) в точку anchor (парит там). Вызывает режиссёр.</summary>
        public static Enemy SpawnBoss(Vector3 anchor, Vector3 entry, Vector3 from)
        {
            GameObject go = new GameObject("GreatSnuffer");
            go.transform.position = from;
            Enemy e = go.AddComponent<Enemy>();
            e.Setup(EnemyKind.Boss);
            e.BossInit(anchor, entry, from);
            return e;
        }

        void BossInit(Vector3 anchor, Vector3 entry, Vector3 from)
        {
            bossAnchor = anchor;
            bossEntry = entry;
            bossFrom = from;
            transform.position = from;
            bossMode = BossMode.Arrive;
            bossT = 0f;
            bossPhase = 1;
            bossSegMax = maxHealth / 3f;
            bossSegHp = bossSegMax;
            Health = maxHealth;
            bossSfx = ProcAudio.Source(gameObject, "shellopen", false, 1f, true);
            bossSfx.minDistance = 14f;
            bossSfx.maxDistance = 140f;
            PlayerController pl = PlayerController.Instance;
            if (pl != null)
            {
                ProcAudio.OneShot(pl.Sfx, "bossroar", 1f, 1f);
                pl.Rumble(1f);
            }
            FinalScreenFx.Kick(0.5f, 0.5f, 0.1f);
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Notify("Что-то огромное закрывает звёзды…");
                gm.Think("boss1", "Он закрыл собой небо. Это он гасил город, огонь за огнём.");
            }
        }

        /// <summary>Для автотестов: перейти сразу к режиму (минуя перелёт); броня и ядро действуют как обычно.</summary>
        public void DebugBossMode(BossMode mode)
        {
            if (Kind != EnemyKind.Boss || dead) return;
            transform.position = bossAnchor;
            if (mode == BossMode.Armored) BossEnterArmored(true);
            else if (mode == BossMode.Open)
            {
                bossMode = BossMode.Open;
                bossT = 0f;
                lanceState = 0;
                bossOpen = 1f;
            }
        }

        void OneShotBoss(string key, float volume, float pitch = 1f)
        {
            if (bossSfx != null) ProcAudio.OneShot(bossSfx, key, volume, pitch);
        }

        /// <summary>Точка пути перелёта: доля f (0..1) длины ломаной from -> entry -> anchor.</summary>
        public Vector3 BossPathPoint(float f)
        {
            float d1 = Vector3.Distance(bossFrom, bossEntry);
            float d2 = Vector3.Distance(bossEntry, bossAnchor);
            float at = Mathf.Clamp01(f) * (d1 + d2);
            if (at <= d1) return Vector3.Lerp(bossFrom, bossEntry, d1 > 0.01f ? at / d1 : 1f);
            return Vector3.Lerp(bossEntry, bossAnchor, d2 > 0.01f ? (at - d1) / d2 : 1f);
        }

        Vector3 BossMuzzle()
        {
            return transform.position + transform.forward * ((EnemyModels.BossR + 1.5f) * EnemyModels.BossScale);
        }

        void UpdateBoss(float dt, PlayerController pl)
        {
            bossT += dt;
            Transform cam = pl.Cam.transform;
            // героя видно, пока он не прикрыт ладонями: иначе прицел остаётся там, где его видели в последний раз
            if (!pl.Hero.Covered || !aimSeenSet)
            {
                aimSeen = cam.position + Vector3.down * 0.3f;
                aimSeenSet = true;
            }

            // лицом к герою, тело наклонено к нему: дуло смотрит прямо на него
            hasFace = true;
            faceTarget = pl.transform.position;
            Vector3 toHero = cam.position - transform.position;
            float horiz = new Vector2(toHero.x, toHero.z).magnitude;
            float wantPitch = Mathf.Clamp(Mathf.Atan2(-toHero.y, Mathf.Max(0.1f, horiz)) * Mathf.Rad2Deg, -35f, 35f);
            bossPitch = Mathf.MoveTowardsAngle(bossPitch, wantPitch, 25f * dt);

            if (bossMode == BossMode.Arrive)
            {
                float u = Mathf.Clamp01(bossT / Mathf.Max(0.5f, GameConfig.BossFlyTime));
                float e = u * u * (3f - 2f * u);   // медленно выходит из-за крыш, разгоняется, у якоря тормозит
                transform.position = BossPathPoint(e);
                pl.Rumble(0.10f + 0.40f * u * u);
                if (u >= 1f) BossEnterArmored(true);
            }
            else if (bossMode != BossMode.Dying)
            {
                transform.position = bossAnchor + new Vector3(Mathf.Sin(age * 0.31f) * 2.4f, Mathf.Sin(age * 0.7f) * 0.5f, Mathf.Sin(age * 0.23f + 1.3f) * 2.0f);
            }

            float openTarget = bossMode == BossMode.Open ? 1f : 0f;
            bossOpen = Mathf.MoveTowards(bossOpen, openTarget, dt / (openTarget > bossOpen ? 0.5f : 0.25f));

            if (bossMode == BossMode.Armored) BossArmoredStep(dt, pl);
            else if (bossMode == BossMode.Open && bossT >= WindowFor(bossPhase)) BossEnterArmored(false);
        }

        void BossEnterArmored(bool first)
        {
            bossMode = BossMode.Armored;
            bossT = 0f;
            lanceState = 0;
            bossLancesLeft = LancesFor(bossPhase);
            bossGap = first ? 1.2f : 0.7f;
            if (!first) OneShotBoss("shellslam", 0.7f);
        }

        void BossEnterOpen()
        {
            bossMode = BossMode.Open;
            bossT = 0f;
            lanceState = 0;
            OneShotBoss("shellopen", 0.8f);
            FinalScreenFx.Kick(0.35f, 0.3f, 0.2f, transform.position);
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Notify("Ядро открыто: бей!");
                gm.Think("boss2", "Броня глотает мой свет. А ядро открыто — только пока она раскрыта.");
            }
        }

        /// <summary>Под бронёй: залп копий Тьмы одно за другим, потом броня раскрывается.</summary>
        void BossArmoredStep(float dt, PlayerController pl)
        {
            if (lanceState == 0)
            {
                if (bossGap > 0f)
                {
                    bossGap -= dt;
                    return;
                }
                if (bossLancesLeft > 0) BossBeginLance();
                else BossEnterOpen();
                return;
            }
            lanceT += dt;
            lanceFrom = BossMuzzle();
            if (lanceState == 1)
            {
                // линия следит за героем первые 40% прицела, потом замирает: отойти с неё ещё можно
                if (lanceT < lanceTele * 0.4f)
                {
                    Vector3 want = (aimSeen - lanceFrom).normalized;
                    lanceDir = Vector3.RotateTowards(lanceDir, want, 140f * Mathf.Deg2Rad * dt, 0f);
                }
                lanceEnd = ComputeLanceEnd(false);
                if (lanceT >= lanceTele) BossStrike(pl);
            }
            else if (lanceT >= LanceStrikeTime)
            {
                lanceState = 0;
                bossLancesLeft--;
                bossGap = GapFor(bossPhase);
            }
        }

        void BossBeginLance()
        {
            lanceState = 1;
            lanceT = 0f;
            lanceTele = TelegraphFor(bossPhase);
            lanceFrom = BossMuzzle();
            lanceDir = (aimSeen - lanceFrom).normalized;
            lanceEnd = ComputeLanceEnd(false);
            OneShotBoss("lancecharge", 0.55f);
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.Think("boss3", "Линия замирает, и тогда бьёт. Надо уходить с неё: или спрятаться за автобусом.");
        }

        /// <summary>Докуда доходит копьё: первое твёрдое на пути (автобус, дом, земля, герой) или предел дальности. thick — с толщиной копья.</summary>
        Vector3 ComputeLanceEnd(bool thick)
        {
            RaycastHit hit;
            bool any = thick
                ? Physics.SphereCast(lanceFrom, GameConfig.BossLanceRadius, lanceDir, out hit, LanceReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.Raycast(lanceFrom, lanceDir, out hit, LanceReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return any ? lanceFrom + lanceDir * hit.distance : lanceFrom + lanceDir * LanceReach;
        }

        void BossStrike(PlayerController pl)
        {
            lanceState = 2;
            lanceT = 0f;
            LancesFired++;
            lanceFrom = BossMuzzle();
            RaycastHit hit;
            bool any = Physics.SphereCast(lanceFrom, GameConfig.BossLanceRadius, lanceDir, out hit, LanceReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool heroHit = any && hit.collider.GetComponentInParent<PlayerController>() != null;
            lanceEnd = any ? lanceFrom + lanceDir * hit.distance : lanceFrom + lanceDir * LanceReach;
            if (heroHit)
            {
                lanceEnd += lanceDir * 12f;   // для глаз: луч проходит сквозь героя дальше, а не обрывается торцом перед камерой
                LancesHit++;
                pl.Hero.Damage(GameConfig.BossLanceDamage * GameDifficulty.BossLanceScale);
                Vector3 push = pl.transform.position - transform.position;
                push.y = 0f;
                if (push.sqrMagnitude < 0.01f) push = Vector3.forward;
                pl.Push(push.normalized * 6f);
                pl.Rumble(0.8f);
                FinalScreenFx.Kick(0.9f, 1.1f, 0.35f);
            }
            else if (Vector3.Distance(pl.transform.position, lanceEnd) < 9f)
            {
                pl.Rumble(0.35f);
                FinalScreenFx.Kick(0.3f, 0.4f, 0f, lanceEnd);
            }
            OneShotBoss("lance", 0.85f);
            TimedFx.Spawn(lanceEnd, 1.2f, 7f, new Color(0.8f, 0.5f, 1f, 0.8f), 0.35f);
        }

        // ------------------------------------------------------------------ урон

        /// <summary>
        /// Урон боссу: броня глотает всё (свет впустую), открытое ядро берёт его с множителем и проливает часть света герою; опустошённая треть
        /// здоровья захлопывает броню и открывает следующую фазу, третья — гибель.
        /// </summary>
        void BossTakeDamage(float dmg, DamageKind kind)
        {
            if (dead || bossMode == BossMode.Dying) return;
            if (kind == DamageKind.Light) return;   // свет ламп и автобуса его не жжёт
            if (!CoreExposed)
            {
                // броня глотает свет: вспышка на её поверхности (со стороны героя) и глухой звон, не чаще 8 раз в секунду
                hitPulse = Mathf.Max(hitPulse, 0.2f);
                ArmorHits++;
                if (Time.time - armorFxT > 0.12f)
                {
                    armorFxT = Time.time;
                    PlayerController plHit = PlayerController.Instance;
                    Vector3 toHero = plHit != null ? (plHit.Cam.transform.position - transform.position).normalized : Vector3.forward;
                    TimedFx.Spawn(transform.position + toHero * (HitRadius * 0.85f) + Random.insideUnitSphere * 0.8f, 1.2f, 3.4f, new Color(0.9f, 0.75f, 1f, 0.9f), 0.22f);
                    OneShotBoss("shellslam", 0.22f, 2.4f);
                    GameManager gmHint = GameManager.Instance;
                    if (gmHint != null && ArmorHits >= 2) gmHint.Think("boss4", "Броня глотает мой свет впустую. Надо ждать, пока она раскроется.");
                }
                return;
            }
            float applied = Mathf.Min(dmg * GameConfig.BossCoreMul * (kind == DamageKind.Flash ? 0.6f : 1f), bossSegHp);
            if (applied <= 0f) return;
            bossSegHp -= applied;
            Health -= applied;
            CoreDamage += applied;
            hitPulse = 1f;
            bossBleedAcc += applied * GameConfig.BossBleed;
            if (bossBleedAcc >= 2f)
            {
                SpillLight(bossBleedAcc, 1);
                bossBleedAcc = 0f;
            }
            if (bossSegHp <= 0.01f) BossBreak();
        }

        /// <summary>Осколки света, которые тут же летят к герою (босс висит высоко: подбирать их руками было бы нечем).</summary>
        void SpillLight(float value, int count)
        {
            Vector3 at = transform.position + transform.forward * 2.5f;
            for (int i = 0; i < count; i++)
            {
                LightOrb o = LightOrb.Spawn(at + Random.insideUnitSphere * 1.4f, value / count);
                if (o != null) o.Collect();
            }
        }

        void BossBreak()
        {
            SpillLight(GameConfig.BossBreakLight, 2);
            if (bossPhase >= 3)
            {
                BossDie();
                return;
            }
            bossPhase++;
            bossSegHp = bossSegMax;
            Health = bossSegMax * (4 - bossPhase);
            BossEnterArmored(false);
            bossGap = 1.6f;   // захлопнулся: недолгая пауза перед залпом
            PlayerController pl = PlayerController.Instance;
            if (pl != null) pl.Rumble(0.9f);
            FinalScreenFx.Kick(1.0f, 1.0f, 0.5f, transform.position);
            if (bossPhase == 3)
            {
                Vector3 door = Bus.Instance != null ? Bus.Instance.DoorPos : transform.position;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p;
                    if (Director.FindDarkSpot(door, 10f, 24f, EnemyKind.Snuffer, out p)) Spawn(EnemyKind.Snuffer, p);
                }
            }
        }

        void BossDie()
        {
            if (dead) return;
            dead = true;
            bossMode = BossMode.Dying;
            lanceState = 0;
            Health = 0f;
            All.Remove(this);
            SpillLight(GameConfig.BossDeathLight, 4);
            // остальные враги рассеиваются вместе с ним: без осколков и без звука
            for (int i = All.Count - 1; i >= 0; i--)
            {
                if (All[i] != null) All[i].Dissolve();
            }
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnEnemyKilled(this);
                if (gm.Dir != null) gm.Dir.OnBossDefeated();
            }
            PlayerController pl = PlayerController.Instance;
            if (pl != null)
            {
                ProcAudio.OneShot(pl.Sfx, "bossdie", 1f, 1f);
                pl.Rumble(1.6f);
                pl.Hero.FlashPulse = 1f;
            }
            FinalScreenFx.Kick(1.4f, 1.6f, 1.0f, transform.position);
            Collapse();
        }

        /// <summary>Автобус уехал, а босс жив: он остаётся за спиной и рассеивается (без осколков, звуков и подсчёта).</summary>
        public void BossDepart()
        {
            if (Kind != EnemyKind.Boss) return;
            Dissolve();
        }

        /// <summary>Тихо рассеяться (без осколков и звука): остальные враги, когда босс погиб.</summary>
        void Dissolve()
        {
            if (dead) return;
            dead = true;
            All.Remove(this);
            Collapse();
        }
    }
}
