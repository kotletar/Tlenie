using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Four sample-synchronised stems. Only their gains change with danger.
    /// Plus one finished track for the last minute at the bus: when the file Resources/Music/FinalMinute.* exists it plays for exactly
    /// the minute of the final stand (GameConfig.FinalTime) and the stems step aside. Without the file the stems play as before.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        public float Intensity { get; private set; }
        public float TargetIntensity { get; private set; }
        public const float DefaultVolume = 0.65f;
        public const float MixGain = 0.8f;

        /// <summary>Where the last-minute track lives inside Resources (any extension: mp3, ogg, wav). No file: the usual score plays.</summary>
        public const string FinalClipPath = "Music/FinalMinute";
        /// <summary>Loudness of the track against the player's music volume. Stems use MixGain; raise or lower this if the track is off.</summary>
        public static float FinalTrackGain = MixGain;

        readonly AudioSource[] sources = new AudioSource[SuspenseScore.Layers];
        readonly AudioClip[] clips = new AudioClip[SuspenseScore.Layers];
        readonly float[] gains = new float[SuspenseScore.Layers];
        GameManager gm;
        AudioSource finalSource;
        AudioClip finalClip;
        float finalLevel;
        bool finalHeld;

        /// <summary>The last-minute track (replaceable for tests). null: only the adaptive stems play.</summary>
        public AudioClip FinalClip
        {
            get { return finalClip; }
            set
            {
                if (finalClip == value) return;
                StopFinalTrack();
                finalClip = value;
                if (finalSource != null) finalSource.clip = value;
            }
        }

        /// <summary>The track owns the music right now: it plays (or is paused with the game) and the stems have stepped aside.</summary>
        public bool FinalTrackActive { get; private set; }
        /// <summary>The track is paused because the game is paused: the minute stands still, so does the music.</summary>
        public bool FinalTrackHeld { get { return finalHeld; } }
        /// <summary>0..1: how much of the music belongs to the track at the moment (fades in and out).</summary>
        public float FinalTrackLevel { get { return finalLevel; } }
        public float FinalTrackTime { get { return finalSource != null && finalSource.clip != null ? finalSource.time : 0f; } }

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

            // The track of the last minute: a plain one-shot source, silent until the stand starts. The file may be absent.
            finalSource = gameObject.AddComponent<AudioSource>();
            finalSource.loop = false;
            finalSource.playOnAwake = false;
            finalSource.spatialBlend = 0f;
            finalSource.dopplerLevel = 0f;
            finalSource.priority = 32;
            finalSource.volume = 0f;
            finalClip = Resources.Load<AudioClip>(FinalClipPath);
            finalSource.clip = finalClip;
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
            UpdateFinalTrack(playing, dt);

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
            float step = 1f - finalLevel;   // while the last-minute track plays, the stems make room for it
            MoveGain(0, bed * pause * step, dt);
            MoveGain(1, motif * pause * step, dt);
            MoveGain(2, (gm.Paused ? 0f : pulse) * step, dt);
            MoveGain(3, (gm.Paused ? 0f : pressure) * step, dt);
        }

        /// <summary>
        /// The track starts together with the minute (the engine starts), is paused with the game, follows the stand clock if the two drift,
        /// and fades out when the stand ends (the bus leaves, the hero dies) or when the track itself is shorter than the minute.
        /// </summary>
        void UpdateFinalTrack(bool playing, float dt)
        {
            Bus bus = Bus.Instance;
            bool wanted = playing && finalClip != null && bus != null && bus.State == Bus.Mode.Running
                && bus.StandLength > 0.5f && bus.StandElapsed < finalClip.length - 0.05f;
            if (wanted)
            {
                float elapsed = bus.StandElapsed;
                if (!FinalTrackActive)
                {
                    FinalTrackActive = true;
                    finalHeld = false;
                    finalSource.clip = finalClip;
                    finalSource.time = Mathf.Clamp(elapsed, 0f, Mathf.Max(0f, finalClip.length - 0.1f));
                    finalSource.Play();
                }
                if (gm.Paused != finalHeld)
                {
                    finalHeld = gm.Paused;
                    if (finalHeld) finalSource.Pause(); else finalSource.UnPause();
                }
                else if (!finalHeld && Mathf.Abs(finalSource.time - elapsed) > 0.4f)
                {
                    finalSource.time = Mathf.Clamp(elapsed, 0f, Mathf.Max(0f, finalClip.length - 0.1f));   // after a long frame or a seek
                }
                finalLevel = Mathf.MoveTowards(finalLevel, 1f, dt * 2.5f);
            }
            else
            {
                finalLevel = Mathf.MoveTowards(finalLevel, 0f, dt * 1.2f);
                if (FinalTrackActive && finalLevel <= 0.001f) StopFinalTrack();
            }
            if (finalSource != null)
                finalSource.volume = finalLevel * Mathf.Clamp01(GameSettings.MusicVolume) * FinalTrackGain;
        }

        void StopFinalTrack()
        {
            FinalTrackActive = false;
            finalHeld = false;
            finalLevel = 0f;
            if (finalSource != null)
            {
                finalSource.Stop();
                finalSource.volume = 0f;
            }
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
            // stems leave quickly while the track is playing (so it sounds clean) and come back slowly
            float rate = target < gains[layer] && finalLevel > 0f ? 1.6f : 0.45f;
            gains[layer] = Mathf.MoveTowards(gains[layer], target, dt * rate);
            sources[layer].volume = gains[layer] * Mathf.Clamp01(GameSettings.MusicVolume) * MixGain;
        }

        void OnDestroy()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null) sources[i].Stop();
                if (clips[i] != null) Destroy(clips[i]);
            }
            if (finalSource != null) finalSource.Stop();   // the track itself is a shared Resources asset: not destroyed here
        }
    }
}
