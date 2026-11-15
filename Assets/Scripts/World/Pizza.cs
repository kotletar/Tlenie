using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Пицца в переулке — находка. Герой берёт её на E и тут же съедает: ломтики по два летят к лицу, на каждом из трёх укусов
    /// свет прибавляется на треть (<see cref="GameConfig.PizzaLight"/> всего). Пустая коробка остаётся лежать.
    /// В тёмном переулке пиццу выдаёт слабое тёплое свечение-ореол: по нему её можно заметить издалека. Настоящего света она
    /// не излучает (иначе пересвечивала бы саму себя и не влияла бы на людей и Теней).
    /// </summary>
    public class Pizza : MonoBehaviour
    {
        public static readonly List<Pizza> All = new List<Pizza>();

        public const int Bites = 3;
        const float FlyTime = 0.42f;                       // кусок взлетает от коробки к лицу
        const float BiteFirst = 0.62f;                     // когда первый укус
        const float BiteStep = 0.42f;                      // и промежуток между укусами
        const float EndTime = BiteFirst + (Bites - 1) * BiteStep + 0.4f;
        const float HaloAlpha = 0.40f;                     // яркость ореола (мерцает вокруг этого значения)

        /// <summary>Подобрана: летит к лицу, её едят или уже съели (взять второй раз нельзя).</summary>
        public bool Taken { get { return taken; } }
        /// <summary>Съедена целиком: на земле осталась только пустая коробка.</summary>
        public bool Finished { get { return finished; } }
        /// <summary>Сколько укусов уже сделано.</summary>
        public int BitesDone { get { return bitesDone; } }
        public Vector3 FocusPoint { get { return transform.position + Vector3.up * 0.12f; } }

        bool taken;
        bool finished;
        float t;
        float age;
        float phase;
        int bitesDone;
        Transform slices;
        readonly GameObject[] groups = new GameObject[Bites];
        GameObject halo;
        Material haloMat;
        Vector3 startPos;
        Quaternion startRot;

        public static Pizza Create(Transform parent, Vector3 pos, float yaw)
        {
            GameObject go = new GameObject("Pizza");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Pizza p = go.AddComponent<Pizza>();
            p.Setup();
            return p;
        }

        void Setup()
        {
            Material lit = Gfx.Lit(Color.white);

            MeshBatch box = new MeshBatch();
            Models.PizzaBox(box);
            Mesh boxMesh = box.Build("PizzaBox");
            LevelBuilder.TrackMesh(boxMesh);
            Gfx.MeshObject(transform, boxMesh, lit, "Box");

            slices = new GameObject("Slices").transform;
            slices.SetParent(transform, false);
            for (int g = 0; g < Bites; g++)
            {
                MeshBatch sb = new MeshBatch();
                Models.PizzaSlices(sb, g);
                Mesh sm = sb.Build("PizzaSlices" + g);
                LevelBuilder.TrackMesh(sm);
                groups[g] = Gfx.MeshObject(slices, sm, lit, "Slices" + g);
            }

            // ореол зажигается только в игре (Update): в предыстории и в меню город должен быть тёмным
            haloMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(1f, 0.72f, 0.38f, 0f), Gfx.TexHalo, true, false, 0.2f));
            halo = Gfx.Sprite(transform, new Vector3(0f, 0.3f, 0f), new Vector2(2.6f, 2.6f), haloMat, false);

            phase = Random.value * 6.28f;
            All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        /// <summary>Герой взял пиццу: она отправляется к лицу, дальше её едят (укусы идут сами, пока Update).</summary>
        public void Take()
        {
            if (taken) return;
            taken = true;
            t = 0f;
            startPos = slices.position;
            startRot = slices.rotation;
            if (halo != null) halo.SetActive(false);
            // у самого лица пицца оказывается в упор у света героя и выгорала бы в белое пятно: тусклее материал
            Material dim = Gfx.Lit(new Color(0.45f, 0.45f, 0.45f, 1f));
            for (int g = 0; g < Bites; g++)
            {
                if (groups[g] != null) groups[g].GetComponent<MeshRenderer>().sharedMaterial = dim;
            }
            PlayerController pl = PlayerController.Instance;
            if (pl != null) ProcAudio.OneShot(pl.Sfx, "rustle", 0.55f, 1.7f);   // шорох картона
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing || gm.Paused) return;
            float dt = Time.deltaTime;
            age += dt;
            if (finished) return;
            PlayerController pl = PlayerController.Instance;
            if (pl == null) return;

            if (!taken)
            {
                haloMat.SetColor("_Color", new Color(1f, 0.72f, 0.38f, HaloAlpha + 0.1f * Mathf.Sin(age * 1.9f + phase)));
                // подпись — только когда смотришь на пиццу вблизи
                GameUI.Label(transform.position + Vector3.up * 0.75f, "ПИЦЦА", new Color(1f, 0.84f, 0.5f, 1f), 4.4f,
                    transform.position + Vector3.up * 0.2f, 0.86f);
                return;
            }

            t += dt;
            Transform cam = pl.Cam.transform;
            Vector3 up = cam.up;
            // пицца у лица: чуть ниже центра экрана, верхней стороной к глазам
            Vector3 held = cam.position + cam.forward * 0.72f - up * 0.24f;
            Vector3 n = (-cam.forward * 0.72f + up * 0.69f).normalized;
            Quaternion heldRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(up, n).normalized, n);

            float u = Mathf.Clamp01(t / FlyTime);
            float s = u * u * (3f - 2f * u);
            Vector3 pos = Vector3.Lerp(startPos, held, s) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 0.2f);
            // перед каждым укусом пицца на миг подаётся ко рту
            float chomp = 0f;
            for (int k = 0; k < Bites; k++)
            {
                float c = 1f - Mathf.Abs(t - (BiteFirst + k * BiteStep)) / 0.13f;
                if (c > chomp) chomp = c;
            }
            pos += cam.forward * (-0.11f * chomp * s);
            slices.position = pos;
            slices.rotation = Quaternion.Slerp(startRot, heldRot, s);
            slices.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, s);

            while (bitesDone < Bites && t >= BiteFirst + bitesDone * BiteStep) Bite(pl);
            if (t >= EndTime) Finish(gm);
        }

        void Bite(PlayerController pl)
        {
            if (groups[bitesDone] != null) groups[bitesDone].SetActive(false);
            bitesDone++;
            bool last = bitesDone >= Bites;
            pl.Hero.Gain(GameConfig.PizzaLight / Bites, LightReason.Pizza);
            pl.Hero.FlashPulse = Mathf.Max(pl.Hero.FlashPulse, last ? 0.5f : 0.25f);   // свет на миг вспыхивает теплее
            ProcAudio.OneShot(pl.Sfx, "munch", 0.9f, Random.Range(0.92f, 1.08f));
        }

        void Finish(GameManager gm)
        {
            finished = true;
            slices.gameObject.SetActive(false);
            PlayerController pl = PlayerController.Instance;
            if (pl != null) ProcAudio.OneShot(pl.Sfx, "orb", 0.5f, 0.8f);
            gm.Think("pizza", "Пицца. Остыла давно, а в груди потеплело. Выходит, огонь можно кормить и этим.");
        }
    }
}
