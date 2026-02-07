using System.Text.Json;

namespace CopilotVoice.Config;

public class ConfigManager
{
    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot-voice");
    
    private readonly string ConfigDir;
    private readonly string ConfigFile;

    public ConfigManager() : this(DefaultConfigDir) { }

    public ConfigManager(string configDir)
    {
        ConfigDir = configDir;
        ConfigFile = Path.Combine(configDir, "config.json");
    }
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppConfig Load()
    {
        if (!File.Exists(ConfigFile))
            return new AppConfig();

        var json = File.ReadAllText(ConfigFile);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFile, json);
    }

    public AppConfig LoadOrCreate()
    {
        if (File.Exists(ConfigFile))
            return Load();

        var config = new AppConfig();
        Save(config);
        return config;
    }

    public bool ConfigExists() => File.Exists(ConfigFile);
}
