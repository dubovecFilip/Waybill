using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

    /// <summary>What ts-map writes beside the tiles it exports: the same square of the
    /// world and the same range of levels, under its own names. Read as it is rather
    /// than converted by hand, so exporting a map again is a copy and nothing else.</summary>
    private sealed class TileMapInfo {
        public double x1 { get; set; }
        public double x2 { get; set; }
        public double y1 { get; set; }
        public double y2 { get; set; }
        public int minZoom { get; set; }
        public int maxZoom { get; set; }
    }

    /// <summary>A town the game has, wherever the exporter found it. Nothing to do
    /// with whether this driver has ever been there.</summary>
    public sealed class Place {
        public string Name { get; set; } = "";
        public float X { get; set; }

        /// <summary>The exporter's y, which is the game's z. Same axis, older name.</summary>
        public float Y { get; set; }
    }

    /// <summary>
    /// Every town on the map, read once when something first asks.
    ///
    /// Waybill knows only the cities it has driven to, which is a handful of dots in a
    /// country. These are the rest, so a delivery is read against somewhere rather
    /// than against nowhere, and they are drawn quietly because the driver has no
    /// history in them.
    /// </summary>
    public IReadOnlyList<Place> Places {
        get {
            if (_places is not null) return _places;
            try {
                var path = Path.Combine(_folder, PlacesName);
                _places = File.Exists(path)
                    ? JsonConvert.DeserializeObject<List<Place>>(File.ReadAllText(path)) ?? new List<Place>()
                    : new List<Place>();
            } catch {
                _places = new List<Place>();
            }
            return _places;
        }
    }
    private List<Place>? _places;

    /// <summary>Somewhere on the map worth stopping at: a pump, a rest area, a
    /// service, a garage, a dealer, a weigh station, a viewpoint, a ferry.</summary>
    public sealed class Stop {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public float X { get; set; }

        /// <summary>The exporter's y, which is the game's z.</summary>
        public float Y { get; set; }
    }

    /// <summary>
    /// The stops, read once when something first asks for them.
    ///
    /// Companies are deliberately left out. Their icon is a logo, and a driver running
    /// company mods would be shown whatever happened to be installed on the day the
    /// map was exported, which is worse than showing nothing. Road shields go too:
    /// the roads themselves are already drawn underneath.
    /// </summary>
    public IReadOnlyList<Stop> Stops {
        get {
            if (_stops is not null) return _stops;
            try {
                var path = Path.Combine(_folder, StopsName);
                var all = File.Exists(path)
                    ? JsonConvert.DeserializeObject<List<Stop>>(File.ReadAllText(path)) ?? new List<Stop>()
                    : new List<Stop>();
                _stops = all.FindAll(s => Kinds.Contains(s.Type));
            } catch {
                _stops = new List<Stop>();
            }
            return _stops;
        }
    }
    private List<Stop>? _stops;

    /// <summary>What is drawn, and nothing else. Anything the exporter learns to write
    /// that is not named here is ignored rather than drawn as a mystery.</summary>
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) {
        "Fuel", "Parking", "Service", "Garage", "TruckDealer", "WeightStation", "Viewpoint", "Ferry",
    };

    /// <summary>The picture for a stop, from the icons the exporter wrote beside the
    /// tiles. Kept for the same reason the tiles are: the same handful are asked for
    /// again on every draw.</summary>
    public Bitmap? Icon(string name) {
        if (_icons.TryGetValue(name, out var had)) return had;
        Bitmap? icon = null;
        try {
            var path = Path.Combine(_folder, IconFolder, name + ".png");
            if (File.Exists(path)) {
                using var bytes = new MemoryStream(File.ReadAllBytes(path));
                icon = new Bitmap(bytes);
            }
        } catch {
            icon = null;
        }
        _icons[name] = icon;
        return icon;
    }
    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name of the descriptor, looked for in the folder of tiles.</summary>
    public const string DescriptorName = "waybill-map.json";

    /// <summary>What the exporter calls its own, read when ours is not there.</summary>
    public const string ExportedName = "TileMapInfo.json";

    /// <summary>The towns, as the exporter writes them beside the tiles.</summary>
    public const string PlacesName = "Cities.json";

    /// <summary>Everything the exporter marks on the map, and the folder of pictures
    /// it marks them with.</summary>
    public const string StopsName = "Overlays.json";
    public const string IconFolder = "Overlays";

    private readonly string _folder;
    private readonly Descriptor _map;

    /// <summary>Decoded tiles, kept because a pan of one pixel asks for the same nine
    /// pictures again. Small: a hundred tiles of 256 pixels is about 26 MB, and the
    /// oldest go first.</summary>
    private readonly Dictionary<string, Bitmap?> _tiles = new();
    private readonly LinkedList<string> _order = new();
    private const int KeepTiles = 120;

    public string Game => _map.Game;

    /// <summary>
    /// What this map is called, which is the name of the folder it was dropped in.
    ///
    /// A game has as many maps as the driver has worlds: the one the game shipped with,
    /// and whatever a map mod makes of it. Nothing in an export says which it is, and
    /// nothing in the telemetry says which is loaded, so the name is the one thing a
    /// person can set by dragging a folder.
    /// </summary>
    public string Name { get; private set; } = "";

    /// <summary>
    /// The colour the map draws open country in, taken from the map itself.
    ///
    /// Read off the single tile that holds the whole world, so it is whatever this
    /// export chose and stays right for a map drawn in another palette. Used behind
    /// the tiles, so the part of the panel the map does not reach looks like more of
    /// the same nothing rather than like a picture that failed to load.
    /// </summary>
    public Color Ground {
        get {
            if (_ground is { } had) return had;
            var whole = Tile(_map.MinZoom, 0, 0);
            _ground = whole is null ? Color.FromArgb(22, 25, 29) : Commonest(whole);
            return _ground.Value;
        }
    }
    private Color? _ground;

    /// <summary>The colour most of a tile is, which on the tile that holds the whole
    /// world is the ground everything else is drawn on.</summary>
    private static Color Commonest(Bitmap tile) {
        var seen = new Dictionary<int, int>();
        for (var y = 0; y < tile.Height; y += 4) {
            for (var x = 0; x < tile.Width; x += 4) {
                var argb = tile.GetPixel(x, y).ToArgb();
                seen[argb] = seen.TryGetValue(argb, out var n) ? n + 1 : 1;
            }
        }
        var best = 0;
        var most = 0;
        foreach (var (argb, n) in seen) {
            if (n <= most) continue;
            most = n;
            best = argb;
        }
        return most == 0 ? Color.FromArgb(22, 25, 29) : Color.FromArgb(best);
    }

    /// <summary>
    /// Whether this world has a town about here.
    ///
    /// What a delivery's two ends are asked against. Every world a map mod builds keeps
    /// the towns the game shipped with, so a drive between two of those could have
    /// happened in either; a drive that ends where only one world has a town could only
    /// have happened in that one.
    /// </summary>
    public bool HasTownNear(float x, float z, float metres) {
        var reach = metres * metres;
        foreach (var place in Places) {
            var dx = place.X - x;
            var dz = place.Y - z;
            if (dx * dx + dz * dz <= reach) return true;
        }
        return false;
    }

    /// <summary>The square of the world the tiles cover, in the game's metres.</summary>
    public RectangleF Bounds => RectangleF.FromLTRB(
        (float)_map.MinX, (float)_map.MinZ, (float)_map.MaxX, (float)_map.MaxZ);

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
            var map = Ours(folder) ?? Exported(folder);
            if (map is null || map.MaxX <= map.MinX || map.MaxZ <= map.MinZ) return null;
            if (map.TileSize <= 0 || map.MaxZoom < map.MinZoom) return null;
            var named = new DirectoryInfo(folder).Name;
            if (map.Game.Length == 0) map.Game = named;
            return new MapBackdrop(folder, map) { Name = named };
        } catch {
            // A backdrop is decoration. Nothing about it is worth failing to draw a
            // delivery over.
            return null;
        }
    }

    /// <summary>The descriptor written for Waybill, which is the one that wins: a map
    /// somebody has adjusted by hand is a deliberate answer to whatever the exporter
    /// said.</summary>
    private static Descriptor? Ours(string folder) {
        var path = Path.Combine(folder, DescriptorName);
        return File.Exists(path) ? JsonConvert.DeserializeObject<Descriptor>(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// The exporter's own file, turned into a descriptor.
    ///
    /// ts-map writes the square it covered and the levels it went to, and puts the
    /// pictures in a `Tiles` folder beside it. That is everything needed, so an export
    /// is dropped in as it came out: no conversion step to get wrong, and re-exporting
    /// after a game patch is a copy.
    /// </summary>
    private static Descriptor? Exported(string folder) {
        var path = Path.Combine(folder, ExportedName);
        if (!File.Exists(path)) return null;
        var info = JsonConvert.DeserializeObject<TileMapInfo>(File.ReadAllText(path));
        if (info is null) return null;
        return new Descriptor {
            MinX = info.x1, MaxX = info.x2,
            // The exporter's y is the game's z. It calls the ground plane x and y, the
            // way anything drawing a map from above does; the telemetry calls the same
            // two axes x and z and keeps y for height.
            MinZ = info.y1, MaxZ = info.y2,
            MinZoom = info.minZoom, MaxZoom = info.maxZoom,
            TileSize = 256,
            Pattern = "Tiles/{z}/{x}/{y}.png",
            TopDown = true,
        };
    }

    /// <summary>The side of the square the pyramid covers, in the game's metres. The
    /// square is what the tiles divide, so the longer side wins.</summary>
    private double WorldSide => Math.Max(_map.MaxX - _map.MinX, _map.MaxZ - _map.MinZ);

    /// <summary>How many metres one tile covers at that level.</summary>
    private double TileSpan(int zoom) => WorldSide / Math.Pow(2, zoom);

    /// <summary>How much detail the finest level holds, in pixels per metre. Zooming
    /// much past this is blowing up a picture rather than looking closer at one.
    /// </summary>
    public float Finest => (float)(_map.TileSize / TileSpan(_map.MaxZoom));

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

                var left = _map.MinX + tx * span;
                var top = _map.MinZ + tz * span;

                // Only the part of the tile the panel can see, rather than the whole
                // of it. Zoomed in far enough, a whole tile lands on a rectangle
                // hundreds of thousands of pixels wide, and GDI+ answers that by
                // drawing nothing at all: the map simply vanished at the zoom where
                // it was most worth having.
                var cut = RectangleF.Intersect(seen, new RectangleF((float)left, (float)top, (float)span, (float)span));
                if (cut.Width <= 0 || cut.Height <= 0) continue;

                var perTile = _map.TileSize / (float)span;
                var from = new RectangleF(
                    (cut.Left - (float)left) * perTile, (cut.Top - (float)top) * perTile,
                    cut.Width * perTile, cut.Height * perTile);

                var topLeft = toScreen(cut.Left, cut.Top);
                var bottomRight = toScreen(cut.Right, cut.Bottom);
                var box = RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                // Half a pixel over, so neighbouring tiles meet instead of leaving a
                // hairline of background between them.
                box.Inflate(0.5f, 0.5f);
                g.DrawImage(tile, box, from, GraphicsUnit.Pixel);
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
                using var bright = new Bitmap(bytes);
                tile = Dimmed(bright);
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

    /// <summary>
    /// How much of the exported map's own brightness to keep.
    ///
    /// An exporter draws a map to be looked at on its own: pale land, white roads. Under
    /// a drive it has a different job, which is to say where the roads are and then get
    /// out of the way. At this much the land sits about where the panel's own background
    /// does and the roads stay clearly readable, with the line of the drive brighter than
    /// either. Done to the picture once when it is read rather than at every draw, since
    /// the same nine tiles are asked for again after a pan of one pixel.
    /// </summary>
    private const float Keep = 0.42f;

    private static Bitmap Dimmed(Bitmap bright) {
        var dim = new Bitmap(bright.Width, bright.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(dim);
        using var how = new ImageAttributes();
        how.SetColorMatrix(new ColorMatrix(new[] {
            new[] { Keep, 0f,   0f,   0f, 0f },
            new[] { 0f,   Keep, 0f,   0f, 0f },
            new[] { 0f,   0f,   Keep, 0f, 0f },
            new[] { 0f,   0f,   0f,   1f, 0f },
            new[] { 0f,   0f,   0f,   0f, 1f },
        }));
        g.DrawImage(bright, new Rectangle(0, 0, bright.Width, bright.Height),
                    0, 0, bright.Width, bright.Height, GraphicsUnit.Pixel, how);
        return dim;
    }

    public void Dispose() {
        foreach (var tile in _tiles.Values) tile?.Dispose();
        _tiles.Clear();
        _order.Clear();
        foreach (var icon in _icons.Values) icon?.Dispose();
        _icons.Clear();
    }
}
