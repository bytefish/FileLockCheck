using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace FileLockCheck.Configuration;

/// <summary>
/// Saves the keyboard shortcut and settings in the user's roaming profile.
/// </summary>
public class AppConfig
{
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;

    public Key HotKey { get; set; } = Key.U;

    public string Language { get; set; } = "";

    private static string GetConfigFilePath()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string appFolderPath = Path.Combine(appDataPath, "FileLockCheck");

        if (!Directory.Exists(appFolderPath))
        {
            Directory.CreateDirectory(appFolderPath);
        }

        return Path.Combine(appFolderPath, "config.json");
    }

    public static AppConfig Load()
    {
        try
        {
            string configPath = GetConfigFilePath();

            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);

                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            string configPath = GetConfigFilePath();

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(configPath, json);
        }
        catch { }
    }
}