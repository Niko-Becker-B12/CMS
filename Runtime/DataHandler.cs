using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor.Build.Content;
using System.Threading.Tasks;
using System.Xml.Linq;
using static UnityEngine.Experimental.Rendering.RayTracingAccelerationStructure;
using UnityEditor.PackageManager;
using Hextant;
using UnityEngine.Video;
using UnityEditor;
using System.Threading;
using Sirenix.OdinInspector;


namespace B12.CMS
{


public static class DataHandler
{

    //Invoked per File/asset
    public static event Action<DownloadData> OnDownloadDataFileComplete;
    public static event Action<UnityEngine.Object, string> OnDownloadAssetFileComplete;
    public static event Action OnDownloadAssetFileFailed;

    //Invoked once everything is finished!
    public static event Action OnDownloadFinished;

    public static List<string> downloadedDataLocalPaths = new List<string>();

    public static List<DataGroup> assetTags = new List<DataGroup>();

    //Temp Data:
    [Searchable]
    public static List<DownloadData> downloadedDataFiles = new List<DownloadData>();
    [Searchable]
    public static List<ComponentDataObjectBase> downloadedDataObjects = new List<ComponentDataObjectBase>();
    [Searchable]
    public static List<UnityEngine.Object> downloadedAssets = new List<UnityEngine.Object>();

    public static string cmsDepotUrl = "";
    public static string cmsStartUrl = "";

    // Added CancellationTokenSource to manage task cancellation
    private static CancellationTokenSource cancellationTokenSource;

    // Initialie the hashSet 
    private static HashSet<string> processedFiles = new HashSet<string>();
    public static async void StartDownload()
    {
        Debug.Log("StartDownload: Initiating download process.");

        // Clear lists at the start
        downloadedDataFiles.Clear();
        downloadedDataLocalPaths.Clear();
        downloadedDataObjects.Clear();

        processedFiles.Clear();

        // Ensure necessary directories exist
        if (!Directory.Exists($"{Application.persistentDataPath}/Data"))
        {
            //if it doesn't, create it
            Directory.CreateDirectory($"{Application.persistentDataPath}/Data");

        }

        if (!Directory.Exists($"{Application.persistentDataPath}/Assets"))
        {
            //if it doesn't, create it
            Directory.CreateDirectory($"{Application.persistentDataPath}/Assets");

        }

        // Assign URLs from settings
        cmsDepotUrl = CmsSettings.instance.DepotUrl;
        cmsStartUrl = CmsSettings.instance.StartUrl;

        Debug.Log($"StartDownload: Created directories if not exist, assigned URLs from settings " +
            $"(cmsDepotUrl: {cmsDepotUrl}, cmsStartUrl: {cmsStartUrl}), initialized DataInterpreter, and awaiting DownloadDataAsync.");

        DataInterpreter interpreter = new DataInterpreter();


#if UNITY_EDITOR
        // Subscribe to play mode state change event
        EditorApplication.playModeStateChanged += HandleEvents;
#endif

        // Initilialize the CancellationTokenSource
        cancellationTokenSource = new CancellationTokenSource();

        try
        {
            //if(!CmsSettings.instance.RedownloadFiles)
            //{
            //    // Skip the download phase and handle the already downloaded data
            //    Debug.Log("download phase skipped");
            //    await LocalFileChecker(cancellationTokenSource.Token);
            //}
            //else
            //{
            //    // Start the download process with cancellation support
            //    Debug.Log("download phase data is happend");
            //    await DownloadDataAsync(cancellationToken: cancellationTokenSource.Token);
            //}

            await DownloadDataAsync(cancellationToken: cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Download was canceled");
        }
    }

#if UNITY_EDITOR
    static void HandleEvents(PlayModeStateChange state)
    {

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            // Clear downloaded data lists
            downloadedDataLocalPaths.Clear();
            downloadedDataObjects.Clear();
            downloadedDataFiles.Clear();
            processedFiles.Clear();

            // Cancel any ongoing async tasks
            cancellationTokenSource?.Cancel();

            // Unsubscribe from the play mode state change event
            EditorApplication.playModeStateChanged -= HandleEvents;

        }

    }
#endif

    public static async Task LocalFileChecker(CancellationToken cancellationToken)
    {
        Debug.Log("LocalFileChecker: Processing already downloaded files.");

        // Define the path to the directory containing the downloaded data files
        string path = $"{Application.persistentDataPath}/Data/";
        DirectoryInfo dir = new DirectoryInfo(path);
        FileInfo[] files = dir.GetFiles("*.json");

        // Processs each file only once
        foreach (FileInfo file in files)
        {   
            // Check if the file has already been processed
            if (processedFiles.Contains(file.Name))
            {
                Debug.LogWarning($"File {file.Name} is already processed. Skipping to avoid infinite loop.");
                continue;
            }

            await ProcessExistingFileAsync(file, cancellationToken);

            // Mark the file as processed
            processedFiles.Add(file.Name);
        }
        
        // Invoke the OnDownloadFinished event after handling all files I scream to the void
        OnDownloadFinished?.Invoke();
        
    }

    private static async Task ProcessExistingFileAsync(FileInfo file, CancellationToken cancellationToken)
    {
        Debug.Log($"ProcessFileAsync: Processing file: {file.Name}");

        // Check for cancellation
        cancellationToken.ThrowIfCancellationRequested();

        // Read the content of the file asynchronously
        string downloadedDataContent = await File.ReadAllTextAsync(file.FullName);
        // Extract the file name without extension
        string fileName = Path.GetFileNameWithoutExtension(file.Name);

        // Parse the JSON content to extract the collection name
        JObject jsonObject = JObject.Parse(downloadedDataContent);
        string collectionName = jsonObject["ContentType"]?.ToString() ?? "default";

        // Create a DownloadData object with the extracted information
        DownloadData downloadMetadata = new DownloadData()
        {
            fileName = fileName,
            collectionName = collectionName,
            fileType = ".json",
            jsonObject = jsonObject
        };

        // Call the HandleDownloadedDataAsync with the correct parameters
        await HandleDownloadedDataAsync(downloadedDataContent, fileName, downloadMetadata, false, cancellationToken);
    }


    public static string ConstructUrl(string fileName, DownloadData downloadData)
    {
        Debug.Log($"ConstructUrl for {fileName} {downloadData?.fileName}{downloadData?.fileType}");
        string url = "";

        if (string.IsNullOrWhiteSpace(fileName) || downloadData == null)
        {
            // if Cms.Seetings.instance.RedownloadFiles is true, we will always download the files from the server else we lie to the system and we create a local URl which is a path to the file
            if (CmsSettings.instance.RedownloadFiles)
                url = $"{cmsStartUrl}";
            else
                url = $"{Application.persistentDataPath}/Data/start.json";
        }
        else
        {   
            // if Cms.Seetings.instance.RedownloadFiles is true, we will always download the files from the server else we lie to the system and we create a local URl which is a path to the file
            if (CmsSettings.instance.RedownloadFiles)
                url = $"{cmsDepotUrl}".Replace("##collection##", $"{downloadData.collectionName}").Replace("##uid##", $"{downloadData.fileName}").Replace("##fileType##", $"{downloadData.fileType}");
            else
                url = $"{Application.persistentDataPath}/Data/{fileName}{downloadData.fileType}";
        }

        Debug.Log($"Created URL: {url}");

        return url;
    }

    private static async Task<string> FetchDataFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        Debug.Log("FetchDataFromUrlAsync from " + url);
        using (UnityWebRequest uwr = UnityWebRequest.Get(url))
        {
            // Creates a UnityWebRequest to perform a GET request to the specified URL
            UnityWebRequestAsyncOperation asyncOp = uwr.SendWebRequest();
            while (!asyncOp.isDone)
            {
                // Check for cancellation
                cancellationToken.ThrowIfCancellationRequested();
                // Sends the web request asynchronously and waits for it to complete using await Task.Yield() in a loop 
                await Task.Yield();
            }
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                // Checks if the request was unsuccessful and logs an error if so, returning null
                Debug.LogError($"URL: {url} - {uwr.error}");
                return null;
            }
            // Returns the downloaded data as a string from the downloadHandler
            return uwr.downloadHandler.text;
        }
    }

    private static Task<DownloadData> HandleDownloadedDataAsync(string downloadedDataContent, string fileName, DownloadData downloadMetadata, bool handleAsChild, CancellationToken cancellationToken)
    {
        Debug.Log($"HandleDownloadedDataAsync | {fileName}");

        // Check for cancellation
        cancellationToken.ThrowIfCancellationRequested();

        // Check if the file name is already processed
        if (downloadedDataFiles.Any(d => d.fileName == fileName))
        {
            Debug.LogWarning($"File {fileName} is already processed. Skipping to avoid infinite loop.");
            return Task.FromResult<DownloadData>(null);
        }

        // If filename is empty or Null, create a new Downloaddata object with default values
        if (string.IsNullOrWhiteSpace(fileName))
        {
            
            DownloadData startFileData = new DownloadData()
            {
                fileName = "start",
                collectionName = "start",
                fileType = ".json",
                jsonObject = JObject.Parse(downloadedDataContent)
            };

            Debug.Log($"HandleDownloadedDataAsync, filename is null so we created an new objectt | {startFileData}");
            return Task.FromResult(startFileData);
        }
        else
        {
            Debug.Log($"HandleDownloadedDataAsync, filename is not null | {fileName}");
            
            // Extract contentType from the jsonObject of the downloadData object  
            string contentType = downloadMetadata.jsonObject["ContentType"].ToString();

            // Create a new DownloadData obejct with values from the existing object we have passed
            // with the extracted ContentType
            DownloadData newDownloadData = new DownloadData()
            {
                fileName = downloadMetadata.fileName,
                collectionName = contentType, // heeeeere
                fileType = downloadMetadata.fileType,
                jsonObject = JObject.Parse(downloadedDataContent)
            };

            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            if (!handleAsChild)
            {   
                Debug.Log($"Adding newDownloadData to downloadedDataFiles | {newDownloadData}");
                downloadedDataFiles.Add(newDownloadData);
            }

            if (newDownloadData != null)
            {   
                Debug.Log($"Invoking OnDownloadDataFileComplete with newDownloadData | {newDownloadData}");
                OnDownloadDataFileComplete?.Invoke(newDownloadData);
            }

            return Task.FromResult(newDownloadData);
        }
    }

    public static async Task<DownloadData> DownloadDataAsync(string fileName = "", DownloadData downloadMetadata = null, bool handleAsChild = false, CancellationToken cancellationToken = default)
    {

        //If(settings.download == false) -> skip

        Debug.Log($"Async: DownloadDataAsy | {fileName}");

        // Step 1: Construct the URL for the download
        string url = ConstructUrl(fileName, downloadMetadata);

        //#if UNITY_EDITOR
        //        string path = $"{CmsSettings.instance.EditorBasedSaveLocation}";
        //#else
        //        string path = $"{Application.persistentDataPath}/Data/";
        //#endif

        string path = $"{Application.persistentDataPath}/Data/";

        // Step 2: Determine the file path
        if (string.IsNullOrWhiteSpace(fileName))
            path += $"start.json";
        else
            path += $"{fileName}{downloadMetadata.fileType}";

        // Step 3: Check if the pat has already been used in the download cycle
        if (downloadedDataLocalPaths.Contains(path))
        {
            Debug.Log("Path was already used in this download cycle!");
            return null;
        }

        // Step 4: Add the path to the list of used paths
        downloadedDataLocalPaths.Add(path);

        Debug.Log($"Downloading File from {url} and saved to {path}");

        // init the downloadedDataContent
        string downloadedDataContent = null;

        // Step 5: Fetch data from the URL
        // if Cms.Settings.instance.RedownloadFiles is true, we will redownload the files from the servers
        // else we will skip fetching the data from the URL since we already have the data in the local path
        if (!CmsSettings.instance.RedownloadFiles && File.Exists(path))
        {
            Debug.Log("File already exists, skipping download.");
            string downloadedData = File.ReadAllText(path);
            Debug.Log($"Download data content returned from the local file in step 5 is: {downloadedData}");
            downloadMetadata = await HandleDownloadedDataAsync(downloadedData, fileName, downloadMetadata, handleAsChild, cancellationToken);
            //return downloadMetadata;
        }
        else
        {
            downloadedDataContent = await FetchDataFromUrlAsync(url, cancellationToken);
            Debug.Log($"Download data content returned from the webrequest in step 5 is: {downloadedDataContent}");
        }

        Debug.Log("Helloooooo");

        // Check for Download Failure, if null throw an Exception
        if (downloadedDataContent == null)
        {   
            throw new Exception("Failed to download or read the data.");
        }

        // Step 6: Parse the download data into the downloadData object if it exists
        if (downloadedDataContent != null && downloadMetadata != null)
        {   
            Debug.Log("Downloaded data is being parsed into the downloadData object, step 6");
            downloadMetadata.jsonObject = JObject.Parse(downloadedDataContent);
        }
        // same logic as above I dont need to write the existing data to the file
        // Step 7: Write the downloaded data to the file
        if (CmsSettings.instance.RedownloadFiles)
        {   
            Debug.Log("RedownloadFiles is true, writing the downloaded data to the file");
            File.WriteAllText(path, downloadedDataContent);
        }

        Debug.Log($"fileName is {fileName}");
        // Step 8: Handle the downloaded data 
        if (string.IsNullOrWhiteSpace(fileName))
        {   
            Debug.Log("Start file data is being handled");
            DownloadData startFileData = await HandleDownloadedDataAsync(downloadedDataContent, "", null, false, cancellationToken);
            Debug.Log($"start file data is: {startFileData}");
            Debug.Log($"{startFileData.fileName}");

            if (startFileData != null) {
                Debug.Log($"We are invoking OnDownloadDataFileComplete where the Intepreter Slection is register");
                Debug.Log($"{startFileData.fileType}");
                OnDownloadDataFileComplete?.Invoke(startFileData);
            }

            return startFileData;
        }
        else
        {   
            Debug.Log("Normal file data is being handled");
            downloadMetadata = await HandleDownloadedDataAsync(downloadedDataContent, fileName, downloadMetadata, handleAsChild, cancellationToken);
            return downloadMetadata;
        }
    }


    public static async Task DownloadAssetAsync(string assetFileName, string assetHash, string assetType, string parentGUID, DownloadData newAssetData)
    {

        downloadedDataFiles.Add(newAssetData);

        string jsonString = JsonConvert.SerializeObject(newAssetData.jsonObject, Formatting.Indented);

        Debug.Log($"DownloadData.jsonObject:\n{jsonString}");


        string url = ConstructUrl(assetFileName, newAssetData);
        Debug.Log($"Download Asset Url is {url}");
        string path = $"{Application.persistentDataPath}/Assets/{assetHash}{assetType}";  
        Debug.Log($"Downloading Asset {assetFileName} from URL: {url} for Data Parent {parentGUID}");


        UnityEngine.Object obj = null;

        if (File.Exists(path))
        {

            obj = downloadedAssets.Find(x => x.name == assetHash);

        }

        if (obj != null)
        {

            Debug.Log($"File exist at {path} with name {obj?.name}");

            OnDownloadAssetFileComplete?.Invoke(obj, parentGUID);

            return;

        }

        using (var uwr = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
        {
            uwr.downloadHandler = new DownloadHandlerFile(path);
            UnityWebRequestAsyncOperation asyncOp = uwr.SendWebRequest();
            while (!asyncOp.isDone)
            {
                await Task.Yield();
            }

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"URL: {url} - {uwr.error}");
                OnDownloadAssetFileFailed?.Invoke();
            }
            else
            {
                Debug.Log($"CreateAsset is getting called with {assetType} as file type and {path} as path");
                obj = CreateAsset(assetType, path);

                if (obj == null)
                {
                    Debug.LogError($"Failed to create asset from file: {path}");
                    OnDownloadAssetFileFailed?.Invoke();
                    return;

                }
                
                // How does the obj looks after the CreateAsset method
                Debug.Log($"Created Asset returns: {obj} to be passed in AssignAsset");

                obj.name = assetHash;
                Debug.Log($"Invoking OnDownloadAssetFileComplete with {obj} and {parentGUID}");

                downloadedAssets.Add(obj);
                OnDownloadAssetFileComplete?.Invoke(obj, parentGUID);
            }
        }
    }


    // asset creation logic
    // for now only pictures and audio 
    public static UnityEngine.Object CreateAsset(string fileType, string path)
    {
        Debug.Log("Flow: createasset");
        // class reference 
        DownloadData downloadedAssetData = new DownloadData();
        if (fileType == ".jpg" ||
            fileType == ".png" ||
            fileType == ".jpeg" ||
            fileType == ".gif" ||
            fileType == ".webp")
        {
            downloadedAssetData.collectionName = "asset-texture";
            Texture2D tex = new Texture2D(960, 540);
            tex.LoadImage(File.ReadAllBytes(path), false);
            tex.Apply(true, false);
            Debug.Log("texture created");
            return tex;
        }
        else if (fileType == ".mp3" ||
            fileType == ".ogg" ||
            fileType == ".MP3" ||
            fileType == ".OGG" ||
            fileType == ".wav")
        {
            downloadedAssetData.collectionName = "asset-audio";
            AudioClip clip = AudioClip.Create(Path.GetFileName(path), 44100 * 2, 1, 44100, true);
            return clip;
        }
        else if (fileType == ".webm" ||
            fileType == ".mp4")
        {
            downloadedAssetData.collectionName = "asset-video";
            Debug.Log("Video Thing dose not work");
            return null;
        }
        else
        {
            Debug.Log("No asset created");
            return null;
        }
    }

}



[System.Serializable]
[Searchable]
public class DownloadData
{

    public string fileName;

    public string Name => fileName;

    public string collectionName;
    public string fileType;
    [HideInInspector]
    public JObject jsonObject;
    public List<DownloadData> children = new List<DownloadData>();

}
}