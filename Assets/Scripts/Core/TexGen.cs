using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Процедурные текстуры мира (арта в проекте нет; враги — меши из EnemyModels). Всё считается кодом при старте и быстро: крупные поля шума
    /// считаются один раз на маленькой сетке и читаются билинейно, а не по шуму на каждый пиксель.
    /// Фасады 512x512 (на 24 м стены): кирпич, штукатурка, панели, гофра; окна — целые, разбитые, заколоченные, с занавесками
    /// или пустые; подоконники, перемычки, потёки, трещины. Света в окнах нет совсем: альфа-канал фасада пуст.
    /// Асфальт и плитка: трещины, заплатки, пятна, лужи; их альфа-канал — «мокрость» (блеск в шейдере).
    /// </summary>
    public static class TexGen
    {
        const int FS = 512;      // размер фасада
        const int CS = 64;       // клетка этажа/окна

        public static void BuildAll()
        {
            for (int i = 0; i < 4; i++) Gfx.TexFacade[i] = Facade(i);
            Gfx.TexAsphalt = Asphalt();
            Gfx.TexPavement = Pavement();
            Gfx.TexGrass = Grass();
        }

        // ------------------------------------------------------------------ помощники

        static Color32 C(float r, float g, float b, float a)
        {
            return new Color32(
                (byte)Mathf.Clamp(r, 0f, 255f),
                (byte)Mathf.Clamp(g, 0f, 255f),
                (byte)Mathf.Clamp(b, 0f, 255f),
                (byte)Mathf.Clamp(a, 0f, 255f));
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float len2 = abx * abx + aby * aby;
            float t = 0f;
            if (len2 > 0.0001f) t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / len2);
            float dx = px - (ax + abx * t);
            float dy = py - (ay + aby * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Texture2D Make(int w, int h, Color32[] px, bool mips, TextureWrapMode wrap, int aniso, bool noLongerReadable)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            t.SetPixels32(px);
            t.wrapMode = wrap;
            t.filterMode = FilterMode.Bilinear;
            t.anisoLevel = aniso;
            t.Apply(mips, noLongerReadable);
            return t;
        }

        // ------------------------------------------------------------------ фасады

        static Texture2D Facade(int variant)
        {
            int seed = 100 + variant * 37;
            float[] tone = Noise.Field(128, 4, 3, seed);
            float[] stain = Noise.FieldStretched(128, 40, 3, seed + 1);
            float[] grime = Noise.Field(128, 8, 4, seed + 2);
            float[] crack = Noise.Field(128, 24, 2, seed + 3);
            Color32[] px = new Color32[FS * FS];
            for (int y = 0; y < FS; y++)
            {
                for (int x = 0; x < FS; x++)
                {
                    px[y * FS + x] = FacadePixel(variant, x, y, seed, tone, stain, grime, crack);
                }
            }
            return Make(FS, FS, px, true, TextureWrapMode.Repeat, 4, true);
        }

        static void FrameColor(int variant, out float r, out float g, out float b)
        {
            switch (variant)
            {
                case 0: r = 92f; g = 84f; b = 72f; break;
                case 1: r = 88f; g = 94f; b = 88f; break;
                case 2: r = 104f; g = 108f; b = 116f; break;
                default: r = 66f; g = 63f; b = 60f; break;
            }
        }

        static Color32 FacadePixel(int variant, int x, int y, int seed, float[] tone, float[] stain, float[] grime, float[] crack)
        {
            float u = (x + 0.5f) / FS;
            float v = (y + 0.5f) / FS;
            int cx = x >> 6;
            int cy = y >> 6;
            int lx = x & 63;
            int ly = y & 63;
            float r, g, b;

            bool inWin = lx >= 13 && lx < 51 && ly >= 17 && ly < 53;
            bool inFrame = !inWin && lx >= 11 && lx < 53 && ly >= 15 && ly < 55;
            bool inSill = lx >= 9 && lx < 55 && ly >= 12 && ly < 15;
            bool inSillShadow = lx >= 9 && lx < 55 && ly >= 8 && ly < 12;
            bool inLintel = lx >= 9 && lx < 55 && ly >= 55 && ly < 58;

            if (inWin)
            {
                WindowPixel(variant, cx, cy, lx, ly, seed, out r, out g, out b);
                return C(r, g, b, 0f);
            }

            // --- стена ---
            switch (variant)
            {
                case 0:
                    {
                        int course = y >> 3;
                        int off = (course & 1) * 8;
                        int bx = (x + off) & 15;
                        int by = y & 7;
                        float h = Noise.Hash((x + off) >> 4, course, seed);
                        if (bx == 0 || by == 0)
                        {
                            float m = 0.9f + 0.2f * Noise.Hash(x, y, seed + 4);
                            r = 80f * m; g = 76f * m; b = 70f * m;
                        }
                        else
                        {
                            float k = 0.78f + 0.44f * h;
                            if (h > 0.93f) k *= 0.55f;
                            if (bx == 1 || by == 1) k *= 1.08f;
                            if (bx == 15 || by == 7) k *= 0.88f;
                            r = 108f * k; g = 62f * k; b = 48f * k;
                        }
                        break;
                    }
                case 1:
                    {
                        float gm = Noise.Sample(grime, 128, u, v);
                        float fine = 0.92f + 0.16f * Noise.Hash(x, y, seed + 4);
                        if (gm > 0.60f)
                        {
                            // штукатурка обвалилась: видна кирпичная кладка
                            float bk = 0.8f + 0.4f * Noise.Hash(x >> 2, y >> 2, seed + 6);
                            r = 96f * bk; g = 58f * bk; b = 48f * bk;
                            if (gm < 0.625f) { r *= 0.5f; g *= 0.5f; b *= 0.5f; }
                        }
                        else
                        {
                            r = 60f * fine; g = 78f * fine; b = 74f * fine;
                        }
                        break;
                    }
                case 2:
                    {
                        float shade = 0.95f + 0.1f * ((lx & 31) / 32f);
                        r = 88f * shade; g = 94f * shade; b = 106f * shade;
                        if (lx == 0 || ly == 0 || lx == 32) { r = 56f; g = 60f; b = 70f; }
                        if (ly >= 58) { r *= 0.88f; g *= 0.88f; b *= 0.88f; }
                        // болты на стыках панелей
                        bool bolt = ((lx == 3 || lx == 29 || lx == 35 || lx == 60) && (ly == 3 || ly == 60));
                        if (bolt) { r = 128f; g = 134f; b = 146f; }
                        float fn = 0.94f + 0.12f * Noise.Hash(x, y, seed + 4);
                        r *= fn; g *= fn; b *= fn;
                        break;
                    }
                default:
                    {
                        float s = ((x & 3) < 2) ? 1.10f : 0.88f;
                        r = 84f * s; g = 78f * s; b = 72f * s;
                        float st = Noise.Sample(stain, 128, u, v);
                        if (st > 0.58f)
                        {
                            float k = Mathf.Min(0.7f, (st - 0.58f) * 3.2f);   // ржавые потёки
                            r = Mathf.Lerp(r, 118f, k); g = Mathf.Lerp(g, 64f, k); b = Mathf.Lerp(b, 36f, k);
                        }
                        if (lx == 0) { r *= 0.5f; g *= 0.5f; b *= 0.5f; }
                        break;
                    }
            }

            // --- крупные тона, потёки, трещины, грязь у пола этажа ---
            float t = 0.86f + 0.28f * Noise.Sample(tone, 128, u, v);
            float sk = Noise.Sample(stain, 128, u, v);
            float streak = 1f - 0.30f * Smooth(0.55f, 0.80f, sk);
            float cr = Mathf.Abs(Noise.Sample(crack, 128, u, v) - 0.5f);
            if (cr < 0.011f) t *= 0.5f;
            if (ly < 6) t *= 0.92f;
            t *= streak;
            r *= t; g *= t; b *= t;

            // --- лепнина окна ---
            if (inFrame)
            {
                float fr, fg, fb;
                FrameColor(variant, out fr, out fg, out fb);
                float fk = 0.9f + 0.2f * Noise.Hash(x, y, seed + 8);
                r = fr * fk; g = fg * fk; b = fb * fk;
            }
            else if (inSill)
            {
                r = 112f * t; g = 108f * t; b = 100f * t;
            }
            else if (inSillShadow)
            {
                r *= 0.62f; g *= 0.62f; b *= 0.62f;
            }
            else if (inLintel)
            {
                r *= 0.72f; g *= 0.72f; b *= 0.72f;
            }
            return C(r, g, b, 0f);
        }

        /// <summary>Окно 38x36 пикселей в клетке: целое, разбитое, заколоченное, с занавесками или пустое. Не светится.</summary>
        static void WindowPixel(int variant, int cx, int cy, int lx, int ly, int seed, out float r, out float g, out float b)
        {
            float hs = Noise.Hash(cx, cy, seed + 7);
            int wx = lx - 13;     // 0..37
            int wy = ly - 17;     // 0..35
            float fr, fg, fb;
            FrameColor(variant, out fr, out fg, out fb);

            bool mull = false;
            switch (variant)
            {
                case 0:
                case 1: mull = (wx >= 18 && wx < 20) || (wy >= 16 && wy < 18); break;
                case 2: mull = (wx < 1 || wx >= 37) || (wy % 7 == 3); break;
                default: mull = (wx == 12 || wx == 25) || (wy == 11 || wy == 23); break;
            }

            if (hs < 0.46f)
            {
                // целое стекло: холодный отражённый отсвет, сверху светлее
                float gl = 9f + 20f * (wy / 35f);
                if (Mathf.Abs(wx - wy * 0.8f - 14f) < 3.5f) gl += 9f;
                r = gl * 0.78f; g = gl * 0.95f; b = gl * 1.30f;
                if (mull) { r = fr * 0.8f; g = fg * 0.8f; b = fb * 0.8f; }
            }
            else if (hs < 0.60f)
            {
                // разбитое: тёмная пустота и трещины
                r = 5f; g = 7f; b = 10f;
                float ccx = 14f + 10f * Noise.Hash(cx, cy, seed + 11);
                float ccy = 12f + 12f * Noise.Hash(cx, cy, seed + 12);
                float d0 = SegDist(wx, wy, ccx, ccy, 0f, 36f * Noise.Hash(cx, cy, seed + 13));
                float d1 = SegDist(wx, wy, ccx, ccy, 37f, 30f * Noise.Hash(cx, cy, seed + 14));
                float d2 = SegDist(wx, wy, ccx, ccy, 20f * Noise.Hash(cx, cy, seed + 15), 0f);
                float d3 = SegDist(wx, wy, ccx, ccy, 37f * Noise.Hash(cx, cy, seed + 16), 36f);
                if (Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3)) < 0.75f) { r = 64f; g = 72f; b = 80f; }
                if (wx > 24 && wy > 22 && Noise.Hash(cx, cy, seed + 17) > 0.5f) { r = 2f; g = 3f; b = 4f; }   // выбитый угол
                if (mull) { r = fr * 0.7f; g = fg * 0.7f; b = fb * 0.7f; }
            }
            else if (hs < 0.72f)
            {
                // заколочено досками
                int plank = wy / 12;
                int py = wy % 12;
                float wood = 0.8f + 0.4f * Noise.Hash(plank, wx >> 3, seed + 21);
                wood *= 0.92f + 0.08f * Mathf.Sin(wx * 0.9f + plank * 3f);
                r = 70f * wood; g = 52f * wood; b = 36f * wood;
                if (py == 11) { r = 9f; g = 7f; b = 5f; }
                if ((wx == 2 || wx == 35) && py == 5) { r = 150f; g = 150f; b = 150f; }   // гвозди
            }
            else if (hs < 0.86f)
            {
                // занавески
                float pal = Noise.Hash(cx, cy, seed + 9);
                float cr, cg, cb;
                if (pal < 0.25f) { cr = 110f; cg = 48f; cb = 48f; }
                else if (pal < 0.5f) { cr = 118f; cg = 98f; cb = 48f; }
                else if (pal < 0.75f) { cr = 64f; cg = 92f; cb = 70f; }
                else { cr = 76f; cg = 84f; cb = 110f; }
                bool side = wx < 12 || wx >= 26;
                bool top = wy >= 29;
                if (side || top)
                {
                    float fold = 0.75f + 0.25f * Mathf.Sin(wx * 1.3f);
                    r = cr * fold * 0.55f; g = cg * fold * 0.55f; b = cb * fold * 0.55f;
                }
                else
                {
                    r = 10f; g = 12f; b = 18f;
                }
                if (wy >= 34) { r = 90f; g = 80f; b = 70f; }   // карниз
            }
            else
            {
                r = 3f; g = 4f; b = 6f;   // пустой чёрный проём
            }
        }

        // ------------------------------------------------------------------ земля

        static Texture2D Asphalt()
        {
            const int S = 256;
            int seed = 500;
            float[] tone = Noise.Field(64, 3, 3, seed);
            float[] puddle = Noise.Field(64, 4, 4, seed + 1);
            float[] oil = Noise.Field(64, 6, 3, seed + 2);
            float[] crackA = Noise.Field(64, 7, 2, seed + 3);
            float[] crackB = Noise.Field(64, 15, 2, seed + 4);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S;
                    float v = (y + 0.5f) / S;
                    float t = 0.9f + 0.2f * Noise.Sample(tone, 64, u, v);
                    float v0 = 46f + 22f * Noise.Hash(x, y, seed + 5);          // щебень
                    float r = v0 * t, g = v0 * t, b = v0 * t * 1.06f;

                    // заплатки: ровные прямоугольники 64x64 px (3 м)
                    int pcx = x >> 6;
                    int pcy = y >> 6;
                    float ph = Noise.Hash(pcx, pcy, seed + 6);
                    if (ph > 0.80f)
                    {
                        r *= 0.78f; g *= 0.78f; b *= 0.8f;
                        int ex = x & 63;
                        int ey = y & 63;
                        if (ex < 2 || ey < 2 || ex > 61 || ey > 61) { r *= 0.55f; g *= 0.55f; b *= 0.55f; }
                    }

                    // масляные пятна
                    float oi = Noise.Sample(oil, 64, u, v);
                    if (oi > 0.68f)
                    {
                        float k = Smooth(0.68f, 0.78f, oi) * 0.55f;
                        r = Mathf.Lerp(r, 24f, k); g = Mathf.Lerp(g, 22f, k); b = Mathf.Lerp(b, 28f, k);
                    }

                    // трещины
                    float ca = Mathf.Abs(Noise.Sample(crackA, 64, u, v) - 0.5f);
                    float cb2 = Mathf.Abs(Noise.Sample(crackB, 64, u, v) - 0.5f);
                    if (ca < 0.010f || cb2 < 0.007f) { r *= 0.42f; g *= 0.42f; b *= 0.42f; }

                    // мокрость: лужи, влажный асфальт, сухие пятна (идёт в альфа-канал: блеск в шейдере)
                    float pu = Noise.Sample(puddle, 64, u, v);
                    float wet = 55f + 50f * Noise.Sample(tone, 64, u + 0.3f, v + 0.7f);
                    if (pu > 0.60f)
                    {
                        float k = Smooth(0.60f, 0.66f, pu);
                        wet = Mathf.Lerp(wet, 255f, k);
                        r = Mathf.Lerp(r, r * 0.62f, k); g = Mathf.Lerp(g, g * 0.66f, k); b = Mathf.Lerp(b, b * 0.78f + 6f, k);
                    }
                    px[y * S + x] = C(r, g, b, wet);
                }
            }
            return Make(S, S, px, true, TextureWrapMode.Repeat, 4, true);
        }

        static Texture2D Pavement()
        {
            const int S = 256;
            int seed = 700;
            float[] tone = Noise.Field(64, 4, 3, seed);
            float[] moss = Noise.Field(64, 8, 4, seed + 1);
            float[] stain = Noise.Field(64, 5, 3, seed + 2);
            float[] crack = Noise.Field(64, 12, 2, seed + 3);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S;
                    float v = (y + 0.5f) / S;
                    int sx = x & 127;
                    int sy = y & 127;
                    float slab = 0.88f + 0.22f * Noise.Hash(x >> 7, y >> 7, seed + 4);
                    float t = 0.9f + 0.2f * Noise.Sample(tone, 64, u, v);
                    float sp = 0.93f + 0.14f * Noise.Hash(x, y, seed + 5);
                    float r = 90f * slab * t * sp, g = 90f * slab * t * sp, b = 88f * slab * t * sp;

                    bool joint = sx < 2 || sy < 2;
                    bool chamfer = !joint && (sx < 4 || sy < 4);
                    float jd = Mathf.Min(Mathf.Min(sx, 127 - sx), Mathf.Min(sy, 127 - sy));   // расстояние до шва
                    float mo = Noise.Sample(moss, 64, u, v);
                    if (jd < 14f && mo > 0.55f)
                    {
                        float k = Smooth(0.55f, 0.7f, mo) * (1f - jd / 14f) * 0.75f;
                        r = Mathf.Lerp(r, 46f, k); g = Mathf.Lerp(g, 60f, k); b = Mathf.Lerp(b, 38f, k);   // мох у швов
                    }
                    if (joint) { r *= 0.34f; g *= 0.34f; b *= 0.34f; }
                    else if (chamfer) { r *= 1.12f; g *= 1.12f; b *= 1.12f; }

                    float st = Noise.Sample(stain, 64, u, v);
                    if (st > 0.66f) { float k = Smooth(0.66f, 0.76f, st) * 0.4f; r *= 1f - k; g *= 1f - k; b *= 1f - k; }
                    float cr = Mathf.Abs(Noise.Sample(crack, 64, u, v) - 0.5f);
                    if (cr < 0.008f) { r *= 0.45f; g *= 0.45f; b *= 0.45f; }

                    float wet = 50f + 60f * Noise.Sample(stain, 64, u + 0.5f, v + 0.2f);
                    if (joint) wet = 200f;
                    px[y * S + x] = C(r, g, b, wet);
                }
            }
            return Make(S, S, px, true, TextureWrapMode.Repeat, 4, true);
        }

        /// <summary>Трава парка: тусклая ночная зелень, сухие жёлтые пролысины, земля, палая листва и редкие цветы.</summary>
        static Texture2D Grass()
        {
            const int S = 256;
            int seed = 910;
            float[] tone = Noise.Field(64, 5, 3, seed);
            float[] dry = Noise.Field(64, 6, 3, seed + 1);
            float[] dirt = Noise.Field(64, 4, 3, seed + 2);
            Color32[] px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S;
                    float v = (y + 0.5f) / S;
                    float t = 0.82f + 0.36f * Noise.Sample(tone, 64, u, v);
                    float blade = 0.86f + 0.28f * Noise.Hash(x, y, seed + 3);
                    // короткие вертикальные «травинки»: полоски по x с шумом по y
                    float strand = 0.9f + 0.2f * Noise.Hash(x, (y >> 2) + 17, seed + 4);
                    float r = 52f * t * blade * strand, g = 74f * t * blade * strand, b = 38f * t * blade * strand;
                    float d = Noise.Sample(dry, 64, u, v);
                    if (d > 0.58f)
                    {
                        float k = Smooth(0.58f, 0.74f, d) * 0.7f;
                        r = Mathf.Lerp(r, 96f * blade, k); g = Mathf.Lerp(g, 88f * blade, k); b = Mathf.Lerp(b, 48f * blade, k);
                    }
                    float di = Noise.Sample(dirt, 64, u + 0.3f, v + 0.6f);
                    if (di > 0.68f)
                    {
                        float k = Smooth(0.68f, 0.8f, di) * 0.85f;
                        r = Mathf.Lerp(r, 74f * blade, k); g = Mathf.Lerp(g, 58f * blade, k); b = Mathf.Lerp(b, 42f * blade, k);
                    }
                    float h = Noise.Hash(x, y, seed + 9);
                    if (h > 0.9965f) { r = 120f; g = 74f; b = 30f; }                 // палый лист
                    else if (h < 0.0012f) { r = 150f; g = 140f; b = 60f; }          // одуванчик
                    else if (h > 0.9945f && h <= 0.9955f) { r = 140f; g = 130f; b = 150f; }
                    px[y * S + x] = C(r, g, b, 30f);
                }
            }
            return Make(S, S, px, true, TextureWrapMode.Repeat, 4, true);
        }
    }
}
