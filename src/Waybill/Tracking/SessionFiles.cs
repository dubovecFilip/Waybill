using System.IO.Compression;

namespace Waybill.Tracking;

/// <summary>
/// Session recordings are plain JSON Lines while being written and gzipped once
/// finished. They compress about 13x (one measured 9.9 MB recording became
/// 750 KB), which is the difference between tens of megabytes per week and a few
/// megabytes per year - so nothing ever has to be deleted to save space.
/// </summary>
public static class SessionFiles {
    public const string Extension = ".jsonl";
    public const string CompressedExtension = ".jsonl.gz";

    /// <summary>Reads a recording whether it is compressed or not, so callers
    /// (replay, tests) never have to care which form it is in.</summary>
    public static IEnumerable<string> ReadLines(string path) {
        if (path.EndsWith(CompressedExtension, StringComparison.OrdinalIgnoreCase)) {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            while (reader.ReadLine() is { } line) yield return line;
            yield break;
        }

        foreach (var line in File.ReadLines(path)) yield return line;
    }

    /// <summary>Gzips a finished recording and removes the original. Returns the new
    /// path, or the original one if compression failed - a recording that could not
    /// be compressed is still a recording worth keeping.</summary>
    public static string Compress(string path) {
        if (!File.Exists(path) || path.EndsWith(CompressedExtension, StringComparison.OrdinalIgnoreCase)) return path;

        // Starting the app without the game running records nothing, and packing that
        // leaves an empty archive behind. They pile up fast (28 of 42 recordings on
        // one machine), bury the real ones, and an empty or truncated archive is not
        // valid gzip, so anything walking the folder trips over it.
        if (new FileInfo(path).Length == 0) {
            try { File.Delete(path); } catch { /* it will be picked up on the next start */ }
            return path;
        }

        var target = path + ".gz";
        try {
            using (var source = File.OpenRead(path))
            using (var destination = File.Create(target))
            using (var gzip = new GZipStream(destination, CompressionLevel.Optimal)) {
                source.CopyTo(gzip);
            }
            File.Delete(path);
            return target;
        } catch {
            if (File.Exists(target)) {
                try { File.Delete(target); } catch { /* leave the half-written file alone */ }
            }
            return path;
        }
    }

    /// <summary>Compresses leftover uncompressed recordings - ones whose app was
    /// killed before it could pack them. <paramref name="skip"/> is the recording
    /// currently being written.</summary>
    public static int CompressOrphans(string directory, string? skip = null) {
        if (!Directory.Exists(directory)) return 0;

        var packed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*" + Extension)) {
            if (skip != null && string.Equals(path, skip, StringComparison.OrdinalIgnoreCase)) continue;
            if (Compress(path) != path) packed++;
        }

        // Empty archives from before the check above, plus any left by a process that
        // died mid-compress. Zero bytes is not a recording that failed to pack, it is
        // not a gzip stream at all, so there is nothing in one to lose.
        foreach (var path in Directory.EnumerateFiles(directory, "*" + CompressedExtension)) {
            try {
                if (new FileInfo(path).Length == 0) File.Delete(path);
            } catch { /* not worth failing a start over */ }
        }
        return packed;
    }
}
