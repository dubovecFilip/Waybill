using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using Microsoft.Win32;

namespace Waybill;

public enum SimGame { Ats, Ets2 }

/// <summary>
/// Finds the installed games and launches them through Steam, so the tracker can
/// start the game rather than the other way round.
///
/// Launching via steam:// rather than the executable directly means Steam
/// initialises normally (overlay, cloud saves, achievements) instead of the game
/// complaining that it wasn't started by Steam.
/// </summary>
public static class GameLauncher {
    private const int AtsAppId = 270880;
    private const int Ets2AppId = 227300;

    public static int AppId(SimGame game) => game == SimGame.Ats ? AtsAppId : Ets2AppId;

    /// <summary>Folders the user picked by hand, consulted before any search. Steam's
    /// registry entries only describe Steam's own idea of where things are, and it is
    /// wrong often enough (moved libraries, leftover folders, non Steam copies) that
    /// there has to be a way to simply say where the game is.</summary>
    private static readonly Dictionary<SimGame, string> Overrides = new();

    public static void SetOverride(SimGame game, string? path) {
        if (string.IsNullOrWhiteSpace(path)) Overrides.Remove(game);
        else Overrides[game] = path;
    }

    public static string? GetOverride(SimGame game) => Overrides.TryGetValue(game, out var p) ? p : null;

    /// <summary>Whether a folder really holds this game, which is the same question
    /// the automatic search asks: is the executable in it.</summary>
    public static bool LooksLikeGameDirectory(SimGame game, string path) =>
        File.Exists(Path.Combine(path, "bin", "win_x64", ExecutableName(game)));

    public static string DisplayName(SimGame game) =>
        game == SimGame.Ats ? "American Truck Simulator" : "Euro Truck Simulator 2";

    private static string FolderName(SimGame game) => DisplayName(game);

    private static string ExecutableName(SimGame game) => game == SimGame.Ats ? "amtrucks.exe" : "eurotrucks2.exe";

    /// <summary>Whether the game appears to be installed. Used to grey out the
    /// launch entry rather than offering something that can only fail.</summary>
    public static bool IsInstalled(SimGame game) => FindGameDirectory(game) != null;

    public static bool IsRunning(SimGame game) {
        var process = game == SimGame.Ats ? "amtrucks" : "eurotrucks2";
        return Process.GetProcessesByName(process).Length > 0;
    }

    public static void Launch(SimGame game) {
        Process.Start(new ProcessStartInfo($"steam://rungameid/{AppId(game)}") { UseShellExecute = true });
    }

    /// <summary>Where the game is installed, or null. Steam libraries can live on
    /// any drive, so the registry's library list is consulted rather than guessing
    /// at Program Files.</summary>
    public static string? FindGameDirectory(SimGame game) {
        // A folder the user pointed at wins outright, including over a working
        // automatic result: it was set precisely because the search was wrong.
        if (GetOverride(game) is { } chosen) return Directory.Exists(chosen) ? chosen : null;

        foreach (var library in SteamLibraries()) {
            var candidate = Path.Combine(library, "steamapps", "common", FolderName(game));

            // The executable, not just the folder. Moving a game to another library
            // leaves the old folder behind, empty apart from a bin\win_x64\plugins
            // tree, and matching on the folder alone picks that ghost: the plugin
            // then reads as missing while it sits installed in the copy actually
            // being played, and installing it writes into a folder no game reads.
            if (File.Exists(Path.Combine(candidate, "bin", "win_x64", ExecutableName(game)))) return candidate;
        }
        return null;
    }

    /// <summary>The game's plugin folder, creating it if the game is installed but
    /// has never had a plugin put in it. Returns null with a reason rather than
    /// throwing: this runs from a menu click, and an exception there wedges the UI.</summary>
    public static string? PluginDirectory(SimGame game, out string problem) {
        problem = "";
        var root = FindGameDirectory(game);
        if (root == null) {
            problem = $"{DisplayName(game)} sa nenašla v žiadnej knižnici Steamu.";
            return null;
        }

        var plugins = Path.Combine(root, "bin", "win_x64", "plugins");
        try {
            Directory.CreateDirectory(plugins);
            return plugins;
        } catch (Exception ex) {
            problem = $"Priečinok pre plugin sa nepodarilo vytvoriť:\n{plugins}\n\n{ex.Message}";
            return null;
        }
    }

    /// <summary>Looks for the telemetry plugin shipped with this project, so the
    /// usual case needs no file picker at all. Checks next to the executable first
    /// (published layout), then the repository's third-party folder, which ships the
    /// plugin as the upstream release zip rather than a loose DLL.</summary>
    public static string? FindBundledPlugin() {
        var candidates = new List<string>();
        var baseDir = AppContext.BaseDirectory;

        candidates.Add(Path.Combine(baseDir, "scs-telemetry.dll"));
        candidates.Add(Path.Combine(baseDir, "plugin", "scs-telemetry.dll"));

        // Walk up from the executable towards the repository root looking for
        // third-party/, which holds the plugin release.
        var dir = new DirectoryInfo(baseDir);
        var zips = new List<string>();
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent) {
            var thirdParty = Path.Combine(dir.FullName, "third-party");
            candidates.Add(Path.Combine(thirdParty, "scs-telemetry.dll"));
            if (Directory.Exists(thirdParty)) zips.AddRange(Directory.GetFiles(thirdParty, "*scs-telemetry*.zip"));
        }

        return candidates.FirstOrDefault(File.Exists) ?? zips.Select(ExtractFromZip).FirstOrDefault(p => p != null);
    }

    /// <summary>Pulls Win64/scs-telemetry.dll out of an upstream release zip into a
    /// folder of our own and hands back the path. The zip is what the project ships,
    /// so without this the "bundled" plugin could never be found and every install
    /// fell through to asking the user to go and find the file themselves.</summary>
    private static string? ExtractFromZip(string zipPath) {
        try {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("Win64/scs-telemetry.dll", StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            var target = Path.Combine(Storage.DeliveryStore.DefaultDir(), "plugin", "scs-telemetry.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // Re-extract only when the zip is newer, so the copy stays current if the
            // bundled release is ever updated without needing a version check.
            if (!File.Exists(target) || File.GetLastWriteTimeUtc(zipPath) > File.GetLastWriteTimeUtc(target)) {
                entry.ExtractToFile(target, overwrite: true);
            }
            return target;
        } catch {
            // A damaged or unreadable zip just means falling back to the file picker.
            return null;
        }
    }

    public static bool IsPluginInstalled(SimGame game) {
        var plugins = FindGameDirectory(game) is { } root
            ? Path.Combine(root, "bin", "win_x64", "plugins")
            : null;
        return plugins != null && File.Exists(Path.Combine(plugins, "scs-telemetry.dll"));
    }

    private static IEnumerable<string> SteamLibraries() {
        var steam = SteamRoot();
        if (steam == null) yield break;

        yield return steam;

        // Additional libraries are listed in libraryfolders.vdf as `"path" "D:\\Games"`.
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        foreach (var line in File.ReadLines(vdf)) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) yield return parts[^1].Replace(@"\\", @"\");
        }
    }

    private static string? SteamRoot() {
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" }) {
            using var registry = Registry.LocalMachine.OpenSubKey(key);
            if (registry?.GetValue("InstallPath") is string path && Directory.Exists(path)) return path;
        }
        using var user = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
        return user?.GetValue("SteamPath") as string;
    }
}
