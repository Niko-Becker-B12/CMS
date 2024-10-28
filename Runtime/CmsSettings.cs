using Hextant;
using Hextant.Editor;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;


namespace B12.CMS
{

    [Settings(SettingsUsage.RuntimeProject, "B12/CMS Settings")]
    public class CmsSettings : Settings<CmsSettings>
    {

        [SettingsProvider]
        public static SettingsProvider GetSettingsProvider() => instance.GetSettingsProvider();

        [SerializeField]
        string depotUrl;

        public string DepotUrl
        {
            get => depotUrl;
            set => Set(ref depotUrl, value);
        }

        [SerializeField]
        string startUrl;

        public string StartUrl
        {
            get => startUrl;
            set => Set(ref startUrl, value);
        }

        [SerializeField]
        List<DataGroup> assetTags = new List<DataGroup>();

        public List<DataGroup> AssetTags
        {
            get => assetTags;
            set => Set(ref assetTags, value);
        }

        [SerializeField]
        string editorBasedSaveLocation;

        public string EditorBasedSaveLocation
        {
            get => editorBasedSaveLocation;
            set => Set(ref editorBasedSaveLocation, value);
        }

        [SerializeField]
        bool redownloadFiles;

        public bool RedownloadFiles
        {
            get => redownloadFiles;
            set => Set(ref redownloadFiles, value);
        }


        private void Awake()
        {



        }

        private void OnEnable()
        {

#if UNITY_EDITOR
            SaveSettingsToJson();
#else
            if (File.Exists($"{Application.persistentDataPath}/CMS-Settings.json"))
            {

                string jsonString = File.ReadAllText($"{Application.persistentDataPath}/CMS-Settings.json");

                JsonUtility.FromJsonOverwrite(jsonString, CmsSettings.instance);

            }
            else
            {

                SaveSettingsToJson();

            }
#endif

        }

        public static void SaveSettingsToJson()
        {

            string jsonString = JsonUtility.ToJson(CmsSettings.instance, true);

            File.WriteAllText($"{Application.persistentDataPath}/CMS-Settings.json", jsonString);

        }

    }
}