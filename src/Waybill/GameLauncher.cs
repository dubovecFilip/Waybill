using System.Diagnostics;
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

    public static string DisplayName(SimGame game) =>
        game == SimGame.Ats ? "American Truck Simulator" : "Euro Truck Simulator 2";

    private static string FolderName(SimGame game) => DisplayName(game);

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
        foreach (var library in SteamLibraries()) {
            var candidate = Path.Combine(library, "steamapps", "common", FolderName(game));
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The game's plugin folder, creating it if the game is installed but
    /// has never had a plugin put in it.</summary>
    public static string? PluginDirectory(SimGame game) {
        var root = FindGameDirectory(game);
        if (root == null) return null;

        var plugins = Path.Combine(root, "bin", "win_x64", "plugins");
        Directory.CreateDirectory(plugins);
        return plugins;
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
