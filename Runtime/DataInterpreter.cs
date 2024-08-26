using Newtonsoft.Json.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace B12.CMS
{

    public class DataInterpreter
    {
        public static List<DataGroup> assetTags = new List<DataGroup>();
        public List<string> childGUIDs = new List<string>();
        public static List<ComponentDataObjectBase> dataObjectsForAssignAsset = new List<ComponentDataObjectBase>();

        // event to trigger handle asset assgingnemt
        public static event Action<UnityEngine.Object, string> OnAssignAsset;

        private CancellationTokenSource cancellationTokenSource;

        // Why when I rename this method the class also gets renamed?
        public DataInterpreter()
        {

            Debug.Log("Register events and instanciate AssetTags");

            assetTags = CmsSettings.instance.AssetTags;
            Debug.Log($"AssetTags: {string.Join(", ", assetTags.Select((x) => x.assetTag))}");

            // Initialize the CancellationTokenSource
            cancellationTokenSource = new CancellationTokenSource();

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += HandleEvents;
#endif

            DataHandler.OnDownloadDataFileComplete += SelectInterpreter;
            DataHandler.OnDownloadAssetFileComplete += AssignAsset;
            OnAssignAsset += AssignAsset;
        }

#if UNITY_EDITOR
        void HandleEvents(PlayModeStateChange state)
        {

            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Cancel the CancellationTokenSource
                cancellationTokenSource?.Cancel();

#if UNITY_EDITOR
                EditorApplication.playModeStateChanged -= HandleEvents;
#endif

                DataHandler.OnDownloadDataFileComplete -= SelectInterpreter;
                DataHandler.OnDownloadAssetFileComplete -= AssignAsset;

                // Optionally, I reinitialize the CancellationTokenSource if needed
                // cancellationTokenSource = new CancellationTokenSource();

            }

        }
#endif

        private void SelectInterpreter(DownloadData downloadData)
        {

            Debug.Log("Select Interpreter and pass downloadData Object");
            // Check if cancellationTokenSource is null
            if (cancellationTokenSource == null)
            {
                Debug.LogWarning("CancellationTokenSource was null. Initializing a new CancellationTokenSource.");
                cancellationTokenSource = new CancellationTokenSource();
            }

            if (downloadData.fileName == "start")
            {
                InterpretStartFile(downloadData, cancellationTokenSource.Token);
            }
            else
            {
                InterpretFile(downloadData, cancellationTokenSource.Token);
            }
        }

        async void InterpretStartFile(DownloadData downloadData, CancellationToken token)
        {

            Debug.Log("Flow: InterPret start");
            List<JToken> tokens = downloadData.jsonObject.Children().ToList();
            Debug.Log($"The list of InterpretStartFIle holds{string.Join(", ", tokens)}");

            List<JToken> childTokens = downloadData.jsonObject["ContentTreeChildren"].Children().ToList();

            for (int i = 0; i < childTokens.Count; i++)
            {

                // Check for cancellation
                token.ThrowIfCancellationRequested();
                string childUid = childTokens[i].ToString();

                DownloadData startingChildDownloadMetaData = new DownloadData()
                {
                    fileName = childUid,
                    fileType = ".json",
                    //collectionName = "levels"
                    collectionName = downloadData.jsonObject["ContentType"]?.ToString() ?? "defaultCollection" // Assign collectio 
                };

                if (CmsSettings.instance.RedownloadFiles)
                {
                    await DataHandler.DownloadDataAsync(childUid, startingChildDownloadMetaData, false, token);
                }
                else
                {
                    Debug.Log("LocalFileChecker for the start json");
                    await DataHandler.LocalFileChecker(token);
                }


            }

        }

        async void InterpretFile(DownloadData downloadData, CancellationToken token)
        {
            Debug.Log("Flow: InterpretFile");
            if (downloadData == null)
                return;

            Debug.Log($"{downloadData.collectionName} - {downloadData.fileName}");

            // Identify all the properties in the JSON object associated with downloadData
            List<string> jsonProperties = downloadData.jsonObject.Cast<JProperty>().Select(x => x.Name).ToList();

            // Process each property in the JSON object to determine if it matches any known asset tags
            // I need to refactor this to be more efficient later
            foreach (var property in jsonProperties)
            {

                //Debug.Log($" We are inside the foreach to chech for known asset tags. Property: {property}");
                // Check for cancellation
                token.ThrowIfCancellationRequested();

                var assetTag = assetTags.Find(x => x.assetTag == property);
                if (assetTag != null)
                {
                    Debug.Log($"{downloadData.fileName}{downloadData.collectionName} contains {assetTag.assetTag} with " +
                        $"{downloadData.jsonObject.Root["data"][assetTag.assetTag].Children().Count()} Children");

                    // Extract child elements from the JSON property
                    List<JToken> results = downloadData.jsonObject[assetTag.assetTag].Children().ToList();

                    // Process results outside the outer loop
                    foreach (var result in results)
                    {
                        // Check for cancellation
                        token.ThrowIfCancellationRequested();

                        // Create DownloadData instances for each child
                        DownloadData childDownloadMetaData = new DownloadData()
                        {
                            fileName = result["id"].ToString(),
                            fileType = ".json",
                            collectionName = assetTag.assetTag
                        };

                        // Add these instances to the children list of downloadData
                        downloadData.children.Add(childDownloadMetaData);

                        Debug.Log($"{result} - {childDownloadMetaData.fileName} - {childDownloadMetaData.collectionName}");

                        // Initiate asynchronous downloads for each child or skip and handle the already existing files
                        if (CmsSettings.instance.RedownloadFiles)
                        {
                            await DataHandler.DownloadDataAsync(childDownloadMetaData.fileName, childDownloadMetaData, false, token);
                        }
                        else
                        {
                            await DataHandler.LocalFileChecker(token);
                        }
                    }
                }
            }

            // Handle ContentTreeChildren separately
            if (downloadData.jsonObject["ContentTreeChildren"] != null)
            {
                Debug.Log("ContentTreeChildren is not null, but it can be an emtpy array");
                List<JToken> childTokens = downloadData.jsonObject["ContentTreeChildren"].Children().ToList();
                Debug.Log($"the list with the json children: {string.Join(", ", childTokens)} the father is {downloadData.fileName}");
                Debug.Log($"the number of childTokens is {childTokens.Count}");

                // Check if there are any children
                if (childTokens.Count > 0)
                {
                    foreach (var childToken in childTokens)
                    {
                        // Check for cancellation
                        token.ThrowIfCancellationRequested();
                        string childUid = childToken.ToString();

                        // Create DownloadData instances for each child
                        DownloadData childDownloadMetaData = new DownloadData()
                        {
                            fileName = childUid,
                            fileType = ".json",
                            collectionName = downloadData.jsonObject["ContentType"]?.ToString()
                        };

                        // Add the new DownloadData to the children list
                        downloadData.children.Add(childDownloadMetaData);

                        // Log the newDownloadData details
                        Debug.Log($"Created new DownloadData: {childDownloadMetaData.fileName} - {childDownloadMetaData.collectionName}");

                        // Initiate asynchronous downloads for each child or skip and handle the already existing files
                        if (CmsSettings.instance.RedownloadFiles)
                        {
                            await DataHandler.DownloadDataAsync(childDownloadMetaData.fileName, childDownloadMetaData, false, token);
                        }
                        else
                        {
                            Debug.Log("LocalFileChecker for the children json");
                            await DataHandler.LocalFileChecker(token);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"{downloadData.fileName} has zero children in ContentTreeChildren.");
                }
            }
            else
            {
                Debug.LogWarning($"{downloadData.fileName} has no ContentTreeChildren property.");
            }

            Debug.Log($"we exit the child loop and we have collection name of: {downloadData.collectionName}");

            // Create a new ScriptableObject instance to store the data.
            ComponentDataObjectBase generatedParentFile;

            // Checks if the collectionName of the downloadData matches any assetTag in the assetTags list
            var matchingAssetTag = assetTags.Find(x => x.assetTag == downloadData.collectionName);
            if (matchingAssetTag != null)
            {
                Debug.Log($"collectionName: {downloadData.fileName} has asset tag");

                // Create an instance of a ScriptableObject with type taken from associated with the assetTag
                generatedParentFile = ComponentDataObjectBase.CreateInstance(matchingAssetTag.scriptableObject.GetType()) as ComponentDataObjectBase; // Cast the instance to ComponentDataObjectBase scriptableObject
            }
            else // didn't find any tag for this object -> bad
            {
                Debug.Log($"collectionName: {downloadData.fileName} has no asset tag");

                generatedParentFile = (ComponentDataObjectBase)ComponentDataObjectBase.CreateInstance(typeof(ComponentDataObjectBase));
            }

            generatedParentFile.guid = downloadData.jsonObject["Uid"].ToString();
            generatedParentFile.name = downloadData.jsonObject["Uid"].ToString();

            string[] formattedTitle = downloadData.jsonObject["Name"].ToString().Split('(');
            generatedParentFile.title = formattedTitle[0];

            DataHandler.downloadedDataObjects.Add(generatedParentFile);
            dataObjectsForAssignAsset.Add(generatedParentFile);

            // Count the number of JSON files having the property "AssetDataSet"
            if (jsonProperties.Contains("AssetDataSet"))
            {
                Debug.Log("jsonProperties contains 'AssetDataSet'");
            }

            if (jsonProperties.Contains("AssetDataSet") && downloadData.jsonObject["AssetDataSet"] != null)
            {
                Debug.Log("wow AssetDataSet is not null wow wow wow");

                // Handle the checks: is "AssetDataSet" JObject or JValue.(Damn I hate Json :D)
                var assetDataSet = downloadData.jsonObject["AssetDataSet"];
                if (assetDataSet is JObject)
                {
                    Debug.Log($"assetDataSet is JObject with: {assetDataSet.ToString()}");

                    var assetFileProperties = assetDataSet["AssetFileProperties"];
                    if (assetFileProperties is JObject)
                    {
                        Debug.Log($"assetFileProperties is JObject is a {assetFileProperties.GetType()} with value {assetFileProperties}");

                        // if file hush dose not exist we have a problem
                        if (assetFileProperties is JObject jObject && jObject.ContainsKey("FileHash"))
                        {
                            Debug.Log($"FileHash exists in {downloadData.fileName} {downloadData.collectionName}");
                        }



                        // Add detailed logging for FileHash and FileType
                        var fileHashToken = assetFileProperties["FileHash"];
                        var fileTypeToken = assetFileProperties["FileType"];

                        Debug.Log($"FileHash Token: {fileHashToken}");
                        Debug.Log($"FileType Token: {fileTypeToken}");

                        var assetHash = fileHashToken?.ToString();
                        var assetType = $".{fileTypeToken?.ToString()}";

                        if (!string.IsNullOrEmpty(assetHash))
                        {
                            Debug.Log($"Data {generatedParentFile.name}{generatedParentFile.title} contains an AssetDataSet Property with FileHash: {assetHash} Filetype: {assetType.ToString()}");
                            string assetName = assetHash;

                            Debug.Log($"wow {downloadData.fileName} | {downloadData.fileType} | {downloadData.collectionName} contains Asset {assetName}");

                            DownloadData newAssetData = new DownloadData
                            {
                                fileName = assetHash,
                                collectionName = downloadData.fileName,
                                fileType = assetType
                            };

                            downloadData.children.Add(newAssetData);


                            if (CmsSettings.instance.RedownloadFiles)
                            {

                                // Check the logs cause the condition checcks for assetHash and not all JSON files have it
                                if (assetHash == null)
                                {
                                    Debug.LogWarning($"AssetHash is null for {downloadData.fileName} {downloadData.collectionName}");
                                }

                                await DataHandler.DownloadAssetAsync(assetName, assetHash, assetType, downloadData.fileName, newAssetData);

                            }
                            else
                            {
                                //To-Do find asset file on disk

                                string path = $"{Application.persistentDataPath}/Assets/{assetName}{assetType}";

                                Debug.Log($"CreateAsset is getting called with {assetType} as file type and {path} as path");
                                UnityEngine.Object assetFile = DataHandler.CreateAsset(assetType, path);

                                // We need to invoke a new event we pass the asset to trigger SelectInterpreter my dude :D
                                OnAssignAsset?.Invoke(assetFile, downloadData.fileName);

                            }
                        }
                        else
                        {
                            Debug.LogWarning($"FileHash is null or empty. Checking for alternative asset identifiers {downloadData.jsonObject.ToString()}");
                        }
                    }
                    else
                    {
                        Debug.Log($"assetFileProperties is not JObject is a {assetFileProperties.GetType()} with value {assetFileProperties}");
                    }
                }
                else if (assetDataSet is JValue)
                {
                    JValue jValue = (JValue)assetDataSet;
                    if (jValue.Value != null)
                    {
                        Debug.Log($"assetDataSet is JValue with: {jValue.Value}");
                    }
                    else
                    {
                        // it seems that all of JValues are null
                        Debug.Log($"assetDataSet is JValue with null value");
                    }
                }
                else
                {
                    Debug.Log($"it does not work? AssetDataSet is of type {assetDataSet.GetType()} with value {assetDataSet}");
                }
            }
        }

        // problems I identified: a) depsite all jsonobjcts have the property AssetDataSet only a few (25) are looged from :Debug.Log("jsonProperties contains 'AssetDataSet'");
        // then this may be related with why only 4 are JObject and the rest are JValue and this for sure causes only 4 assets to be created
        // i need to chech how I assing and read the json object and see if I am missing something the rest should be fine
        private static void AssignAsset(UnityEngine.Object assetObject, string parentGUID)
        {
            // debug print the json object of the assetObject
            Debug.Log($"Assigning Asset object passed is {assetObject} ");

            if (!string.IsNullOrWhiteSpace(parentGUID))
            {
                Debug.Log($" parentGUID of: {parentGUID}");

                int index = dataObjectsForAssignAsset.FindIndex(x => x.guid == parentGUID);
                ComponentDataObjectBase foundDataObject = dataObjectsForAssignAsset.Find(x => x.guid == parentGUID);

                Debug.Log($"Checking for object {foundDataObject?.name} {index} | {parentGUID}");

                if (foundDataObject == null || index == -1)
                {
                    Debug.LogWarning($"FoundDataObject is null or index is -1, was looking for uid {parentGUID}");
                    //To-Do add event for others to know we stopped assigning the asset!
                    return;

                }

                // it seems that we are using the newAssetData lets see what objects are existing in the previous steps

                // instead of youing the newAssetData JSON object which is not existing in the previous steps we can direclty use the downloadedDataFiles

                // until here only 4 AseetDataSets with values truly exist
                Debug.Log($"DownloadedDataFiles found: {foundDataObject.name} {DataHandler.downloadedDataFiles[index].fileName} + {DataHandler.downloadedDataFiles[index].fileName != null}");
                Debug.Log($"DownloadedDataFiles fileName: {DataHandler.downloadedDataFiles[index].fileName}");

                // Check for the properties in the JSON object
                Debug.Log($"Checking for properties in the JSON object for {DataHandler.downloadedDataFiles[index].fileName}");
                List<string> fileNamesList = DataHandler.downloadedDataFiles.Select(x => x.fileName).ToList();

                // file hash and file type are not found in the JSON object but in the newAssetData object where we have saved us file name and file type accordingly
                var fileHash = DataHandler.downloadedDataFiles[index].fileName;
                var fileType = DataHandler.downloadedDataFiles[index].fileType;

                if (!string.IsNullOrEmpty(fileHash))
                {
                    Debug.Log($"Data {foundDataObject.name}{foundDataObject.title} contains an AssetDataSet Property with FileHash: {fileHash}");

                    // why is my flow breaking here?
                    // I am having the log coontains an AssetDataSet many times but then the Aseet of type only 4 as many as the Saved objects
                    // I need to check the flow of the code and see if I am missing something

                    string assetName = fileHash;
                    string assetType = fileType;

                    Debug.Log($"Asset {assetName} of type {assetType}");

                    //System.Reflection.FieldInfo propertyInfo = foundDataObject.GetType().GetField("Uid");

                    if (assetObject != null)
                    {
                        Debug.Log($"Found Object with name {assetObject.name} {foundDataObject.name}");
                        //propertyInfo.SetValue(foundDataObject, assetObject);
                        foundDataObject.childAssets.Add(assetObject);

                        // Save the object as an asset
                        SaveObjectAsAsset(foundDataObject);
                    }
                    else
                    {
                        Debug.LogWarning("Obj is null");
                    }
                }
            }
        }


        private static void SaveObjectAsAsset(ComponentDataObjectBase dataObject)
        {
            // Define the path where the asset will be saved
            string path = $"Assets/SavedDataObjects/{dataObject.name}.asset";

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create and save the asset
            AssetDatabase.CreateAsset(dataObject, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Saved {dataObject.name} as an asset at {path}");
        }

    } // end of the class

    [System.Serializable]
    public class DataGroup
    {

        public string assetTag;
        [TypeFilter("GetFilteredTypeList")]
        public ComponentDataObjectBase scriptableObject;

        public IEnumerable<Type> GetFilteredTypeList()
        {
            var enumerableList = typeof(ComponentDataObjectBase).Assembly.GetTypes()
                //.Where(x => !x.IsAbstract)                                          // Excludes BaseClass
                .Where(x => !x.IsGenericTypeDefinition)                             // Excludes C1<>
                .Where(x => typeof(ComponentDataObjectBase).IsAssignableFrom(x));                 // Excludes classes not inheriting from BaseClass


            return enumerableList;
        }

    }

}