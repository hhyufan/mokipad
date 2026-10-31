using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace MokiPad.Controls;

/// <summary>
/// 负责把 Markdown 里的图片地址变成 WPF 能显示的 <see cref="ImageSource"/>。
/// 支持本地文件、网络地址、data URI、位图和 SVG；解码不占用界面线程，并有容量上限缓存。
/// </summary>
/// <remarks>
/// WPF 里常见的几种图片类型：
/// <list type="bullet">
/// <item><description><see cref="BitmapImage"/>：位图（PNG/JPEG/GIF 的一帧等）。</description></item>
/// <item><description><see cref="DrawingImage"/>：矢量图，SharpVectors 把 SVG 转成它。</description></item>
/// <item><description><see cref="ImageSource"/>：上面两者的共同基类，界面控件只认它。</description></item>
/// </list>
/// </remarks>
public static class ImageLoader
{
    // 解析 SVG 内部结构用的正则。RegexOptions.Compiled 让正则在第一次使用时编译成原生代码，
    // 适合会被反复调用的固定模式。
    private static readonly Regex ViewBoxPattern = new("viewBox\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex XmlDeclarationPattern = new(@"<\?xml[^>]*\?>", RegexOptions.Compiled);
    private static readonly Regex SvgOpenPattern = new("<svg[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SvgClosePattern = new("</svg>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TitlePattern = new("<title>.*?</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex ImageSelfClosingPattern = new("<image\\b([^>]*?)/>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImagePairedPattern = new("<image\\b([^>]*)>.*?</image>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HrefPattern = new("(?:xlink:)?href\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>单张图片的最大字节数，防止误点一个超大文件把内存吃光。</summary>
    private const int MaxBytes = 24 * 1024 * 1024;

    /// <summary>网络图片下载器；超时设为 15 秒，避免一直卡住。</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>已解码图片的缓存，按像素占用的估算字节数限制在 128 MB 左右。</summary>
    private static readonly BoundedImageCache<ImageSource> Cache = new(128L * 1024 * 1024);

    /// <summary>原始字节缓存，给 GIF 播放器复用，避免同一地址重复下载。</summary>
    private static readonly BoundedImageCache<byte[]> RawCache = new(96L * 1024 * 1024);

    // 同时最多解码三张图：解码是处理器密集型工作，太多并发反而会拖慢界面。
    private static readonly SemaphoreSlim DecodeGate = new(3, 3);

    /// <summary>
    /// 专门用来解析 SVG 的后台 STA 线程。WPF 的 Drawing/Image 对象必须在 STA 线程创建，
    /// 而 SharpVectors 又需要 Dispatcher，所以这里起一个常驻线程并运行自己的消息循环。
    /// </summary>
    private static readonly Lazy<Dispatcher> SvgWorker = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "MokiPad SVG decoder" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    /// <summary>
    /// 按图片地址加载图片。同一个地址会被缓存；网络/磁盘读取与解码都在后台完成。
    /// </summary>
    /// <param name="source">Markdown 里的图片地址，可以是本地路径、网址或 data URI。</param>
    /// <param name="documentPath">当前文档路径，用来解析相对路径。</param>
    /// <param name="pixelWidth">期望显示宽度；大于零时会按 256 像素分档解码，减少大图内存。</param>
    public static Task<ImageSource?> LoadAsync(string source, string? documentPath, int pixelWidth = 0)
    {
        var key = Key(source, documentPath);

        // 按 256 像素为一档，避免窗口宽度每变化一点就重新解码一张大图。
        var width = pixelWidth <= 0 ? 0 : (int)Math.Ceiling(pixelWidth / 256.0) * 256;

        return key is null
            ? Task.FromResult<ImageSource?>(null)
            : Cache.GetAsync(key + ":" + width,
                () => Task.Run(() => LoadCoreAsync(source, documentPath, width)),
                image => image is BitmapSource bitmap
                    ? (long)bitmap.PixelWidth * bitmap.PixelHeight * 4
                    : 64 * 1024);
    }

    /// <summary>
    /// 把图片地址解析成本地文件路径；网络地址和 data URI 返回 <c>null</c>。
    /// 相对路径以当前文档所在目录为基准。
    /// </summary>
    private static string? LocalPath(string source, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var value = Uri.UnescapeDataString(source.Trim());
        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(value, UriKind.Absolute, out var fileUri))
            return fileUri.LocalPath;
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return null;
        try
        {
            if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
            var basePath = documentPath is null ? null : Path.GetDirectoryName(documentPath);
            return basePath is null ? null : Path.GetFullPath(Path.Combine(basePath, value));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// 生成缓存键。本地文件把“长度 + 最后修改时间”也算进去，
    /// 这样文件被外部修改后不会继续用旧图。
    /// </summary>
    private static string? Key(string source, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var value = source.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        if (Uri.TryCreate(Uri.UnescapeDataString(value), UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https") return uri.AbsoluteUri;
        var path = LocalPath(value, documentPath);
        if (path is null) return null;
        var info = new FileInfo(path);
        return path.ToLowerInvariant() + ":" + (info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "missing");
    }

    /// <summary>
    /// 读取字节并解码。位图在当前线程池线程解码；SVG 则切到专门的 STA 线程，
    /// 因为 WPF 的矢量绘图对象有线程亲和性。
    /// </summary>
    private static async Task<ImageSource?> LoadCoreAsync(string source, string? documentPath, int width)
    {
        var bytes = await ReadBytesAsync(source, documentPath).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0) return null;
        await DecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return LooksLikeSvg(bytes)
                ? await SvgWorker.Value.InvokeAsync(() => DecodeSvg(bytes), DispatcherPriority.Background).Task.ConfigureAwait(false)
                : DecodeBitmap(bytes, width);
        }
        catch (Exception)
        {
            return null;
        }
        finally { DecodeGate.Release(); }
    }

    /// <summary>原始图片字节，给 GIF 播放器使用；结果同样有容量上限缓存。</summary>
    public static async Task<byte[]?> ReadBytesAsync(string source, string? documentPath)
    {
        var key = Key(source, documentPath);
        return key is null ? null : await RawCache.GetAsync(key, () => Task.Run(() => ReadBytesCoreAsync(source, documentPath)), bytes => bytes.LongLength).ConfigureAwait(false);
    }
    /// <summary>通过文件头判断是不是 GIF，不需要先完整解码。</summary>
    public static bool IsGif(byte[] bytes) =>
        bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F';

    /// <summary>
    /// 真正读取字节的方法：处理 data URI、网络下载（带磁盘缓存和一次重试）以及本地文件。
    /// </summary>
    private static async Task<byte[]?> ReadBytesCoreAsync(string source, string? documentPath)
    {
        try
        {
            var value = source.Trim();
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = value.IndexOf(',');
                if (comma < 0) return null;
                var meta = value[..comma];
                var payload = value[(comma + 1)..];
                return meta.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(payload)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var cached = RemoteCachePath(value);
                if (cached is not null && File.Exists(cached))
                    try
                    {
                        return await File.ReadAllBytesAsync(cached).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                    }

                for (var attempt = 0; attempt < 2; attempt++)
                    try
                    {
                        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                            .ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) continue;
                        var limit = LimitFor(value);
                        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > limit)
                            return null;
                        await using var remote = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var output = new MemoryStream(); var chunk = new byte[65536];
                        int read;
                        while ((read = await remote.ReadAsync(chunk).ConfigureAwait(false)) != 0)
                        {
                            if (output.Length + read > limit) return null;
                            output.Write(chunk, 0, read);
                        }
                        var downloaded = output.ToArray();
                        if (downloaded.Length is 0 or > 8 * 1024 * 1024 || cached is null) return downloaded;
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                            await File.WriteAllBytesAsync(cached, downloaded).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                        }

                        return downloaded;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                    {
                        if (attempt == 0) await Task.Delay(250).ConfigureAwait(false);
                    }

                return null;
            }

            var path = LocalPath(value, documentPath);
            if (path is null || !File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length > LimitFor(path)) return null;
            return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException
                                       or TaskCanceledException or FormatException or NotSupportedException
                                       or ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    private static long LimitFor(string source)
    {
        var clean = source.Split('?', '#')[0];
        return clean.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? 64L * 1024 * 1024 : MaxBytes;
    }

    private static string? RemoteCachePath(string url)
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MokiPad", "images");
            return Path.Combine(directory, hash + ".bin");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 判断字节流是不是 SVG。先检查 PNG/JPEG/GIF/BMP/WEBP/ICO 的文件头，
    /// 因为有些位图会在元数据里带 “&lt;svg” 字样，不能只看文本。
    /// </summary>
    private static bool LooksLikeSvg(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        // Bitmap signatures first: PNG/JPEG/GIF/BMP/WEBP/ICO may carry SVG strings in metadata.
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return false;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return false;
        if (bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F') return false;
        if (bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return false;
        if (bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F')
            return false;
        if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0) return false;
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256))
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 用 SharpVectors 把 SVG 转成 WPF 的 <see cref="DrawingImage"/>。
    /// 转换完成后调用 <c>Freeze()</c>，让图片变成只读对象，可以跨线程安全使用，WPF 也能做性能优化。
    /// </summary>
    private static DrawingImage? DecodeSvg(byte[] bytes)
    {
        try
        {
            // Shields-style badges embed their logo as a nested data-URI SVG image.
            // Inline that SVG so SharpVectors renders the icon instead of failing on WIC.
            var text = InlineNestedSvgImages(Encoding.UTF8.GetString(bytes));
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = false };
            using var reader = new FileSvgReader(settings);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var drawing = reader.Read(stream);
            if (drawing is null) return null;
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 处理 Shields 风格徽章：SVG 里再嵌套一个 data URI 的 SVG 图片时，
    /// SharpVectors 默认无法解析，这里把内层 SVG 直接展开成普通 &lt;svg&gt; 节点。
    /// </summary>
    private static string InlineNestedSvgImages(string svg)
    {
        svg = ImageSelfClosingPattern.Replace(svg, Replace);
        svg = ImagePairedPattern.Replace(svg, Replace);
        return svg;

        string Replace(Match match)
        {
            var attributes = match.Groups[1].Value;
            var href = HrefPattern.Match(attributes).Groups[1].Value;
            const string prefix = "data:image/svg+xml;base64,";
            if (!href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
            string inner;
            try
            {
                inner = Encoding.UTF8.GetString(Convert.FromBase64String(href[prefix.Length..]));
            }
            catch (FormatException)
            {
                return "";
            }

            var viewBox = ViewBoxPattern.Match(inner).Groups[1].Value;
            if (viewBox.Length == 0) viewBox = "0 0 24 24";
            inner = XmlDeclarationPattern.Replace(inner, "");
            inner = SvgOpenPattern.Replace(inner, "");
            inner = SvgClosePattern.Replace(inner, "");
            inner = TitlePattern.Replace(inner, "");

            return
                $"<svg x=\"{Attr("x", attributes)}\" y=\"{Attr("y", attributes)}\" width=\"{Attr("width", attributes)}\" height=\"{Attr("height", attributes)}\" viewBox=\"{viewBox}\">{inner}</svg>";

            string Attr(string name, string source)
            {
                return Regex.Match(source, "\\b" + name + "\\s*=\\s*\"([^\"]*)\"")
                    .Groups[1].Value;
            }
        }
    }

    /// <summary>
    /// 解码位图。先用一个临时解码器读出原始像素宽度，再按显示宽度决定是否缩放，
    /// 避免把四千万像素的大图原样读进内存。
    /// </summary>
    private static BitmapImage? DecodeBitmap(byte[] bytes, int requestedWidth)
    {
        int width;
        try
        {
            using var probe = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            width = decoder.Frames[0].PixelWidth;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);

        // BitmapImage 是 WPF 的位图类型。BeginInit/EndInit 之间是“批量设置属性”，
        // EndInit 时才真正解码；这样只需要读一次流。
        var image = new BitmapImage();
        image.BeginInit();

        // OnLoad：解码后立刻把像素读进内存，不依赖这个 MemoryStream 的生命周期。
        image.CacheOption = BitmapCacheOption.OnLoad;

        // 只解码到需要的宽度，避免把原图完整读进内存。
        image.DecodePixelWidth = requestedWidth > 0 && width > requestedWidth ? requestedWidth : 0;
        image.StreamSource = stream;
        image.EndInit();

        // 冻结后图片变成只读，可跨线程使用，WPF 也能做渲染优化。
        image.Freeze();
        return image;
    }

}
