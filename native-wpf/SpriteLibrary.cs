using System.IO;
using System.Windows.Media.Imaging;

namespace DeepSeekPet;

/// <summary>按状态加载 assets/frames/&lt;state&gt;/fNN.png 逐帧图片，并做内存缓存。</summary>
internal sealed class SpriteLibrary
{
    private readonly string _root;
    private readonly Dictionary<string, BitmapSource[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SpriteLibrary(string root) => _root = root;

    public BitmapSource[] Get(string state)
    {
        if (_cache.TryGetValue(state, out BitmapSource[]? cached) && cached.Length > 0)
            return cached;

        string dir = Path.Combine(_root, state);
        var frames = new List<BitmapSource>();
        if (Directory.Exists(dir))
        {
            string[] files = Directory.GetFiles(dir, "*.png");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(file, UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.EndInit();
                    image.Freeze();
                    frames.Add(image);
                }
                catch { }
            }
        }

        BitmapSource[] result = frames.ToArray();
        _cache[state] = result;
        return result;
    }
}
