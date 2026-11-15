using UnityEditor;
using UnityEngine;

namespace PokaGoryu.EditorTools
{
    /// <summary>
    /// Музыка из Assets/Resources/Music при первом импорте: Vorbis, в памяти хранится сжатой (на старте игры дорожка не
    /// распаковывается целиком), стерео и частота как в файле. Потом настройки можно менять вручную: они не перезаписываются.
    /// </summary>
    public sealed class MusicImportSettings : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Resources/Music/") || !assetImporter.importSettingsMissing) return;
            AudioImporter importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.8f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }
    }
}
