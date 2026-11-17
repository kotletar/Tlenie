using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Выживший. Сначала "потухший": еле тлеющий уголёк, неподвижен. Герой выдыхает на него свет и будит.
    /// Проснувшийся идёт за героем и держится внутри света (Тьма вне света его съедает),
    /// греет героя (резонанс), а если свет героя погаснет — может раздуть его обратно.
    /// Здоровье человека читается по его свечению, не по интерфейсу.
    /// </summary>
    public class Survivor : MonoBehaviour, ILightSource
    {
        public enum Mode { Asleep, Following, Boarding, Boarded, Dead }

        public static readonly List<Survivor> All = new List<Survivor>();

        public Mode State = Mode.Asleep;
        public float Health;
        public float Invested;      // сколько света герой вложил (долг, который вернётся)
        public float WakeProgress;
        public Vector3 SpawnPos;
        public float Progress;      // доля пути до автобуса в точке, где стоит человек

        public bool IsAwake { get { return State == Mode.Following; } }
        public bool IsAsleep { get { return State == Mode.Asleep; } }
        public Vector3 FocusPoint { get { return transform.position + Vector3.up * 1.2f; } }

        System.Collections.Generic.List<Pt> path;
        int pathIdx;
        float repathT;

        CharacterController cc;
        Transform visualRoot;
        Transform torso;
        Transform legL;
        Transform legR;
        Transform armL;
        Transform armR;
        GameObject eyesOpen;
        GameObject eyesClosed;
        float walkAmt;        // 0 — стоит, 1 — идёт
        float walkPhase;
        Transform coreT;
        Material coreMat;
        Material haloMat;
        AudioSource voice;
        float seed;
        float followAngle;
        float followDist;
        float hurtFlash;
        Vector3 doorPos;

        /// <summary>Слой людей: «Ignore Raycast» (выстрелы и лучи проходят сквозь них). Герой их не задевает: его CharacterController исключает этот слой.</summary>
        public const int Layer = 2;

        public static Survivor Create(Transform parent, Vector3 pos, int index)
        {
            GameObject go = new GameObject("Survivor" + index);
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.transform.position = pos + Vector3.up * 0.1f;
            CharacterController c = go.AddComponent<CharacterController>();
            c.height = 1.7f;
            c.radius = 0.28f;
            c.center = new Vector3(0f, 0.85f, 0f);
            c.stepOffset = 0.25f;
            Survivor s = go.AddComponent<Survivor>();
            s.Setup(index, pos, c);
            return s;
        }

        void Setup(int index, Vector3 pos, CharacterController c)
        {
            cc = c;
            SpawnPos = pos + Vector3.up * 0.1f;
            Progress = CityMap.Progress(pos.x, pos.z);
            seed = index * 7.31f;
            followAngle = (index % 5) * 1.3f - 2.6f;
            followDist = 2.2f + (index % 3) * 0.6f;

            visualRoot = new GameObject("Visual").transform;
            visualRoot.SetParent(transform, false);

            // Человек из частей, чтобы он мог идти: туловище с головой, две ноги (качаются в бёдрах), две руки (в плечах),
            // глаза открытые и закрытые (потухший спит). Цвета в вершинах, у каждой части один материал.
            torso = MakePart(visualRoot, "Torso", delegate(MeshBatch b) { Models.HumanBody(b, index); }, Vector3.zero, index);
            legL = MakePart(visualRoot, "LegL", delegate(MeshBatch b) { Models.HumanLeg(b, index); }, new Vector3(-0.1f, 0.84f, 0f), index);
            legR = MakePart(visualRoot, "LegR", delegate(MeshBatch b) { Models.HumanLeg(b, index); }, new Vector3(0.1f, 0.84f, 0f), index);
            armL = MakePart(torso, "ArmL", delegate(MeshBatch b) { Models.HumanArm(b, index); }, new Vector3(-0.33f, 1.38f, 0f), index);
            armR = MakePart(torso, "ArmR", delegate(MeshBatch b) { Models.HumanArm(b, index); }, new Vector3(0.33f, 1.38f, 0f), index);
            eyesOpen = MakePart(torso, "EyesOpen", delegate(MeshBatch b) { Models.HumanEyes(b, index, true); }, Vector3.zero, index).gameObject;
            eyesClosed = MakePart(torso, "EyesClosed", delegate(MeshBatch b) { Models.HumanEyes(b, index, false); }, Vector3.zero, index).gameObject;

            coreMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(0.5f, 0.1f, 0.03f, 1f), null, false, false, 0.4f));
            coreT = Gfx.Prim(PrimitiveType.Sphere, visualRoot, new Vector3(0f, 1.12f, 0.2f), new Vector3(0.1f, 0.1f, 0.1f), coreMat, false).transform;
            haloMat = OwnedResources.Keep(gameObject, Gfx.Unlit(new Color(1f, 0.8f, 0.45f, 0.4f), Gfx.TexHalo, true, false, 0.2f));
            Gfx.Sprite(visualRoot, new Vector3(0f, 1.1f, 0f), new Vector2(2.6f, 2.6f), haloMat, false);

            voice = ProcAudio.Source(gameObject, "wake", false, 1f, true);

            SetPosture();
            All.Add(this);
            LightField.Register(this);
        }

        /// <summary>Часть тела человека: свой меш (его строит build), материал общий, начало — в суставе pivot.</summary>
        static Transform MakePart(Transform parent, string name, System.Action<MeshBatch> build, Vector3 pivot, int index)
        {
            MeshBatch hb = new MeshBatch();
            build(hb);
            Mesh hm = hb.Build("PG_Human_" + name + index);
            LevelBuilder.TrackMesh(hm);
            GameObject go = Gfx.MeshObject(parent, hm, Gfx.Lit(Color.white), name);
            go.transform.localPosition = pivot;
            return go.transform;
        }

        void OnDestroy()
        {
            All.Remove(this);
            LightField.Unregister(this);
        }

        /// <summary>Анимация ходьбы: ноги и руки качаются в противофазе, туловище чуть подпрыгивает и наклоняется вперёд.</summary>
        void UpdateAnim(float dt)
        {
            Vector3 v = cc != null ? cc.velocity : Vector3.zero;
            float speed = new Vector2(v.x, v.z).magnitude;
            bool moving = (State == Mode.Following || State == Mode.Boarding) && speed > 0.25f;
            float target = moving ? Mathf.Clamp01(speed / 2.6f) : 0f;
            walkAmt = Mathf.MoveTowards(walkAmt, target, dt * 6f);
            if (walkAmt > 0.02f) walkPhase += dt * (6.5f + 3.2f * Mathf.Clamp01(speed / GameConfig.SurvivorSpeed));
            float sw = Mathf.Sin(walkPhase) * walkAmt;
            float idle = 1f - walkAmt;
            float breath = Mathf.Sin(Time.time * 1.5f + seed);
            legL.localRotation = Quaternion.Euler(sw * 36f, 0f, 0f);
            legR.localRotation = Quaternion.Euler(-sw * 36f, 0f, 0f);
            // рука идёт в противофазе с ногой с той же стороны
            armL.localRotation = Quaternion.Euler(-sw * 30f, 0f, 3f + breath * 1.5f * idle);
            armR.localRotation = Quaternion.Euler(sw * 30f, 0f, -3f - breath * 1.5f * idle);
            torso.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(walkPhase)) * 0.045f * walkAmt + breath * 0.004f * idle, 0f);
            torso.localRotation = Quaternion.Euler(4f * walkAmt, Mathf.Sin(walkPhase) * 5f * walkAmt, 0f);
        }

        /// <summary>Предыстория: на улице перед блэкаутом никто не стоит «потухшим» — прячем людей на время вида на улицу.</summary>
        public static void IntroHideAll(bool hide)
        {
            for (int i = 0; i < All.Count; i++)
            {
                Survivor s = All[i];
                if (s == null || s.visualRoot == null) continue;
                if (s.State == Mode.Boarded) continue;
                s.visualRoot.gameObject.SetActive(!hide);
            }
        }

        /// <summary>Переставить человека при сборке города: он оказался внутри предмета (машины, ящика).</summary>
        public void Relocate(Vector3 p)
        {
            cc.enabled = false;
            transform.position = p + Vector3.up * 0.1f;
            cc.enabled = true;
            SpawnPos = p + Vector3.up * 0.1f;
            Progress = CityMap.Progress(p.x, p.z);
        }

        // ------------------------------------------------------------- события

        public void Wake()
        {
            if (State != Mode.Asleep) return;
            State = Mode.Following;
            Health = GameConfig.SurvivorWakeHealth;
            WakeProgress = 1f;
            SetPosture();
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.OnSurvivorWoken(this);
            ProcAudio.OneShot(voice, "wake", 0.9f, 1f);
            TimedFx.Spawn(LightPosition, 1f, 8f, new Color(1f, 0.85f, 0.5f, 0.8f), 0.5f);
        }

        /// <summary>Урон от Тьмы и врагов.</summary>
        public void Hurt(float dmg)
        {
            if (State != Mode.Following && State != Mode.Boarding) return;
            Health -= dmg;
            hurtFlash = 0.15f;
            if (Health <= 0f) Die();
        }

        public void Heal(float hp)
        {
            if (State != Mode.Following) return;
            Health = Mathf.Min(GameConfig.SurvivorMaxHealth, Health + hp);
        }

        /// <summary>Человек отдаёт свет (например, чтобы раздуть героя обратно).</summary>
        public void GiveLight(float amount)
        {
            Health = Mathf.Max(1f, Health - amount);
            Invested = 0f;
        }

        /// <summary>Пожиратель проглотил.</summary>
        public void Devour()
        {
            if (State != Mode.Following && State != Mode.Boarding) return;
            Health = 0f;
            Die();
        }

        void Die()
        {
            if (State == Mode.Dead) return;
            State = Mode.Dead;
            Health = 0f;
            SetPosture();
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.OnSurvivorDied(this);
            LightOrb.Spawn(transform.position, 10f);
        }

        public void BeginBoarding(Vector3 door)
        {
            if (State != Mode.Following) return;
            State = Mode.Boarding;
            doorPos = door;
        }

        public void ResetToInitial()
        {
            State = Mode.Asleep;
            Health = 0f;
            Invested = 0f;
            WakeProgress = 0f;
            MoveTo(SpawnPos);
            SetVisible(true);
            SetPosture();
        }

        public void ReviveAt(Vector3 pos)
        {
            State = Mode.Following;
            Health = Mathf.Max(60f, Health);
            MoveTo(pos);
            SetVisible(true);
            SetPosture();
        }

        void MoveTo(Vector3 pos)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
        }

        void SetVisible(bool v)
        {
            visualRoot.gameObject.SetActive(v);
            cc.enabled = v;
            if (v) LightField.Register(this);
            else LightField.Unregister(this);
        }

        void SetPosture()
        {
            // глаза: у потухшего и погибшего закрыты
            bool awake = State == Mode.Following || State == Mode.Boarding || State == Mode.Boarded;
            if (eyesOpen != null) eyesOpen.SetActive(awake);
            if (eyesClosed != null) eyesClosed.SetActive(!awake);
            if (State == Mode.Dead)
            {
                visualRoot.localRotation = Quaternion.Euler(86f, 0f, 0f);
                visualRoot.localPosition = new Vector3(0f, 0.12f, 0f);
            }
            else if (State == Mode.Asleep)
            {
                visualRoot.localRotation = Quaternion.Euler(14f, 0f, 0f);
                visualRoot.localPosition = Vector3.zero;
            }
            else
            {
                visualRoot.localRotation = Quaternion.identity;
                visualRoot.localPosition = Vector3.zero;
            }
        }

        // ------------------------------------------------------------- кадр

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            float dt = Time.deltaTime;
            UpdateVisual();
            UpdateAnim(dt);
            // подпись «потухший» — только когда смотришь на человека с расстояния, с которого его можно разбудить (3 м)
            if (State == Mode.Asleep)
                GameUI.Label(transform.position + Vector3.up * 2.2f, "потухший", new Color(1f, 0.45f, 0.3f, 1f), 3.8f,
                    transform.position + Vector3.up * 1.1f, 0.94f);
            if (gm.State != GameState.Playing || gm.Paused) return;
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, dt);

            if (State == Mode.Following) UpdateFollowing(dt, gm);
            else if (State == Mode.Boarding) UpdateBoarding(dt);
        }

        void UpdateFollowing(float dt, GameManager gm)
        {
            PlayerController pl = PlayerController.Instance;
            if (pl == null) return;
            Vector3 hp = pl.transform.position;
            Vector3 pos = transform.position;
            Vector3 toHero = hp - pos;
            toHero.y = 0f;
            float d = toHero.magnitude;

            float speed = GameConfig.SurvivorSpeed;
            if (d > 9f) speed *= 1.5f;
            if (d > 48f)
            {
                // Отстал слишком сильно — догоняет "через тьму".
                MoveTo(hp - pl.transform.forward * 3f + Vector3.up * 0.1f);
                path = null;
                return;
            }

            // Идёт за героем по дорогам: напрямую, если видно, иначе по найденному пути (углы, переулки).
            Vector3 goal = hp;
            bool direct = CityMap.LineClear(pos.x, pos.z, hp.x, hp.z, 0.45f, true);
            if (direct)
            {
                path = null;
                Vector3 anchor = hp - pl.transform.forward * (followDist * 0.6f) + pl.transform.right * Mathf.Sin(followAngle) * (followDist * 0.8f);
                if (CityMap.LineClear(pos.x, pos.z, anchor.x, anchor.z, 0.45f, true)) goal = anchor;
            }
            else
            {
                repathT -= dt;
                if (path == null || repathT <= 0f)
                {
                    repathT = 0.6f;
                    path = CityMap.FindPath(new Pt(pos.x, pos.z), new Pt(hp.x, hp.z), false, 0f);
                    pathIdx = 1;
                }
                if (path != null && path.Count > 1)
                {
                    if (pathIdx >= path.Count) pathIdx = path.Count - 1;
                    Vector3 wp = new Vector3(path[pathIdx].X, 0f, path[pathIdx].Z);
                    Vector3 toWp = wp - pos;
                    toWp.y = 0f;
                    if (toWp.magnitude < 1.3f && pathIdx < path.Count - 1) pathIdx++;
                    goal = new Vector3(path[pathIdx].X, 0f, path[pathIdx].Z);
                }
            }
            Vector3 toGoal = goal - pos;
            toGoal.y = 0f;
            float dg = toGoal.magnitude;
            Vector3 vel = Vector3.zero;
            if (d > 2.3f && dg > 0.6f) vel = toGoal.normalized * speed * Mathf.Clamp01(dg / 2f + 0.3f);
            cc.SimpleMove(vel);

            Vector3 face = vel.sqrMagnitude > 0.01f ? vel : toHero;
            if (face.sqrMagnitude > 0.01f)
            {
                Quaternion want = Quaternion.LookRotation(face.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-8f * dt));
            }

            float h = Mathf.Clamp01(Health / GameConfig.SurvivorMaxHealth);

            // Резонанс: спасённый рядом греет героя.
            if (d < Mathf.Max(4f, pl.Hero.LightRadius))
            {
                int awake = 0;
                for (int i = 0; i < All.Count; i++) if (All[i].IsAwake) awake++;
                float groupBonus = 1f + 0.08f * Mathf.Max(0, awake - 1);
                pl.Hero.Gain(GameConfig.ResonancePerSec * GameDifficulty.ResonanceScale * dt * h * groupBonus, LightReason.Resonance);
            }

            // Тьма вне света ест человека; у очага он отдыхает.
            bool inHearth = gm.Dir != null && gm.Dir.IsInHearth(pos);
            if (inHearth)
            {
                Heal(GameConfig.HearthHealPerSec * dt);
            }
            else
            {
                float b = LightField.Sample(pos + Vector3.up * 1.0f, this);
                if (b < GameConfig.DarkThreshold) Hurt(GameConfig.DarknessDps * dt);
                else Heal(GameConfig.SurvivorRegenPerSec * dt);   // в свете человек понемногу приходит в себя
            }
        }

        void UpdateBoarding(float dt)
        {
            Vector3 to = doorPos - transform.position;
            to.y = 0f;
            if (to.magnitude < 1.4f)
            {
                State = Mode.Boarded;
                SetVisible(false);
                if (Bus.Instance != null) Bus.Instance.OnBoarded(this);
                return;
            }
            cc.SimpleMove(to.normalized * 5f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-10f * dt));
        }

        void UpdateVisual()
        {
            float h = Mathf.Clamp01(Health / GameConfig.SurvivorMaxHealth);
            if (State == Mode.Asleep)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 1.7f + seed);
                coreMat.SetColor("_Color", new Color(0.55f * pulse, 0.10f * pulse, 0.03f * pulse, 1f));
                coreT.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                haloMat.SetColor("_Color", new Color(0.9f, 0.22f, 0.06f, 0.18f * pulse));
            }
            else if (State == Mode.Dead)
            {
                coreMat.SetColor("_Color", new Color(0.05f, 0.03f, 0.03f, 1f));
                haloMat.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            }
            else
            {
                float flick = 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 8f + seed, seed);
                if (hurtFlash > 0f) flick *= 0.5f;
                Color c = Color.Lerp(new Color(0.8f, 0.2f, 0.06f, 1f), new Color(1f, 0.82f, 0.48f, 1f), h) * flick;
                c.a = 1f;
                coreMat.SetColor("_Color", c);
                float s = 0.09f + 0.10f * h;
                coreT.localScale = new Vector3(s, s, s);
                haloMat.SetColor("_Color", new Color(c.r, c.g, c.b, 0.15f + 0.4f * h));
            }
        }

        // ------------------------------------------------------------- ILightSource

        public Vector3 LightPosition { get { return transform.position + Vector3.up * 1.2f; } }

        public float LightRadius
        {
            get
            {
                if (State == Mode.Dead || State == Mode.Boarded) return 0f;
                if (State == Mode.Asleep) return 0.9f;
                return 3.2f + 3f * Mathf.Clamp01(Health / GameConfig.SurvivorMaxHealth);
            }
        }

        public float LightIntensity
        {
            get
            {
                if (State == Mode.Dead || State == Mode.Boarded) return 0f;
                if (State == Mode.Asleep) return 0.12f;
                float h = Mathf.Clamp01(Health / GameConfig.SurvivorMaxHealth);
                return 0.2f + 0.45f * h;
            }
        }

        public Color LightColor
        {
            get
            {
                if (State == Mode.Asleep) return new Color(1f, 0.3f, 0.1f, 1f);
                return new Color(1f, 0.8f, 0.5f, 1f);
            }
        }
    }
}
