using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Стена Тьмы, перегородившая улицу. Выжигается выстрелами и лучом (ЛКМ) — это и есть «прочистить путь».
    /// Дорого, но награждает осколком света. Над стеной подсказка и процент, пока она не сгорела.
    /// Печать (Sealed) — маленькая стена у входа в хранилище света: прямой свет её не берёт, горит только луч, отражённый
    /// от зеркала (см. LightOptic, BeamTrace).
    /// </summary>
    public class Growth : MonoBehaviour, ILightTarget
    {
        public static readonly List<Growth> All = new List<Growth>();

        /// <summary>ILightTarget (PalmShot): прямое попадание выжигает стену (печать прямой свет отбрасывает).</summary>
        public void OnLightHit(float damage) { Burn(damage); }

        public float MaxHealth = 120f;
        public float Health = 120f;
        public bool Dead;
        public float Progress;

        /// <summary>Печать хранилища: прямой свет (луч, выстрел, вспышка) её не берёт, горит только отражённый от зеркала луч.</summary>
        public bool Sealed;
        /// <summary>Печать сгорела (награда хранилища). У обычной стены null: осколок света и «Путь расчищен».</summary>
        public System.Action OnDeath;

        public Bounds WorldBounds { get { return box.bounds; } }
        public Vector3 Center { get { return transform.position; } }

        CellRect rect;
        BoxCollider box;
        Transform fx;
        bool fxOn = true;
        bool blocksCells = true;   // у печати хранилища клетки карты не перекрыты: это не стена на дороге
        float rejectFlash;
        Transform[] blobs;
        Vector3[] blobScale;
        Transform[] eyes;
        Vector3[] eyeScale;
        Material hazeMat;
        float hitFlash;

        public static Growth Create(Transform parent, CellRect rect, int index)
        {
            GameObject go = new GameObject("DarknessWall" + index);
            go.transform.SetParent(parent, false);
            Pt c = rect.Center;
            go.transform.position = new Vector3(c.X, 0f, c.Z);
            Growth g = go.AddComponent<Growth>();
            g.Setup(rect, index);
            return g;
        }

        /// <summary>Печать у входа в хранилище: size — ширина и толщина, hp — прочность (горит только от отражённого луча).</summary>
        public static Growth CreateSeal(Transform parent, Vector3 center, float yawDeg, Vector2 size, float hp, int index)
        {
            GameObject go = new GameObject("DarknessSeal" + index);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(center.x, 0f, center.z);
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            Growth g = go.AddComponent<Growth>();
            g.Sealed = true;
            g.blocksCells = false;
            g.MaxHealth = hp;
            g.Health = hp;
            g.Progress = CityMap.Progress(center.x, center.z);
            g.box = go.AddComponent<BoxCollider>();
            g.box.size = new Vector3(size.x, 3.2f, size.y);
            g.box.center = new Vector3(0f, 1.6f, 0f);
            g.BuildBody(size.x, size.y, index, true);
            All.Add(g);
            return g;
        }

        void Setup(CellRect r, int index)
        {
            rect = r;
            Progress = CityMap.Progress(transform.position.x, transform.position.z);
            float sx = r.SizeX;
            float sz = r.SizeZ;

            box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(sx, 8f, sz);
            box.center = new Vector3(0f, 4f, 0f);

            BuildBody(sx, sz, index, false);
            All.Add(this);
        }

        /// <summary>Всё видимое — в одном объекте Fx: вдали (за туманом) он выключается целиком. small — печать: меньше и ниже.</summary>
        void BuildBody(float sx, float sz, int index, bool small)
        {
            fx = new GameObject("Fx").transform;
            fx.SetParent(transform, false);

            System.Random rng = new System.Random(1000 + index * 77);
            Material tar = Gfx.Lit(new Color(0.07f, 0.04f, 0.11f, 1f));
            int n = small ? 8 : 16;
            blobs = new Transform[n];
            blobScale = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float x = ((float)rng.NextDouble() - 0.5f) * sx * 0.8f;
                float z = ((float)rng.NextDouble() - 0.5f) * sz * 0.85f;
                float y = (small ? 0.4f : 0.8f) + (float)rng.NextDouble() * (small ? 2.2f : 3.8f);
                float s = (small ? 1.0f : 3.2f) + (float)rng.NextDouble() * (small ? 0.8f : 2.8f);
                blobScale[i] = new Vector3(s, s * 0.9f, s);
                blobs[i] = Gfx.Prim(PrimitiveType.Sphere, fx, new Vector3(x, y, z), blobScale[i], tar, false).transform;
            }

            // все глаза делят один материал (размер меняется масштабом, цвет у всех одинаковый)
            Material em = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(0.8f, 0.55f, 1f, 0.95f), Gfx.TexHalo, true, false, 0.1f));
            int m = small ? 10 : 26;
            eyes = new Transform[m];
            eyeScale = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                bool onX = rng.NextDouble() < 0.5;
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float ex = onX ? side * (sx * 0.5f + 0.2f) : ((float)rng.NextDouble() - 0.5f) * sx * 0.8f;
                float ez = onX ? ((float)rng.NextDouble() - 0.5f) * sz * 0.8f : side * (sz * 0.5f + 0.2f);
                float y = (small ? 0.5f : 0.8f) + (float)rng.NextDouble() * (small ? 2.4f : 4.2f);
                float sz2 = (small ? 0.25f : 0.3f) + (float)rng.NextDouble() * (small ? 0.25f : 0.4f);
                eyeScale[i] = new Vector3(sz2, sz2, 1f);
                eyes[i] = Gfx.Sprite(fx, new Vector3(ex, y, ez), new Vector2(sz2, sz2), em, false).transform;
            }

            // фиолетовая дымка: стена видна издалека даже в темноте
            hazeMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(0.55f, 0.25f, 0.9f, 0.22f), Gfx.TexHalo, true, false, 0f));
            Gfx.Sprite(fx, new Vector3(0f, small ? 1.6f : 3.5f, 0f), new Vector2(Mathf.Max(sx, sz) * (small ? 2.4f : 1.6f), small ? 4.5f : 11f), hazeMat, false);
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        void SetBlocked(bool v)
        {
            if (!blocksCells) return;
            for (int x = rect.X0; x <= rect.X1; x++)
            {
                for (int z = rect.Z0; z <= rect.Z1; z++)
                {
                    if (CityMap.InGrid(x, z)) CityMap.Blocked[x, z] = v;
                }
            }
        }

        /// <summary>Урон от прямого света (выстрел, вспышка): печать его отбрасывает.</summary>
        public void Burn(float dmg)
        {
            if (Dead) return;
            if (Sealed)
            {
                rejectFlash = 0.3f;
                return;
            }
            Damage(dmg);
        }

        /// <summary>Урон от луча героя: reflected — луч уже отразился от зеркала (только такой жжёт печать); dmg уже усилен линзами.</summary>
        public void BurnBeam(float dmg, bool reflected)
        {
            if (Dead) return;
            if (Sealed && !reflected)
            {
                rejectFlash = 0.3f;
                return;
            }
            Damage(dmg);
        }

        void Damage(float dmg)
        {
            Health -= dmg;
            hitFlash = 0.15f;
            if (Health <= 0f)
            {
                Health = 0f;
                Dead = true;
                box.enabled = false;
                SetBlocked(false);
                if (OnDeath != null) OnDeath();
                else
                {
                    LightOrb.Spawn(transform.position + new Vector3(0f, 1.2f, 0f), 16f);
                    GameManager gm = GameManager.Instance;
                    if (gm != null) gm.Notify("Путь расчищен");
                }
                gameObject.SetActive(false);
            }
        }

        public void ResetToInitial()
        {
            if (Sealed) return;   // открытое хранилище остаётся открытым: иначе, умирая, можно было бы снова и снова собирать его свет
            Dead = false;
            Health = MaxHealth;
            gameObject.SetActive(true);
            box.enabled = true;
            SetBlocked(true);
        }

        void Update()
        {
            rejectFlash = Mathf.MoveTowards(rejectFlash, 0f, Time.deltaTime);
            // далеко (дальше 150 м) стену не видно за туманом: не рисуем и не анимируем
            Transform camT = Gfx.CamTransform;
            if (camT != null)
            {
                float cdx = camT.position.x - transform.position.x;
                float cdz = camT.position.z - transform.position.z;
                float d2 = cdx * cdx + cdz * cdz;
                bool want = fxOn ? d2 < 160f * 160f : d2 < 145f * 145f;
                if (want != fxOn)
                {
                    fxOn = want;
                    fx.gameObject.SetActive(want);
                }
                if (!fxOn) return;
            }

            float k = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(Health / MaxHealth));
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.deltaTime);
            for (int i = 0; i < blobs.Length; i++)
            {
                float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 1.5f + i * 1.7f) + (hitFlash + rejectFlash * 0.5f) * 0.2f;
                blobs[i].localScale = blobScale[i] * (k * pulse);
            }
            for (int i = 0; i < eyes.Length; i++)
            {
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.9f + i * 2.3f);
                float s = eyeScale[i].x * k * (0.6f + 0.4f * blink);
                eyes[i].localScale = new Vector3(s, s, 1f);
            }
            hazeMat.SetColor("_Color", new Color(0.55f, 0.25f, 0.9f, 0.22f * k));

            // подсказка над стеной — только когда смотришь на неё и стоишь достаточно близко, чтобы выжигать (лучом бьют с 30 м,
            // но читать подпись нужно не дальше 16 м); стена большая, поэтому конус широкий
            float reach = Sealed ? 12f : 16f + Mathf.Max(box.size.x, box.size.z) * 0.5f;
            Vector3 top = transform.position + new Vector3(0f, Sealed ? 3.4f : 5.8f, 0f);
            Vector3 gz = transform.position + new Vector3(0f, Sealed ? 1.5f : 2.5f, 0f);
            GameUI.Label(top, Sealed ? "ПЕЧАТЬ ТЬМЫ" : "СТЕНА ТЬМЫ", new Color(0.85f, 0.6f, 1f, 1f), reach, gz, 0.84f);
            string hint;
            if (Sealed)
                hint = rejectFlash > 0f ? "прямой свет её не берёт!"
                    : (Health < MaxHealth - 1f ? "отражённый луч: осталось " + Mathf.CeilToInt(Health / MaxHealth * 100f) + "%" : "жжёт только отражённый свет: зеркало");
            else
                hint = Health < MaxHealth - 1f
                    ? "держи ЛКМ — осталось " + Mathf.CeilToInt(Health / MaxHealth * 100f) + "%"
                    : "выжги светом: ЛКМ (держать)";
            GameUI.Label(top + new Vector3(0f, -1.1f, 0f), hint, new Color(0.95f, 0.85f, 1f, 1f), reach, gz, 0.84f);
        }
    }
}
