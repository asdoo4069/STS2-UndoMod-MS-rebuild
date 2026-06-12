using System.Text.Json;
using Godot;

namespace UndoModMS;

internal static class ModSettings
{
    private static readonly Lock IoLock = new();
    private static SettingsData _data = new();
    private static bool _loaded;

    private const string ConfigDir = "user://ModConfig/";
    private const string FileName = "UndoMod-MS-settings.json";

    private static string GetPath()
    {
        return ConfigDir + FileName;
    }

    public class SettingsData
    {
        public float? IconX { get; set; }
        public float? IconY { get; set; }
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
                var path = GetPath();

                if (!Godot.FileAccess.FileExists(path))
                    return;

                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                var text = file.GetAsText();

                if (string.IsNullOrWhiteSpace(text))
                    return;

                var parsed = JsonSerializer.Deserialize<SettingsData>(text);
                if (parsed != null)
                    _data = parsed;
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
                var path = GetPath();
                DirAccess.MakeDirRecursiveAbsolute(ConfigDir);

                var text = JsonSerializer.Serialize(_data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });

                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
                file.StoreString(text);
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