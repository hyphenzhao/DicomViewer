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

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PythonInterpreterPath) &&
        File.Exists(PythonInterpreterPath) &&
        !string.IsNullOrWhiteSpace(ScriptsDirectory) &&
        Directory.Exists(ScriptsDirectory);

    public static PythonSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<PythonSettings>(json) ?? new PythonSettings();
            }
        }
        catch
        {
            // Corrupt settings file — fall through to defaults.
        }

        var settings = new PythonSettings();
        settings.AutoDetect();
        return settings;
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

        // Default scripts directory to app-relative
        ScriptsDirectory = Path.Combine(AppContext.BaseDirectory, "Scripts");

        // Default models directory
        string defaultModels = Path.Combine(AppContext.BaseDirectory, "Models");
        if (Directory.Exists(defaultModels))
        {
            ModelsDirectory = defaultModels;
        }

        // Default temp directory
        TempDirectory = Path.Combine(Path.GetTempPath(), "DicomViewer_CMT");
    }
}
