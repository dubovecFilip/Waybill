using Newtonsoft.Json;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// User preferences, kept next to the database so they survive rebuilds. Small
/// and hand-editable on purpose; a broken or missing file just means defaults.
/// </summary>
public class Settings {
    /// <summary>"auto" (imperial for ATS, metric for ETS2), "metric", or "imperial".</summary>
    public string Units { get; set; } = "auto";

    /// <summary>UI language code, see Strings.All.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Where the games are installed, when the automatic search is wrong or
    /// finds nothing: a non Steam copy, a library the registry does not list, or the
    /// empty folder Steam leaves behind after moving a game. Null means search.</summary>
    public string? AtsPath { get; set; }
    public string? Ets2Path { get; set; }

    public string? PathFor(SimGame game) => game == SimGame.Ats ? AtsPath : Ets2Path;

    public void SetPathFor(SimGame game, string? path) {
        if (game == SimGame.Ats) AtsPath = path; else Ets2Path = path;
    }

    [JsonIgnore]
    public static string Path => System.IO.Path.Combine(DeliveryStore.DefaultDir(), "settings.json");

    public static Settings Load() {
        try {
            if (File.Exists(Path)) {
                return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path)) ?? new Settings();
            }
        } catch {
            // Preferences are never worth failing to start over.
        }
        return new Settings();
    }

    public void Save() {
        try {
            Directory.CreateDirectory(DeliveryStore.DefaultDir());
            File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
        } catch {
            // Non-fatal: the app keeps working, the choice just won't stick.
        }
    }
}
