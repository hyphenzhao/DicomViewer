using System.IO;
using System.Text.Json;

namespace DicomViewer;

internal sealed class PythonSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DicomViewer");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string PythonInterpreterPath { get; set; } = string.Empty;
    public string ModelsDirectory { get; set; } = string.Empty;
    public string ScriptsDirectory { get; set; } = string.Empty;
    public string TempDirectory { get; set; } = string.Empty;

    /// <summary>
    /// "Local" = run inference via local Python process; "Remote" = send to remote server.
    /// </summary>
    public string SegmentationMode { get; set; } = "Local";

    /// <summary>
    /// Base URL of the remote segmentation server, e.g. "http://192.168.1.100:8000".
    /// </summary>
    public string RemoteServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret API key for authenticating with the remote server.
    /// </summary>
    public string RemoteApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Returns true when the selected segmentation mode is properly configured.
    /// </summary>
    public bool IsConfigured =>
        SegmentationMode == "Remote"
            ? !string.IsNullOrWhiteSpace(RemoteServerUrl)
            : !string.IsNullOrWhiteSpace(PythonInterpreterPath) &&
              File.Exists(PythonInterpreterPath) &&
              !string.IsNullOrWhiteSpace(ScriptsDirectory) &&
              Directory.Exists(ScriptsDirectory);

    public static PythonSettings Load()
    {
        PythonSettings? loaded = null;

        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                loaded = JsonSerializer.Deserialize<PythonSettings>(json);
            }
        }
        catch
        {
            // Corrupt settings file — fall through to defaults.
        }

        if (loaded is null)
        {
            var settings = new PythonSettings();
            settings.AutoDetect();
            return settings;
        }

        // If any saved path no longer exists, re-detect just that path.
        var autoDetected = new PythonSettings();
        autoDetected.AutoDetect();

        if (!File.Exists(loaded.PythonInterpreterPath))
            loaded.PythonInterpreterPath = autoDetected.PythonInterpreterPath;
        if (!Directory.Exists(loaded.ScriptsDirectory))
            loaded.ScriptsDirectory = autoDetected.ScriptsDirectory;
        if (!Directory.Exists(loaded.ModelsDirectory))
            loaded.ModelsDirectory = autoDetected.ModelsDirectory;

        return loaded;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort save; don't crash if we can't write settings.
        }
    }

    private void AutoDetect()
    {
        // Try to find Python in common locations
        string[] candidatePaths =
        [
            // System Python
            @"C:\Python310\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python312\python.exe",
            // User Python
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python310\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python311\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python312\python.exe"),
            // Conda (user install)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"miniconda3\envs\cmt-inference\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"anaconda3\envs\cmt-inference\python.exe"),
            // Conda (system install)
            @"C:\ProgramData\miniconda3\envs\cmt-inference\python.exe",
            @"C:\ProgramData\anaconda3\envs\cmt-inference\python.exe"
        ];

        foreach (string candidate in candidatePaths)
        {
            if (File.Exists(candidate))
            {
                PythonInterpreterPath = candidate;
                break;
            }
        }

        // Default scripts directory: try app-relative first, then walk up (for dev builds)
        ScriptsDirectory = FindDirectoryNear("Scripts", "cmt_segmentation.py");

        // Default models directory: walk up from app dir, prefer segModel subfolder with fold_0/
        string? foundModels = FindDirectoryNear("Models", "segModel-OAIZIB-19Mar2024");
        if (foundModels is not null)
        {
            // Prefer the segmentation model subdirectory (must contain fold_0/model_best.model)
            string segModelDir = Path.Combine(foundModels, "segModel-OAIZIB-19Mar2024");
            if (Directory.Exists(segModelDir) && File.Exists(Path.Combine(segModelDir, "fold_0", "model_best.model")))
            {
                ModelsDirectory = segModelDir;
            }
            else if (File.Exists(Path.Combine(foundModels, "fold_0", "model_best.model")))
            {
                // The foundModels directory itself is a valid model directory
                ModelsDirectory = foundModels;
            }
        }

        // Default temp directory
        TempDirectory = Path.Combine(Path.GetTempPath(), "DicomViewer_CMT");
    }

    /// <summary>
    /// Finds a subdirectory by walking up from the application base directory.
    /// Useful during development where the binary is several levels below the project root.
    /// </summary>
    /// <param name="subDirName">Name of the subdirectory to find (e.g. "Scripts", "Models").</param>
    /// <param name="sentinelFile">A file expected inside the subdirectory, used to confirm it's the right one.</param>
    /// <returns>The full path if found, or {BaseDirectory}/{subDirName} as fallback.</returns>
    private static string FindDirectoryNear(string subDirName, string? sentinelFile = null)
    {
        // Walk up from the application directory (up to 4 levels) looking for subDirName
        string? current = AppContext.BaseDirectory;

        for (int i = 0; i <= 4 && current is not null; i++)
        {
            string candidate = Path.Combine(current, subDirName);

            if (Directory.Exists(candidate) &&
                (sentinelFile is null || File.Exists(Path.Combine(candidate, sentinelFile))))
            {
                return Path.GetFullPath(candidate);
            }

            // Go up one level
            current = Path.GetDirectoryName(current);
        }

        // Fallback to app-relative
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, subDirName));
    }
}
