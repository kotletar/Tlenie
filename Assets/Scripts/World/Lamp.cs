using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Огонь города: фонарь, очаг или ложный "Блуждающий огонь".
    /// Каждый огонь — одновременно: батарейка (вдох, E), убежище (пока горит, под ним отдыхают люди)
    /// и вклад (выдох, F: свет героя превращает лампу в очаг с бо́льшим радиусом).
    /// Charge: 1.0 — полная лампа, 0 — погасла (пока её не раздуют), до 2.0 — очаг.
    /// Под лампой виден конус света в пыльном воздухе; цвет лампы зависит от района.
    /// </summary>
    public class Lamp : MonoBehaviour, ILightSource, ILightTarget
    {
        public static readonly List<Lamp> All = new List<Lamp>();

        /// <summary>ILightTarget (PalmShot): луч рассеивает ложный огонь (настоящие лампы он не трогает).</summary>
        public void OnLightHit(float damage) { Reveal(); }

        /// <summary>
        /// Предыстория (вид на улицу): маска огней по положению лампы. 1 — горит ровно, 0 — погасла, между — мерцает; < 0 — маски нет,
        /// лампа живёт по своему состоянию. Состояние (Charge, мёртвая ли) маска не трогает: меняются только свет и свечение.
        /// </summary>
        public static System.Func<Vector3, float> IntroMask;

        float IntroK()
        {
            return IntroMask != null ? IntroMask(basePos) : -1f;
        }

        public float Charge = 1f;
        public float InitialCharge = 1f;
        public bool IsHearth;
        public bool IsDecoy;
        public bool Unstable;
        public float Progress;      // доля пути до автобуса (для контрольных точек)

        public Vector3 BasePos { get { return basePos; } }
        /// <summary>Куда смотрит кронштейн (от стены на улицу), горизонтально, длина 1.</summary>
        public Vector3 ArmDir { get { return armDir; } }
        public bool Dead { get { return dead; } }
        public Vector3 FocusPoint { get { return basePos + Vector3.up * 1.4f; } }

        Vector3 basePos;
        Vector3 armDir = Vector3.forward;
        Vector3 lightPos;
        Vector3 lightLocal;
        bool dead;
        float seed;
        float flicker = 1f;
        float decoyTimer;
        float decoyActive;
        float revealT;
        bool humOn;
        Color tint = new Color(1f, 0.78f, 0.42f, 1f);
        Material bulbMat;
        Material haloMat;
        Material coneMat;
        AudioSource hum;
        Transform fxRoot;
        bool fxOn = true;

        static readonly Color Cold = new Color(0.62f, 0.82f, 1f, 1f);

        public static Lamp Create(Transform parent, Vector3 basePos, Vector3 armDir, float charge, bool unstable, bool decoy, bool hearth, Color tint)
        {
            GameObject go = new GameObject(hearth ? "Hearth" : (decoy ? "Lamp(wisp)" : "Lamp"));
            go.transform.SetParent(parent, false);
            go.transform.position = basePos;
            Lamp l = go.AddComponent<Lamp>();
            l.Setup(basePos, armDir, charge, unstable, decoy, hearth, tint);
            return l;
        }

        void Setup(Vector3 pos, Vector3 armDirection, float charge, bool unstable, bool decoy, bool hearth, Color lampTint)
        {
            basePos = pos;
            armDir = armDirection;
            Charge = charge;
            InitialCharge = charge;
            Unstable = unstable;
            IsDecoy = decoy;
            IsHearth = hearth;
            tint = lampTint;
            Progress = CityMap.Progress(pos.x, pos.z);
            dead = charge <= 0.001f;
            seed = Random.value * 100f;

            armDir.y = 0f;
            if (armDir.sqrMagnitude < 0.01f) armDir = Vector3.forward;
            armDir.Normalize();

            Color baseCol = decoy ? Cold : tint;
            Transform t = transform;
            float haloSize;
            GameObject bulbGo;

            // Металл (мачта, кронштейн, корпус, жаровня) — в общий батч города: ноль отдельных объектов.
            // Динамическое (огонь, ореол, конус света) — в отдельном Fx: вдали, за туманом, его выключаем.
            MeshBatch pb = LevelBuilder.PropBatchAt(pos);
            bool ownBatch = pb == null;
            if (ownBatch) pb = new MeshBatch();
            fxRoot = new GameObject("Fx").transform;
            fxRoot.SetParent(t, false);

            if (hearth)
            {
                // Бочка-жаровня с огнём
                Models.Brazier(pb, pos);
                bulbMat = OwnedResources.Keep(gameObject, Gfx.Unlit(baseCol, null, false, false, 0.3f));
                bulbGo = Gfx.Prim(PrimitiveType.Sphere, fxRoot, new Vector3(0f, 1.18f, 0f), new Vector3(0.6f, 0.5f, 0.6f), bulbMat, false);
                lightLocal = new Vector3(0f, 1.5f, 0f);
                haloSize = 7f;
            }
            else
            {
                Vector3 bulbLocal = Models.LampPost(pb, pos, armDir);
                bulbMat = OwnedResources.Keep(gameObject, Gfx.Unlit(baseCol, null, false, false, 0.3f));
                bulbGo = Gfx.Prim(PrimitiveType.Sphere, fxRoot, bulbLocal, new Vector3(0.26f, 0.22f, 0.26f), bulbMat, false);
                lightLocal = bulbLocal;
                haloSize = 4.4f;

                // конус света в воздухе
                coneMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(baseCol.r, baseCol.g, baseCol.b, 0.5f), null, true, false, 0.12f));
                coneMat.SetFloat("_EdgeFade", 1f);   // мягкий край: свет в тумане, а не плоский треугольник
                GameObject cone = Gfx.MeshObject(fxRoot, Gfx.ConeMesh(), coneMat, "Cone");
                cone.transform.localPosition = lightLocal;
                cone.transform.localScale = new Vector3(3.6f, lightLocal.y - 0.05f, 3.6f);
            }

            if (ownBatch)
            {
                // лампа создана не при сборке города (в тесте): металл — отдельным мешем
                Mesh m = pb.Build("PG_LampMetal");
                LevelBuilder.TrackMesh(m);
                GameObject mo = Gfx.MeshObject(t, m, Gfx.Lit(Color.white), "Metal");
                mo.transform.position = Vector3.zero;
            }

            haloMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(baseCol.r, baseCol.g, baseCol.b, 0.5f), Gfx.TexHalo, true, false, 0.12f));
            Gfx.Sprite(fxRoot, lightLocal, new Vector2(haloSize, haloSize), haloMat, false);

            if (hearth) hum = ProcAudio.Source(bulbGo, "crackle", true, 0.35f, true);
            else if (!decoy)
            {
                hum = ProcAudio.Source(bulbGo, "hum", true, 0.3f, true);
                hum.pitch = Random.Range(0.92f, 1.1f);
            }
            humOn = hum != null;

            lightPos = pos + lightLocal;
            All.Add(this);
            LightField.Register(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
            LightField.Unregister(this);
        }

        /// <summary>
        /// Имя первого твёрдого предмета, которого касается фонарь (основание, мачта, кронштейн, плафон), или null. margin раздувает
        /// проверку: 0 — настоящее пересечение (столб «в доме»), 0.3 — фонарь вплотную к стене («параллелен дому»). Для проверки расстановки.
        /// </summary>
        public string SolidContact(float margin)
        {
            if (IsHearth) return null;
            Vector3 up = Vector3.up;
            Quaternion q = Quaternion.LookRotation(armDir, up);
            Vector3 headC = basePos + armDir * 1.2f + up * 4.42f;
            Vector3 armC = basePos + armDir * 0.62f + up * 4.5f;
            string hit = FirstSolid(Physics.OverlapBox(basePos + up * 0.06f, new Vector3(0.28f + margin, 0.06f + margin, 0.28f + margin), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore));
            if (hit == null) hit = FirstSolid(Physics.OverlapCapsule(basePos + up * 0.25f, basePos + up * 4.25f, 0.12f + margin, ~0, QueryTriggerInteraction.Ignore));
            if (hit == null) hit = FirstSolid(Physics.OverlapBox(armC, new Vector3(0.05f + margin, 0.15f + margin, 0.66f), q, ~0, QueryTriggerInteraction.Ignore));
            if (hit == null) hit = FirstSolid(Physics.OverlapBox(headC, new Vector3(0.18f + margin, 0.1f + margin, 0.37f + margin), q, ~0, QueryTriggerInteraction.Ignore));
            return hit;
        }

        static string FirstSolid(Collider[] hits)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i];
                if (c.isTrigger || c.gameObject.name == "Floor") continue;
                if (c.GetComponentInParent<Survivor>() != null || c.GetComponentInParent<PlayerController>() != null) continue;
                return c.gameObject.name;
            }
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            decoyActive = Mathf.MoveTowards(decoyActive, 0f, dt);
            if (decoyActive <= 0f) decoyTimer = Mathf.MoveTowards(decoyTimer, 0f, dt * 0.7f);
            revealT = Mathf.MoveTowards(revealT, 0f, dt);

            if (!dead && Charge <= 0.001f) Die();
            else if (dead && Charge > 0.02f) dead = false;

            float n = Mathf.PerlinNoise(Time.time * (Unstable ? 11f : 4f) + seed, seed);
            if (IsDecoy) flicker = 1f;
            else flicker = 1f - (Unstable ? 0.55f : 0.08f) * n;
            if (Unstable && n > 0.82f) flicker *= 0.15f;

            // дальние огни (за туманом) не рисуем: каждый — три draw call'а, а их не видно
            Transform camT = Gfx.CamTransform;
            if (camT != null)
            {
                float cdx = camT.position.x - basePos.x;
                float cdz = camT.position.z - basePos.z;
                float d2 = cdx * cdx + cdz * cdz;
                bool want = fxOn ? d2 < 140f * 140f : d2 < 125f * 125f;
                if (want != fxOn)
                {
                    fxOn = want;
                    fxRoot.gameObject.SetActive(want);
                }
            }

            float tide = GameManager.TideFactorSafe;
            float ik = IntroK();   // в предыстории огни улицы ведёт сценарий: все горят ровно, пока их не накроет Тьма
            bool effDead = ik >= 0f ? ik <= 0.01f : dead;
            float vis = Mathf.Clamp(ik >= 0f ? ik : Charge, 0f, 1.5f) * (ik >= 0f ? 1f : flicker);
            Color baseCol = IsDecoy ? Cold : tint;
            if (!fxOn)
            {
                // не видно: цвета не обновляем
            }
            else if (effDead)
            {
                bulbMat.SetColor("_Color", new Color(0.06f, 0.06f, 0.07f, 1f));
                haloMat.SetColor("_Color", new Color(baseCol.r, baseCol.g, baseCol.b, 0f));
                if (coneMat != null) coneMat.SetColor("_Color", new Color(baseCol.r, baseCol.g, baseCol.b, 0f));
            }
            else
            {
                float k = Mathf.Min(1f, vis + 0.1f);
                bulbMat.SetColor("_Color", new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k, 1f));
                haloMat.SetColor("_Color", new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Clamp01(0.55f * vis * tide)));
                if (coneMat != null) coneMat.SetColor("_Color", new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Clamp01(0.5f * vis * tide)));
            }

            // звук гудения — только у тех ламп, что рядом с героем
            if (hum != null)
            {
                PlayerController pl = PlayerController.Instance;
                bool near = true;
                if (pl != null)
                {
                    float dx = pl.transform.position.x - basePos.x;
                    float dz = pl.transform.position.z - basePos.z;
                    near = dx * dx + dz * dz < 32f * 32f;
                }
                if (near != humOn)
                {
                    humOn = near;
                    if (near) hum.Play();
                    else hum.Stop();
                }
                AudioMix.SetVolume(hum, dead ? 0f : Mathf.Clamp01(Charge) * (IsHearth ? 0.35f : 0.3f));
            }

            // подпись над очагом — только когда смотришь на него вблизи (с расстояния, где он работает как очаг и лампа)
            if (IsHearth && !dead)
                GameUI.Label(basePos + Vector3.up * 2.2f, "ОЧАГ", new Color(1f, 0.85f, 0.5f, 1f), 5.2f,
                    basePos + Vector3.up * 1.0f, 0.93f);
        }

        // ------------------------------------------------------------- действия героя и врагов

        /// <summary>Герой вдыхает: возвращает, сколько света получил. Отдача падает по мере угасания лампы.</summary>
        public float Take(float amount, bool lowLifeBonus)
        {
            if (dead || IsHearth || IsDecoy) return 0f;
            float c01 = Mathf.Clamp01(Charge);
            float eff = Mathf.Lerp(GameConfig.InhaleEffMin, 1f, c01);
            if (lowLifeBonus) eff *= GameConfig.LowLifeBonus;
            float units = Mathf.Min(amount, Charge * GameConfig.LampCapacity);
            Charge -= units / GameConfig.LampCapacity;
            if (Charge <= 0.001f)
            {
                Charge = 0f;
                Die();
            }
            return units * eff;
        }

        /// <summary>Герой выдыхает в лампу: зажигает погасшую, а полную раздувает до очага.</summary>
        public void Feed(float amount)
        {
            if (IsDecoy) return;
            Charge = Mathf.Min(GameConfig.LampMaxCharge, Charge + amount);
            if (dead && Charge > 0.02f) dead = false;
        }

        /// <summary>Гаситель сбивает огонь.</summary>
        public void Snuff(float amount)
        {
            if (dead || IsDecoy) return;
            Charge -= amount;
            if (Charge <= 0.001f)
            {
                Charge = 0f;
                Die();
            }
        }

        /// <summary>Герой вдыхает ложный огонь: через пару секунд он лопается и выпускает Теней.</summary>
        public void DecoyTick(float dt)
        {
            if (dead) return;
            decoyActive = 0.25f;
            decoyTimer += dt;
            if (decoyTimer > GameConfig.DecoyBurstTime) Burst();
        }

        /// <summary>Выстрел или луч выжигает ложный огонь (сразу).</summary>
        public void Reveal()
        {
            if (dead || !IsDecoy) return;
            dead = true;
            Charge = 0f;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.Notify("Блуждающий огонь рассеялся");
            TimedFx.Spawn(lightPos, 1f, 6f, new Color(0.6f, 0.8f, 1f, 0.8f), 0.4f);
        }

        /// <summary>Постепенное выжигание лучом.</summary>
        public void RevealTick(float dt)
        {
            if (dead) return;
            revealT += dt * 2f;
            if (revealT > 0.7f) Reveal();
        }

        void Burst()
        {
            dead = true;
            Charge = 0f;
            decoyTimer = 0f;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.Notify("Это был Блуждающий огонь!");
            TimedFx.Spawn(lightPos, 1f, 9f, new Color(0.6f, 0.8f, 1f, 0.9f), 0.5f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.094f + 0.5f;
                Vector3 p = basePos + new Vector3(Mathf.Cos(a) * 3f, 0f, Mathf.Sin(a) * 3f);
                Enemy.Spawn(EnemyKind.Shade, p);
            }
        }

        void Die()
        {
            if (dead) return;
            dead = true;
            Charge = 0f;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.OnLampDied(this);
            if (hum != null) ProcAudio.OneShot(hum, "lampdie", 0.8f, 1f);
        }

        public void ResetToInitial()
        {
            Charge = InitialCharge;
            dead = Charge <= 0.001f;
            decoyTimer = 0f;
            decoyActive = 0f;
            revealT = 0f;
        }

        // ------------------------------------------------------------- ILightSource

        public Vector3 LightPosition { get { return lightPos; } }

        public float LightRadius
        {
            get
            {
                float ik = IntroK();
                if (ik >= 0f ? ik <= 0.01f : dead) return 0f;
                float ch = ik >= 0f ? ik : Charge;
                float c = Mathf.Clamp01(ch);
                float r = Mathf.Lerp(2.5f, 13f, c) + Mathf.Max(0f, ch - 1f) * 7f;
                return r * (0.8f + 0.2f * GameManager.TideFactorSafe);
            }
        }

        public float LightIntensity
        {
            get
            {
                float ik = IntroK();
                if (ik >= 0f ? ik <= 0.01f : dead) return 0f;
                if (ik >= 0f) return Mathf.Clamp(ik, 0f, 1.6f) * GameManager.TideFactorSafe * 1.1f;
                return Mathf.Clamp(Charge, 0f, 1.6f) * flicker * GameManager.TideFactorSafe * 1.1f;
            }
        }

        public Color LightColor { get { return IsDecoy ? Cold : tint; } }
    }
}
