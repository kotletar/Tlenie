using UnityEngine;

namespace PokaGoryu
{
    /// <summary>
    /// «Камкордер»: сцена рендерится в маленькую текстуру (360 строк), которую вторая камера показывает на экране
    /// через шейдер Pokagoryu/Post (искажение линзы, хроматическая аберрация, свечение ярких огней, строки, зерно,
    /// сбои «трекинга» при уроне, виньетка, холодные тени). F2 — вкл/выкл. Яркость — клавиши - и =.
    /// Если картинка пропала: нажмите F2 или поставьте Enabled = false.
    /// </summary>
    public class LowRes : MonoBehaviour
    {
        public static LowRes Instance;
        public bool Enabled = true;
        public int TargetHeight = 360;
        public float Glitch;        // 0..1: сбой картинки (ставит игра при уроне)
        public float ExtraGlitch;   // 0..1: помехи сверх урона (концовка: Тьма накрывает автобус)
        public const float DefaultVignette = 2f;
        public float Vignette = DefaultVignette;   // сила виньетки; концовка смыкает края кадра

        const int PostLayer = 31;
        const int FeatherLayer = 30;

        Camera cam;
        Camera postCam;
        RenderTexture rt;
        Material postMat;
        Transform quad;

        public void Init(Camera c)
        {
            Instance = this;
            cam = c;
            postMat = Gfx.PostMaterial();
            if (postMat == null)
            {
                Enabled = false;
                return;
            }

            GameObject pg = new GameObject("PostCamera");
            pg.transform.SetParent(transform, false);
            pg.transform.position = new Vector3(0f, -5000f, 0f);
            postCam = pg.AddComponent<Camera>();
            postCam.orthographic = true;
            postCam.orthographicSize = 1f;
            postCam.nearClipPlane = 0.1f;
            postCam.farClipPlane = 10f;
            postCam.clearFlags = CameraClearFlags.SolidColor;
            postCam.backgroundColor = Color.black;
            postCam.cullingMask = 1 << PostLayer;
            postCam.depth = 100f;

            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Collider col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.name = "PostQuad";
            q.layer = PostLayer;
            q.transform.SetParent(pg.transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, 2f);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = postMat;
            quad = q.transform;

            cam.cullingMask = ~((1 << PostLayer) | (1 << FeatherLayer));   // сцена полоски здоровья (слой 30) в кадр не попадает
        }

        void OnDestroy()
        {
            Release();
            if (Instance == this) Instance = null;
        }

        void Release()
        {
            if (cam != null) cam.targetTexture = null;
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
                rt = null;
            }
        }

        void LateUpdate()
        {
            if (cam == null) return;
            if (!Enabled || postMat == null || postCam == null)
            {
                if (cam.targetTexture != null) cam.targetTexture = null;
                if (postCam != null) postCam.enabled = false;
                return;
            }
            int sw = Screen.width;
            int sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            int h = Mathf.Min(TargetHeight, sh);
            int w = Mathf.Max(16, Mathf.RoundToInt(h * (float)sw / sh));
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null)
                {
                    cam.targetTexture = null;
                    rt.Release();
                    Destroy(rt);
                }
                rt = new RenderTexture(w, h, 24);
                rt.name = "PokaGoryuLowRes";
                rt.filterMode = FilterMode.Bilinear;
                rt.wrapMode = TextureWrapMode.Clamp;
            }
            if (cam.targetTexture != rt) cam.targetTexture = rt;
            postCam.enabled = true;

            float aspect = (float)sw / sh;
            quad.localScale = new Vector3(2f * aspect, 2f, 1f);
            postMat.mainTexture = rt;
            postMat.SetFloat("_Brightness", Gfx.Brightness);
            postMat.SetFloat("_Glitch", Mathf.Clamp01(Glitch + ExtraGlitch));
            postMat.SetFloat("_Vignette", Vignette);
        }
    }
}
