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
