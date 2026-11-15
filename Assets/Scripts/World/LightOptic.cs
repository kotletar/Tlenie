using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    public enum OpticKind { Mirror, Lens }

    /// <summary>
    /// Зеркало или линза хранилища света. Зеркало отражает луч героя (ЛКМ) по закону отражения, E поворачивает панель на 45°
    /// (четыре положения: двусторонняя панель). Линза — шар света на стойке: луч, прошедший сквозь неё, жжёт втрое сильнее
    /// (и врагов тоже). Сам по себе ни то ни другое света не даёт: это инструменты, не огни.
    /// </summary>
    public class LightOptic : MonoBehaviour
    {
        public static readonly List<LightOptic> All = new List<LightOptic>();

        public const float MirrorWidth = 1.7f;
        public const float MirrorHeight = 1.9f;
        public const float LensRadius = 0.7f;
        public const float LensHeight = 1.55f;
        /// <summary>Во сколько раз линза усиливает луч (урон по всему, что после неё).</summary>
        public const float LensFocus = 3f;
        public const float ReachE = 3.4f;

        public OpticKind Kind;
        /// <summary>Зеркало: 0..3 — панель повёрнута на Step*45° вокруг вертикали.</summary>
        public int Step;

        public Vector3 FocusPoint { get { return transform.position + Vector3.up * 1.4f; } }
        public Vector3 LensCenter { get { return transform.position + Vector3.up * LensHeight; } }

        /// <summary>Горизонтальная нормаль панели (зеркало).</summary>
        public Vector3 PlaneNormal
        {
            get
            {
                float a = Step * 45f * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
        }

        Transform panel;
        Material glowMat;
        Material haloMat;
        AudioSource sfx;
        float pulse;

        static readonly Color MirrorTint = new Color(0.50f, 0.78f, 0.95f, 1f);
        static readonly Color LensTint = new Color(1f, 0.86f, 0.5f, 1f);

        public static LightOptic Create(Transform parent, OpticKind kind, Vector3 pos, int step)
        {
            GameObject go = new GameObject(kind == OpticKind.Mirror ? "Mirror" : "Lens");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            LightOptic o = go.AddComponent<LightOptic>();
            o.Kind = kind;
            o.Step = step & 3;
            o.Build();
            All.Add(o);
            return o;
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        void Build()
        {
            glowMat = OwnedResources.Keep(gameObject, Gfx.Unlit(Kind == OpticKind.Mirror ? MirrorTint : LensTint, null, false, false, 0.3f));
            haloMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(Kind == OpticKind.Mirror ? 0.5f : 1f, Kind == OpticKind.Mirror ? 0.8f : 0.8f, Kind == OpticKind.Mirror ? 1f : 0.4f, 0.0f), Gfx.TexHalo, true, false, 0.2f));

            // стойка и основание (металл): отдельным мешем, как у фонаря
            MeshBatch mb = new MeshBatch();
            Vector3 p = transform.position;
            Color32 metal = new Color32(46, 46, 52, 255);
            Color32 dark = new Color32(26, 26, 30, 255);
            mb.Prism(p + new Vector3(0f, 0.07f, 0f), Vector3.up, 0.5f, 0.14f, 10, dark, true);
            mb.Prism(p + new Vector3(0f, 0.35f, 0f), Vector3.up, 0.1f, 0.5f, 8, metal, true);
            if (Kind == OpticKind.Mirror)
            {
                mb.Prism(p + new Vector3(0f, 1.45f, 0f), Vector3.up, 0.06f, 2.2f, 8, metal, true);
            }
            else
            {
                mb.Prism(p + new Vector3(0f, 0.8f, 0f), Vector3.up, 0.07f, 1.3f, 8, metal, true);
                // кольцо-обод под шаром
                mb.Prism(p + new Vector3(0f, LensHeight - 0.5f, 0f), Vector3.up, 0.34f, 0.06f, 12, metal, true);
            }
            Mesh m = mb.Build("PG_OpticMetal");
            LevelBuilder.TrackMesh(m);
            GameObject mo = Gfx.MeshObject(transform, m, Gfx.Lit(Color.white), "Metal");
            mo.transform.position = Vector3.zero;

            // у основания — твёрдый столбик, чтобы сквозь стойку не ходили
            BoxCollider foot = gameObject.AddComponent<BoxCollider>();
            foot.size = new Vector3(0.5f, 1.0f, 0.5f);
            foot.center = new Vector3(0f, 0.5f, 0f);

            if (Kind == OpticKind.Mirror)
            {
                // панель: светящаяся прохладная пластина в раме; поворачивается вместе с коллайдером (по нему луч и отражается)
                panel = new GameObject("Panel").transform;
                panel.SetParent(transform, false);
                panel.localPosition = new Vector3(0f, 1.45f, 0f);
                panel.localRotation = Quaternion.Euler(0f, Step * 45f, 0f);
                Material frame = Gfx.Lit(new Color(0.16f, 0.16f, 0.2f));
                Gfx.Prim(PrimitiveType.Cube, panel, Vector3.zero, new Vector3(MirrorWidth, MirrorHeight, 0.05f), glowMat, false);
                Gfx.Prim(PrimitiveType.Cube, panel, new Vector3(0f, MirrorHeight * 0.5f + 0.05f, 0f), new Vector3(MirrorWidth + 0.2f, 0.1f, 0.12f), frame, false);
                Gfx.Prim(PrimitiveType.Cube, panel, new Vector3(0f, -MirrorHeight * 0.5f - 0.05f, 0f), new Vector3(MirrorWidth + 0.2f, 0.1f, 0.12f), frame, false);
                Gfx.Prim(PrimitiveType.Cube, panel, new Vector3(MirrorWidth * 0.5f + 0.05f, 0f, 0f), new Vector3(0.1f, MirrorHeight, 0.12f), frame, false);
                Gfx.Prim(PrimitiveType.Cube, panel, new Vector3(-MirrorWidth * 0.5f - 0.05f, 0f, 0f), new Vector3(0.1f, MirrorHeight, 0.12f), frame, false);
                BoxCollider bc = panel.gameObject.AddComponent<BoxCollider>();
                bc.size = new Vector3(MirrorWidth + 0.2f, MirrorHeight + 0.2f, 0.12f);
                panel.gameObject.name = "MirrorPanel";
                Gfx.Sprite(transform, new Vector3(0f, 1.45f, 0f), new Vector2(3.2f, 3.2f), haloMat, false);
            }
            else
            {
                // шар-линза и два перекрещённых диска стекла
                GameObject ball = Gfx.Prim(PrimitiveType.Sphere, transform, new Vector3(0f, LensHeight, 0f), Vector3.one * 0.62f, glowMat, false);
                ball.name = "LensBall";
                Gfx.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, LensHeight, 0f), new Vector3(1.4f, 0.015f, 1.4f), glowMat, false);
                GameObject cross = Gfx.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, LensHeight, 0f), new Vector3(1.4f, 0.015f, 1.4f), glowMat, false);
                cross.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Gfx.Sprite(transform, new Vector3(0f, LensHeight, 0f), new Vector2(3.6f, 3.6f), haloMat, false);
            }
            sfx = ProcAudio.Source(gameObject, "orb", false, 0.5f, true);
        }

        /// <summary>E: повернуть зеркало на 45°.</summary>
        public void Rotate()
        {
            if (Kind != OpticKind.Mirror) return;
            SetStep(Step + 1);
            if (sfx != null) ProcAudio.OneShot(sfx, "tick", 0.6f, 0.8f + 0.1f * Step);
        }

        public void SetStep(int step)
        {
            Step = step & 3;
            if (panel != null) panel.localRotation = Quaternion.Euler(0f, Step * 45f, 0f);
            Physics.SyncTransforms();
        }

        /// <summary>Луч попал в зеркало или прошёл сквозь линзу: на миг ярче.</summary>
        public void Touch()
        {
            pulse = 0.3f;
        }

        void Update()
        {
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime);
            Transform camT = Gfx.CamTransform;
            if (camT != null)
            {
                float dx = camT.position.x - transform.position.x;
                float dz = camT.position.z - transform.position.z;
                if (dx * dx + dz * dz > 120f * 120f) return;
            }
            bool playing = GameManager.Instance != null && GameManager.Instance.State == GameState.Playing;
            float k = 0.40f + 0.45f * pulse / 0.3f + 0.06f * Mathf.Sin(Time.time * 2.1f + transform.position.x);   // тусклее: бледное свечение, не маяк
            Color baseC = Kind == OpticKind.Mirror ? MirrorTint : LensTint;
            glowMat.SetColor("_Color", baseC * Mathf.Clamp(k, 0.3f, 1.4f));
            haloMat.SetColor("_Color", new Color(baseC.r, baseC.g, baseC.b, playing ? 0.10f + 0.35f * pulse / 0.3f : 0f));

            // подпись и подсказка — только когда смотришь на предмет вблизи
            Vector3 top = transform.position + new Vector3(0f, Kind == OpticKind.Mirror ? 3.0f : 2.5f, 0f);
            Vector3 gz = transform.position + new Vector3(0f, 1.5f, 0f);
            GameUI.Label(top, Kind == OpticKind.Mirror ? "ЗЕРКАЛО" : "ЛИНЗА", new Color(0.75f, 0.92f, 1f, 1f), 9f, gz, 0.9f);
            GameUI.Label(top + new Vector3(0f, -0.5f, 0f), Kind == OpticKind.Mirror ? "E — повернуть, луч отражается" : "луч сквозь неё жжёт втрое сильнее",
                new Color(0.9f, 0.95f, 1f, 1f), 9f, gz, 0.9f);
        }
    }
}
