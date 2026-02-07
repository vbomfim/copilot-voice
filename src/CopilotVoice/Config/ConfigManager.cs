using System.Text.Json;

namespace CopilotVoice.Config;

public class ConfigManager
{
    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot-voice");
    
    private readonly string _configDir;
    private readonly string _configFile;

    public ConfigManager() : this(DefaultConfigDir) { }

    public ConfigManager(string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Config directory path cannot be null or whitespace.", nameof(configDir));

        _configDir = configDir;
        _configFile = Path.Combine(configDir, "config.json");
    }
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppConfig Load()
    {
        if (!File.Exists(_configFile))
            return new AppConfig();

        var json = File.ReadAllText(_configFile);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_configDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configFile, json);
    }

    public AppConfig LoadOrCreate()
    {
        if (File.Exists(_configFile))
            return Load();

        var config = new AppConfig();
        Save(config);
        return config;
    }

    public bool ConfigExists() => File.Exists(_configFile);
}
