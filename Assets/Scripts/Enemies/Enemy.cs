using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    public enum EnemyKind { Shade, Snuffer, Eater }
    public enum DamageKind { Flash, Beam, Light }

    /// <summary>
    /// Порождения Тьмы (3D-тела из частей: EnemyRig и EnemyModels; сам объект поворачивается лицом к цели или по ходу движения).
    ///  - Тень: стайная, боится света (шире аура — держатся дальше), в темноте кусает людей и героя.
    ///  - Гаситель: мелкий и быстрый, тянется к слабым огням: гасит лампы и ранит людей. Умирает от вспышки.
    ///  - Пожиратель: огромный, медленный, к свету невосприимчив, жрёт людей. Берётся только лучом (дорого) — или обойти.
    /// В темноте видны только глаза.
    /// </summary>
    public class Enemy : MonoBehaviour, ILightTarget, IFlashTarget
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        /// <summary>ILightTarget (PalmShot): попадание лучом.</summary>
        public void OnLightHit(float damage) { ShotHit(damage); }

        /// <summary>IFlashTarget (PalmShot): оглушение вспышкой.</summary>
        public void OnFlash(float stunTime) { Stun(stunTime); }

        public EnemyKind Kind;
        public float Health;
        public float HitRadius;

        public Vector3 Center { get { return transform.position + Vector3.up * centerY; } }

        float maxHealth;
        float speed;
        float centerY;
        float stun;
        float attackCd;
        float heroCd;
        float retargetT;
        float age;
        float seed;
        float hitPulse;
        bool dead;
        Lamp targetLamp;
        Survivor targetSurvivor;

        // тело и поворот: враг смотрит туда, куда идёт, а нападая — на цель
        EnemyRig rig;
        Vector3 lastPos;
        Vector3 moveVel;
        float yaw;
        float turnRate = 240f;
        bool attacking;
        bool hasFace;
        Vector3 faceTarget;
        float biteT;

        /// <summary>Для автотестов и стенда: число частей тела (отрисовщиков) врага; ноль — тела нет.</summary>
        public int BodyParts { get { return rig != null ? rig.RendererCount : 0; } }

        // навигация по дорогам: враги не проходят сквозь дома и стены
        float navRadius = 0.5f;
        List<Pt> path;
        int pathIdx;
        float repathT;
        Vector3 pathTarget;

        public static Enemy Spawn(EnemyKind kind, Vector3 pos)
        {
            GameObject go = new GameObject(kind.ToString());
            pos.y = 0f;
            go.transform.position = pos;
            Enemy e = go.AddComponent<Enemy>();
            e.Setup(kind);
            return e;
        }

        public static void ClearAll()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                if (All[i] != null) Destroy(All[i].gameObject);
            }
            All.Clear();
        }

        public static int CountOf(EnemyKind k)
        {
            int n = 0;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Kind == k) n++;
            }
            return n;
        }

        void Setup(EnemyKind kind)
        {
            Kind = kind;
            seed = Random.value * 50f;
            switch (kind)
            {
                case EnemyKind.Shade:
                    maxHealth = 25f; speed = 3.4f; HitRadius = 0.9f; centerY = 1.3f; navRadius = 0.55f; turnRate = 260f;
                    break;
                case EnemyKind.Snuffer:
                    maxHealth = 12f; speed = 5.6f; HitRadius = 0.6f; centerY = 1.6f; navRadius = 0.4f; turnRate = 480f;
                    break;
                default:
                    maxHealth = 160f; speed = 1.7f; HitRadius = 1.8f; centerY = 2.4f; navRadius = 1.5f; turnRate = 90f;
                    break;
            }
            Health = maxHealth;
            repathT = Random.Range(0f, 0.6f);
            slideSide = Random.value < 0.5f ? 1f : -1f;
            SnapToRoad();

            // тело: рождается лицом к герою
            PlayerController pl0 = PlayerController.Instance;
            if (pl0 != null)
            {
                Vector3 to = pl0.transform.position - transform.position;
                yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            lastPos = transform.position;
            rig = EnemyRig.Create(kind, transform, seed);

            if (kind == EnemyKind.Eater) ProcAudio.Source(gameObject, "eater", true, 0.6f, true);
            else ProcAudio.Source(gameObject, "shade", true, kind == EnemyKind.Shade ? 0.22f : 0.14f, true);

            All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (rig != null) rig.Dispose();
        }

        static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Поставить врага на дорогу (если родился вплотную к стене или в здании).</summary>
        void SnapToRoad()
        {
            Vector3 p = transform.position;
            if (CityMap.CanStand(p.x, p.z, navRadius, true)) return;
            int rx, rz;
            if (!CityMap.NearestRoad(p.x, p.z, out rx, out rz)) return;
            Pt c = CityMap.CenterOf(rx, rz);
            transform.position = new Vector3(c.X, p.y, c.Z);
        }

        static readonly Collider[] solidBuf = new Collider[8];
        float slideSide = 1f;   // в какую сторону обходить препятствие, когда упёрся лбом (автобус, машина)
        float detourT;          // сколько ещё секунд идти в обход
        Vector3 detourDir;

        /// <summary>
        /// Есть ли в точке твёрдый предмет (автобус, машина, дерево, фонтан, витрина...): враги ходят по сетке дорог и о
        /// предметах ничего не знали, поэтому проходили сквозь автобус. Люди и герой (CharacterController) и пол не в счёт.
        /// </summary>
        bool SolidAt(Vector3 p)
        {
            float r = navRadius * 0.8f;
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * 0.3f, p + Vector3.up * 1.6f, r, solidBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = solidBuf[i];
                if (c == null || c is CharacterController) continue;
                if (c.name == "Floor") continue;
                return true;
            }
            return false;
        }

        /// <summary>Можно ли встать в точку: дорога (не здание, не нарост Тьмы) и без твёрдого предмета; уже застрявшему в предмете разрешено выйти.</summary>
        bool Open(Vector3 p, bool alreadyInside)
        {
            if (!CityMap.CanStand(p.x, p.z, navRadius, true)) return false;
            return alreadyInside || !SolidAt(p);
        }

        /// <summary>Свободен ли путь вперёд по направлению шага на ~5 м (проверка каждые 1.2 м).</summary>
        bool LookAheadClear(Vector3 pos, Vector3 delta)
        {
            float dm = delta.magnitude;
            if (dm < 1e-5f) return true;
            Vector3 dir = delta / dm;
            for (float s = 1.2f; s <= 4.9f; s += 1.2f)
            {
                if (!Open(pos + dir * s, false)) return false;
            }
            return true;
        }

        /// <summary>
        /// Шаг с учётом города: здания и живые наросты Тьмы не пускают (враг скользит вдоль стены), твёрдые предметы тоже;
        /// упёрся лбом (оба скольжения закрыты, например прямо в борт автобуса) — обходит сбоку.
        /// Раньше враги просто летели по прямой — теперь они ходят только по дорогам.
        /// </summary>
        void Step(Vector3 delta)
        {
            delta.y = 0f;
            Vector3 pos = transform.position;
            Vector3 np = pos + delta;
            bool inside = SolidAt(pos);
            // идёт обход: на прямой путь возвращаемся, только когда по нему свободно на несколько метров вперёд (не «срезаем» угол
            // автобуса и не упираемся в него снова)
            if (Open(np, inside) && (detourT <= 0f || LookAheadClear(pos, delta)))
            {
                transform.position = np;
                detourT = 0f;
                return;
            }
            // скольжение вдоль стены по оси — только если по этой оси остаётся заметная часть шага (иначе «скольжение» на пару
            // миллиметров в кадр было бы вечным стоянием лбом в борт автобуса); пока идёт обход, не скользим: иначе скольжение
            // тянет врага обратно к линии на героя, а обход — от неё, и он «маятником» стоит у борта
            float dm = delta.magnitude;
            if (detourT <= 0f && Mathf.Abs(delta.x) >= 0.35f * dm)
            {
                Vector3 sx = new Vector3(np.x, pos.y, pos.z);
                if (Open(sx, inside))
                {
                    transform.position = sx;
                    return;
                }
            }
            if (detourT <= 0f && Mathf.Abs(delta.z) >= 0.35f * dm)
            {
                Vector3 sz = new Vector3(pos.x, pos.y, np.z);
                if (Open(sz, inside))
                {
                    transform.position = sz;
                    return;
                }
            }
            // лбом в препятствие (борт автобуса, машина): обход. Выбираем сторону и идём вдоль препятствия несколько секунд,
            // не оглядываясь на цель: иначе враг «маятником» ходит по линии на героя и никуда не доходит
            if (dm > 1e-5f)
            {
                if (detourT <= 0f)
                {
                    detourDir = new Vector3(-delta.z, 0f, delta.x) / dm * slideSide;
                    detourT = 4f;
                }
                detourT -= Time.deltaTime;
                Vector3 dn = pos + detourDir * dm;
                if (!Open(dn, inside))
                {
                    detourDir = -detourDir;     // и там упёрлись (угол, стена): в другую сторону
                    slideSide = -slideSide;
                    dn = pos + detourDir * dm;
                }
                if (Open(dn, inside))
                {
                    transform.position = dn;
                    return;
                }
            }
            if (!CityMap.CanStand(pos.x, pos.z, navRadius, true))
            {
                // оказался внутри стены (нарост вырос или спавн у стены): вытолкнуть к центру ближайшей клетки дороги
                int rx, rz;
                if (CityMap.NearestRoad(pos.x, pos.z, out rx, out rz))
                {
                    Pt c = CityMap.CenterOf(rx, rz);
                    Vector3 to = new Vector3(c.X - pos.x, 0f, c.Z - pos.z);
                    float len = to.magnitude;
                    if (len > 0.01f) transform.position = pos + to / len * Mathf.Min(len, delta.magnitude + 0.05f);
                }
            }
        }

        /// <summary>
        /// Идти к точке по дорогам: напрямую, если путь свободен, иначе по найденному пути (повороты, переулки).
        /// stopDist — на каком расстоянии остановиться. Возвращает false, если пути нет (враг стоит).
        /// </summary>
        bool MoveToward(Vector3 target, float spd, float dt, float stopDist)
        {
            Vector3 pos = transform.position;
            Vector3 to = target - pos;
            to.y = 0f;
            if (to.magnitude <= stopDist) return true;

            Vector3 goal = target;
            bool direct = CityMap.LineClear(pos.x, pos.z, target.x, target.z, navRadius * 0.8f, true);
            if (direct)
            {
                path = null;
            }
            else
            {
                repathT -= dt;
                Vector3 moved = target - pathTarget;
                moved.y = 0f;
                if (path == null || repathT <= 0f || moved.sqrMagnitude > 16f)
                {
                    repathT = 0.7f + Random.Range(0f, 0.3f);
                    pathTarget = target;
                    path = CityMap.FindPath(new Pt(pos.x, pos.z), new Pt(target.x, target.z), false, 0f);
                    pathIdx = 1;
                }
                if (path == null || path.Count < 2) return false;   // дороги к цели нет (например, нарост Тьмы): стоим
                if (pathIdx >= path.Count) pathIdx = path.Count - 1;
                Vector3 wp = new Vector3(path[pathIdx].X, 0f, path[pathIdx].Z);
                Vector3 toWp = wp - pos;
                toWp.y = 0f;
                if (toWp.magnitude < 1.4f && pathIdx < path.Count - 1) pathIdx++;
                goal = new Vector3(path[pathIdx].X, 0f, path[pathIdx].Z);
            }
            Vector3 dir = goal - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) Step(dir.normalized * spd * dt);
            return true;
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || dead) return;
            if (gm.State != GameState.Playing || gm.Paused) return;
            PlayerController pl = PlayerController.Instance;
            if (pl == null) return;

            float dt = Time.deltaTime;
            age += dt;
            stun -= dt;
            attackCd -= dt;
            heroCd -= dt;
            hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 6f);
            biteT = Mathf.MoveTowards(biteT, 0f, dt * 3.5f);

            if (Horizontal(transform.position, pl.transform.position) > 85f)
            {
                Remove();
                return;
            }

            attacking = false;
            hasFace = false;
            switch (Kind)
            {
                case EnemyKind.Shade: UpdateShade(dt, pl); break;
                case EnemyKind.Snuffer: UpdateSnuffer(dt, pl); break;
                default: UpdateEater(dt, pl); break;
            }
            if (dead) return;
            AnimateBody(dt);
        }

        /// <summary>Поворот лицом по ходу (а нападая — на цель) и анимация частей тела.</summary>
        void AnimateBody(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 vel = dt > 0f ? (pos - lastPos) / dt : Vector3.zero;
            vel.y = 0f;
            lastPos = pos;
            moveVel = Vector3.Lerp(moveVel, vel, 1f - Mathf.Exp(-9f * dt));
            Vector3 look = Vector3.zero;
            if (hasFace) look = faceTarget - pos;
            else if (moveVel.magnitude > 0.35f) look = moveVel;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0004f)
            {
                float want = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, want, turnRate * dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            EnemyPose pose = new EnemyPose
            {
                Age = age,
                Speed = moveVel.magnitude,
                Attacking = attacking || DebugAttack,
                Stun = Mathf.Max(0f, stun),
                Hit = hitPulse,
                Bite = biteT
            };
            rig.Animate(pose, dt);
        }

        /// <summary>Цель нападения: враг разворачивается к ней и тянется.</summary>
        void Attack(Vector3 target)
        {
            attacking = true;
            hasFace = true;
            faceTarget = target;
        }

        // ------------------------------------------------------------------ Тень

        void UpdateShade(float dt, PlayerController pl)
        {
            Vector3 pos = transform.position;
            float b = LightField.Sample(pos + Vector3.up * 1.2f);
            if (b > GameConfig.ShadeLightFear)
            {
                // В свете: горит и пятится от героя.
                Health -= b * GameConfig.ShadeBurnPerLight * dt;
                Vector3 away = pos - pl.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = new Vector3(1f, 0f, 0f);
                Step(away.normalized * speed * 1.2f * dt);
                if (Health <= 0f) Die(DamageKind.Light);
                return;
            }
            if (stun > 0f) return;

            float best = 45f;
            Vector3 tp = pos;
            bool haveTarget = false;
            bool toHero = false;
            Survivor ts = null;

            float dh = Horizontal(pos, pl.transform.position);
            bool heroVisible = !(pl.Hero.Covered && dh > 3f);
            if (heroVisible && dh < best)
            {
                best = dh;
                tp = pl.transform.position;
                haveTarget = true;
                toHero = true;
            }
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Survivor s = Survivor.All[i];
                if (!s.IsAwake) continue;
                float d = Horizontal(pos, s.transform.position);
                if (d < best)
                {
                    best = d;
                    tp = s.transform.position;
                    ts = s;
                    haveTarget = true;
                    toHero = false;
                }
            }

            if (!haveTarget)
            {
                // цели нет: медленно тянется к герою — по дорогам
                MoveToward(pl.transform.position, speed * 0.5f, dt, 2f);
                return;
            }
            // атакует только то, до чего можно дойти напрямую (не через угол здания)
            bool reach = best <= 1.3f && CityMap.LineClear(pos.x, pos.z, tp.x, tp.z, 0f, true);
            if (!reach)
            {
                MoveToward(tp, speed, dt, 1.2f);
            }
            else if (toHero)
            {
                Attack(pl.transform.position);
                pl.Hero.Damage(GameConfig.ShadeDpsHero * dt);
            }
            else if (ts != null)
            {
                Attack(tp);
                ts.Hurt(GameConfig.ShadeDpsSurvivor * dt);
            }
        }

        // ------------------------------------------------------------------ Гаситель

        void UpdateSnuffer(float dt, PlayerController pl)
        {
            Vector3 pos = transform.position;
            float b = LightField.Sample(pos + Vector3.up * 1.5f);
            if (b > 1.2f)
            {
                Health -= b * 6f * dt;
                if (Health <= 0f)
                {
                    Die(DamageKind.Light);
                    return;
                }
            }
            if (stun > 0f) return;

            retargetT -= dt;
            if (retargetT <= 0f || (targetLamp == null && targetSurvivor == null))
            {
                retargetT = 1.0f;
                targetLamp = null;
                targetSurvivor = null;
                float best = 60f;
                for (int i = 0; i < Lamp.All.Count; i++)
                {
                    Lamp l = Lamp.All[i];
                    if (l.Dead || l.IsDecoy) continue;
                    float d = Horizontal(pos, l.BasePos) + l.Charge * 10f;
                    if (d < best)
                    {
                        best = d;
                        targetLamp = l;
                        targetSurvivor = null;
                    }
                }
                for (int i = 0; i < Survivor.All.Count; i++)
                {
                    Survivor s = Survivor.All[i];
                    if (!s.IsAwake) continue;
                    float d = Horizontal(pos, s.transform.position);
                    if (d < best)
                    {
                        best = d;
                        targetSurvivor = s;
                        targetLamp = null;
                    }
                }
            }

            if (targetLamp != null)
            {
                if (targetLamp.Dead)
                {
                    targetLamp = null;
                    return;
                }
                Vector3 lp = targetLamp.BasePos;
                Vector3 dir = new Vector3(lp.x - pos.x, 0f, lp.z - pos.z);
                bool near = dir.magnitude <= 1.4f && CityMap.LineClear(pos.x, pos.z, lp.x, lp.z, 0f, true);
                if (!near) MoveToward(lp, speed, dt, 1.2f);
                else
                {
                    Attack(lp);
                    targetLamp.Snuff(GameConfig.SnufferDpsLamp * dt);
                }
            }
            else if (targetSurvivor != null)
            {
                if (!targetSurvivor.IsAwake)
                {
                    targetSurvivor = null;
                    return;
                }
                Vector3 sp = targetSurvivor.transform.position;
                Vector3 dir = sp - pos;
                dir.y = 0f;
                bool near = dir.magnitude <= 1.2f && CityMap.LineClear(pos.x, pos.z, sp.x, sp.z, 0f, true);
                if (!near) MoveToward(sp, speed, dt, 1.1f);
                else
                {
                    Attack(sp);
                    targetSurvivor.Hurt(GameConfig.SnufferDpsSurvivor * dt);
                }
            }
            else
            {
                MoveToward(pl.transform.position, speed * 0.4f, dt, 2f);
            }
        }

        // ------------------------------------------------------------------ Пожиратель

        void UpdateEater(float dt, PlayerController pl)
        {
            if (stun > 0f) return;
            Vector3 pos = transform.position;

            Survivor ts = null;
            float best = 70f;
            for (int i = 0; i < Survivor.All.Count; i++)
            {
                Survivor s = Survivor.All[i];
                if (!s.IsAwake) continue;
                float d = Horizontal(pos, s.transform.position);
                if (d < best)
                {
                    best = d;
                    ts = s;
                }
            }

            Vector3 tp = ts != null ? ts.transform.position : pl.transform.position;
            Vector3 dir = tp - pos;
            dir.y = 0f;
            bool inReach = dir.magnitude <= 2.0f && CityMap.LineClear(pos.x, pos.z, tp.x, tp.z, 0f, true);
            if (!inReach) MoveToward(tp, speed, dt, 1.8f);
            else
            {
                Attack(tp);   // цель в пасти: жуёт, а на укусе захлопывает
                if (ts != null && attackCd <= 0f)
                {
                    // укус, а не мгновенная смерть: у человека есть время убежать к свету
                    attackCd = 2.5f;
                    biteT = 1f;
                    ts.Hurt(GameConfig.EaterBiteSurvivor);
                    GameManager gm = GameManager.Instance;
                    if (gm != null && ts.State == Survivor.Mode.Dead) gm.Notify("Пожиратель проглотил огонёк");
                }
            }

            if (heroCd <= 0f && Horizontal(pos, pl.transform.position) < 2.4f
                && CityMap.LineClear(pos.x, pos.z, pl.transform.position.x, pl.transform.position.z, 0f, true))
            {
                heroCd = 2.5f;
                biteT = 1f;
                pl.Hero.Damage(GameConfig.EaterHeroDamage);
                Vector3 push = pl.transform.position - pos;
                push.y = 0f;
                if (push.sqrMagnitude < 0.01f) push = Vector3.forward;
                pl.Push(push.normalized * 9f);
            }
        }

        // ------------------------------------------------------------------ урон

        /// <summary>Оглушение: враг стоит на месте (вспышка вокруг героя, ПКМ).</summary>
        public void Stun(float seconds)
        {
            if (dead) return;
            if (seconds > stun) stun = seconds;
        }

        /// <summary>Вспышка вокруг героя (ПКМ): грубо — оглушает и ранит, осколков света не оставляет.</summary>
        public void Nova(float dmg, float stunTime)
        {
            if (dead) return;
            Stun(stunTime);
            TakeDamage(dmg, DamageKind.Flash);
        }

        /// <summary>Выстрел или луч (ЛКМ): точный; убитый так враг оставляет осколок света.</summary>
        public void ShotHit(float dmg)
        {
            TakeDamage(dmg, DamageKind.Beam);
        }

        public void TakeDamage(float dmg, DamageKind kind)
        {
            if (dead) return;
            Health -= dmg;
            hitPulse = Mathf.Max(hitPulse, 0.6f);
            if (Health <= 0f) Die(kind);
        }

        void Die(DamageKind kind)
        {
            if (dead) return;
            dead = true;
            // убитый врагом оставляет осколок света: убивая, герой зарабатывает (выстрел и луч — полный, вспышка — половину);
            // сгоревший в свете ламп осколка не оставляет
            if (kind == DamageKind.Beam || kind == DamageKind.Flash)
            {
                float v = Kind == EnemyKind.Shade ? GameConfig.OrbValueShade : (Kind == EnemyKind.Snuffer ? GameConfig.OrbValueSnuffer : GameConfig.OrbValueEater);
                if (kind == DamageKind.Flash) v *= GameConfig.FlashOrbShare;
                LightOrb.Spawn(transform.position + Vector3.up * 0.9f, v);   // на уровне груди героя, даже у высокого Пожирателя: осколок подхватывается
            }
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.OnEnemyKilled(this);
            PlayerController pl = PlayerController.Instance;
            if (pl != null) ProcAudio.OneShot(pl.Sfx, "shadedie", 0.6f, Random.Range(0.9f, 1.1f));
            TimedFx.Spawn(Center, 0.5f, 3f, new Color(0.7f, 0.8f, 1f, 0.6f), 0.3f);
            All.Remove(this);
            Collapse();
        }

        /// <summary>Убитый враг исчезает не мгновенно: тело оседает и тает за полсекунды (логика уже отключена, из списка он вынут).</summary>
        void Collapse()
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            for (int i = 0; i < sources.Length; i++) sources[i].Stop();
            if (rig == null)
            {
                Destroy(gameObject);
                return;
            }
            gameObject.AddComponent<EnemyCorpse>().Init(rig);
        }

        /// <summary>Для стенда: принудительно показать позу нападения (иначе она включается, только когда есть цель в досягаемости).</summary>
        public bool DebugAttack;

        /// <summary>Для стенда: как будто враг только что укусил.</summary>
        public void DebugBite() { biteT = 1f; }

        void Remove()
        {
            dead = true;
            All.Remove(this);
            Destroy(gameObject);
        }
    }

    /// <summary>Тело убитого врага: за полсекунды оседает, растекается и пропадает вместе с объектом.</summary>
    public sealed class EnemyCorpse : MonoBehaviour
    {
        EnemyRig rig;
        float t;

        public void Init(EnemyRig r)
        {
            rig = r;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;   // доигрывает и на паузе: иначе осевшее тело повисло бы в кадре
            float u = Mathf.Clamp01(t / 0.5f);
            if (rig != null) rig.Collapse(u);
            if (u >= 1f) Destroy(gameObject);
        }
    }
}
