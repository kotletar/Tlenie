using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Заброшенный автобус на автовокзале. Его заводят светом: встань у двери (светящееся кольцо на земле)
    /// и держи F — герой отдаёт свой свет, а каждый спасённый рядом добавляет сам. Шкала «топлива» — окна автобуса,
    /// которые загораются по мере зарядки. Когда автобус заведён, люди садятся, а герой удерживает круг целую минуту
    /// (финальная волна Тьмы, GameConfig.FinalTime; на эту минуту играет музыка из MusicDirector). Потом автобус уезжает
    /// по дороге из города на рассвет — победа.
    /// </summary>
    public class Bus : MonoBehaviour, ILightSource
    {
        public enum Mode { Idle, Running, Leaving }

        public static Bus Instance;

        public Mode State = Mode.Idle;
        public float Charge;
        public float FinalLeft;
        public int BoardedCount;

        /// <summary>Сколько всего длится последняя минута (GameConfig.FinalTime на момент запуска двигателя).</summary>
        public float StandLength { get; private set; }
        /// <summary>Сколько секунд обороны уже прошло: по нему идёт музыка последней минуты.</summary>
        public float StandElapsed { get { return Mathf.Clamp(StandLength - FinalLeft, 0f, StandLength); } }

        /// <summary>true в концовке: автобус едет не сам, его ведёт режиссёр (EndingDirector).</summary>
        public bool Scripted;
        public Vector3 StartPos { get { return startPos; } }

        /// <summary>
        /// Концовка: насколько фары светят «снаружи» (1 — как у заведённого автобуса, пока герой бежит к двери; 0 — как в салоне).
        /// Режиссёр сводит её к нулю после порога.
        /// </summary>
        public float CineLight;

        /// <summary>Кузов автобуса снаружи сейчас рисуется (в концовке прячется, когда камера входит в салон).</summary>
        public bool ExteriorVisible { get; private set; }

        public bool CanFeed { get { return State == Mode.Idle; } }
        public Vector3 DoorPos { get { return startPos + new Vector3(-2.6f, 0f, 1.5f); } }
        public Vector3 FocusPoint { get { return DoorPos + Vector3.up * 1.3f; } }
        public float ChargeFrac { get { return Mathf.Clamp01(Charge / GameConfig.BusNeed); } }

        Vector3 startPos;
        Material lampMat;
        Material haloMat;
        Material ringMat;
        Material gaugeMat;
        Transform ring;
        AudioSource engine;
        float leaveT;
        Renderer[] bodyParts;   // собственные детали автобуса; салон концовки (RideStage) — тоже его дочерний объект, этот список его не включает

        public static Bus Create(Transform parent, Vector3 pos)
        {
            GameObject go = new GameObject("Bus");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Bus b = go.AddComponent<Bus>();
            b.Setup(pos);
            return b;
        }

        void Setup(Vector3 pos)
        {
            Instance = this;
            startPos = pos;
            Transform t = transform;

            // Кузов целиком — один меш (борта, окна, колёса, фары, зеркала, крыша); столкновение — одна коробка
            MeshBatch bb = new MeshBatch();
            Models.BusBody(bb);
            Mesh bm = bb.Build("PG_Bus");
            LevelBuilder.TrackMesh(bm);
            GameObject bodyGo = Gfx.MeshObject(t, bm, Gfx.Lit(Color.white), "BusBody");
            bodyGo.transform.localPosition = Vector3.zero;
            GameObject col = Gfx.StaticBox(t, pos + new Vector3(0f, 1.75f, 0f), new Vector3(2.7f, 2.9f, 11f), "BusCollider");
            col.transform.SetParent(t, true);

            lampMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(0.2f, 0.2f, 0.18f, 1f), null, false, false, 0.2f));
            Gfx.Prim(PrimitiveType.Sphere, t, new Vector3(-0.9f, 1.0f, 5.52f), new Vector3(0.4f, 0.4f, 0.4f), lampMat, false);
            Gfx.Prim(PrimitiveType.Sphere, t, new Vector3(0.9f, 1.0f, 5.52f), new Vector3(0.4f, 0.4f, 0.4f), lampMat, false);
            haloMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(0.9f, 0.95f, 1f, 0f), Gfx.TexHalo, true, false, 0.1f));
            Gfx.Sprite(t, new Vector3(0f, 1.1f, 6.2f), new Vector2(7f, 7f), haloMat, false);

            // шкала топлива: светящаяся полоса вдоль борта (видна издалека, растёт по мере зарядки)
            gaugeMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(0.15f, 0.3f, 0.3f, 1f), null, false, false, 0.1f));
            GameObject gauge = Gfx.Prim(PrimitiveType.Cube, t, new Vector3(-1.372f, 1.62f, 0f), new Vector3(0.03f, 0.13f, 9.9f), gaugeMat, false);
            gauge.name = "FuelGauge";

            // кольцо на земле у двери: «встань сюда»
            ringMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(1f, 0.9f, 0.55f, 0.75f), Gfx.TexRing, true, false, 0.1f));
            GameObject r = Gfx.Prim(PrimitiveType.Quad, null, DoorPos + new Vector3(0f, 0.06f, 0f), new Vector3(4.2f, 4.2f, 1f), ringMat, false);
            r.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            r.transform.SetParent(t.parent, true);
            r.name = "BusRing";
            ring = r.transform;

            engine = ProcAudio.Source(gameObject, "engine", true, 0f, true);
            LightField.Register(this);
            bodyParts = GetComponentsInChildren<Renderer>(true);
            ExteriorVisible = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            LightField.Unregister(this);
            if (ring != null) Destroy(ring.gameObject);
        }

        public float DistanceTo(Vector3 p)
        {
            Vector3 d = DoorPos - p;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>Герой выдыхает свет в двигатель.</summary>
        public void Feed(float amount)
        {
            if (State != Mode.Idle) return;
            Charge += amount;
            if (Charge >= GameConfig.BusNeed) StartEngine();
        }

        public void OnBoarded(Survivor s)
        {
            BoardedCount++;
        }

        void StartEngine()
        {
            if (State != Mode.Idle) return;
            State = Mode.Running;
            StandLength = GameConfig.FinalTime;
            FinalLeft = StandLength;
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Notify("Автобус заведён! Продержитесь минуту, пока люди садятся");
                if (gm.Dir != null) gm.Dir.StartFinal();
            }
            for (int i = 0; i < Survivor.All.Count; i++) Survivor.All[i].BeginBoarding(DoorPos);
        }

        void Leave()
        {
            State = Mode.Leaving;
            leaveT = 0f;
            Enemy boss = Enemy.FindBoss();
            if (boss != null) boss.BossDepart();   // босс не успел погибнуть: автобус уехал, он остался позади
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            gm.Notify("Автобус отправляется");
            // классика: все сели и автобус тронулся — начинается концовка (в бесконечном городе победа приходит через 3.6 с, как раньше)
            if (!gm.Endless) gm.Win();
        }

        /// <summary>
        /// Концовка: автобус снова у вокзала (людей он уже принял), дальше его ведёт режиссёр. Нет ни шкалы топлива, ни кольца у двери.
        /// fromOutside: концовка начинается с того, что герой бежит к двери, значит, кузов снаружи и фары нужны, пока он не вошёл
        /// (HideExterior). Без этого камера сразу в салоне и кузов не рисуется.
        /// </summary>
        public void BeginCinematic(bool fromOutside = false)
        {
            Scripted = true;
            State = Mode.Leaving;
            transform.position = startPos;
            if (engine != null) AudioMix.SetVolume(engine, 0f);
            if (ring != null) ring.gameObject.SetActive(false);
            CineLight = fromOutside ? 1f : 0f;
            haloMat.SetColor("_Color", new Color(0.9f, 0.95f, 1f, fromOutside ? 0.55f : 0f));
            // Камера едет внутри: кузов с его гранями наружу изнутри не нужен (верх нижнего короба висел бы «вторым полом» на 1.6 м)
            SetBodyVisible(fromOutside);
        }

        /// <summary>Камера вошла в салон: кузов снаружи не рисуется (его грани смотрят наружу, а верх нижнего короба висел бы в салоне «вторым полом»).</summary>
        public void HideExterior()
        {
            if (ExteriorVisible) SetBodyVisible(false);
        }

        /// <summary>Камера снова снаружи (перемотка концовки назад в автотестах).</summary>
        public void ShowExterior()
        {
            if (!ExteriorVisible) SetBodyVisible(true);
        }

        void SetBodyVisible(bool visible)
        {
            ExteriorVisible = visible;
            if (bodyParts == null) return;
            for (int i = 0; i < bodyParts.Length; i++)
            {
                if (bodyParts[i] != null) bodyParts[i].enabled = visible;
            }
        }

        public void ResetToInitial()
        {
            if (Scripted) SetBodyVisible(true);
            Scripted = false;
            CineLight = 0f;
            State = Mode.Idle;
            Charge = 0f;
            BoardedCount = 0;
            leaveT = 0f;
            FinalLeft = 0f;
            StandLength = 0f;
            transform.position = startPos;
            if (engine != null) AudioMix.SetVolume(engine, 0f);
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Paused) return;
            float dt = Time.deltaTime;

            if (State == Mode.Leaving)
            {
                if (Scripted) return;
                leaveT += dt;
                transform.position += Vector3.forward * (leaveT * leaveT * 2.4f) * dt;
                AudioMix.SetVolume(engine, 0.8f);
                ring.gameObject.SetActive(false);
                if (leaveT > 3.6f && gm.State == GameState.Playing) gm.Win();
                return;
            }

            if (gm.State != GameState.Playing) return;

            float ratio = ChargeFrac;
            PlayerController pl = PlayerController.Instance;
            float pd = pl != null ? DistanceTo(pl.transform.position) : 999f;

            if (State == Mode.Idle)
            {
                if (Charge > 0.01f)
                {
                    // Спасённые рядом подпитывают двигатель сами.
                    for (int i = 0; i < Survivor.All.Count; i++)
                    {
                        Survivor s = Survivor.All[i];
                        if (s.IsAwake && (s.transform.position - DoorPos).magnitude < 8f)
                            Charge += GameConfig.BusFeedFromSurvivor * dt;
                    }
                    if (Charge >= GameConfig.BusNeed) StartEngine();
                }
                lampMat.SetColor("_Color", new Color(0.2f + 0.7f * ratio, 0.2f + 0.7f * ratio, 0.18f + 0.65f * ratio, 1f));
                haloMat.SetColor("_Color", new Color(0.9f, 0.95f, 1f, 0.25f * ratio));
                gaugeMat.SetColor("_Color", new Color(0.15f + 0.85f * ratio, 0.3f + 0.6f * ratio, 0.3f + 0.1f * ratio, 1f));
                AudioMix.SetVolume(engine, 0.15f * ratio);
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 4f);
                ringMat.SetColor("_Color", new Color(1f, 0.9f, 0.55f, 0.35f + 0.5f * pulse));
                ring.localScale = new Vector3(4.2f + 0.5f * pulse, 4.2f + 0.5f * pulse, 1f);

                // подписи — только когда смотришь на автобус вблизи: название с 14 м, что делать — от двери (8 м)
                GameUI.Label(startPos + new Vector3(0f, 4.4f, 0f), "АВТОБУС", new Color(1f, 0.9f, 0.6f, 1f), 14f,
                    startPos + new Vector3(0f, 1.6f, 0f), 0.80f);
                string hint = ratio > 0.01f
                    ? "Двигатель " + Mathf.RoundToInt(ratio * 100f) + "%: держи F"
                    : "Встань в кольцо и держи F: отдай автобусу свет";
                GameUI.Label(startPos + new Vector3(-1.5f, 3.6f, 0f), hint, new Color(1f, 0.95f, 0.8f, 1f), 8f,
                    startPos + new Vector3(-1.4f, 1.6f, 1.5f), 0.86f);
            }
            else if (State == Mode.Running)
            {
                FinalLeft -= dt;
                lampMat.SetColor("_Color", new Color(1f, 1f, 0.92f, 1f));
                haloMat.SetColor("_Color", new Color(0.9f, 0.95f, 1f, 0.55f));
                gaugeMat.SetColor("_Color", new Color(1f, 0.95f, 0.7f, 1f));
                AudioMix.SetVolume(engine, 0.6f);
                ringMat.SetColor("_Color", new Color(0.6f, 1f, 0.7f, 0.5f));

                // Минута идёт целиком: автобус не уезжает раньше, даже когда все уже сели. Тех, кого разбудили уже во время
                // обороны, тоже ведём к двери (BeginBoarding трогает только идущих за героем).
                for (int i = 0; i < Survivor.All.Count; i++) Survivor.All[i].BeginBoarding(DoorPos);
                if (FinalLeft <= 0f) Leave();
            }
        }

        // ------------------------------------------------------------- ILightSource

        public Vector3 LightPosition { get { return transform.position + new Vector3(0f, 1.4f, 6.3f); } }

        public float LightRadius
        {
            get
            {
                if (State == Mode.Idle)
                {
                    if (Charge <= 0.01f) return 0f;
                    return 3f + 5f * Mathf.Clamp01(Charge / GameConfig.BusNeed);
                }
                return Scripted ? Mathf.Lerp(10f, 22f, CineLight) : 22f;   // в концовке камера внутри: фары рядом с салоном были бы слишком яркими
            }
        }

        public float LightIntensity
        {
            get
            {
                if (State == Mode.Idle) return 0.3f * Mathf.Clamp01(Charge / GameConfig.BusNeed);
                return Scripted ? Mathf.Lerp(0.35f, 1.4f, CineLight) : 1.4f;
            }
        }

        public Color LightColor { get { return new Color(0.92f, 0.96f, 1f, 1f); } }
    }
}
