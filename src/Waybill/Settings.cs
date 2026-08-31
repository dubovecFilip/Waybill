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
    /// <summary>How wide the sidebar is, in pixels. Kept because the strip of what
    /// just happened lives in it, and how much of an award's name fits is worth more
    /// to one driver than the width of the page beside it is.</summary>
    public int SidebarWidth { get; set; } = 200;

    /// <summary>
    /// Which map to draw under a game's drives, by the name of its folder.
    ///
    /// Only meaningful once there is more than one, which happens the moment somebody
    /// exports a map mod's world beside the one the game shipped with. Empty, or naming
    /// a folder that is not there, means the first one found.
    /// </summary>
    public Dictionary<string, string> MapChoice { get; set; } = new();

    public string? AtsPath { get; set; }
    public string? Ets2Path { get; set; }

    /// <summary>Show the current delivery on the Discord profile. On by default,
    /// but it only does anything once <see cref="DiscordAppId"/> is filled in, so
    /// nothing is published without the user having set it up deliberately.</summary>
    public bool DiscordPresence { get; set; } = true;

    /// <summary>Discord application ID from the developer portal. Public by design,
    /// not a secret: it only tells Discord whose icons and name to show.</summary>
    public string? DiscordAppId { get; set; }

    /// <summary>How long a break between runs of the app still counts as the same
    /// sitting at the wheel. An hour by default: closing Waybill for a moment, or a
    /// crash and a restart, is not the end of an evening's driving. Left open, a
    /// sitting never ends, however long the pause, because what ends one is the
    /// driver getting up.</summary>
    public int SessionGapMinutes { get; set; } = Tracking.Sessions.DefaultGapMinutes;

    /// <summary>
    /// Draw the route on the page for the drive in progress.
    ///
    /// On by default, and worth a switch because it is the one drawing in the app
    /// that is redrawn while something else is running: the line grows every second
    /// the truck moves, and the game is what the machine is really busy with. Off,
    /// the page keeps the figures and the log and costs nothing at all.
    /// </summary>
    public bool LiveMap { get; set; } = true;

    /// <summary>Name the state or the country beside a city: "Yakima, WA". Off shows
    /// the city as the game names it. Only the delivery list, the card and the sheet
    /// ask: the maps have the names in them already and a code on every one of them
    /// would be a page of abbreviations with a route somewhere underneath.</summary>
    public bool CityRegions { get; set; } = true;

    /// <summary>The driver's signature, as the strokes they drew, for the foot of an
    /// exported sheet. Empty means the line is left blank, which is what a form does
    /// when nobody has signed it. See <see cref="Signature"/> for the notation.</summary>
    public string? SignatureStrokes { get; set; }

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
