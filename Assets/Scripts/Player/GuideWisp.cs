using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Проводник (клавиша G): тёплый огонёк-«блуждающий огонь» наоборот — он не заманивает, а ведёт к автобусу.
    /// Летит по кратчайшему пути по дорогам города (CityMap.FindPath), оставляет за собой гаснущие искры,
    /// ждёт героя, если тот отстал, и останавливается у стены Тьмы (её надо выжечь). Первый вызов бесплатный.
    /// </summary>
    public class GuideWisp : MonoBehaviour, ILightSource
    {
        public static GuideWisp Instance;

        const int TrailN = 48;
        static float cooldownLeft;
        static bool freeUsed;
        static bool playerActivated;

        public static float CooldownLeft { get { return cooldownLeft; } }
        /// <summary>
        /// Игрок сам вызвал Проводника клавишей G (в том числе нажал её, пока огонёк уже ведёт или отдыхает: он знает, как это делается).
        /// Автоматический вызов в начале забега не в счёт: пока его нет, в строке цели остаётся подсказка «G — Проводник покажет путь».
        /// </summary>
        public static bool PlayerActivated { get { return playerActivated; } }
        public static bool IsActive { get { return Instance != null && Instance.active; } }

        bool active;
        List<Pt> path;
        float[] cum;
        float total;
        float along;
        float timeLeft;
        float fade = 1f;
        float pulse;
        float trailAcc;
        bool wallNotified;
        Vector3 pos;
        Transform orbT;
        Material orbMat;
        Material haloMat;
        Material ringMat;
        Transform haloT;
        Transform ringT;
        Transform[] trail;
        Material[] trailMat;
        float[] trailAge;
        int trailHead;
        AudioSource snd;

        public static GuideWisp Create(Transform parent)
        {
            GameObject go = new GameObject("GuideWisp");
            go.transform.SetParent(parent, false);
            GuideWisp w = go.AddComponent<GuideWisp>();
            w.Setup();
            Instance = w;
            return w;
        }

        public static void CancelActive()
        {
            if (Instance != null) Instance.Stop();
        }

        /// <summary>Следующий город бесконечного режима: игрок уже знает про Проводника, подсказка в строке цели не возвращается.</summary>
        public static void KeepKnown(bool known)
        {
            playerActivated = known;
        }

        /// <summary>Новая игра: снова один бесплатный вызов, перезарядки нет.</summary>
        public static void ResetAll()
        {
            freeUsed = false;
            playerActivated = false;
            cooldownLeft = 0f;
            CancelActive();
        }

        void Setup()
        {
            orbMat = Gfx.Unlit(new Color(1f, 0.93f, 0.62f, 1f), null, false, false, 0.05f);
            orbT = Gfx.Prim(PrimitiveType.Sphere, transform, Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f), orbMat, false).transform;
            haloMat = Gfx.Unlit(new Color(1f, 0.85f, 0.45f, 0.7f), Gfx.TexHalo, true, false, 0.05f);
            haloT = Gfx.Sprite(transform, Vector3.zero, new Vector2(2.4f, 2.4f), haloMat, false).transform;
            ringMat = Gfx.Unlit(new Color(1f, 0.95f, 0.7f, 0.8f), Gfx.TexRing, true, false, 0.05f);
            ringT = Gfx.Sprite(transform, Vector3.zero, new Vector2(0.9f, 0.9f), ringMat, false).transform;

            trail = new Transform[TrailN];
            trailMat = new Material[TrailN];
            trailAge = new float[TrailN];
            for (int i = 0; i < TrailN; i++)
            {
                trailMat[i] = Gfx.Unlit(new Color(1f, 0.85f, 0.45f, 0f), Gfx.TexHalo, true, false, 0.05f);
                trail[i] = Gfx.Sprite(transform, Vector3.zero, new Vector2(0.5f, 0.5f), trailMat[i], false).transform;
                trailAge[i] = 99f;
                trail[i].gameObject.SetActive(false);
            }
            snd = ProcAudio.Source(gameObject, "wake", false, 1f, false);
            SetVisible(false);
            LightField.Register(this);
        }

        void OnDestroy()
        {
            LightField.Unregister(this);
            if (Instance == this) Instance = null;
        }

        void SetVisible(bool v)
        {
            orbT.gameObject.SetActive(v);
            haloT.gameObject.SetActive(v);
            ringT.gameObject.SetActive(v);
        }

        // ------------------------------------------------------------------ вызов

        /// <summary>byPlayer — вызов от клавиши G; автовызов в начале забега (GameManager) идёт без него.</summary>
        public static void TryCast(bool byPlayer = false)
        {
            if (Instance == null) return;
            PlayerController pl = PlayerController.Instance;
            GameManager gm = GameManager.Instance;
            if (pl == null || gm == null || gm.State != GameState.Playing || gm.Paused) return;
            if (Instance.active)
            {
                if (byPlayer) playerActivated = true;
                gm.Notify("Проводник уже ведёт тебя: иди за огоньком");
                return;
            }
            if (pl.Hero.InLastBreath) return;
            if (cooldownLeft > 0f)
            {
                if (byPlayer) playerActivated = true;
                gm.Notify("Проводник отдыхает: " + Mathf.CeilToInt(cooldownLeft) + " с");
                return;
            }
            float cost = freeUsed ? GameConfig.GuideCost : 0f;
            if (pl.Hero.Life < cost + 3f)
            {
                gm.Notify("Мало света для Проводника");
                return;
            }

            Vector3 hp = pl.transform.position;
            Pt to = CityMap.CenterOf(CityMap.BusX, CityMap.BusZ);
            if (Bus.Instance != null) to = new Pt(Bus.Instance.DoorPos.x, Bus.Instance.DoorPos.z);
            List<Pt> p = CityMap.FindPath(new Pt(hp.x, hp.z), to, true, 80f);
            if (p == null || p.Count < 2)
            {
                gm.Notify("Проводник не нашёл пути");
                return;
            }
            if (cost > 0f) pl.Hero.Spend(cost, LightReason.Guide);
            freeUsed = true;
            cooldownLeft = GameConfig.GuideCooldown;
            Instance.Begin(p, hp);
            if (byPlayer) playerActivated = true;
            gm.Notify("Проводник показывает путь к автобусу: иди за огоньком");
        }

        void Begin(List<Pt> p, Vector3 hp)
        {
            path = p;
            cum = new float[p.Count];
            float s = 0f;
            cum[0] = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                float dx = p[i].X - p[i - 1].X;
                float dz = p[i].Z - p[i - 1].Z;
                s += Mathf.Sqrt(dx * dx + dz * dz);
                cum[i] = s;
            }
            total = s;
            along = Mathf.Min(3f, total);
            timeLeft = GameConfig.GuideTime;
            fade = 1f;
            wallNotified = false;
            trailAcc = 0f;
            pos = PointAt(along) + new Vector3(0f, 1.6f, 0f);
            active = true;
            SetVisible(true);
            ProcAudio.OneShot(snd, "wake", 0.6f, 1.4f);
        }

        void Stop()
        {
            active = false;
            SetVisible(false);
        }

        Vector3 PointAt(float s)
        {
            if (path == null || path.Count == 0) return Vector3.zero;
            if (s <= 0f) return new Vector3(path[0].X, 0f, path[0].Z);
            for (int i = 1; i < path.Count; i++)
            {
                if (s <= cum[i])
                {
                    float seg = cum[i] - cum[i - 1];
                    float t = seg > 0.001f ? (s - cum[i - 1]) / seg : 0f;
                    return new Vector3(
                        Mathf.Lerp(path[i - 1].X, path[i].X, t), 0f,
                        Mathf.Lerp(path[i - 1].Z, path[i].Z, t));
                }
            }
            Pt last = path[path.Count - 1];
            return new Vector3(last.X, 0f, last.Z);
        }

        // ------------------------------------------------------------------ кадр

        void Update()
        {
            GameManager gm = GameManager.Instance;
            float dt = Time.deltaTime;
            if (gm == null || gm.Paused) dt = 0f;
            if (cooldownLeft > 0f) cooldownLeft = Mathf.Max(0f, cooldownLeft - dt);

            UpdateTrail(dt);
            if (!active) return;

            PlayerController pl = PlayerController.Instance;
            if (pl == null || gm == null || gm.State != GameState.Playing) return;

            Vector3 hp = pl.transform.position;
            timeLeft -= dt;
            if (timeLeft <= 0f)
            {
                Stop();
                return;
            }
            fade = Mathf.Clamp01(timeLeft / 2f);

            float dx = hp.x - pos.x;
            float dz = hp.z - pos.z;
            float dh = Mathf.Sqrt(dx * dx + dz * dz);
            if (dh > 55f)
            {
                Stop();
                return;
            }

            bool wait = dh > GameConfig.GuideWaitDist;
            Vector3 ahead = PointAt(Mathf.Min(along + 3.5f, total));
            bool atWall = !CityMap.IsRoad(ahead.x, ahead.z, true);
            float before = along;
            if (!wait && !atWall && along < total)
            {
                along = Mathf.Min(total, along + GameConfig.GuideSpeed * dt);
            }
            if (atWall && !wallNotified)
            {
                wallNotified = true;
                gm.Notify("Проводник ждёт у стены Тьмы: выжги её (ЛКМ)");
            }
            if (!atWall) wallNotified = false;

            float bob = Mathf.Sin(Time.time * 3.1f) * 0.18f;
            pos = PointAt(along) + new Vector3(0f, 1.7f + bob, 0f);
            pulse += dt * ((wait || atWall) ? 10f : 4.5f);

            // оставляем искры вдоль пути
            trailAcc += along - before;
            if (trailAcc > 1.3f)
            {
                trailAcc = 0f;
                SpawnSpark(pos);
            }

            // визуал
            float p = 0.75f + 0.25f * Mathf.Sin(pulse);
            float s = 0.34f * (0.85f + 0.3f * p);
            orbT.position = pos;
            orbT.localScale = new Vector3(s, s, s);
            orbMat.SetColor("_Color", new Color(1f * fade, 0.93f * fade, 0.62f * fade, 1f));
            haloT.position = pos;
            float hs = 2.2f + 0.8f * p;
            haloT.localScale = new Vector3(hs, hs, 1f);
            haloMat.SetColor("_Color", new Color(1f, 0.85f, 0.45f, 0.7f * fade));
            ringT.position = pos;
            float rs = 0.7f + 0.5f * Mathf.Abs(Mathf.Sin(pulse * 0.5f));
            ringT.localScale = new Vector3(rs, rs, 1f);
            ringMat.SetColor("_Color", new Color(1f, 0.95f, 0.7f, 0.8f * fade));

            // реплики огонька — только когда смотришь на него вблизи
            if (wait) GameUI.Label(pos + Vector3.up * 0.9f, "жду тебя…", new Color(1f, 0.9f, 0.6f, 1f), 12f, pos, 0.88f);
            else if (atWall) GameUI.Label(pos + Vector3.up * 0.9f, "здесь Тьма: ЛКМ", new Color(0.9f, 0.7f, 1f, 1f), 12f, pos, 0.88f);
        }

        void SpawnSpark(Vector3 p)
        {
            trailHead = (trailHead + 1) % TrailN;
            trail[trailHead].position = p;
            trailAge[trailHead] = 0f;
            trail[trailHead].gameObject.SetActive(true);
        }

        void UpdateTrail(float dt)
        {
            for (int i = 0; i < TrailN; i++)
            {
                if (trailAge[i] > 8f) continue;
                trailAge[i] += dt;
                float k = 1f - trailAge[i] / 8f;
                if (k <= 0f)
                {
                    trail[i].gameObject.SetActive(false);
                    continue;
                }
                float sz = 0.5f * (0.6f + 0.4f * k);
                trail[i].localScale = new Vector3(sz, sz, 1f);
                trailMat[i].SetColor("_Color", new Color(1f, 0.85f, 0.45f, 0.75f * k * k));
            }
        }

        // ------------------------------------------------------------------ ILightSource

        public Vector3 LightPosition { get { return pos; } }
        public float LightRadius { get { return active ? 7f * fade : 0f; } }
        public float LightIntensity { get { return active ? 0.7f * fade : 0f; } }
        public Color LightColor { get { return new Color(1f, 0.88f, 0.55f, 1f); } }
    }
}
