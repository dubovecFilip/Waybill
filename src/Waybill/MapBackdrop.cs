using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;

namespace Waybill;

/// <summary>
/// A picture of the game's own map, laid under the drives.
///
/// Everything Waybill draws is in the game's own coordinates, and so is every tool
/// that reads the game's map out of its archives. That is the whole reason this can
/// exist at all: there is no projection to guess at and no landmarks to line up by,
/// only arithmetic between two numbers that already mean the same thing.
///
/// What it holds is a pyramid of tiles, the shape any map renderer exports: a square
/// of the world cut into ever smaller squares, each one a picture. Waybill reads the
/// descriptor beside them, works out which squares the panel can see, and draws those.
/// It never reads the game's own files itself, which is what keeps this out of the
/// business of following a format that changes with every other patch.
///
/// The descriptor is ours rather than any tool's, so a different tool, or a set of
/// vector roads drawn by hand later, can be put behind the same door.
/// </summary>
public sealed class MapBackdrop : IDisposable {
    /// <summary>What the file beside the tiles has to say. Written by whoever exported
    /// them, or by hand: five numbers and a pattern.</summary>
    public sealed class Descriptor {
        /// <summary>Which game these tiles are of, "Ets2" or "Ats".</summary>
        public string Game { get; set; } = "";

        /// <summary>The square of the world the pyramid covers, in the game's metres.
        /// The tiles at any level divide exactly this square.</summary>
        public double MinX { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxZ { get; set; }

        public int TileSize { get; set; } = 256;
        public int MinZoom { get; set; }
        public int MaxZoom { get; set; } = 8;

        /// <summary>Where a tile lives, relative to the folder. `{z}`, `{x}` and `{y}`
        /// are filled in.</summary>
        public string Pattern { get; set; } = "{z}/{x}/{y}.png";

        /// <summary>Whether the y index counts from the top of the square downwards,
        /// which is how a web map does it and how the screen does it. Anything counting
        /// the other way says so here rather than needing its own reader.</summary>
        public bool TopDown { get; set; } = true;
    }

    /// <summary>The name of the descriptor, looked for in the folder of tiles.</summary>
    public const string DescriptorName = "waybill-map.json";

    private readonly string _folder;
    private readonly Descriptor _map;

    /// <summary>Decoded tiles, kept because a pan of one pixel asks for the same nine
    /// pictures again. Small: a hundred tiles of 256 pixels is about 26 MB, and the
    /// oldest go first.</summary>
    private readonly Dictionary<string, Bitmap?> _tiles = new();
    private readonly LinkedList<string> _order = new();
    private const int KeepTiles = 120;

    public string Game => _map.Game;

    private MapBackdrop(string folder, Descriptor map) {
        _folder = folder;
        _map = map;
    }

    /// <summary>Opens the tiles in a folder, or nothing if there is no descriptor in it
    /// that makes sense. A backdrop that cannot be read is not an error: it is a map
    /// nobody has exported yet.</summary>
    public static MapBackdrop? Open(string? folder) {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try {
            var path = Path.Combine(folder, DescriptorName);
            if (!File.Exists(path)) return null;
            var map = JsonConvert.DeserializeObject<Descriptor>(File.ReadAllText(path));
            if (map is null || map.MaxX <= map.MinX || map.MaxZ <= map.MinZ) return null;
            if (map.TileSize <= 0 || map.MaxZoom < map.MinZoom) return null;
            return new MapBackdrop(folder, map);
        } catch {
            // A backdrop is decoration. Nothing about it is worth failing to draw a
            // delivery over.
            return null;
        }
    }

    /// <summary>The side of the square the pyramid covers, in the game's metres. The
    /// square is what the tiles divide, so the longer side wins.</summary>
    private double WorldSide => Math.Max(_map.MaxX - _map.MinX, _map.MaxZ - _map.MinZ);

    /// <summary>How many metres one tile covers at that level.</summary>
    private double TileSpan(int zoom) => WorldSide / Math.Pow(2, zoom);

    /// <summary>
    /// Which level to draw at, for a view showing this many pixels per metre.
    ///
    /// The one whose tiles are at least as detailed as the panel, so a picture is
    /// shrunk rather than blown up: a stretched tile reads as a blurred map and a
    /// shrunk one reads as a map.
    /// </summary>
    private int LevelFor(float perMetre) {
        for (var zoom = _map.MinZoom; zoom <= _map.MaxZoom; zoom++) {
            var density = _map.TileSize / TileSpan(zoom);
            if (density >= perMetre) return zoom;
        }
        return _map.MaxZoom;
    }

    /// <summary>
    /// Draws whatever of the map the panel can see.
    ///
    /// `toScreen` turns a point of the world into a point of the panel, which is the
    /// one thing this needs to know about the view: everything else here is the map's
    /// own arithmetic.
    /// </summary>
    public void Draw(Graphics g, RectangleF seen, float perMetre, Func<float, float, PointF> toScreen) {
        if (perMetre <= 0) return;

        var zoom = LevelFor(perMetre);
        var span = TileSpan(zoom);
        if (span <= 0) return;
        var across = (int)Math.Pow(2, zoom);

        var firstX = (int)Math.Floor((seen.Left - _map.MinX) / span);
        var lastX = (int)Math.Floor((seen.Right - _map.MinX) / span);
        var firstZ = (int)Math.Floor((seen.Top - _map.MinZ) / span);
        var lastZ = (int)Math.Floor((seen.Bottom - _map.MinZ) / span);

        // A view zoomed out over the whole world asks for every tile of its level,
        // which is what the level was chosen to keep sane. This is the guard against
        // a descriptor that says something silly.
        if ((long)(lastX - firstX + 1) * (lastZ - firstZ + 1) > 400) return;

        for (var tx = Math.Max(firstX, 0); tx <= Math.Min(lastX, across - 1); tx++) {
            for (var tz = Math.Max(firstZ, 0); tz <= Math.Min(lastZ, across - 1); tz++) {
                var tile = Tile(zoom, tx, _map.TopDown ? tz : across - 1 - tz);
                if (tile is null) continue;

                var topLeft = toScreen((float)(_map.MinX + tx * span), (float)(_map.MinZ + tz * span));
                var bottomRight = toScreen((float)(_map.MinX + (tx + 1) * span), (float)(_map.MinZ + (tz + 1) * span));
                var box = RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                // Half a pixel over, so neighbouring tiles meet instead of leaving a
                // hairline of background between them.
                box.Inflate(0.5f, 0.5f);
                g.DrawImage(tile, box);
            }
        }
    }

    private Bitmap? Tile(int zoom, int x, int y) {
        var key = $"{zoom}/{x}/{y}";
        if (_tiles.TryGetValue(key, out var had)) {
            _order.Remove(key);
            _order.AddLast(key);
            return had;
        }

        Bitmap? tile = null;
        try {
            var path = Path.Combine(_folder, _map.Pattern
                .Replace("{z}", zoom.ToString())
                .Replace("{x}", x.ToString())
                .Replace("{y}", y.ToString())
                .Replace('/', Path.DirectorySeparatorChar));
            // Read into memory and decode from there: opening the file as a bitmap
            // holds it locked for as long as the picture lives, and a map is thousands
            // of files somebody may want to replace while the window is open.
            if (File.Exists(path)) {
                using var bytes = new MemoryStream(File.ReadAllBytes(path));
                tile = new Bitmap(bytes);
            }
        } catch {
            // A missing or broken tile is a hole in the map, not a broken window.
            tile = null;
        }

        _tiles[key] = tile;
        _order.AddLast(key);
        while (_order.Count > KeepTiles && _order.First is { } oldest) {
            _order.RemoveFirst();
            if (_tiles.Remove(oldest.Value, out var gone)) gone?.Dispose();
        }
        return tile;
    }

    public void Dispose() {
        foreach (var tile in _tiles.Values) tile?.Dispose();
        _tiles.Clear();
        _order.Clear();
    }
}
