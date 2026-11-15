using System;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Цель, по которой попадает луч (интерфейс из PalmShot.cs). Её реализуют враги, стена Тьмы и ложные огни.</summary>
    public interface ILightTarget
    {
        void OnLightHit(float damage);
    }

    /// <summary>Цель, которую можно ослепить вспышкой (ПКМ).</summary>
    public interface IFlashTarget
    {
        void OnFlash(float stunTime);
    }

    /// <summary>
    /// PalmShot.cs из ссылки: руки с лучами света.
    ///  ЛКМ нажал и отпустил: короткий выстрел двумя лучами из ладоней.
    ///  ЛКМ зажал: лучи сходятся в один толстый и бьют, пока держишь кнопку.
    ///  ПКМ: вспышка света вокруг игрока.
    /// Поля, фазы, тайминги и формы эффектов (лучи из двух кубов, вращающийся шар, крестовая вспышка, отдача рук, тряска
    /// камеры, белая вспышка экрана) оставлены как в оригинале. Пальцы раскрываются так же: baseRot * AngleAxis(openAngle * open, jointAxis).
    /// Что подогнано под игру: ввод через GameInput, расход идёт из жизненного света героя (HeroLight), цели находятся
    /// без физики (враги, стена Тьмы, ложные огни реализуют ILightTarget), вместо Unity-света ладони светят через LightField,
    /// а белая вспышка экрана рисуется в GameUI (в проекте нет пакета uGUI).
    /// Руки (ладонь-пустышка и суставы пальцев) строит HeroBody под этот контракт.
    /// </summary>
    public class PalmShot : MonoBehaviour
    {
        [Serializable]
        public class FingerJoint
        {
            public Transform t;
            public float openAngle = 15f;
            [HideInInspector] public Quaternion baseRot = Quaternion.identity;
        }

        /// <summary>Свет ладони для шейдеров игры (в оригинале это Unity Point Light).</summary>
        public class PalmLight : ILightSource
        {
            public Vector3 Pos;
            public float Intensity;
            public Vector3 LightPosition { get { return Pos; } }
            public float LightRadius { get { return Intensity > 0.02f ? 5f + Intensity * 4f : 0f; } }
            public float LightIntensity { get { return Intensity; } }
            public Color LightColor { get { return new Color(1f, 0.91f, 0.63f, 1f); } }
        }

        [Serializable]
        public class HandFx
        {
            public Transform palm;          // пустышка в центре ладони, её +Z смотрит от ладони
            public FingerJoint[] joints;

            [NonSerialized] public Transform beamRoot, beamOuter, beamCore;
            [NonSerialized] public Transform orb, orbOuter, orbInner;
            [NonSerialized] public Transform flare;
            [NonSerialized] public PalmLight glow;
        }

        enum Mode { None, Shot, Flash }
        enum ShotPhase { Charge, Beam, Fade }

        [Header("Руки")]
        public Transform handsRoot;
        public HandFx leftHand;
        public HandFx rightHand;
        public Vector3 palmOffset = new Vector3(0f, 0f, 0.08f);
        public Vector3 jointAxis = Vector3.right;

        [Header("Камера и прицел")]
        public Camera cam;
        public LayerMask hitMask = ~0;
        public float maxRange = GameConfig.CombatRange;

        [Header("Управление")]
        public int shotButton = 0;     // ЛКМ
        public int flashButton = 1;    // ПКМ

        [Header("Жизненный свет (доля от максимума)")]
        public float shotCost = GameConfig.ShotCost / GameConfig.MaxLight;                    // 0.05
        public float flashCost = GameConfig.NovaCost / GameConfig.MaxLight;                   // 0.2
        public float beamDrainPerSecond = GameConfig.BeamDrainPerSec / GameConfig.MaxLight;   // 0.08, пока держишь ЛКМ

        [Header("Выстрел и непрерывный луч (ЛКМ)")]
        public float shotDamage = GameConfig.ShotDamage;
        public float holdDamagePerSecond = GameConfig.HoldDps;
        public float mergeTime = GameConfig.MergeTime;                    // за сколько секунд лучи сливаются
        public float mergedWidthMultiplier = GameConfig.MergedWidthMul;   // насколько толще слитый луч

        [Header("Вспышка (ПКМ)")]
        public float flashRadius = GameConfig.NovaRadius;
        public float flashDamage = GameConfig.NovaDamage;
        public float flashStunTime = GameConfig.NovaStun;

        [Header("Размеры эффектов")]
        public float beamWidth = 0.08f;
        public float beamCoreWidth = 0.03f;
        public float orbSize = 0.12f;
        public float flareSize = 0.5f;
        public float lightIntensity = 0.45f;   // для шейдеров игры (у Unity-лампы в оригинале 1.5)

        [Header("Отдача и тряска")]
        public float recoilDistance = 0.08f;
        public float recoilAngle = 6f;
        public float shakeAmount = 0.03f;

        public event Action<Vector3> BeamHit;
        public event Action FlashFired;

        /// <summary>Только для автотестов: считать ЛКМ зажатой (слитый луч).</summary>
        public bool ForceHold;

        // Выходные значения для игры
        public float ScreenFlash;          // 0..0.9: белая вспышка экрана (рисует GameUI)
        public float PoseWeight;           // 0..1: насколько руки в боевой позе
        public Vector3 ShakeOffset;        // тряска камеры (применяет PlayerController)

        public bool Busy { get { return mode != Mode.None; } }

        static readonly Color Yellow = new Color(1f, 0.91f, 0.63f, 0.6f);
        static readonly Color White = Color.white;
        const float TickInterval = 0.1f;

        PlayerController pl;
        HeroLight hero;
        AudioSource sfx;
        AudioSource beamLoop;

        Mode mode = Mode.None;
        ShotPhase phase;
        float ft, fadeStart, mergeT, tick;
        bool started;
        Vector3 aimPoint;
        float aimDist = 30f;
        Vector3 handsBasePos;
        Quaternion handsBaseRot = Quaternion.identity;
        float lowHint;

        // ------------------------------------------------------------------ создание

        public void Init(PlayerController pc, HeroBody body)
        {
            pl = pc;
            hero = pc.Hero;
            cam = pc.Cam;
            handsRoot = body.HandsRoot;
            leftHand = body.Hands[0].Fx;
            rightHand = body.Hands[1].Fx;

            sfx = ProcAudio.Source(gameObject, "flash", false, 1f, false);
            beamLoop = ProcAudio.Source(gameObject, "beam", true, 0f, false);

            handsBasePos = handsRoot.localPosition;
            handsBaseRot = handsRoot.localRotation;

            Setup(leftHand);
            Setup(rightHand);
            SetupExtraBeams();
            RegisterLights();
        }

        // ------------------------------------------------------------------ отражённые участки луча (зеркала хранилищ света)

        const int ExtraBeams = 6;
        Transform[] extraRoot, extraOuter, extraCore;

        void SetupExtraBeams()
        {
            Material yel = OwnedResources.Keep(gameObject, Gfx.Unlit(Yellow, null, false, true, 0f));
            Material wht = OwnedResources.Keep(gameObject, Gfx.Unlit(White, null, false, true, 0f));
            extraRoot = new Transform[ExtraBeams];
            extraOuter = new Transform[ExtraBeams];
            extraCore = new Transform[ExtraBeams];
            for (int i = 0; i < ExtraBeams; i++)
            {
                extraRoot[i] = new GameObject("BeamLeg" + i).transform;
                extraRoot[i].SetParent(transform, false);
                extraOuter[i] = NewCube("Outer", extraRoot[i], yel);
                extraCore[i] = NewCube("Core", extraRoot[i], wht);
                extraRoot[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Участки луча после зеркал: ладони рисуют только прямую часть до первого зеркала, дальше рисуем сами (толще после линзы).</summary>
        void UpdateExtraBeams(float lenF, float th)
        {
            if (extraRoot == null) return;
            int used = 0;
            if (mode == Mode.Shot && lenF > 0.98f && th > 0.01f && BeamTrace.Count > 1)
            {
                float widthMul = Mathf.Lerp(1f, mergedWidthMultiplier, mergeT);
                for (int s = 0; s < BeamTrace.Count && used < ExtraBeams; s++)
                {
                    BeamSeg seg = BeamTrace.Segs[s];
                    if (!seg.Reflected) continue;
                    Transform r = extraRoot[used];
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    r.position = seg.O;
                    r.rotation = Quaternion.LookRotation(seg.D);
                    float boost = seg.Focus > 1.5f ? 1.35f : 1f;
                    SetBeam(extraOuter[used], beamWidth * th * widthMul * boost, seg.Len);
                    SetBeam(extraCore[used], beamCoreWidth * th * widthMul * boost, seg.Len);
                    used++;
                }
            }
            for (int i = used; i < ExtraBeams; i++)
            {
                if (extraRoot[i].gameObject.activeSelf) extraRoot[i].gameObject.SetActive(false);
            }
        }

        public void RegisterLights()
        {
            if (leftHand != null && leftHand.glow != null) LightField.Register(leftHand.glow);
            if (rightHand != null && rightHand.glow != null) LightField.Register(rightHand.glow);
        }

        void OnDestroy()
        {
            if (leftHand != null && leftHand.glow != null) LightField.Unregister(leftHand.glow);
            if (rightHand != null && rightHand.glow != null) LightField.Unregister(rightHand.glow);
        }

        void Setup(HandFx h)
        {
            if (h == null || h.palm == null) return;

            Material yel = Gfx.Unlit(Yellow, null, false, true, 0f);
            Material wht = Gfx.Unlit(White, null, false, true, 0f);

            h.beamRoot = new GameObject("Beam").transform;
            h.beamRoot.SetParent(transform, false);
            h.beamOuter = NewCube("Outer", h.beamRoot, yel);
            h.beamCore = NewCube("Core", h.beamRoot, wht);
            h.beamRoot.gameObject.SetActive(false);

            h.orb = new GameObject("Orb").transform;
            h.orb.SetParent(transform, false);
            h.orbOuter = NewCube("Outer", h.orb, yel);
            h.orbOuter.localScale = Vector3.one * orbSize;
            h.orbOuter.localRotation = Quaternion.Euler(40f, 40f, 0f);
            h.orbInner = NewCube("Inner", h.orb, wht);
            h.orbInner.localScale = Vector3.one * orbSize * 0.5f;
            h.orb.gameObject.SetActive(false);

            h.flare = new GameObject("Flare").transform;
            h.flare.SetParent(transform, false);
            float[] angles = { 0f, 90f, 45f, -45f };
            for (int i = 0; i < angles.Length; i++)
            {
                Transform arm = NewCube("Arm" + i, h.flare, i < 2 ? wht : yel);
                arm.localRotation = Quaternion.Euler(0f, 0f, angles[i]);
                float len = i < 2 ? flareSize : flareSize * 0.7f;
                arm.localScale = new Vector3(len, 0.012f, 0.012f);
            }
            h.flare.gameObject.SetActive(false);

            h.glow = new PalmLight();
        }

        static Transform NewCube(string name, Transform parent, Material mat)
        {
            GameObject g = Gfx.Prim(PrimitiveType.Cube, parent, Vector3.zero, Vector3.one, mat, false);
            g.name = name;
            return g.transform;
        }

        /// <summary>Сбросить всё (смерть, перезапуск, пауза).</summary>
        public void CancelAll()
        {
            mode = Mode.None;
            mergeT = 0f;
            ScreenFlash = 0f;
            ShakeOffset = Vector3.zero;
            PoseWeight = 0f;
            if (handsRoot != null)
            {
                handsRoot.localPosition = handsBasePos;
                handsRoot.localRotation = handsBaseRot;
            }
            HandFx[] hs = { leftHand, rightHand };
            for (int i = 0; i < hs.Length; i++)
            {
                HandFx h = hs[i];
                if (h == null || h.beamRoot == null) continue;
                h.beamRoot.gameObject.SetActive(false);
                h.orb.gameObject.SetActive(false);
                h.flare.gameObject.SetActive(false);
                h.glow.Intensity = 0f;
            }
            if (beamLoop != null) AudioMix.SetVolume(beamLoop, 0f);
        }

        // ------------------------------------------------------------------ ввод

        void Update()
        {
            GameManager gm = GameManager.Instance;
            bool playing = gm != null && gm.State == GameState.Playing && !gm.Paused;
            if (!playing)
            {
                if (mode != Mode.None || ScreenFlash > 0f) CancelAll();
                return;
            }

            lowHint -= Time.deltaTime;
            bool blocked = hero.InLastBreath || hero.Covered || pl.Inhaling || pl.Exhaling;
            if (blocked)
            {
                if (mode != Mode.None) mode = Mode.None;
                return;
            }
            if (GameInput.MouseDown(shotButton)) FireShot();
            if (GameInput.MouseDown(flashButton)) FireFlash();
        }

        public void FireShot()
        {
            if (mode != Mode.None) return;
            if (hero.Life < shotCost * GameConfig.MaxLight + 1f)
            {
                NoLight();
                return;
            }
            hero.Spend(shotCost * GameConfig.MaxLight, LightReason.Shot);
            if (GameManager.Instance != null) GameManager.Instance.Think("shot", "Свет слушается руки и бьёт по Тьме. Выходит, это — оружие?.. Но он убавился.");
            mode = Mode.Shot;
            phase = ShotPhase.Charge;
            ft = 0f;
            mergeT = 0f;
            tick = 0f;
            started = false;
        }

        public void FireFlash()
        {
            if (mode != Mode.None) return;
            if (hero.Life < flashCost * GameConfig.MaxLight + 1f)
            {
                NoLight();
                return;
            }
            hero.Spend(flashCost * GameConfig.MaxLight, LightReason.Flash);
            if (GameManager.Instance != null) GameManager.Instance.Think("flash", "Вспышка! Тьма отпрянула. Но каждая такая вспышка — мой собственный свет.");
            mode = Mode.Flash;
            ft = 0f;
            started = false;
        }

        void NoLight()
        {
            if (lowHint > 0f) return;
            lowHint = 2f;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.Notify("Мало света: отдышись у лампы");
        }

        // ------------------------------------------------------------------ кадр (LateUpdate оригинала)

        /// <summary>Вызывается из HeroBody.LateUpdate после того, как руки заняли позу.</summary>
        public void Tick(float dt)
        {
            if (pl == null || handsRoot == null) return;
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Paused || gm.State != GameState.Playing) dt = 0f;

            float kick = 0f, lenF = 0f, th = 0f, orbS = 0f, li = 0f;
            float open = 0f, fl = 0f, screen = 0f, shake = 0f;

            if (mode == Mode.Shot)
            {
                ft += dt;
                bool held = (GameInput.MouseHeld(shotButton) || ForceHold) && hero.Life > 2f;

                if (phase == ShotPhase.Charge)
                {
                    if (ft < 0.1f)
                    {
                        float a = ft / 0.1f;
                        orbS = 0.6f * a;
                        kick = 0.1f * a;
                        li = a;
                        open = 0.4f * a;
                    }
                    else
                    {
                        phase = ShotPhase.Beam;
                    }
                }

                if (phase == ShotPhase.Beam)
                {
                    float u = ft - 0.1f;

                    if (ft >= 0.35f && !held)
                    {
                        phase = ShotPhase.Fade;
                        fadeStart = ft;
                    }
                    else
                    {
                        lenF = Mathf.Min(1f, u / 0.06f);
                        th = 1f + 0.1f * Mathf.Sin(ft * 90f);
                        orbS = 0.8f;
                        kick = 0.1f + 0.3f * Mathf.Exp(-u * 12f) + 0.02f * Mathf.Sin(ft * 70f);
                        li = 2.2f + mergeT;
                        shake = 0.04f + 0.02f * mergeT;
                        open = 0.6f + 0.2f * mergeT;

                        if (!started)
                        {
                            started = true;
                            DealShotDamage(shotDamage);
                            ProcAudio.OneShot(sfx, "flash", 0.6f, UnityEngine.Random.Range(1.15f, 1.3f));
                        }

                        if (u > 0.2f)
                        {
                            mergeT = Mathf.MoveTowards(mergeT, 1f, dt / Mathf.Max(0.01f, mergeTime));
                            hero.Spend(beamDrainPerSecond * GameConfig.MaxLight * dt, LightReason.Beam);
                            tick += dt;
                            while (tick >= TickInterval)
                            {
                                tick -= TickInterval;
                                DealShotDamage(holdDamagePerSecond * TickInterval);
                            }
                        }
                    }
                }

                if (phase == ShotPhase.Fade)
                {
                    float d = (ft - fadeStart) / 0.15f;
                    if (d >= 1f)
                    {
                        mode = Mode.None;
                        mergeT = 0f;
                    }
                    else
                    {
                        lenF = 1f;
                        th = Mathf.Max(0.01f, 1f - d);
                        orbS = 0.8f * (1f - d);
                        kick = 0.1f * (1f - d);
                        li = (2.2f + mergeT) * (1f - d);
                        open = (0.6f + 0.2f * mergeT) * (1f - d);
                    }
                }
            }
            else if (mode == Mode.Flash)
            {
                ft += dt;
                if (ft < 0.25f)
                {
                    float a = ft / 0.25f;
                    orbS = 1.6f * a;
                    kick = 0.2f * a;
                    li = 2f * a;
                    open = 0.5f * a;
                }
                else if (ft < 0.55f)
                {
                    float u = ft - 0.25f;
                    orbS = 1.2f + 1.3f * Mathf.Max(0f, 1f - u / 0.1f);
                    kick = 0.2f + 0.5f * Mathf.Exp(-u * 10f);
                    li = 3f + 6f * Mathf.Max(0f, 1f - u / 0.2f);
                    shake = 0.08f + 0.14f * Mathf.Max(0f, 1f - u / 0.25f);
                    open = 1f;
                    fl = Mathf.Max(0f, 1f - u / 0.2f);
                    screen = 0.9f * Mathf.Max(0f, 1f - u / 0.3f);
                    if (!started)
                    {
                        started = true;
                        DoFlash();
                    }
                }
                else if (ft < 0.9f)
                {
                    float d = (ft - 0.55f) / 0.35f;
                    orbS = 1.2f * (1f - d);
                    kick = 0.2f * (1f - d);
                    li = 3f * (1f - d);
                    open = 1f - d;
                }
                else
                {
                    mode = Mode.None;
                }
            }

            ScreenFlash = screen;
            PoseWeight = Mathf.MoveTowards(PoseWeight, mode != Mode.None ? 1f : 0f, dt * 9f);

            // отдача рук: корень рук сдвигается назад и чуть запрокидывается (как в оригинале)
            float k = kick / 0.65f;
            handsRoot.localPosition = handsBasePos + new Vector3(0f, 0f, -k * recoilDistance);
            handsRoot.localRotation = handsBaseRot * Quaternion.Euler(-k * recoilAngle, 0f, 0f);

            // тряска камеры: её применяет PlayerController
            if (shake > 0f)
            {
                Vector3 rnd = new Vector3(UnityEngine.Random.value - 0.5f, UnityEngine.Random.value - 0.5f, 0f);
                ShakeOffset = rnd * ((shake / 0.18f) * shakeAmount * 2f);
            }
            else
            {
                ShakeOffset = Vector3.zero;
            }

            UpdateAim();

            float power = Mathf.Lerp(0.5f, 1f, hero.Life01);
            Vector3 oL = leftHand.palm.TransformPoint(palmOffset);
            Vector3 oR = rightHand.palm.TransformPoint(palmOffset);
            Vector3 mid = (oL + oR) * 0.5f;

            ApplyHand(leftHand, oL, mid, false, lenF, th * power, orbS, li * power, open, fl);
            ApplyHand(rightHand, oR, mid, true, lenF, th * power, orbS, li * power, open, fl);
            UpdateExtraBeams(lenF, th * power);

            if (beamLoop != null)
            {
                float target = (mode == Mode.Shot && phase == ShotPhase.Beam && mergeT > 0.05f) ? 0.35f : 0f;
                AudioMix.SetVolume(beamLoop, Mathf.MoveTowards(AudioMix.Volume(beamLoop), target, Time.deltaTime * 6f));
            }
        }

        // ------------------------------------------------------------------ прицел и урон

        void UpdateAim()
        {
            if (cam == null) return;
            Transform ct = cam.transform;
            // путь луча: прямо до первого твёрдого или зеркала, дальше по зеркалам (BeamTrace); ладони рисуют первый участок
            bool firing = mode == Mode.Shot && phase == ShotPhase.Beam;
            BeamTrace.Trace(ct.position, ct.forward, maxRange, hitMask, firing);
            aimDist = BeamTrace.FirstLeg;
            aimPoint = ct.position + ct.forward * aimDist;
        }

        /// <summary>Урон тому, на что направлен прицел (враг, стена Тьмы, ложный огонь); стены зданий закрывают цель.</summary>
        void DealShotDamage(float dmg)
        {
            if (cam == null) return;
            // по всему пути луча: прямой участок, отражённые участки после зеркал, усиление после линз
            Vector3 firstEnd = cam.transform.position + cam.transform.forward * aimDist;
            for (int s = 0; s < BeamTrace.Count; s++)
            {
                BeamSeg seg = BeamTrace.Segs[s];
                float reach = DamageAlong(seg, dmg * seg.Focus);
                if (s == 0) firstEnd = seg.O + seg.D * reach;
            }
            if (BeamHit != null) BeamHit(firstEnd);
        }

        /// <summary>Урон ближайшей цели на одном участке пути луча (враг, стена Тьмы, ложный огонь); возвращает, докуда дошёл луч.</summary>
        float DamageAlong(BeamSeg seg, float dmg)
        {
            Vector3 o = seg.O;
            Vector3 dir = seg.D;
            float best = seg.Len + 0.3f;
            ILightTarget target = null;

            for (int i = 0; i < Enemy.All.Count; i++)
            {
                Enemy e = Enemy.All[i];
                Vector3 to = e.Center - o;
                float t = Vector3.Dot(to, dir);
                if (t < 0.4f || t > best) continue;
                Vector3 closest = o + dir * t;
                if ((e.Center - closest).magnitude < e.HitRadius)
                {
                    best = t;
                    target = e;
                }
            }

            Ray ray = new Ray(o, dir);
            for (int i = 0; i < Growth.All.Count; i++)
            {
                Growth g = Growth.All[i];
                if (g.Dead) continue;
                float t;
                if (g.WorldBounds.IntersectRay(ray, out t) && t >= 0f && t < best)
                {
                    best = t;
                    target = g;
                }
            }

            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                if (!l.IsDecoy || l.Dead) continue;
                Vector3 to = l.LightPosition - o;
                float t = Vector3.Dot(to, dir);
                if (t < 0.4f || t > best) continue;
                Vector3 closest = o + dir * t;
                if ((l.LightPosition - closest).magnitude < 1.0f)
                {
                    best = t;
                    target = l;
                }
            }

            Growth wall = target as Growth;
            if (wall != null) wall.BurnBeam(dmg, seg.Reflected);   // печать хранилища горит только от отражённого луча
            else if (target != null) target.OnLightHit(dmg);
            return best;
        }

        /// <summary>Вспышка вокруг героя: враги в радиусе оглушены и ранены, стена Тьмы рядом обожжена, ложные огни рассеяны.</summary>
        void DoFlash()
        {
            Vector3 center = cam.transform.position;
            float r = flashRadius;
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                Enemy e = Enemy.All[i];
                if ((e.Center - center).magnitude <= r) e.Nova(flashDamage, flashStunTime);
            }
            for (int i = 0; i < Growth.All.Count; i++)
            {
                Growth g = Growth.All[i];
                if (g.Dead) continue;
                Vector3 cp = g.WorldBounds.ClosestPoint(center);
                if ((cp - center).magnitude <= r) g.Burn(GameConfig.NovaDamageGrowth);
            }
            for (int i = 0; i < Lamp.All.Count; i++)
            {
                Lamp l = Lamp.All[i];
                if (l.IsDecoy && !l.Dead && (l.LightPosition - center).magnitude <= r) l.Reveal();
            }
            hero.FlashPulse = 1f;
            ProcAudio.OneShot(sfx, "flash", 1f, UnityEngine.Random.Range(0.65f, 0.8f));
            if (FlashFired != null) FlashFired();
        }

        // ------------------------------------------------------------------ эффекты ладоней

        void ApplyHand(HandFx h, Vector3 origin, Vector3 mid, bool isRight, float lenF, float th,
                       float orbS, float li, float open, float fl)
        {
            if (h == null || h.palm == null || h.beamRoot == null) return;

            // пальцы: опорная поза (её ставит HeroBody) + угол раскрытия, как в оригинале
            for (int i = 0; i < h.joints.Length; i++)
            {
                FingerJoint j = h.joints[i];
                if (j.t == null) continue;
                j.t.localRotation = j.baseRot * Quaternion.AngleAxis(j.openAngle * open, jointAxis);
            }

            Vector3 src = Vector3.Lerp(origin, mid, mergeT);
            bool hidden = isRight && mergeT > 0.98f;
            float widthMul = Mathf.Lerp(1f, mergedWidthMultiplier, mergeT);

            bool beamOn = lenF > 0.01f && th > 0.01f && !hidden;
            h.beamRoot.gameObject.SetActive(beamOn);
            if (beamOn)
            {
                Vector3 toAim = aimPoint - src;
                float len = Mathf.Max(0.01f, toAim.magnitude * lenF);
                h.beamRoot.position = src;
                h.beamRoot.rotation = Quaternion.LookRotation(toAim.normalized);
                SetBeam(h.beamOuter, beamWidth * th * widthMul, len);
                SetBeam(h.beamCore, beamCoreWidth * th * widthMul, len);
            }

            bool orbOn = orbS > 0.01f && !hidden;
            h.orb.gameObject.SetActive(orbOn);
            if (orbOn)
            {
                h.orb.position = src;
                h.orb.localScale = Vector3.one * (orbS * Mathf.Lerp(1f, 1.4f, mergeT));
                h.orbOuter.Rotate(0f, 200f * Time.deltaTime, 130f * Time.deltaTime, Space.Self);
            }

            bool flareOn = fl > 0.02f;
            h.flare.gameObject.SetActive(flareOn);
            if (flareOn)
            {
                h.flare.position = origin + h.palm.forward * 0.05f;
                Vector3 toCam = cam.transform.position - h.flare.position;
                if (toCam.sqrMagnitude > 0.0001f) h.flare.rotation = Quaternion.LookRotation(toCam);
                h.flare.localScale = Vector3.one * fl;
            }

            h.glow.Pos = origin + h.palm.forward * 0.2f;
            h.glow.Intensity = li * lightIntensity;
        }

        static void SetBeam(Transform t, float width, float len)
        {
            t.localPosition = new Vector3(0f, 0f, len * 0.5f);
            t.localScale = new Vector3(width, width, len);
        }
    }
}
