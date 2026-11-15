#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PokaGoryu.EditorTools
{
    /// <summary>
    /// Меню Pokagoryu: одним кликом создаёт сцену Assets/Scenes/Main.unity и добавляет её в Build Settings
    /// (нужно только для сборки: для Play в редакторе ничего делать не надо).
    /// </summary>
    public static class PokaGoryuMenu
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>Печатает карту города в Console: # здание, . улица, , переулок, o площадь, = тоннель, W стена Тьмы,
        /// H очаг, S старт, B автобус, g генератор, 1-9 и A — выжившие. Править карту: CityMap.Layout().</summary>
        [MenuItem("Pokagoryu/Print City Map (ASCII)")]
        public static void PrintCityMap()
        {
            CityMap.Build();
            Debug.Log("[Тлеющая тьма] Карта города (сверху север):\n" + CityMap.ToAscii());
        }

        [MenuItem("Pokagoryu/Create Main Scene and Build Settings")]
        public static void CreateMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.Refresh();
            Debug.Log("[Тлеющая тьма] Сцена создана: " + ScenePath + " и добавлена в Build Settings. Нажмите Play.");
        }
    }
}
#endif
