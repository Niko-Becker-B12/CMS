using Hextant;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using Hextant.Editor;
using UnityEditor;
#endif


namespace B12.CMS
{

#if UNITY_EDITOR
    [Settings(SettingsUsage.RuntimeProject, "B12/CMS Settings")]
#endif
    public class CmsSettings : Settings<CmsSettings>
    {

#if UNITY_EDITOR
        [SettingsProvider]
        public static SettingsProvider GetSettingsProvider() => instance.GetSettingsProvider();
#endif

        [SerializeField]
        string depotUrl;

        public string DepotUrl
        {
            get => depotUrl;
            set => depotUrl = value;
        }

        [SerializeField]
        string startUrl;

        public string StartUrl
        {
            get => startUrl;
            set => startUrl = value;
        }

        [SerializeField]
        List<DataGroup> assetTags = new List<DataGroup>();

        public List<DataGroup> AssetTags
        {
            get => assetTags;
            set => assetTags = value;
        }

        [SerializeField]
        string editorBasedSaveLocation;

        public string EditorBasedSaveLocation
        {
            get => editorBasedSaveLocation;
            set => editorBasedSaveLocation = value;
        }

        [SerializeField]
        bool redownloadFiles;

        public bool RedownloadFiles
        {
            get => redownloadFiles;
            set => redownloadFiles = value;
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