using System.Windows.Media;

namespace MokiPad.Services;

/// <summary>
/// 阅读器使用的字体。字体文件随程序打包，字体名前的 <c>#</c> 是 WPF 的写法：
/// <c>pack://application:,,,/程序集;component/路径/#字体族名</c>。
/// </summary>
/// <remarks>
/// <see cref="FontFamily"/> 的字符串是一个“回退列表”：前面的字体缺字时，
/// WPF 会继续尝试后面的字体。正文使用与 MokiDown 相同的 Poppins，
/// 代码使用 JetBrains Mono，并额外放一个中文字体处理中文标点和汉字。
/// </remarks>
public static class AppFonts
{
    /// <summary>
    /// WPF 资源地址的根。三个逗号表示“当前程序集”，<c>component</c> 表示资源在程序集内部。
    /// 这样发布后不需要依赖系统安装字体。
    /// </summary>
    private static readonly Uri FontRoot = new("pack://application:,,,/MokiPad;component/");

    /// <summary>正文/标题字体：Poppins 优先，中文回退到内置字体和微软雅黑。</summary>
    public static FontFamily Body { get; } = new(FontRoot,
        "./Assets/Fonts/#Poppins, ./Assets/Fonts/#Moki CJK Fallback, Microsoft YaHei UI, Segoe UI");

    /// <summary>代码/数学字体：JetBrains Mono 优先，缺字时回退到中文和 Consolas。</summary>
    public static FontFamily Code { get; } = new(FontRoot,
        "./Assets/Fonts/#JetBrains Mono, ./Assets/Fonts/#Moki CJK Fallback, Microsoft YaHei UI, Consolas");
}
