using System.Collections.Generic;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// Хранилище света: низкая каморка на площади или широкой улице, запертая печатью Тьмы. Печать не берёт прямой свет — только луч,
    /// отражённый от зеркала (E поворачивает зеркало на 45°); у тяжёлых хранилищ на пути луча стоит линза (усиливает втрое).
    /// Когда печать сгорает, из каморки выкатываются осколки света. Всё необязательное: на путь к автобусу хранилища не влияют.
    /// </summary>
    public sealed class LightVault
    {
        public int Tier;                                  // 1 — одно зеркало; 2 — зеркало и линза; 3 — два зеркала и линза
        public Vector3 Center;                            // центр каморки
        public Vector3 Gate;                              // центр ворот
        public Vector3 Stand;                             // откуда бить (на земле): с этого места луч в первое зеркало решает загадку
        public Vector3 StandAim;                          // центр первого зеркала на высоте луча
        public Growth Seal;
        public readonly List<LightOptic> Mirrors = new List<LightOptic>();   // по порядку вдоль луча от героя
        public readonly List<int> Solution = new List<int>();                // нужное положение (Step) каждого зеркала
        public LightOptic Lens;
        public bool Opened { get { return Seal == null || Seal.Dead; } }
    }

    /// <summary>Ореол над хранилищем: тихий золотой свет, пока печать цела и идёт игра (в меню и предыстории его нет).</summary>
    public sealed class VaultGlow : MonoBehaviour
    {
        public Material Halo;
        public Growth Seal;
        [System.NonSerialized] public LightVault Vault;
        float phase;
        int bornFrame = -1;
        bool checkedSolvable;

        void Update()
        {
            if (bornFrame < 0) bornFrame = Time.frameCount;
            if (!checkedSolvable && Time.frameCount > bornFrame + 1)
            {
                // страховка: хранилище должно решаться настоящим трассировщиком луча (то же проверяет автотест)
                checkedSolvable = true;
                if (Vault != null && !LevelBuilder.VaultSolvable(Vault)) Debug.LogWarning("[PokaGoryu] Light vault of tier " + Vault.Tier + " is not solvable");
            }
            if (Halo == null) return;
            GameManager gm = GameManager.Instance;
            bool on = gm != null && gm.State == GameState.Playing && Seal != null && !Seal.Dead;
            phase += Time.deltaTime;
            Halo.SetColor("_Color", new Color(1f, 0.82f, 0.45f, on ? 0.26f + 0.08f * Mathf.Sin(phase * 1.7f) : 0f));
        }
    }

    public static partial class LevelBuilder
    {
        /// <summary>Хранилища света этого города (0..3). Раскладываются после всего остального, своим генератором случайных чисел.</summary>
        public static readonly List<LightVault> Vaults = new List<LightVault>();

        // «Ручки» хранилищ: прочность печати, награда (число осколков × свет в каждом) и в каком отрезке пути стоят (доля до автобуса).
        static readonly float[] VaultSealHp = { 100f, 220f, 260f };
        static readonly int[] VaultOrbs = { 3, 4, 5 };
        static readonly float[] VaultOrbValue = { 14f, 16f, 18f };
        static readonly float[] VaultProgressMin = { 0.10f, 0.35f, 0.55f };
        static readonly float[] VaultProgressMax = { 0.45f, 0.70f, 0.95f };

        const float VaultEye = 1.65f;   // высота луча (камера героя)

        static void BuildVaults(Transform root)
        {
            Vaults.Clear();
            Physics.SyncTransforms();
            System.Random vr = new System.Random(77031 + CitySeed * 31);   // свой генератор: основная раскладка города не сдвигается
            for (int tier = 1; tier <= 3; tier++)
            {
                LightVault v = FindVault(root, tier, vr);
                if (v == null)
                {
                    Debug.LogWarning("[PokaGoryu] No site for the light vault of tier " + tier);
                    continue;
                }
                Vaults.Add(v);
                // Решаемость проверяет VaultGlow на втором кадре: у только что созданных примитивов Unity убирает коллайдеры в конце
                // кадра, и до этого они закрывали бы луч.
            }
        }

        static LightVault FindVault(Transform root, int tier, System.Random vr)
        {
            List<CellPos> cells = new List<CellPos>();
            for (int z = 0; z < CityMap.H; z++)
            {
                for (int x = 0; x < CityMap.W; x++)
                {
                    // только площади: на улицах каморка с зеркалами сужала бы проход, а площади просторны (вокзал отсекает NearPoints)
                    if (CityMap.Kind[x, z] != CityMap.Plaza || CityMap.Blocked[x, z]) continue;
                    Vector3 c = CellCenter(x, z, 0f);
                    float prog = CityMap.Progress(c.x, c.z);
                    if (prog < VaultProgressMin[tier - 1] || prog > VaultProgressMax[tier - 1]) continue;
                    cells.Add(new CellPos(x, z));
                }
            }
            for (int i = cells.Count - 1; i > 0; i--)   // перемешать: города с разным зерном ставят хранилища в разные места
            {
                int j = vr.Next(i + 1);
                CellPos tmp = cells[i];
                cells[i] = cells[j];
                cells[j] = tmp;
            }
            int tested = 0;
            for (int i = 0; i < cells.Count && tested < 4000; i++)
            {
                for (int d = 0; d < 4; d++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        tested++;
                        Vector3 n = new Vector3(Dx[d], 0f, Dz[d]);
                        Vector3 t = new Vector3(-n.z, 0f, n.x) * (side == 0 ? 1f : -1f);
                        LightVault v = TryVault(root, tier, cells[i], n, t, vr);
                        if (v != null) return v;
                    }
                }
            }
            return null;
        }

        /// <summary>Хватает ли места: вся дорога под каморкой, зеркалами, линзой и местом, откуда бьют; ничего твёрдого и ничего занятого.</summary>
        static bool VaultSpotOk(Vector3 p, float roadR, float boxHalf, float reserve)
        {
            if (!CityMap.CanStand(p.x, p.z, roadR, true)) return false;
            if (NearPoints(p, reserve) || Reserved(p, reserve * 0.5f)) return false;
            for (int i = 0; i < treePts.Count; i++)
            {
                float dx = treePts[i].x - p.x;
                float dz = treePts[i].z - p.z;
                if (dx * dx + dz * dz < (boxHalf + 1.2f) * (boxHalf + 1.2f)) return false;
            }
            return !Physics.CheckBox(p + Vector3.up * 1.2f, new Vector3(boxHalf, 0.85f, boxHalf), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Луч от a к b на высоте луча свободен (и дорога под ним).</summary>
        static bool VaultLegClear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.1f) return true;
            d /= len;
            for (float s = 0f; s <= len; s += 1.2f)
            {
                Vector3 p = a + d * s;
                if (!CityMap.IsRoad(p.x, p.z, true)) return false;
            }
            Vector3 o = new Vector3(a.x, VaultEye, a.z);
            return !Physics.SphereCast(o, 0.25f, d, out RaycastHit hit, len, ~0, QueryTriggerInteraction.Ignore);
        }

        static int MirrorStep(Vector3 inDir, Vector3 outDir)
        {
            Vector3 m = outDir - inDir;
            float deg = Mathf.Atan2(m.x, m.z) * Mathf.Rad2Deg;
            deg = ((deg % 180f) + 180f) % 180f;
            return Mathf.RoundToInt(deg / 45f) & 3;
        }

        static LightVault TryVault(Transform root, int tier, CellPos cell, Vector3 n, Vector3 t, System.Random vr)
        {
            Vector3 c = CellCenter(cell.X, cell.Z, 0f);
            Vector3 G = c + new Vector3(((float)vr.NextDouble() - 0.5f) * 3f, 0f, ((float)vr.NextDouble() - 0.5f) * 3f);   // ворота
            Vector3 E = G - n * 1.8f;                                                                                          // центр каморки

            Vector3 M1, M2 = Vector3.zero, L = Vector3.zero, S;
            Vector3 in1, out1;
            if (tier == 1)
            {
                M1 = G + n * 6.5f;
                in1 = -t; out1 = -n;
                S = M1 + t * 7f;
            }
            else if (tier == 2)
            {
                M1 = G + n * 7.5f;
                L = G + n * 3.6f;
                in1 = -t; out1 = -n;
                S = M1 + t * 7f;
            }
            else
            {
                M2 = G + n * 6f;
                M1 = M2 + t * 6.5f;
                L = G + n * 3.0f;
                in1 = -n; out1 = -t;
                S = M1 + n * 8f;
            }

            // хранилища не толпятся: разные площади или хотя бы разные концы большой
            for (int i = 0; i < Vaults.Count; i++)
            {
                if ((Vaults[i].Center - E).magnitude < 45f || (Vaults[i].Stand - S).magnitude < 20f) return null;
            }

            if (!VaultSpotOk(E, 3.0f, 2.4f, 14f)) return null;
            if (!VaultSpotOk(M1, 1.2f, 1.0f, 5f)) return null;
            if (!VaultSpotOk(S, 0.7f, 0.5f, 5f)) return null;
            if (tier >= 2 && !VaultSpotOk(L, 1.0f, 0.8f, 4f)) return null;
            if (tier == 3 && !VaultSpotOk(M2, 1.2f, 1.0f, 5f)) return null;
            if (tier == 3)
            {
                if (!VaultLegClear(S, M1) || !VaultLegClear(M1, M2) || !VaultLegClear(M2, G)) return null;
            }
            else if (!VaultLegClear(S, M1) || !VaultLegClear(M1, G)) return null;

            // место подошло: строим
            LightVault v = new LightVault();
            v.Tier = tier;
            v.Center = E;
            v.Gate = G;
            v.Stand = new Vector3(S.x, 0.1f, S.z);
            v.StandAim = new Vector3(M1.x, VaultEye, M1.z);
            GameObject holder = new GameObject("LightVault" + tier);
            holder.transform.SetParent(root, false);

            BuildVaultShell(holder.transform, v, E, G, n, t, tier);

            int sol1 = MirrorStep(in1, out1);
            LightOptic m1 = LightOptic.Create(holder.transform, OpticKind.Mirror, M1, (sol1 + 2) & 3);
            v.Mirrors.Add(m1);
            v.Solution.Add(sol1);
            if (tier == 3)
            {
                int sol2 = MirrorStep(-t, -n);
                LightOptic m2 = LightOptic.Create(holder.transform, OpticKind.Mirror, M2, (sol2 + 2) & 3);
                v.Mirrors.Add(m2);
                v.Solution.Add(sol2);
            }
            if (tier >= 2) v.Lens = LightOptic.Create(holder.transform, OpticKind.Lens, L, 0);
            Physics.SyncTransforms();
            return v;
        }

        /// <summary>Каморка 3.6 × 3.6 м с низкими стенами и воротами 2.4 м, закрытыми печатью; внутри — ореол награды.</summary>
        static void BuildVaultShell(Transform holder, LightVault v, Vector3 E, Vector3 G, Vector3 n, Vector3 t, int tier)
        {
            MeshBatch mb = new MeshBatch();
            Color32 stone = new Color32(66, 62, 60, 255);
            Color32 cap = new Color32(84, 78, 72, 255);
            float h = 1.5f;
            // длина по n и по t -> размеры по осям мира (n и t лежат вдоль осей)
            System.Func<float, float, Vector3> size = (alongN, alongT) =>
                Mathf.Abs(n.x) > 0.5f ? new Vector3(alongN, h, alongT) : new Vector3(alongT, h, alongN);
            System.Action<Vector3, Vector3> wall = (center, sz) =>
            {
                Vector3 cc = new Vector3(center.x, h * 0.5f, center.z);
                mb.BoxYaw(cc, sz, 0f, stone, MeshBatch.AllFaces);
                mb.BoxYaw(cc + new Vector3(0f, h * 0.5f + 0.04f, 0f), new Vector3(sz.x + 0.08f, 0.08f, sz.z + 0.08f), 0f, cap, MeshBatch.AllFaces);
                Gfx.StaticBox(holder, cc, sz, "VaultWall", 0f);
            };
            wall(E - n * 1.55f, size(0.5f, 4.1f));                              // задняя
            wall(E + t * 1.55f + n * 0.125f, size(3.35f, 0.5f));                 // боковые
            wall(E - t * 1.55f + n * 0.125f, size(3.35f, 0.5f));
            wall(G - n * 0.25f + t * 1.5f, size(0.5f, 0.6f));                    // перемычки у ворот (посередине — печать шириной 2.4 м)
            wall(G - n * 0.25f - t * 1.5f, size(0.5f, 0.6f));
            Mesh mesh = mb.Build("PG_VaultWalls");
            TrackMesh(mesh);
            GameObject mo = Gfx.MeshObject(holder, mesh, Gfx.Lit(Color.white), "VaultWalls");
            mo.transform.position = Vector3.zero;

            // печать: прочнее у тяжёлых хранилищ; награда — осколки света внутри
            float yaw = Mathf.Atan2(-t.z, t.x) * Mathf.Rad2Deg;
            Growth seal = Growth.CreateSeal(holder, G - n * 0.25f, yaw, new Vector2(2.4f, 0.8f), VaultSealHp[tier - 1], 100 + tier);   // 2.4 м: в печать проще попасть
            v.Seal = seal;
            int orbs = VaultOrbs[tier - 1];
            float value = VaultOrbValue[tier - 1];
            Vector3 centre = E;
            seal.OnDeath = () =>
            {
                for (int i = 0; i < orbs; i++)
                {
                    float a = i * Mathf.PI * 2f / orbs;
                    LightOrb.Spawn(centre + new Vector3(Mathf.Sin(a) * 0.8f, 0.9f, Mathf.Cos(a) * 0.8f), value);
                }
                GameManager gm = GameManager.Instance;
                if (gm != null) gm.Notify("Печать пала: свет хранилища свободен");
            };

            Material halo = OwnedResources.Keep(holder.gameObject, Gfx.Unlit(new Color(1f, 0.82f, 0.45f, 0f), Gfx.TexHalo, true, false, 0.2f));
            Gfx.Sprite(holder, E + new Vector3(0f, 1.0f, 0f), new Vector2(3.4f, 3.4f), halo, false);
            VaultGlow glow = holder.gameObject.AddComponent<VaultGlow>();
            glow.Halo = halo;
            glow.Seal = seal;
            glow.Vault = v;
        }

        /// <summary>
        /// Решается ли хранилище: если поставить зеркала в нужные положения и ударить лучом с места Stand в первое зеркало, луч после
        /// отражений упирается в печать. Проверка настоящим трассировщиком луча (BeamTrace), как в игре. Положения зеркал потом возвращаются.
        /// </summary>
        public static bool VaultSolvable(LightVault v)
        {
            if (v == null || v.Seal == null || v.Seal.Dead || v.Mirrors.Count == 0) return false;
            int[] saved = new int[v.Mirrors.Count];
            for (int i = 0; i < saved.Length; i++)
            {
                saved[i] = v.Mirrors[i].Step;
                v.Mirrors[i].SetStep(v.Solution[i]);
            }
            Physics.SyncTransforms();
            Vector3 eye = v.Stand + new Vector3(0f, VaultEye - 0.1f, 0f);
            Vector3 dir = v.StandAim - new Vector3(eye.x, VaultEye, eye.z);
            dir.y = 0f;
            dir.Normalize();
            eye.y = VaultEye;
            BeamTrace.Trace(eye, dir, GameConfig.CombatRange, ~0, false);
            bool ok = false;
            if (BeamTrace.Count > 0)
            {
                BeamSeg last = BeamTrace.Segs[BeamTrace.Count - 1];
                Vector3 end = last.O + last.D * (last.Len + 0.1f);
                ok = last.Reflected && v.Seal.WorldBounds.Contains(end);
            }
            for (int i = 0; i < saved.Length; i++) v.Mirrors[i].SetStep(saved[i]);
            Physics.SyncTransforms();
            return ok;
        }
    }
}
