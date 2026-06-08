using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcanum.Core;

public class ConfigData
{
    [JsonPropertyName("last_vault")]  public string? LastVault  { get; set; }
    [JsonPropertyName("lock_seconds")] public int    LockSeconds { get; set; } = 60;
}

public static class AppConfig
{
    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".arcanum", "config.json");

    public static ConfigData Load()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ConfigData>(json) ?? new ConfigData();
        }
        catch { return new ConfigData(); }
    }

    public static void Save(ConfigData config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config));
        }
        catch { }
    }

    public static void SaveLockSeconds(int seconds)
    {
        var cfg = Load();
        cfg.LockSeconds = seconds;
        Save(cfg);
    }
}
