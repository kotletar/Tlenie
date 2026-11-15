using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Four sample-synchronised stems. Only their gains change with danger.</summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        public float Intensity { get; private set; }
        public float TargetIntensity { get; private set; }
        public const float DefaultVolume = 0.65f;
        public const float MixGain = 0.8f;

        readonly AudioSource[] sources = new AudioSource[SuspenseScore.Layers];
        readonly AudioClip[] clips = new AudioClip[SuspenseScore.Layers];
        readonly float[] gains = new float[SuspenseScore.Layers];
        GameManager gm;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            for (int i = 0; i < sources.Length; i++)
            {
                clips[i] = AudioClip.Create("Suspense_" + i, SuspenseScore.Frames, 2, SuspenseScore.Rate, false);
                clips[i].SetData(SuspenseScore.Render(i), 0);
                AudioSource s = gameObject.AddComponent<AudioSource>();
                s.clip = clips[i];
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.dopplerLevel = 0f;
                s.priority = 32; // Keep all stems in sync, even when a busy fight uses many voices.
                s.volume = 0f;
                sources[i] = s;
            }
            double start = AudioSettings.dspTime + 0.15;
            for (int i = 0; i < sources.Length; i++) sources[i].PlayScheduled(start);
        }

        void LateUpdate()
        {
            if (gm == null) return;
            bool playing = gm.State == GameState.Playing;
            TargetIntensity = playing ? ReadDanger() : 0f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!gm.Paused)
            {
                float response = TargetIntensity > Intensity ? 0.9f : 0.20f;
                Intensity = Mathf.Lerp(Intensity, TargetIntensity, 1f - Mathf.Exp(-response * dt));
            }

            float bed = 0f, motif = 0f, pulse = 0f, pressure = 0f;
            if (playing)
            {
                bed = 0.65f;
                motif = Mathf.Lerp(0.48f, 0.80f, Intensity);
                pulse = Mathf.SmoothStep(0f, 0.90f, Mathf.InverseLerp(0.12f, 0.85f, Intensity));
                pressure = Mathf.SmoothStep(0f, 0.85f, Mathf.InverseLerp(0.42f, 1f, Intensity));
            }
            else if (gm.State == GameState.Title) { bed = 0.42f; motif = 0.32f; }
            else if (gm.State == GameState.Intro && gm.Intro != null)
            {
                float t = IntroDirector.KitchenTime(gm.Intro.Elapsed);
                float blackout = Mathf.InverseLerp(IntroDirector.TBlackout - 2f, IntroDirector.TBurn, t);
                bed = 0.12f + 0.32f * blackout;
                motif = 0.10f + 0.18f * blackout;
                pressure = 0.18f * blackout;
            }
            else if (gm.State == GameState.Ending && gm.Ending != null)
            {
                // Ending: a warm bed while the bus rides toward the "dawn", the stems drain away as the trap closes, silence in the kitchen.
                float ride = 1f - Mathf.InverseLerp(14.2f, 15.4f, gm.Ending.Elapsed);
                float trap = Mathf.InverseLerp(10.0f, 14.6f, gm.Ending.Elapsed);
                bed = ride * Mathf.Lerp(0.30f, 0.18f, trap);
                motif = ride * 0.22f * (1f - trap);
                pressure = ride * 0.75f * trap;
            }
            // Death and victory fade every stem out, leaving the existing event cues audible.
            float pause = gm.Paused ? 0.18f : 1f;
            MoveGain(0, bed * pause, dt);
            MoveGain(1, motif * pause, dt);
            MoveGain(2, gm.Paused ? 0f : pulse, dt);
            MoveGain(3, gm.Paused ? 0f : pressure, dt);
        }

        float ReadDanger()
        {
            float nearby = 0f;
            if (gm.Player != null)
            {
                Vector3 p = gm.Player.transform.position;
                for (int i = 0; i < Enemy.All.Count; i++)
                {
                    Enemy e = Enemy.All[i];
                    if (e == null || e.Health <= 0f || !e.gameObject.activeInHierarchy) continue;
                    float d = Vector3.Distance(p, e.transform.position);
                    float weight = e.Kind == EnemyKind.Eater ? 0.80f : 0.48f;
                    nearby += Mathf.InverseLerp(24f, 4f, d) * weight;
                }
            }
            return EvaluateDanger(gm.Hero != null ? gm.Hero.Life01 : 1f,
                gm.Hero != null && gm.Hero.InLastBreath, nearby,
                gm.Dir != null ? gm.Dir.HeroProgress : 0f,
                gm.Dir != null && gm.Dir.TideActive, gm.Dir != null && gm.Dir.FinalStand,
                gm.Dir != null && gm.Dir.InHearth);
        }

        // Pure policy shared with the audio verification program.
        public static float EvaluateDanger(float light, bool lastBreath, float nearby,
            float progress, bool tide, bool finalStand, bool hearth)
        {
            float low = Mathf.InverseLerp(0.65f, 0.08f, light);
            float value = Mathf.Clamp01(0.10f + Mathf.Clamp01(progress) * 0.14f + low * 0.44f + Mathf.Clamp01(nearby) * 0.55f);
            if (tide) value = Mathf.Max(value, 0.76f);
            if (finalStand || lastBreath) value = 1f;
            if (hearth && !finalStand && !tide && nearby < 0.1f && !lastBreath) value *= 0.28f;
            return value;
        }

        void MoveGain(int layer, float target, float dt)
        {
            gains[layer] = Mathf.MoveTowards(gains[layer], target, dt * 0.45f);
            sources[layer].volume = gains[layer] * Mathf.Clamp01(GameSettings.MusicVolume) * MixGain;
        }

        void OnDestroy()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null) sources[i].Stop();
                if (clips[i] != null) Destroy(clips[i]);
            }
        }
    }
}
