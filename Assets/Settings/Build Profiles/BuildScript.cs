using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    public const string kVersionNumberBase = "0.01."; // Major version numbers are manually updated
    public const string kBuildRootPath = "Build"; // All builds are inside this top level project folder

    public const string kPlayerDirectory = "Player";

    public const bool kUseAssetBundle = false;
    public const string kAssetBundleDirectory = "AssetBundles";
    public const string kTextureSourceDirectory = "Assets/Textures";
    public const string kTextureSearchPattern = "*.png";


    // Global variable so that RegisterContentForPlayer can find the correct AssetBundles to include in the player build
    public static string gAppName = "";
    public static string gCurrentBuildRootPath = null;

    [MenuItem("Build/Build Active Profile")]
    public static void ManualBuildPlayerAndBundles()
    {
        _LoadAppNameFromRepoConfigFile();
        BuildPlayerAndBundles();
    }

    [MenuItem("Build/Build Active Profile (Dev)")]
    public static void ManualBuildPlayerAndBundlesDev()
    {
        _LoadAppNameFromRepoConfigFile();
        BuildPlayerAndBundlesDev();
    }

    public static void BuildPlayerAndBundles() => _BuildPlayerAndBundles(false);
    public static void BuildPlayerAndBundlesDev() => _BuildPlayerAndBundles(true);

    private static void _LoadAppNameFromRepoConfigFile()
    {
        // We're here:
        // ".../Boto.Survivor/Assets/Settings/Build Profiles/BuildScript.cs"
        // We want to get here:
        // ".../Boto.Survivor/Commands/build.config"

        // Get directory
        DirectoryInfo commandsFolder = new DirectoryInfo("Commands");
        if (!commandsFolder.Exists)
        {
            throw new BuildFailedException("No './Commands' folder inside directory. This should be included in the repo...");
        }

        // Attempt to find file
        foreach (var file in commandsFolder.EnumerateFiles())
        {
            if (file.Name == "build.config")
            {
                // Read file:
                // App name will be a single line:
                // APP_NAME="App Name"
                const string APP_NAME_KEY = "APP_NAME=";
                foreach (var line in File.ReadLines(file.FullName))
                {
                    var index = line.IndexOf(APP_NAME_KEY);
                    if (index < 0)
                        continue;

                    var comment = line.IndexOf('#');
                    if (comment >= 0 && comment < index)
                        continue;

                    gAppName = line.Substring(index + APP_NAME_KEY.Length);
                    return;
                }
            }
        }

        if (string.IsNullOrEmpty(gAppName))
        {
            Debug.LogError("No './Commands/build.config' file inside directory. " +
                           "This is created when using the build scripts in the './Commands' folder. " +
                           "Defaulting to fallback game name: 'Game'");
            gAppName = "Game";
        }
    }

    private static void _BuildPlayerAndBundles(bool development)
    {
        // Get all command line arguments
        string[] args = System.Environment.GetCommandLineArgs();
        Debug.Log("Unity Editor started with the following arguments:");

        // Iterate through and log each argument
        for (int i = 0; i < args.Length; i++)
        {
            Debug.Log("ARG " + i + ": " + args[i]);
        }

        // Example of checking for a specific custom argument (e.g., "-myCustomArg")
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-app-name" && i + 1 < args.Length)
            {
                gAppName = args[i + 1];
                Debug.Log("Found -app-name with value: " + gAppName);
            }
        }

        if (string.IsNullOrEmpty(gAppName))
            throw new BuildFailedException("Must add '-app-name \"something\"' to command line args.");

        var profile = BuildProfile.GetActiveBuildProfile();
        if (profile == null)
            throw new BuildFailedException("No active build profile is set." +
                                           "Use the Build Profiles window or the `-activeBuildProfile` cli argument");
        var buildsFolderPath = $"{kBuildRootPath}/{profile.name}";
        DirectoryInfo buildsFolderDir = new DirectoryInfo(buildsFolderPath);
        if (!buildsFolderDir.Exists) buildsFolderDir.Create();

        // Use a timestamp so that each build goes to a unique output folder
        var dateTimeFormat = "yyyy-MM-dd_HH-mm-ss";
        var timeStamp = System.DateTime.UtcNow.ToString(dateTimeFormat);
        
        // Delete 10th oldest build
        var folders = buildsFolderDir.GetDirectories();
        List<(DateTime time, DirectoryInfo dir)> folderTimes = new();
        List<(string version, DirectoryInfo dir)> folderVersions = new();
        foreach (var folder in folders)
        {
            var versionStringEnd = folder.Name.LastIndexOf('.');
            if (versionStringEnd == -1) continue;
            var folderVersion = folder.Name.Substring(0, versionStringEnd);
            folderVersions.Add((folderVersion, folder));

            var folderDT = folder.Name.Substring(versionStringEnd + 1);
            if (System.DateTime.TryParseExact(folderDT, dateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
            {
                folderTimes.Add((result, folder));
            }
        }

        folderTimes = folderTimes.OrderBy(x => x.time).ToList();
        Debug.Log($"Old builds:\n{string.Join("\n", folderTimes.Select(t => $"{t.dir.FullName}"))}");
        while (folderTimes.Count > 10)
        {
            Debug.Log($"Deleting build: {folderTimes[0].dir.FullName}...");
            Directory.Delete(folderTimes[0].dir.FullName, true);
            folderTimes.RemoveAt(0);
        }

        // Get the version for the current version number and order them to find the new build index
        folderVersions = folderVersions.Where(x =>
        {
            var latestSubVersion = x.version.Substring(0, x.version.LastIndexOf('.') + 1);
            return latestSubVersion == kVersionNumberBase;
        }).OrderBy(x => x.version).ToList();
        string latestVersion = kVersionNumberBase + "000";
        if (folderTimes.Count > 0)
        {
            Debug.Log($"Latest version: {folderVersions[^1].version}");
            latestVersion = folderVersions[^1].version;
        }

        var currentVersion = kVersionNumberBase + (int.Parse(latestVersion.Substring(latestVersion.LastIndexOf('.') + 1)) + 1).ToString("000");
        Debug.Log($"Setting current version: {currentVersion}");
        PlayerSettings.bundleVersion = currentVersion;

        // Start making the build now we have the version number info
        gCurrentBuildRootPath = $"{buildsFolderPath}/{currentVersion}.{timeStamp}";
        Debug.Log($"New build target path: {gCurrentBuildRootPath}");

        // Build the player
        var playerBuildOptions = new BuildPlayerWithProfileOptions()
        {
            buildProfile = profile,
            locationPathName = CreatePlayerOutputPath(gCurrentBuildRootPath),

            // These options can be adjusted as needed.
            // Note: the development and compression flags come from the build profile
            options = BuildOptions.CleanBuildCache | BuildOptions.StrictMode
        };

        if (kUseAssetBundle)
        {
            // Build AssetBundles so that they can be shipped inside the player
            var assetBundleBuildPath = BuildAssetBundles(gCurrentBuildRootPath);

            // To preserve types used by the AssetBundles
            playerBuildOptions.assetBundleManifestPath = assetBundleBuildPath + "/AssetBundles.manifest";
        }

        // Convenient for manual testing
        if (!Application.isBatchMode)
            playerBuildOptions.options |= BuildOptions.AutoRunPlayer;

        // Development build flag (webgl compression is disabled for dev builds!)
        if (development)
            playerBuildOptions.options |= BuildOptions.Development;


        var report = BuildPipeline.BuildPlayer(playerBuildOptions);
        var buildRootPath = gCurrentBuildRootPath;
        gCurrentBuildRootPath = null;

        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Player build failed, see Editor log for details");

        Debug.Log($"Completed build to {buildRootPath}");

        // Clone to latest folder
        var latestBuildPath = $"{buildsFolderPath}/latest";
        Debug.Log($"Cloning to 'latest' folder: {latestBuildPath}");
        if (Directory.Exists(latestBuildPath))
            Directory.Delete(latestBuildPath, true);
        DirectoryCloner.CloneDirectory(buildRootPath, latestBuildPath, true);
        
        // Delete the debug info
        Debug.Log($"Deleting build debug (folders ending in _DoNotShip or _ButDontShipItWithYourGame)");
        DirectoryInfo buildRootDir = new DirectoryInfo(buildRootPath);
        if (buildRootDir.Exists)
        {    
            var directoriesToDelete = Directory.GetDirectories(latestBuildPath, "*_DoNotShip", SearchOption.AllDirectories);
            foreach (var dir in directoriesToDelete)
            {
                try
                {
                    // Set recursive to true to delete files and subdirectories within
                    Directory.Delete(dir, recursive: true);
                    Console.WriteLine($"Deleted: {dir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting {dir}: {ex.Message}");
                }
            }
            
            directoriesToDelete = Directory.GetDirectories(latestBuildPath, "*_ButDontShipItWithYourGame", SearchOption.AllDirectories);
            foreach (var dir in directoriesToDelete)
            {
                try
                {
                    // Set recursive to true to delete files and subdirectories within
                    Directory.Delete(dir, recursive: true);
                    Console.WriteLine($"Deleted: {dir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting {dir}: {ex.Message}");
                }
            }
        }
    }

    private static string BuildAssetBundles(string buildRootDirectory)
    {
        var assetBundlePath = buildRootDirectory + "/" + kAssetBundleDirectory;

        if (!Directory.Exists(assetBundlePath))
            Directory.CreateDirectory(assetBundlePath);

        List<AssetBundleBuild> bundleDefinitions = new();

        //// For simplicity in this example, define a single AssetBundle,
        //// containing all the textures found inside a hard-coded directory in the project
        //string[] texturePaths = Directory.GetFiles(kTextureSourceDirectory, kTextureSearchPattern, SearchOption.AllDirectories);
//
        //var assetBundleContents = new AssetBundleBuild()
        //{
        //    assetBundleName = "textures.bundle",
        //    assetNames = texturePaths
        //};

        // The target platform will be automatically set based on the active build profile
        var assetBundleBuildOptions = new BuildAssetBundlesParameters()
        {
            outputPath = assetBundlePath,
            bundleDefinitions = bundleDefinitions.ToArray()
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(assetBundleBuildOptions);

        if (manifest == null)
            throw new BuildFailedException("AssetBundle build failed, see Editor log for details");

        return assetBundlePath;
    }

    private static string CreatePlayerOutputPath(string buildRootDirectory)
    {
        var playerOutputFolder = $"{buildRootDirectory}/{kPlayerDirectory}";

        if (!Directory.Exists(playerOutputFolder))
            Directory.CreateDirectory(playerOutputFolder);

        var playerPath = $"{playerOutputFolder}/{gAppName}";

        // This property will match the target in the active build profile
        var target = EditorUserBuildSettings.activeBuildTarget;

        // See "Build path requirements for target platforms" in the Unity Manual
        if ((target == BuildTarget.StandaloneWindows64) ||
            (target == BuildTarget.StandaloneWindows))
            playerPath += ".exe";
        else if (target == BuildTarget.StandaloneOSX)
            playerPath += ".app";
        else if (target == BuildTarget.StandaloneLinux64)
            playerPath += ".x86_64";
        else if (target == BuildTarget.Android)
            playerPath += ".aab";

        return playerPath;
    }
}

// Put the AssetBundle build directory into the StreamingAssets folder of the player output.
// This approach keeps built content separate from the source project, avoiding clutter in "Assets/StreamingAssets".
public class RegisterContentForPlayer : BuildPlayerProcessor
{
    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        if (!BuildScript.kUseAssetBundle) return;

        var currentBuildPath = BuildScript.gCurrentBuildRootPath;

        if (string.IsNullOrEmpty(currentBuildPath))
            // Do not do anything if we are not in a build initiated by BuildScript
            return;

        buildPlayerContext.AddAdditionalPathToStreamingAssets(currentBuildPath + "/" + BuildScript.kAssetBundleDirectory);
    }

    public override int callbackOrder => 1;
}

public static class DirectoryCloner
{
    public static void CloneDirectory(string sourceDir, string destinationDir, bool copySubDirs = true)
    {
        // Get the subdirectories for the specified directory.
        DirectoryInfo dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        // If the destination directory doesn't exist, create it.
        Directory.CreateDirectory(destinationDir);

        // Get the files in the directory and copy them to the new location.
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true); // The 'true' overwrites if the file exists
        }

        // If copying subdirectories, copy them and their contents to new location.
        if (copySubDirs)
        {
            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CloneDirectory(subDir.FullName, newDestinationDir, true);
            }
        }
    }
}