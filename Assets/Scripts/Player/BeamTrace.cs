using UnityEngine;

namespace PokaGoryu
{
    /// <summary>Один прямой отрезок пути луча героя.</summary>
    public struct BeamSeg
    {
        public Vector3 O;          // начало
        public Vector3 D;          // направление (длина 1)
        public float Len;          // длина отрезка
        public bool Reflected;     // луч уже отражался от зеркала (печати хранилищ горят только от такого)
        public float Focus;        // во сколько раз усилен линзами (1 — не усилен)
    }

    /// <summary>
    /// Путь луча героя (ЛКМ): от камеры прямо до первого твёрдого, дальше — по зеркалам (угол падения равен углу отражения,
    /// не больше четырёх отражений), линзы по пути усиливают всё, что луч жжёт после них. Результат лежит в статических полях и
    /// переиспользуется каждый кадр (без выделения памяти). Считает PalmShot; тот же путь использует автотест хранилищ.
    /// </summary>
    public static class BeamTrace
    {
        public const int Max = 10;
        public const int MaxBounces = 4;

        public static readonly BeamSeg[] Segs = new BeamSeg[Max];
        public static int Count;
        /// <summary>Длина прямой части до первого твёрдого предмета или зеркала (то, что рисуют ладони).</summary>
        public static float FirstLeg;
        public static int Bounces;
        /// <summary>Чем закончился путь: зеркало, в которое упёрся последний отрезок (null — твёрдое или пустота).</summary>
        public static LightOptic LastMirror;

        /// <summary>
        /// Построить путь из точки o в направлении d (длина 1) длиной range. touch — отметить зеркала и линзы на пути как «освещённые»
        /// (они на миг вспыхивают, пока луч бьёт).
        /// </summary>
        public static void Trace(Vector3 o, Vector3 d, float range, int mask, bool touch)
        {
            Count = 0;
            FirstLeg = 0f;
            Bounces = 0;
            LastMirror = null;
            float left = range;
            bool reflected = false;
            float focus = 1f;
            ulong lensUsed = 0UL;
            int guard = 0;

            while (left > 0.05f && Count < Max && guard++ < 24)
            {
                float len = left;
                LightOptic mirror = null;
                RaycastHit hit = new RaycastHit();
                if (Physics.Raycast(o, d, out hit, left, mask, QueryTriggerInteraction.Ignore))
                {
                    len = hit.distance;
                    LightOptic optic = hit.collider.GetComponentInParent<LightOptic>();
                    if (optic != null && optic.Kind == OpticKind.Mirror && hit.collider.gameObject.name == "MirrorPanel") mirror = optic;
                }

                // линза на этом отрезке (ближайшая, ещё не пройденная): луч проходит насквозь, дальше он сильнее
                int lensIndex = -1;
                float lensT = len;
                for (int i = 0; i < LightOptic.All.Count && i < 64; i++)
                {
                    LightOptic lens = LightOptic.All[i];
                    if (lens.Kind != OpticKind.Lens || (lensUsed & (1UL << i)) != 0UL) continue;
                    Vector3 c = lens.LensCenter;
                    float t = Vector3.Dot(c - o, d);
                    if (t < 0.2f || t > len) continue;
                    if ((o + d * t - c).sqrMagnitude > LightOptic.LensRadius * LightOptic.LensRadius) continue;
                    if (t < lensT) { lensT = t; lensIndex = i; }
                }
                if (lensIndex >= 0)
                {
                    Add(o, d, lensT, reflected, focus);
                    if (!reflected) FirstLeg += lensT;
                    lensUsed |= 1UL << lensIndex;
                    if (touch) LightOptic.All[lensIndex].Touch();
                    focus *= LightOptic.LensFocus;
                    o += d * lensT;
                    left -= lensT;
                    continue;
                }

                Add(o, d, len, reflected, focus);
                if (!reflected) FirstLeg += len;
                if (mirror == null) break;   // твёрдое или пустота: путь окончен

                // отражение от зеркала: считаем только удар в плоскость панели (не в торец)
                Vector3 n = mirror.PlaneNormal;
                float dn = Vector3.Dot(d, n);
                if (Mathf.Abs(Vector3.Dot(hit.normal, n)) < 0.9f || Mathf.Abs(dn) < 0.05f || Bounces >= MaxBounces)
                {
                    LastMirror = mirror;
                    break;   // торец, скользящий луч или лимит отражений: поглощён
                }
                if (touch) mirror.Touch();
                d = d - 2f * dn * n;
                d.Normalize();
                o = hit.point + d * 0.03f;
                left -= len;
                reflected = true;
                Bounces++;
            }
        }

        static void Add(Vector3 o, Vector3 d, float len, bool reflected, float focus)
        {
            if (Count >= Max) return;
            Segs[Count].O = o;
            Segs[Count].D = d;
            Segs[Count].Len = len;
            Segs[Count].Reflected = reflected;
            Segs[Count].Focus = focus;
            Count++;
        }
    }
}
