namespace PokaGoryu
{
    public enum Challenge { Gentle, Standard, Harsh }

    public static class GameDifficulty
    {
        public static Challenge Mode = Challenge.Standard;

        /// <summary>
        /// Номер города в режиме «Бесконечный город» (1 — первый; в классике всегда 1). Каждый следующий город темнее и опаснее:
        /// чаще и гуще Тени, быстрее тает свет, больше потухших ламп. Первый город равен классике.
        /// </summary>
        public static int City = 1;

        static int Extra { get { return City > 1 ? City - 1 : 0; } }

        public static float DrainScale { get { return (Mode == Challenge.Gentle ? 0.8f : Mode == Challenge.Harsh ? 1.15f : 1f) * (1f + 0.03f * Extra); } }
        public static float OrbScale { get { return Mode == Challenge.Gentle ? 1.15f : Mode == Challenge.Harsh ? 0.9f : 1f; } }
        public static float ResonanceScale { get { return Mode == Challenge.Gentle ? 1.2f : Mode == Challenge.Harsh ? 0.9f : 1f; } }
        public static float PressureScale { get { return (Mode == Challenge.Gentle ? 0.8f : Mode == Challenge.Harsh ? 1.2f : 1f) * (1f + 0.15f * Extra); } }

        /// <summary>Во сколько раз Великий Гаситель крепче: мягкая 0.8, суровая 1.25, в каждом следующем городе ещё на 12%.</summary>
        public static float BossScale { get { return (Mode == Challenge.Gentle ? 0.8f : Mode == Challenge.Harsh ? 1.25f : 1f) * (1f + 0.12f * Extra); } }

        /// <summary>Во сколько раз сильнее удар копья Тьмы: мягкая 0.8, суровая 1.2.</summary>
        public static float BossLanceScale { get { return Mode == Challenge.Gentle ? 0.8f : Mode == Challenge.Harsh ? 1.2f : 1f; } }

        /// <summary>Лишние Тени одновременно: по одной за каждые два пройденных города.</summary>
        public static int ExtraShades { get { return Extra / 2; } }

        /// <summary>Добавка к броску кубика при раскладке ламп: чем дальше город, тем больше потухших и нестабильных (не больше 0.2).</summary>
        public static double LampWearBonus { get { return System.Math.Min(0.2, 0.03 * Extra); } }

        public static string Name(Challenge mode)
        {
            return mode == Challenge.Gentle ? "Мягкая" : mode == Challenge.Harsh ? "Суровая" : "Обычная";
        }
    }
}
