using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Звуки синтезируются кодом при первом обращении (файлов нет). Чтобы заменить звук своим:
    /// положите AudioClip в Resources/Audio и в Get() сначала вызывайте Resources.Load.
    /// </summary>
    public static class ProcAudio
    {
        const int Rate = 22050;
        const float TwoPi = 6.2831853f;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            foreach (AudioClip clip in cache.Values) if (clip != null) Object.Destroy(clip);
            cache.Clear();
        }

        public static AudioClip Get(string key)
        {
            AudioClip c;
            if (cache.TryGetValue(key, out c) && c != null) return c;
            c = Build(key);
            cache[key] = c;
            return c;
        }

        public static AudioSource Source(GameObject go, string key, bool loop, float volume, bool spatial, AudioBus bus = AudioBus.Auto)
        {
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = Get(key);
            s.loop = loop;
            if (bus == AudioBus.Auto)
                bus = key == "drone" || key == "hum" || key == "crackle" || key == "fridge" || key == "engine" ? AudioBus.Ambience : AudioBus.Effects;
            AudioMix.Register(s, bus, volume);
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.dopplerLevel = 0f;
            if (spatial)
            {
                s.minDistance = 2f;
                s.maxDistance = 26f;
                s.rolloffMode = AudioRolloffMode.Linear;
            }
            if (loop) s.Play();
            return s;
        }

        public static void OneShot(AudioSource s, string key, float volume, float pitch)
        {
            if (s == null) return;
            s.pitch = pitch;
            s.PlayOneShot(Get(key), volume);
        }

        // ------------------------------------------------------------------ синтез

        static AudioClip Build(string key)
        {
            switch (key)
            {
                case "drone": return Drone();
                case "heart": return Heart();
                case "hum": return Hum();
                case "crackle": return Crackle();
                case "inhale": return Breath(true);
                case "exhale": return Breath(false);
                case "flash": return Flash();
                case "beam": return Beam();
                case "wake": return Wake();
                case "lampdie": return LampDie();
                case "shadedie": return ShadeDie();
                case "hit": return Hit();
                case "tide": return Tide();
                case "win": return Win();
                case "eater": return Eater();
                case "shade": return ShadeWhisper();
                case "step": return Footstep(0);
                case "step2": return Footstep(1);
                case "rustle": return Rustle();
                case "engine": return Engine();
                case "orb": return Orb();
                case "munch": return Munch();
                // предыстория (кухня)
                case "fridge": return Fridge();
                case "tick": return Tick();
                case "powerdown": return PowerDown();
                case "rumble": return Rumble();
                case "sizzle": return Sizzle();
                case "swell": return Swell();
                // концовка (кухня)
                case "drip": return Drip();
                // Великий Гаситель (босс последней минуты)
                case "bossdrone": return BossDrone();
                case "bossroar": return BossRoar();
                case "lancecharge": return LanceCharge();
                case "lance": return LanceStrike();
                case "shellopen": return ShellOpen();
                case "shellslam": return ShellSlam();
                case "bossdie": return BossDie();
            }
            return Make("silence", new float[Rate / 10]);
        }

        // Гул босса: три близкие низкие частоты (биение 0,5 Гц), квинта выше и тихий «хор» (цикл 2 с, все частоты кратны 0,5 Гц: шва нет).
        static AudioClip BossDrone()
        {
            float[] d = Buf(2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float choir = 0.05f * Mathf.Sin(TwoPi * 204f * t) * (0.6f + 0.4f * Mathf.Sin(TwoPi * 1f * t));
                float v = 0.38f * Mathf.Sin(TwoPi * 34f * t) + 0.30f * Mathf.Sin(TwoPi * 33.5f * t) + 0.22f * Mathf.Sin(TwoPi * 51f * t)
                    + 0.12f * Mathf.Sin(TwoPi * 68f * t) + choir;
                d[i] = v * (0.8f + 0.2f * Mathf.Sin(TwoPi * 3f * t));
            }
            return Make("bossdrone", d);
        }

        // Рёв при появлении: падающий низкий тон с гармониками, шум и рокот, медленно нарастает и уходит.
        static AudioClip BossRoar()
        {
            float dur = 3.2f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(71);
            float phase = 0f;
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float env = Mathf.Clamp01(t / 0.35f) * Mathf.Pow(1f - Mathf.Clamp01((t - 1.6f) / 1.6f), 1.5f);
                float f0 = Mathf.Lerp(66f, 44f, k) * (1f + 0.02f * Mathf.Sin(TwoPi * 6f * t));
                phase += TwoPi * f0 / Rate;
                float v = 0f;
                for (int h = 1; h <= 7; h++) v += Mathf.Sin(phase * h) / (h * (h > 4 ? 1.8f : 1f));
                y += (Noise(r) - y) * 0.03f;
                d[i] = (0.55f * v + 1.3f * y) * env;
            }
            return Make("bossroar", d);
        }

        // Заряд копья: тонкий вой снизу вверх (180 -> 1900 Гц) с дрожью и нарастающим шумом.
        static AudioClip LanceCharge()
        {
            float dur = 1.2f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(72);
            float phase = 0f;
            float hp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float f = 180f * Mathf.Pow(1900f / 180f, k);
                phase += TwoPi * f * (1f + 0.015f * Mathf.Sin(TwoPi * 14f * t)) / Rate;
                float n = Noise(r);
                float band = n - hp;
                hp = n;
                float env = Mathf.Pow(k, 1.4f) * (k > 0.96f ? (1f - k) / 0.04f : 1f);
                d[i] = (0.55f * Mathf.Sin(phase) + 0.18f * Mathf.Sin(phase * 2f) + 0.35f * band * k + 0.3f * Mathf.Sin(TwoPi * 58f * t)) * env;
            }
            return Make("lancecharge", d);
        }

        // Удар копья: щелчок, тон падает со 1400 до 90 Гц, следом глухой гул.
        static AudioClip LanceStrike()
        {
            float[] d = Buf(0.9f);
            System.Random r = new System.Random(73);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 90f + 1310f * Mathf.Exp(-t * 26f);
                phase += TwoPi * f / Rate;
                float crack = Noise(r) * Mathf.Exp(-t * 38f);
                float boom = Mathf.Sin(TwoPi * 48f * t) * Mathf.Exp(-t * 5.5f);
                d[i] = 0.9f * Mathf.Sin(phase) * Mathf.Exp(-t * 9f) + 0.9f * crack + 0.8f * boom;
            }
            return Make("lance", d);
        }

        // Створки раскрываются: щелчок замка, неровный металлический звон (негармонические частоты) и низкий подъём.
        static AudioClip ShellOpen()
        {
            float dur = 1.5f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(74);
            float[] freqs = { 151f, 243f, 388f, 531f, 702f };
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 120f + 300f * Mathf.Clamp01(t / 0.5f);
                phase += TwoPi * f / Rate;
                float v = 0.30f * Mathf.Sin(phase) * Mathf.Clamp01(1f - (t - 0.2f) / 0.6f);
                for (int j = 0; j < freqs.Length; j++) v += 0.22f / (1f + j * 0.5f) * Mathf.Sin(TwoPi * freqs[j] * t) * Mathf.Exp(-t * (2.2f + 0.5f * j));
                v += 0.5f * Noise(r) * Mathf.Exp(-t * 70f);
                v += 0.4f * Mathf.Sin(TwoPi * 46f * t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                d[i] = v;
            }
            return Make("shellopen", d);
        }

        // Створки захлопываются: тяжёлый удар, шум и короткий звон.
        static AudioClip ShellSlam()
        {
            float[] d = Buf(1.0f);
            System.Random r = new System.Random(75);
            float[] freqs = { 211f, 334f, 481f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0.9f * Noise(r) * Mathf.Exp(-t * 24f) + 0.9f * Mathf.Sin(TwoPi * 42f * t) * Mathf.Exp(-t * 7f);
                for (int j = 0; j < freqs.Length; j++) v += 0.22f * Mathf.Sin(TwoPi * freqs[j] * t) * Mathf.Exp(-t * 5f);
                d[i] = v;
            }
            return Make("shellslam", d);
        }

        // Гибель босса: падающий рык, три волны осколков и поднимающийся светлый аккорд.
        static AudioClip BossDie()
        {
            float dur = 4.8f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(76);
            float[] bells = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float[] bursts = { 0.15f, 0.55f, 1.0f };
            float phase = 0f;
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float f0 = Mathf.Lerp(72f, 20f, Mathf.Pow(k, 0.7f));
                phase += TwoPi * f0 / Rate;
                float growl = 0f;
                for (int h = 1; h <= 5; h++) growl += Mathf.Sin(phase * h) / (h * 1.3f);
                y += (Noise(r) - y) * 0.05f;
                float env = Mathf.Clamp01(t / 0.1f) * Mathf.Pow(1f - Mathf.Clamp01((t - 0.2f) / 2.6f), 1.3f);
                float v = (0.6f * growl + 1.1f * y) * env;
                for (int j = 0; j < bursts.Length; j++)
                {
                    float tb = t - bursts[j];
                    if (tb > 0f) v += 0.9f * Noise(r) * Mathf.Exp(-tb * 22f);
                }
                float tc = t - 1.5f;
                if (tc > 0f)
                {
                    float swell = Mathf.Sin(Mathf.PI * Mathf.Clamp01(tc / 3.2f));
                    for (int j = 0; j < bells.Length; j++) v += 0.14f * Mathf.Sin(TwoPi * bells[j] * t) * swell * Mathf.Exp(-tc * 0.5f);
                }
                d[i] = v;
            }
            return Make("bossdie", d);
        }

        /// <summary>Капля: тон быстро падает, следом тихий всплеск. Для концовки, где с бедра капает на пол.</summary>
        static AudioClip Drip()
        {
            float[] d = Buf(0.42f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 520f + 420f * Mathf.Exp(-t * 34f);
                float s = Mathf.Sin(TwoPi * f * t) * Mathf.Exp(-t * 21f);
                float t2 = t - 0.12f;
                if (t2 > 0f) s += 0.32f * Mathf.Sin(TwoPi * (430f + 220f * Mathf.Exp(-t2 * 30f)) * t2) * Mathf.Exp(-t2 * 25f);
                float click = t < 0.004f ? 0.4f * (1f - t / 0.004f) : 0f;
                d[i] = 0.75f * s + click;
            }
            return Make("drip", d);
        }

        static float[] Buf(float seconds)
        {
            return new float[(int)(seconds * Rate)];
        }

        static AudioClip Make(string name, float[] d)
        {
            float peak = 0f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            if (peak > 0.9f)
                for (int i = 0; i < d.Length; i++) d[i] *= 0.9f / peak;
            AudioClip c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static float Noise(System.Random r)
        {
            return (float)(r.NextDouble() * 2.0 - 1.0);
        }

        static AudioClip ShadeWhisper()
        {
            float[] d = Buf(4f);
            System.Random random = new System.Random(9163);
            float noise = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                noise += (Noise(random) - noise) * 0.12f;
                float envelope = Mathf.Sin(Mathf.PI * t / 4f);
                d[i] = 0.22f * noise * envelope * envelope + 0.07f * Mathf.Sin(TwoPi * 43f * t);
            }
            return Make("shade", d);
        }

        // Низкий гул города: частоты кратны 0.25 Гц, поэтому 4-секундный цикл бесшовный.
        static AudioClip Drone()
        {
            float[] d = Buf(4f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float lfo = 0.6f + 0.4f * Mathf.Sin(TwoPi * 0.25f * t);
                d[i] = 0.16f * Mathf.Sin(TwoPi * 55f * t)
                     + 0.12f * Mathf.Sin(TwoPi * 55.25f * t)
                     + 0.07f * Mathf.Sin(TwoPi * 82.5f * t) * lfo
                     + 0.05f * Mathf.Sin(TwoPi * 110.25f * t);
            }
            return Make("drone", d);
        }

        // Два удара сердца.
        static AudioClip Heart()
        {
            float[] d = Buf(1f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float e1 = Mathf.Exp(-t * 26f);
                float v = 0.9f * e1 * Mathf.Sin(TwoPi * 58f * t);
                if (t > 0.26f)
                {
                    float t2 = t - 0.26f;
                    v += 0.6f * Mathf.Exp(-t2 * 30f) * Mathf.Sin(TwoPi * 52f * t2);
                }
                d[i] = v;
            }
            return Make("heart", d);
        }

        // Гул лампы: целые частоты, 1-секундный бесшовный цикл.
        static AudioClip Hum()
        {
            float[] d = Buf(1f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = 0.30f * Mathf.Sin(TwoPi * 100f * t)
                     + 0.15f * Mathf.Sin(TwoPi * 200f * t)
                     + 0.07f * Mathf.Sin(TwoPi * 300f * t);
            }
            return Make("hum", d);
        }

        // Потрескивание огня.
        static AudioClip Crackle()
        {
            float[] d = Buf(2f);
            System.Random r = new System.Random(5);
            float env = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                if (r.NextDouble() < 0.0012) env = 0.25f + (float)r.NextDouble() * 0.75f;
                d[i] = Noise(r) * env * 0.5f;
                env *= 0.93f;
            }
            return Make("crackle", d);
        }

        // Вдох (фильтр открывается) и выдох (закрывается). Огибающая нулевая по краям — цикл бесшовный.
        static AudioClip Breath(bool inhale)
        {
            float dur = 1.4f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(inhale ? 11 : 12);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float env = Mathf.Sin(Mathf.PI * k);
                env *= env;
                float a = inhale ? Mathf.Lerp(0.03f, 0.22f, k) : Mathf.Lerp(0.22f, 0.03f, k);
                y += (Noise(r) - y) * a;
                d[i] = y * env * 1.8f;
            }
            return Make(inhale ? "inhale" : "exhale", d);
        }

        static AudioClip Flash()
        {
            float dur = 0.5f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(3);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(1500f, 140f, Mathf.Sqrt(t / dur));
                phase += TwoPi * f / Rate;
                float env = Mathf.Exp(-t * 7f);
                float nz = Noise(r) * (t < 0.12f ? 0.5f : 0.1f);
                d[i] = (0.5f * Mathf.Sin(phase) + nz) * env;
            }
            return Make("flash", d);
        }

        // Луч: гармоники 120 Гц, 1-секундный цикл.
        static AudioClip Beam()
        {
            float[] d = Buf(1f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int k = 1; k <= 5; k++) v += Mathf.Sin(TwoPi * 120f * k * t) / k;
                d[i] = v * 0.28f * (0.85f + 0.15f * Mathf.Sin(TwoPi * 8f * t));
            }
            return Make("beam", d);
        }

        // Тёплый аккорд — человек очнулся.
        static AudioClip Wake()
        {
            float[] d = Buf(1.4f);
            float[] freqs = { 523.25f, 659.25f, 783.99f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int j = 0; j < 3; j++)
                {
                    float t0 = j * 0.1f;
                    if (t >= t0)
                    {
                        float tt = t - t0;
                        v += Mathf.Sin(TwoPi * freqs[j] * tt) * Mathf.Exp(-tt * 3.2f);
                    }
                }
                d[i] = v * 0.22f;
            }
            return Make("wake", d);
        }

        static AudioClip LampDie()
        {
            float dur = 0.7f;
            float[] d = Buf(dur);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(900f, 55f, t / dur);
                phase += TwoPi * f / Rate;
                float env = Mathf.Exp(-t * 5f);
                d[i] = (0.4f * Mathf.Sin(phase) + 0.35f * Mathf.Sin(TwoPi * 48f * t) * Mathf.Exp(-t * 9f)) * env;
            }
            return Make("lampdie", d);
        }

        static AudioClip ShadeDie()
        {
            float dur = 0.5f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(8);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Lerp(0.3f, 0.02f, t / dur);
                y += (Noise(r) - y) * a;
                d[i] = y * Mathf.Exp(-t * 6f) * 1.6f;
            }
            return Make("shadedie", d);
        }

        static AudioClip Hit()
        {
            float[] d = Buf(0.3f);
            System.Random r = new System.Random(9);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = 0.8f * Mathf.Exp(-t * 18f) * Mathf.Sin(TwoPi * 72f * t) + Noise(r) * 0.2f * Mathf.Exp(-t * 60f);
            }
            return Make("hit", d);
        }

        // Прилив Тьмы: низкий рокот.
        static AudioClip Tide()
        {
            float dur = 3f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(13);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float env = Mathf.Sin(Mathf.PI * k);
                env *= env;
                y += (Noise(r) - y) * 0.04f;
                float v = 0.5f * Mathf.Sin(TwoPi * 38f * t) + 0.25f * Mathf.Sin(TwoPi * 57f * t);
                d[i] = (v * 0.5f + y * 1.4f) * env;
            }
            return Make("tide", d);
        }

        // Финальный аккорд.
        static AudioClip Win()
        {
            float[] d = Buf(3.5f);
            float[] freqs = { 261.63f, 329.63f, 392f, 523.25f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int j = 0; j < 4; j++)
                {
                    float t0 = j * 0.4f;
                    if (t >= t0)
                    {
                        float tt = t - t0;
                        v += Mathf.Sin(TwoPi * freqs[j] * tt) * Mathf.Exp(-tt * 1.1f);
                    }
                }
                d[i] = v * 0.2f;
            }
            return Make("win", d);
        }

        // Рык Пожирателя: две близкие низкие частоты (цикл 2 с, частоты целые).
        static AudioClip Eater()
        {
            float[] d = Buf(2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float trem = 0.7f + 0.3f * Mathf.Sin(TwoPi * 1f * t);
                d[i] = (0.35f * Mathf.Sin(TwoPi * 45f * t) + 0.30f * Mathf.Sin(TwoPi * 47f * t) + 0.12f * Mathf.Sin(TwoPi * 90f * t)) * trem;
            }
            return Make("eater", d);
        }

        // Шорох картона (пицца): короткий мягкий шум. Раньше точно таким же звуком звучали и шаги героя.
        static AudioClip Rustle()
        {
            float[] d = Buf(0.14f);
            System.Random r = new System.Random(21);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                y += (Noise(r) - y) * 0.25f;
                d[i] = y * Mathf.Exp(-t * 38f) * 1.4f;
            }
            return Make("rustle", d);
        }

        // Шаг по асфальту в тихом городе: «пятка — носок». Глухой удар каблука (тон падает, есть вторая гармоника), «тук» подошвы
        // по асфальту (~450 Гц, его слышно и на маленьких динамиках, где басовый удар пропадает), сухой щелчок, шорох подошвы
        // и через 65 мс тише и выше — носок. Два варианта (левая и правая нога): чуть разный тон и разный шум. Оба выровнены по
        // пику, громкость задаёт вызывающий (PlayerController). Энергия: ~половина ниже 300 Гц, остальное — середина спектра.
        static AudioClip Footstep(int variant)
        {
            float[] d = Buf(0.22f);
            System.Random r = new System.Random(variant == 0 ? 211 : 509);
            float f0 = variant == 0 ? 150f : 128f;   // тон удара каблука, Гц
            float fk = variant == 0 ? 480f : 430f;   // «тук» подошвы, Гц
            float low = 0f, mid = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                low += (n - low) * 0.10f;           // ~350 Гц
                mid += (n - mid) * 0.42f;           // ~1,9 кГц
                float band = mid - low;             // полоса 350–1900 Гц: щелчок и шорох подошвы
                float fHeel = f0 * (1f + 0.40f * Mathf.Exp(-t * 70f));
                float heel = (Mathf.Sin(TwoPi * fHeel * t) + 0.5f * Mathf.Sin(TwoPi * 2f * fHeel * t)) * Mathf.Exp(-t * 52f);
                float knock = Mathf.Sin(TwoPi * fk * t) * Mathf.Exp(-t * 90f);
                float click = band * Mathf.Exp(-t * 300f);
                float scuff = band * Mathf.Clamp01(t / 0.006f) * Mathf.Exp(-t * 20f);
                float tt = t - 0.065f;
                float toe = tt > 0f ? (0.35f * Mathf.Sin(TwoPi * f0 * 1.4f * tt) + 0.6f * band) * Mathf.Exp(-tt * 80f) : 0f;
                d[i] = 0.55f * heel + 0.45f * knock + 1.3f * click + 0.5f * scuff + 0.9f * toe;
            }
            float peak = 0f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            if (peak > 0.001f)
                for (int i = 0; i < d.Length; i++) d[i] *= 0.85f / peak;
            return Make(variant == 0 ? "step" : "step2", d);
        }

        static AudioClip Engine()
        {
            float[] d = Buf(2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float rumble = 0.8f + 0.2f * Mathf.Sin(TwoPi * 6f * t);
                d[i] = (0.35f * Mathf.Sin(TwoPi * 40f * t) + 0.22f * Mathf.Sin(TwoPi * 80f * t) + 0.10f * Mathf.Sin(TwoPi * 120f * t)) * rumble;
            }
            return Make("engine", d);
        }

        static AudioClip Orb()
        {
            float dur = 0.4f;
            float[] d = Buf(dur);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(880f, 1320f, t / dur);
                phase += TwoPi * f / Rate;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 7f) * 0.3f;
            }
            return Make("orb", d);
        }

        // Укус пиццы: короткий сухой хруст (шум без низов), за ним второй, тише, и глухой «чав» снизу.
        static AudioClip Munch()
        {
            float dur = 0.26f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(41);
            float soft = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                soft += (n - soft) * 0.45f;
                float crisp = n - soft;                                   // высокие частоты: хруст корочки
                float e1 = Mathf.Exp(-t * 46f);
                float e2 = t > 0.075f ? Mathf.Exp(-(t - 0.075f) * 52f) * 0.7f : 0f;
                float e3 = t > 0.15f ? Mathf.Exp(-(t - 0.15f) * 60f) * 0.45f : 0f;
                float body = Mathf.Sin(TwoPi * 150f * t) * Mathf.Exp(-t * 34f) * 0.32f;   // глухой «чав»
                d[i] = crisp * 1.1f * (e1 + e2 + e3) + soft * 0.35f * e1 + body;
            }
            return Make("munch", d);
        }

        // ------------------------------------------------------------------ предыстория (кухня)

        // Гул холодильника: 50 Гц и гармоники, медленное дрожание. Частоты кратны 0.5 Гц — 2-секундный цикл бесшовный.
        static AudioClip Fridge()
        {
            float[] d = Buf(2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float trem = 0.86f + 0.14f * Mathf.Sin(TwoPi * 0.5f * t);
                d[i] = (0.20f * Mathf.Sin(TwoPi * 50f * t) + 0.10f * Mathf.Sin(TwoPi * 100f * t) + 0.04f * Mathf.Sin(TwoPi * 150f * t)) * trem;
            }
            return Make("fridge", d);
        }

        // Тик настенных часов.
        static AudioClip Tick()
        {
            float[] d = Buf(0.09f);
            System.Random r = new System.Random(31);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = (Noise(r) * 0.45f + Mathf.Sin(TwoPi * 1750f * t) * 0.5f) * Mathf.Exp(-t * 85f);
            }
            return Make("tick", d);
        }

        // Отключение электричества: щелчок реле, глухой удар и угасающий вой.
        static AudioClip PowerDown()
        {
            float dur = 2.4f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(32);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = Mathf.Pow(t / dur, 0.55f);
                float f = Mathf.Lerp(540f, 26f, k);
                phase += TwoPi * f / Rate;
                float env = Mathf.Exp(-t * 1.5f);
                float v = 0.34f * Mathf.Sin(phase) * env;
                v += 0.55f * Mathf.Exp(-t * 13f) * Mathf.Sin(TwoPi * 52f * t);
                if (t < 0.02f) v += Noise(r) * 0.6f;
                d[i] = v;
            }
            return Make("powerdown", d);
        }

        // Далёкий нарастающий гул (что-то идёт по городу).
        static AudioClip Rumble()
        {
            float dur = 3.6f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(33);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * k), 1.6f);
                y += (Noise(r) - y) * 0.018f;
                float v = 0.34f * Mathf.Sin(TwoPi * 31f * t) + 0.20f * Mathf.Sin(TwoPi * 47f * t) + y * 2.6f;
                d[i] = v * env;
            }
            return Make("rumble", d);
        }

        // Ожог: резкое шипение и глухой удар.
        static AudioClip Sizzle()
        {
            float dur = 1.1f;
            float[] d = Buf(dur);
            System.Random r = new System.Random(34);
            float last = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                float hp = n - last;   // «светлый» шум: шипение
                last = n;
                float env = Mathf.Exp(-t * 3.2f) * (1f - Mathf.Exp(-t * 70f));
                float v = hp * 0.5f * env;
                v += 0.5f * Mathf.Exp(-t * 22f) * Mathf.Sin(TwoPi * 96f * t);
                d[i] = v;
            }
            return Make("sizzle", d);
        }

        // Свет входит в человека: растущий тёплый аккорд с колокольчиком на пике.
        static AudioClip Swell()
        {
            float dur = 3.4f;
            float[] d = Buf(dur);
            float[] freqs = { 220f, 330f, 440f, 660f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.7f));
                env *= env;
                float vib = 1f + 0.004f * Mathf.Sin(TwoPi * 5.5f * t);
                float v = 0f;
                for (int j = 0; j < freqs.Length; j++) v += Mathf.Sin(TwoPi * freqs[j] * vib * t) / (1f + j * 0.6f);
                float bell = t > 1.5f ? Mathf.Sin(TwoPi * 880f * (t - 1.5f)) * Mathf.Exp(-(t - 1.5f) * 2.2f) : 0f;
                d[i] = v * 0.17f * env + bell * 0.16f;
            }
            return Make("swell", d);
        }
    }
}
