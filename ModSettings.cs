using System.Text.Json;

namespace UndoModMS;

internal static class ModSettings
{
    private const string FileName = "settings.json";
    private static readonly Lock IoLock = new();
    private static SettingsData _data = new();
    private static string? _path;
    private static bool _loaded;

    public class SettingsData
    {
        public float? IconX { get; set; }
        public float? IconY { get; set; }
    }

    public static string Path
    {
        get
        {
            if (_path != null) return _path;
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UndoMod-MS");
            Directory.CreateDirectory(dir);
            _path = System.IO.Path.Combine(dir, FileName);
            return _path;
        }
    }

    public static SettingsData Data
    {
        get
        {
            if (!_loaded) Load();
            return _data;
        }
    }

    public static void Load()
    {
        lock (IoLock)
        {
            _loaded = true;
            try
            {
                if (!File.Exists(Path)) return;
                var text = File.ReadAllText(Path);
                if (string.IsNullOrWhiteSpace(text)) return;
                var parsed = JsonSerializer.Deserialize<SettingsData>(text);
                if (parsed != null) _data = parsed;
            }
            catch (Exception ex)
            {
                UndoLogger.Warn($"[Settings] load failed: {ex.Message}");
            }
        }
    }

    public static void Save()
    {
        lock (IoLock)
        {
            try
            {
                var text = JsonSerializer.Serialize(_data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
                File.WriteAllText(Path, text);
            }
            catch (Exception ex)
            {
                UndoLogger.Warn($"[Settings] save failed: {ex.Message}");
            }
        }
    }

    public static void SetIconPosition(float x, float y)
    {
        Data.IconX = x;
        Data.IconY = y;
        Save();
    }
}