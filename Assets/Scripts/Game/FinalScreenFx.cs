using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Эффекты на экране на время последней минуты (пока автобус заведён и идёт оборона): тёплая каёмка по краям кадра, которая бьётся
    /// в такт сердцу (чем дальше минута и чем меньше света у героя, тем чаще; при малом свете каёмка красная), угольки, летящие по экрану, наезд
    /// на каждый удар сердца, края кадра смыкаются, хроматика и редкие сбои «трекинга» растут к концу. Бой с боссом добавляет своё: каёмка
    /// фиолетовая и бьётся чаще, пока копьё целится (быстро, когда линия замерла), оранжевое зарево и больше свечения, пока ядро открыто, волна-рябь от
    /// босса или центра кадра на его события. Последние десять секунд: волна и вспышка каёмки каждую секунду. Всё рисует шейдер Pokagoryu/Post
    /// (поля LowRes); вне минуты эффекты выключены, смерть, возрождение, выход в меню и концовка начинают с чистого экрана (пауза их не снимает:
/// сердце и волна замирают). Выключенная в настройках тряска
    /// смягчает вспышки, волны, наезд и сбои (мигания и пульс не сильнее 2 раз в секунду).
    /// </summary>
    public sealed class FinalScreenFx : MonoBehaviour
    {
        public static FinalScreenFx Instance;

        float k;                 // насколько эффекты включены (0..1): плавно за 1.2 с после запуска двигателя
        float beat;              // удары сердца (доли)
        float ringT = 9f;        // секунд с последней волны
        float ringStrength;
        Vector2 ringCentre = new Vector2(0.5f, 0.5f);
        float chromaKick;
        float flashK;
        float prevLeft = 999f;
        float glitchSeed;
        bool applied;

        /// <summary>Для автотестов: насколько эффекты включены сейчас.</summary>
        public float Level { get { return k; } }
        /// <summary>Для автотестов: удар сердца 0..1 в этот момент.</summary>
        public float Pulse { get; private set; }

        void Awake()
        {
            Instance = this;
            glitchSeed = Random.value * 50f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Удар на экране: волна от центра кадра, всплеск хроматики и вспышка каёмки (strength 0..1.5, chroma и flash 0..1).
        /// </summary>
        public static void Kick(float strength, float chroma, float flash)
        {
            if (Instance != null) Instance.DoKick(strength, chroma, flash, new Vector2(0.5f, 0.5f));
        }

        /// <summary>То же, но волна идёт от точки мира (если она в кадре): босс, на которого смотрят, бьёт сам, а не «весь экран».</summary>
        public static void Kick(float strength, float chroma, float flash, Vector3 world)
        {
            if (Instance == null) return;
            Vector2 centre = new Vector2(0.5f, 0.5f);
            PlayerController pl = PlayerController.Instance;
            if (pl != null && pl.Cam != null)
            {
                Vector3 v = pl.Cam.WorldToViewportPoint(world);
                if (v.z > 0.5f && v.x > -0.2f && v.x < 1.2f && v.y > -0.2f && v.y < 1.2f) centre = new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y));
            }
            Instance.DoKick(strength, chroma, flash, centre);
        }

        void DoKick(float strength, float chroma, float flash, Vector2 centre)
        {
            ringStrength = Mathf.Max(ringT < 1.15f ? ringStrength * 0.5f : 0f, strength);
            ringT = 0f;
            ringCentre = centre;
            chromaKick = Mathf.Max(chromaKick, chroma);
            flashK = Mathf.Max(flashK, flash);
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            LowRes lr = LowRes.Instance;
            if (gm == null || lr == null) return;
            Bus bus = Bus.Instance;
            bool minute = gm.State == GameState.Playing && bus != null && bus.State == Bus.Mode.Running && !bus.Scripted;
            if (!minute)
            {
                if (applied || k > 0f) Off(lr);
                return;
            }

            float udt = Time.unscaledDeltaTime;
            float dt = gm.Paused ? 0f : Time.deltaTime;
            k = Mathf.MoveTowards(k, 1f, udt / 1.2f);
            float p = Mathf.Clamp01(bus.StandElapsed / Mathf.Max(1f, bus.StandLength));
            float left = bus.FinalLeft;
            HeroLight hero = gm.Hero;
            float low = hero != null ? Mathf.InverseLerp(0.35f, 0.08f, hero.Life01) : 0f;   // 0..1: как мало света
            float reduce = GameSettings.Shake ? 1f : 0.35f;

            // сердце: чем дальше минута и чем меньше света, тем чаще (66 -> 108 ударов, до +26 при малом свете)
            float bpm = Mathf.Lerp(66f, 108f, p) + 26f * low;
            beat += dt * bpm / 60f;
            float ph = beat - Mathf.Floor(beat);
            float pulse = Mathf.Clamp01(Mathf.Exp(-ph * 10f) + 0.6f * Mathf.Exp(-Mathf.Abs(ph - 0.30f) * 12f));
            Pulse = pulse;

            // последние десять секунд: каждую секунду малая волна и вспышка каёмки
            if (left < 10f && left > 0.05f && prevLeft < 999f && Mathf.Ceil(left) != Mathf.Ceil(prevLeft)) DoKick(0.30f, 0.30f, 0.28f, new Vector2(0.5f, 0.5f));
            prevLeft = left;

            // босс: что он сейчас делает
            float bossOpen = 0f;
            float aim = 0f;
            bool locked = false;
            Enemy boss = Enemy.FindBoss();
            if (boss != null && boss.BossState != BossMode.Dying)
            {
                bossOpen = boss.CoreExposed ? 1f : boss.BossOpenAmount;
                aim = boss.LanceCharge;
                locked = boss.LanceLocked;
            }

            // цвет и сила каёмки; что важнее, то перекрывает: вспышка > копьё целится > мало света > ядро открыто > обычное горение
            Color tint = new Color(1f, 0.52f, 0.18f, 1f);
            float strength = (0.15f + 0.17f * p) * (0.75f + 0.45f * pulse);
            tint = Color.Lerp(tint, new Color(1f, 0.62f, 0.26f, 1f), bossOpen * 0.8f);
            strength += 0.20f * bossOpen;
            tint = Color.Lerp(tint, new Color(1f, 0.10f, 0.08f, 1f), low);
            strength += 0.22f * low * (0.4f + 0.6f * pulse);
            float aimPulse = 0.55f + 0.45f * Mathf.Sin(Time.time * (locked ? 14f : 6f));   // не чаще двух вспышек в секунду
            tint = Color.Lerp(tint, new Color(0.72f, 0.38f, 1f, 1f), aim * 0.85f);
            strength += 0.40f * aim * aim * Mathf.Lerp(1f, aimPulse, reduce);
            tint = Color.Lerp(tint, new Color(1f, 0.93f, 0.72f, 1f), Mathf.Clamp01(flashK));
            strength += 0.55f * flashK * reduce;
            lr.FinalTint = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(strength * k));

            lr.Vignette = Mathf.Lerp(LowRes.DefaultVignette, 2.7f + 0.9f * p, k) + 0.55f * pulse * k * reduce + 0.5f * aim;
            lr.Embers = k * Mathf.Lerp(0.22f, 0.85f, p);
            lr.ChromaBoost = k * (0.25f + 0.45f * p) + chromaKick * reduce + 0.7f * aim;
            lr.Bloom = Mathf.Lerp(LowRes.DefaultBloom, 1.25f, Mathf.Clamp01(bossOpen * 0.8f * k + 0.3f * flashK));
            float punch = ringT < 0.25f ? 0.012f * (1f - ringT / 0.25f) : 0f;   // наезд на ударе
            lr.Zoom = (k * 0.005f * pulse + punch) * reduce;
            float burst = Mathf.PerlinNoise(Time.time * 0.45f, glitchSeed) > 0.80f ? 0.22f : 0f;   // редкий сбой «трекинга»
            lr.ExtraGlitch = (k * burst + 0.35f * chromaKick) * reduce;

            // волна
            ringT += dt;
            if (ringT < 1.15f)
            {
                float u = ringT / 1.15f;
                lr.Ripple = new Vector4(ringCentre.x, ringCentre.y, u * 1.35f, ringStrength * Mathf.Pow(1f - u, 1.5f) * reduce);
            }
            else
            {
                ringStrength = 0f;
                lr.Ripple = new Vector4(0.5f, 0.5f, 0f, 0f);
            }
            chromaKick = Mathf.MoveTowards(chromaKick, 0f, dt * 1.6f);
            flashK = Mathf.MoveTowards(flashK, 0f, dt * 2.2f);

            // двигатель дрожит под ногами (еле заметно)
            PlayerController pl = gm.Player;
            if (pl != null && GameSettings.Shake) pl.Rumble(0.16f * k);
            applied = true;
        }

        /// <summary>Минута кончилась (уехал автобус, герой погас, меню): эффекты с экрана долой.</summary>
        void Off(LowRes lr)
        {
            k = 0f;
            ringT = 9f;
            ringStrength = 0f;
            chromaKick = 0f;
            flashK = 0f;
            prevLeft = 999f;
            Pulse = 0f;
            if (applied)
            {
                lr.ClearFinalFx();
                lr.Vignette = LowRes.DefaultVignette;
                lr.ExtraGlitch = 0f;
            }
            applied = false;
        }
    }
}
