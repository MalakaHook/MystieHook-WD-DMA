using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using SkiaSharp;

namespace MystieHook.Gui;

public readonly record struct MapTileKey(int Level, int X, int Y);
public readonly record struct MapTileView(int Level, int MinX, int MaxX, int MinY, int MaxY)
{
    public int Count => (MaxX - MinX + 1) * (MaxY - MinY + 1);
}

public static class MapTileLayout
{
    public static Vector2 Rotate(Vector2 point, Vector2 center, float angle)
    {
        var d=point-center;float c=MathF.Cos(angle),s=MathF.Sin(angle);
        return center+new Vector2(d.X*c-d.Y*s,d.X*s+d.Y*c);
    }
    public static MapTileView PlanRotated(float pixels,float zoom,float u,float v,int maximum,float angle)
    {
        float span=MathF.Abs(MathF.Cos(angle))+MathF.Abs(MathF.Sin(angle));
        float pad=(span-1)/(2*zoom);
        return Plan(pixels*span,zoom/span,u-pad,v-pad,maximum);
    }
    public static Vector2 Project(Vector2 imagePosition, Vector2 origin, float size, float zoom, float u, float v)
        => origin + (imagePosition - new Vector2(u, v)) * size * zoom;
    public static int MaximumLevel(string map) => map switch { "bakurani" => 6, "ozeti" or "zestafona" => 7, _ => 0 };
    public static MapTileView Plan(float pixels, float zoom, float u, float v, int maximum)
    {
        int level = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(1, pixels * zoom / 256))), 0, maximum);
        while (true)
        {
            int n = 1 << level;
            var view = new MapTileView(level, Math.Clamp((int)Math.Floor(u * n), 0, n - 1),
                Math.Clamp((int)Math.Ceiling((u + 1 / zoom) * n) - 1, 0, n - 1),
                Math.Clamp((int)Math.Floor(v * n), 0, n - 1), Math.Clamp((int)Math.Ceiling((v + 1 / zoom) * n) - 1, 0, n - 1));
            if (view.Count <= 64 || level == 0) return view;
            level--;
        }
    }
    public static (Vector2 Min, Vector2 Max) AncestorUv(MapTileKey child, MapTileKey parent)
    {
        int factor = 1 << (child.Level - parent.Level);
        var min = new Vector2(child.X % factor, child.Y % factor) / factor;
        return (min, min + new Vector2(1f / factor));
    }
}

// Local files only. One decoder thread, bounded queues and GPU LRU; no DMA/network access.
public sealed class MapTileCache : IDisposable
{
    public const int TextureLimit = 128;
    private sealed record TextureEntry(Texture2D Texture) { public long Used; }
    private sealed record Decoded(MapTileKey Key, SKBitmap Bitmap);
    private readonly string _directory;
    private readonly string _fallback;
    private readonly object _sync = new();
    private readonly Queue<MapTileKey> _requests = new();
    private readonly Queue<Decoded> _ready = new();
    private readonly HashSet<MapTileKey> _pending = [];
    private readonly Dictionary<MapTileKey, long> _retryAfter = new();
    private readonly Dictionary<MapTileKey, TextureEntry> _textures = new();
    private readonly HashSet<MapTileKey> _wanted = [];
    private readonly List<MapTileKey> _order = new(128);
    private readonly Task _worker;
    private bool _stopped;
    private long _useCounter;
    public int ResidentCount => _textures.Count;
    public int MissingCount { get; private set; }
    public int RequestedLevel { get; private set; }

    public MapTileCache(string directory,string fallback=null)
    {
        _directory = directory;
        _fallback = fallback;
        _worker = Task.Run(DecodeLoop);
    }

    public static SKBitmap Decode(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 2_000_000) return null;
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width != 256 || codec.Info.Height != 256) return null;
        var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success) return bitmap;
        bitmap.Dispose(); return null;
    }

    private void DecodeLoop()
    {
        while (true)
        {
            MapTileKey key;
            lock (_sync)
            {
                while (!_stopped && (_requests.Count == 0 || _ready.Count >= 8)) Monitor.Wait(_sync);
                if (_stopped) return;
                key = _requests.Dequeue();
            }
            SKBitmap bitmap = null;
            try
            {
                if(_directory!=null)bitmap=Decode(Path.Combine(_directory, key.Level.ToString(), key.X.ToString(), key.Y + ".webp"));
                if(bitmap==null && key==new MapTileKey(0,0,0) && _fallback!=null)
                {
                    var file=new FileInfo(_fallback);
                    if(file.Exists && file.Length<=16_000_000)
                    {
                        using var stream=File.OpenRead(_fallback);using var codec=SKCodec.Create(stream);
                        if(codec!=null && codec.Info.Width is >0 and <=4096 && codec.Info.Height is >0 and <=4096)
                        {
                            using var source=new SKBitmap(new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul));
                            if(codec.GetPixels(source.Info,source.GetPixels())==SKCodecResult.Success)
                                bitmap=source.Resize(new SKImageInfo(256,256,SKColorType.Rgba8888,SKAlphaType.Unpremul),SKFilterQuality.Medium);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            lock (_sync)
            {
                if (_stopped) { bitmap?.Dispose(); return; }
                if (bitmap is null)
                {
                    // Bound negative caching as well as decoded textures.
                    if (_retryAfter.Count >= 256) _retryAfter.Clear();
                    _retryAfter[key] = Environment.TickCount64 + 5000;
                    _pending.Remove(key);
                }
                else _ready.Enqueue(new(key, bitmap));
            }
        }
    }

    private unsafe void UploadReady()
    {
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < 2 && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2; i++)
        {
            Decoded decoded;
            lock (_sync)
            {
                if (_ready.Count == 0) break;
                decoded = _ready.Dequeue(); _pending.Remove(decoded.Key); Monitor.PulseAll(_sync);
            }
            using var bitmap = decoded.Bitmap;
            if (!_wanted.Contains(decoded.Key) || _textures.ContainsKey(decoded.Key)) continue;
            var texture = Raylib.LoadTextureFromImage(new Image { Data = bitmap.GetPixels().ToPointer(), Width = 256, Height = 256,
                Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 });
            if (texture.Id == 0) continue;
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            if (_textures.Count >= TextureLimit)
            {
                MapTileKey oldest = default; long use = long.MaxValue;
                // Prefer evicting tiles outside the current view/ancestor set.
                foreach (var pair in _textures)
                    if (!_wanted.Contains(pair.Key) && pair.Value.Used < use) { oldest = pair.Key; use = pair.Value.Used; }
                if (use == long.MaxValue)
                    foreach (var pair in _textures)
                        if (pair.Value.Used < use) { oldest = pair.Key; use = pair.Value.Used; }
                Raylib.UnloadTexture(_textures[oldest].Texture); _textures.Remove(oldest);
            }
            _textures.Add(decoded.Key, new(texture) { Used = ++_useCounter });
        }
    }

    public void Draw(ImDrawListPtr draw, Vector2 start, float size, float zoom, float u, float v, int maximum, float angle=0)
    {
        var plan = MapTileLayout.PlanRotated(size * Math.Max(1, ImGui.GetIO().DisplayFramebufferScale.X), zoom, u, v, maximum, angle);
        RequestedLevel = plan.Level;
        _wanted.Clear(); _order.Clear();
        // Coarse ancestors first: detail arrives over a complete lower-resolution map.
        for (int z = 0; z <= plan.Level; z++)
            for (int x = plan.MinX; x <= plan.MaxX; x++) for (int y = plan.MinY; y <= plan.MaxY; y++)
            {
                var key = new MapTileKey(z, x >> (plan.Level - z), y >> (plan.Level - z));
                if (_wanted.Add(key)) _order.Add(key);
            }
        UploadReady();
        lock (_sync)
        {
            // Replace queued obsolete work after fast pan/zoom; leave only an in-flight decode and ready results.
            while (_requests.Count > 0) _pending.Remove(_requests.Dequeue());
            long now = Environment.TickCount64;
            foreach (var key in _order)
                if (!_textures.ContainsKey(key) && !_pending.Contains(key) && (!_retryAfter.TryGetValue(key, out long after) || now >= after))
                { _pending.Add(key); _requests.Enqueue(key); }
            Monitor.PulseAll(_sync);
        }
        MissingCount = 0;
        float cells = 1 << plan.Level, tileSize = size * zoom / cells;
        for (int x = plan.MinX; x <= plan.MaxX; x++) for (int y = plan.MinY; y <= plan.MaxY; y++)
        {
            var key = new MapTileKey(plan.Level, x, y);
            var ancestor = key;
            while (!_textures.ContainsKey(ancestor) && ancestor.Level > 0) ancestor = new(ancestor.Level - 1, ancestor.X / 2, ancestor.Y / 2);
            if (ancestor != key || !_textures.ContainsKey(key)) MissingCount++;
            if (!_textures.TryGetValue(ancestor, out var entry)) continue;
            entry.Used = ++_useCounter;
            var pos = MapTileLayout.Project(new(x / cells, y / cells), start, size, zoom, u, v);
            var uv = MapTileLayout.AncestorUv(key, ancestor);
            Vector2 Rotate(Vector2 p)=>MapTileLayout.Rotate(p,start+new Vector2(size/2),angle);
            draw.AddImageQuad((nint)entry.Texture.Id,Rotate(pos),Rotate(pos+new Vector2(tileSize,0)),
                Rotate(pos+new Vector2(tileSize)),Rotate(pos+new Vector2(0,tileSize)),
                uv.Min,new(uv.Max.X,uv.Min.Y),uv.Max,new(uv.Min.X,uv.Max.Y));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _stopped = true; _requests.Clear(); _pending.Clear();
            while (_ready.Count > 0) _ready.Dequeue().Bitmap.Dispose();
            Monitor.PulseAll(_sync);
        }
        // Decoder owns no graphics objects; it finishes its current bounded decode and exits independently.
        foreach (var entry in _textures.Values) Raylib.UnloadTexture(entry.Texture);
        _textures.Clear();
    }

    public void Suspend()
    {
        lock (_sync)
        {
            while (_requests.Count > 0) _pending.Remove(_requests.Dequeue());
        }
    }
}
