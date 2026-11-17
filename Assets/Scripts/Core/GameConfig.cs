namespace PokaGoryu
{
    /// <summary>
    /// ВСЕ числа баланса в одном месте. Крутите здесь, больше нигде ничего искать не нужно.
    /// "ед." — единицы света. Свет героя — это и есть его здоровье.
    /// </summary>
    public static class GameConfig
    {
        public const string Version = "0.10.0";
        // ---------- Свет героя (он же здоровье) ----------
        public static float MaxLight = 100f;
        public static float StartLight = 75f;
        public static float RespawnLight = 55f;       // с сколькими единицами возрождаешься у очага
        public static float SmolderPerSec = 0.24f;    // "тление": расход всегда, отсидеться негде (было 0,30, потом 0,20)
        public static float HearthRegenPerSec = 0.30f; // у очага свет очень медленно возвращается (с 50 до 100 — около трёх минут)
        public static float AuraMin = 2f;             // мин. радиус ауры (м), колесо мыши
        public static float AuraMax = 10f;
        public static float AuraStart = 5f;
        public static float AuraCostK = 0.040f;       // расход/с = K * радиус^2 (радиус 5 м = 1,0 ед/с; было 0,045 — 1,1, потом 0,036 — 0,9)
        public static float CoverRadius = 1.2f;       // радиус, когда герой прикрывает огонь ладонями
        public static float CoverCostMul = 0.2f;      // во сколько раз дешевле аура под прикрытием
        public static float LastBreathTime = 8f;      // секунд "последнего вздоха" до смерти
        public static float LastBreathDelay = 1.2f;   // через сколько спасённые могут раздуть героя обратно

        // ---------- Руки: выстрел / луч (ЛКМ) и вспышка (ПКМ), как в PalmShot ----------
        public static float ShotCost = 5f;            // тап ЛКМ: выстрел двумя лучами из ладоней
        public static float ShotDamage = 30f;
        public static float HoldDps = 40f;            // ЛКМ удержать: лучи сливаются в один толстый
        public static float BeamDrainPerSec = 8f;     // расход света, пока держишь слитый луч
        public static float MergeTime = 0.3f;
        public static float MergedWidthMul = 1.6f;
        public static float CombatRange = 30f;
        public static float NovaCost = 20f;           // ПКМ: вспышка света вокруг героя
        public static float NovaRadius = 8f;
        public static float NovaDamage = 14f;
        public static float NovaStun = 2.5f;
        public static float NovaDamageGrowth = 10f;

        // ---------- Проводник (G): огонёк, который ведёт к автобусу ----------
        public static float GuideCost = 8f;           // первый вызов бесплатный
        public static float GuideCooldown = 14f;
        public static float GuideTime = 40f;          // сколько живёт огонёк
        public static float GuideSpeed = 7f;          // м/с, ждёт героя, если тот отстал
        public static float GuideWaitDist = 15f;      // дальше этого огонёк останавливается и ждёт

        // ---------- Вдох / выдох ----------
        public static float InhaleReach = 3.4f;       // на каком расстоянии от лампы можно вдохнуть
        public static float InhaleRate = 14f;         // ед/с забирается из лампы
        public static float InhaleEffMin = 0.35f;     // отдача почти потухшей лампы (у полной — 1.0)
        public static float LampCapacity = 45f;       // сколько света в полной лампе
        public static float LowLifeBonus = 1.3f;      // множитель отдачи, когда у героя света мало (против спирали смерти)
        public static float ExhaleRate = 12f;         // ед/с отдаёт герой
        public static float ExhaleLampEff = 0.9f;
        public static float LampMaxCharge = 2f;       // выше 1.0 лампа становится очагом
        public static float WakeCost = 12f;           // сколько стоит разбудить потухшего человека
        public static float WakeTime = 1.2f;
        public static float HealRate = 8f;            // ед/с, которые герой отдаёт раненому
        public static float HealPerLight = 2f;        // сколько здоровья выжившего даёт 1 ед света
        public static float OrbReach = 3.6f;          // на каком расстоянии осколок можно вдохнуть на E
        public static float OrbMagnetRadius = 1.8f;   // ближе этого осколок сам летит к герою (убил вплотную — получил, дальше — подойти и вдохнуть на E)
        public static float DecoyDrain = 18f;         // ед/с, которые вытягивает Блуждающий огонь
        public static float DecoyBurstTime = 1.4f;

        // ---------- Выжившие ----------
        public static float ResonancePerSec = 0.34f;  // ед/с герою за каждого выжившего рядом при полном здоровье (трое примерно гасят тление; было 0,45)
        public static float SurvivorMaxHealth = 160f;     // живучие: было 100
        public static float SurvivorWakeHealth = 100f;    // с каким здоровьем человек просыпается (было 60)
        public static float SurvivorSpeed = 3.9f;
        public static float DarknessDps = 2.5f;           // урон выжившему, если он вне света (было 5)
        public static float SurvivorRegenPerSec = 0.8f;   // в свете человек понемногу приходит в себя
        public static float DarkThreshold = 0.07f;        // яркость ниже этой = "во Тьме"
        public static float HearthHealPerSec = 12f;       // у очага лечатся быстрее (было 7)
        public static float HearthRadius = 7.5f;

        // ---------- Враги ----------
        public static float ShadeLightFear = 0.30f;   // при такой яркости Тень пятится и горит
        public static float ShadeBurnPerLight = 30f;
        public static float ShadeDpsHero = 4f;
        public static float ShadeDpsSurvivor = 7f;    // было 12
        public static float SnufferDpsLamp = 0.55f;   // доли заряда лампы в секунду
        public static float SnufferDpsSurvivor = 6f;  // было 10
        public static float EaterHeroDamage = 24f;
        public static float EaterBiteSurvivor = 70f;  // укус Пожирателя (раньше съедал мгновенно): человек с полным здоровьем выдержит два

        // Осколки света, которые оставляют убитые враги (их вдыхают на E или они сами летят к герою вблизи).
        // Выстрел стоит 5: убитая Тень приносит 11, Гаситель 8 — убивая, герой зарабатывает, но немного.
        public static float OrbValueShade = 11f;      // было 6, потом 14
        public static float OrbValueSnuffer = 8f;     // было 4, потом 9
        public static float OrbValueEater = 46f;      // было 26, потом 60 (слитый луч на него стоит ~32)
        public static float FlashOrbShare = 0.4f;     // убитый вспышкой (ПКМ) оставляет 40% осколка; раньше — ничего

        // ---------- Пицца: находка в переулках (взял на E — тут же съел, это свет) ----------
        public static float PizzaLight = 24f;         // ед. света за целую пиццу (три укуса по трети); свет выше максимума не копится
        public static float PizzaReach = 2.4f;        // с какого расстояния её можно взять: она лежит на земле, подойти надо ближе, чем к лампе

        // ---------- Автобус / финал ----------
        public static float BusNeed = 60f;            // сколько света нужно, чтобы завести автобус
        public static float BusFeedFromSurvivor = 3.5f;
        public static float FinalTime = 60f;          // последняя минута: сколько держать оборону после запуска двигателя (раньше 18 с); под неё ляжет музыка

        // ---------- Босс последней минуты: Великий Гаситель ----------
        // Парит над площадью вокзала. Под бронёй неуязвим и бьёт копьями Тьмы; раз в несколько секунд броня раскрывается, и по ядру можно бить.
        // Здоровье делится на три трети: пока третья не опустошена, окно закрывается по времени и открывается снова.
        // Выстрел по ядру (30 урона) пролетает с множителем BossCoreMul и возвращает часть света: ядро проливает украденное.
        public static bool BossEnabled = true;
        public static float BossArriveDelay = 12f;   // через сколько секунд после запуска двигателя он поднимается над городом
        public static float BossFlyTime = 9f;       // сколько летит к вокзалу (издалека виден силуэт); всё это время броня закрыта
        public static float BossHealth = 540f;       // всего: три трети по 180
        public static float BossCoreMul = 1.5f;      // урон по открытому ядру: выстрел 30 -> 45 (броня глотает весь урон)
        public static float BossBleed = 0.05f;       // света герою за каждую единицу урона по ядру: выстрел стоит 5, возвращает ~2,25
        public static float BossWindow = 4.6f;       // сколько секунд ядро открыто в первой трети (в каждой следующей на 0,5 с меньше)
        public static float BossLanceDamage = 9f;    // света за попадание копья Тьмы
        public static float BossLanceRadius = 1.15f; // толщина копья, м (плюс тело героя)
        public static float BossBreakLight = 8f;     // свет, который проливается, когда опустошена очередная треть
        public static float BossDeathLight = 16f;    // и когда он гибнет

        // Spawn pressure and pacing; Standard preserves the previous balance.
        public static float DistrictPressure = 0.3f;
        public static float TidePressure = 1.2f;
        public static float FinalPressure = 1.5f;
        public static float HearthPressure = 0.4f;
        public static float SpawnIntervalMin = 3.5f;
        public static float SpawnIntervalMax = 6f;
        public static float SnufferIntervalMin = 9f;
        public static float SnufferIntervalMax = 14f;
        public static float EaterAtProgress = 0.62f;
        public static float FinalEaterInterval = 14f;
        public static int ShadesBase = 2;
        public static int ShadesPerDistrict = 2;
        public static int ShadesTide = 3;
        public static int ShadesFinal = 4;

        public static void ResetDefaults()
        {
            MaxLight = 100f;
            StartLight = 75f;
            RespawnLight = 55f;
            SmolderPerSec = 0.24f;
            HearthRegenPerSec = 0.30f;
            AuraMin = 2f;
            AuraMax = 10f;
            AuraStart = 5f;
            AuraCostK = 0.040f;
            CoverRadius = 1.2f;
            CoverCostMul = 0.2f;
            LastBreathTime = 8f;
            LastBreathDelay = 1.2f;
            ShotCost = 5f;
            ShotDamage = 30f;
            HoldDps = 40f;
            BeamDrainPerSec = 8f;
            MergeTime = 0.3f;
            MergedWidthMul = 1.6f;
            CombatRange = 30f;
            NovaCost = 20f;
            NovaRadius = 8f;
            NovaDamage = 14f;
            NovaStun = 2.5f;
            NovaDamageGrowth = 10f;
            GuideCost = 8f;
            GuideCooldown = 14f;
            GuideTime = 40f;
            GuideSpeed = 7f;
            GuideWaitDist = 15f;
            InhaleReach = 3.4f;
            InhaleRate = 14f;
            InhaleEffMin = 0.35f;
            LampCapacity = 45f;
            LowLifeBonus = 1.3f;
            ExhaleRate = 12f;
            ExhaleLampEff = 0.9f;
            LampMaxCharge = 2f;
            WakeCost = 12f;
            WakeTime = 1.2f;
            HealRate = 8f;
            HealPerLight = 2f;
            OrbReach = 3.6f;
            OrbMagnetRadius = 1.8f;
            DecoyDrain = 18f;
            DecoyBurstTime = 1.4f;
            ResonancePerSec = 0.34f;
            SurvivorMaxHealth = 160f;
            SurvivorWakeHealth = 100f;
            SurvivorSpeed = 3.9f;
            DarknessDps = 2.5f;
            SurvivorRegenPerSec = 0.8f;
            DarkThreshold = 0.07f;
            HearthHealPerSec = 12f;
            HearthRadius = 7.5f;
            ShadeLightFear = 0.30f;
            ShadeBurnPerLight = 30f;
            ShadeDpsHero = 4f;
            ShadeDpsSurvivor = 7f;
            SnufferDpsLamp = 0.55f;
            SnufferDpsSurvivor = 6f;
            EaterHeroDamage = 24f;
            EaterBiteSurvivor = 70f;
            OrbValueShade = 11f;
            OrbValueSnuffer = 8f;
            OrbValueEater = 46f;
            FlashOrbShare = 0.4f;
            PizzaLight = 24f;
            PizzaReach = 2.4f;
            BusNeed = 60f;
            BusFeedFromSurvivor = 3.5f;
            FinalTime = 60f;
            BossEnabled = true;
            BossArriveDelay = 12f;
            BossFlyTime = 9f;
            BossHealth = 540f;
            BossCoreMul = 1.5f;
            BossBleed = 0.05f;
            BossWindow = 4.6f;
            BossLanceDamage = 9f;
            BossLanceRadius = 1.15f;
            BossBreakLight = 8f;
            BossDeathLight = 16f;
            DistrictPressure = 0.3f;
            TidePressure = 1.2f;
            FinalPressure = 1.5f;
            HearthPressure = 0.4f;
            SpawnIntervalMin = 3.5f;
            SpawnIntervalMax = 6f;
            SnufferIntervalMin = 9f;
            SnufferIntervalMax = 14f;
            EaterAtProgress = 0.62f;
            FinalEaterInterval = 14f;
            ShadesBase = 2;
            ShadesPerDistrict = 2;
            ShadesTide = 3;
            ShadesFinal = 4;
        }
    }
}
