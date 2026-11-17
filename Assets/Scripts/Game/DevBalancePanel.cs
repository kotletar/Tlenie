using System.Reflection;
using UnityEngine;

namespace PokaGoryu
{
    /// <summary>F7, editor / Development only. These values are read live; no changes to release controls.</summary>
    public sealed class DevBalancePanel : MonoBehaviour
    {
        static readonly string[] Names = { "SmolderPerSec", "AuraCostK", "HearthRegenPerSec", "InhaleRate",
            "ResonancePerSec", "OrbValueShade", "OrbValueSnuffer", "OrbValueEater", "DistrictPressure", "TidePressure", "FinalPressure",
            "FinalTime", "FinalEaterInterval", "BossHealth", "BossCoreMul", "BossBleed", "BossWindow", "BossLanceDamage" };
        static readonly float[] Maximum = { 1f, 0.12f, 2f, 30f, 1.5f, 30f, 30f, 100f, 1f, 3f, 4f, 120f, 40f, 2000f, 4f, 0.3f, 10f, 40f };
        FieldInfo[] fields;
        bool shown, wasPaused;
        GameManager gm;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            fields = new FieldInfo[Names.Length];
            for (int i = 0; i < fields.Length; i++) fields[i] = typeof(GameConfig).GetField(Names[i]);
        }
        void Update()
        {
            if (!Debug.isDebugBuild || gm == null || gm.State != GameState.Playing) return;
            if (shown && !gm.Paused) shown = false;
            if (!GameInput.Down(GKey.F7)) return;
            if (shown) { shown = false; gm.SetPaused(wasPaused); }
            else { wasPaused = gm.Paused; shown = true; gm.SetPaused(true); gm.Cheated = true; }   // числа баланса правят на лету: рекорд не засчитывается
        }
        void OnGUI()
        {
            if (!shown || gm.State != GameState.Playing) return;
            GUI.depth = -50;
            float width = Mathf.Min(620f, Screen.width - 40f);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, 20f, width, Screen.height - 40f), GUI.skin.box);
            GUILayout.Label("БАЛАНС — изменения действуют сразу (F7)");
            for (int i = 0; i < fields.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Names[i], GUILayout.Width(width * 0.4f));
                float old = (float)fields[i].GetValue(null);
                float value = GUILayout.HorizontalSlider(old, 0f, Maximum[i]);
                if (Mathf.Abs(old - value) > 0.00001f) fields[i].SetValue(null, value);
                GUILayout.Label(value.ToString("0.000"), GUILayout.Width(60f));
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Исходные значения")) GameConfig.ResetDefaults();
            if (GUILayout.Button("Закрыть")) { shown = false; gm.SetPaused(wasPaused); }
            GUILayout.EndArea();
        }
    }
}
