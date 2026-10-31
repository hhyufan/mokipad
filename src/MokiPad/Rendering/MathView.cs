using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MokiPad.Services;
using WpfMath.Controls;
using WpfMath.Parsers;
using XamlMath.Exceptions;

namespace MokiPad.Rendering;

/// <summary>
/// 公式的 WPF 宿主控件。TeX 的解析和排版由 WpfMath 库完成，
/// 这里只负责把库生成的 <see cref="FormulaControl"/> 放进阅读流，并在失败时显示原文。
/// </summary>
/// <remarks>
/// 继承 <see cref="ContentControl"/> 是因为 WPF 的内容控件可以很自然地被放进
/// <see cref="System.Windows.Documents.InlineUIContainer"/>，从而在文字段落中间显示一块公式。
/// </remarks>
public sealed class MathView : ContentControl
{
    /// <summary>WpfMath 提供的公式显示控件，真正的排版工作由它完成。</summary>
    private readonly FormulaControl formula = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        IsHitTestVisible = false,
        Focusable = false,
        SystemTextFontName = "Segoe UI",
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top
    };

    /// <summary>是否为独立公式（行间公式）；独立公式会排得稍大。</summary>
    private readonly bool display;

    /// <summary>公式字号；避免与 WPF 的 <c>FontSize</c> 属性混淆，单独保存。</summary>
    private double mathFontSize = 16;

    /// <summary>公式前景色，通常跟随主题的正文颜色。</summary>
    private Brush mathForeground = Brushes.Black;

    /// <summary>TeX 原文，解析失败时用于回退显示。</summary>
    private readonly string source = "";

    /// <summary>创建一个空的公式宿主；真正的内容由对象初始化器设置。</summary>
    public MathView()
    {
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Top;
        Content = formula;
    }

    /// <summary>TeX 原文。只能在对象初始化时设置，设置后会立即重建公式。</summary>
    public string Source
    {
        get => source;
        init
        {
            if (source == value) return;
            source = value;
            Rebuild();
        }
    }

    /// <summary>是否为行间公式。只能在初始化时设置。</summary>
    public bool Display
    {
        get => display;
        init
        {
            if (display == value) return;
            display = value;
            Rebuild();
        }
    }

    /// <summary>公式字号。设置后重新排版，行间公式会在此基础上放大一点。</summary>
    public double MathFontSize
    {
        set
        {
            if (Math.Abs(mathFontSize - value) < .01) return;
            mathFontSize = value;
            Rebuild();
        }
    }

    /// <summary>公式颜色。设置后重新排版。</summary>
    public Brush MathForeground
    {
        set
        {
            if (ReferenceEquals(mathForeground, value)) return;
            mathForeground = value;
            Rebuild();
        }
    }

    /// <summary>上一次排版是否失败；失败时显示原始 TeX，而不是空白。</summary>
    private bool HasError { get; set; }

    /// <summary>WpfMath 的缩放比例。行间公式比行内公式大 10%。</summary>
    private double Scale => Math.Max(6, mathFontSize) * (display ? 1.1 : 1);

    /// <summary>把当前属性同步给 WpfMath 控件；解析失败时回退为等宽文本。</summary>
    private void Rebuild()
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            Content = null;
            HasError = false;
            return;
        }

        try
        {
            // 先自己解析一次，目的是把库的异常挡在这里，避免 FormulaControl 内部再抛。
            WpfTeXFormulaParser.Instance.Parse(source);

            formula.Foreground = mathForeground;
            formula.Scale = Scale;
            formula.Formula = source;
            HasError = formula.HasError;
            Content = HasError ? Fallback() : formula;
        }
        catch (Exception ex) when (ex is TexException or ArgumentException or InvalidOperationException)
        {
            HasError = true;
            Content = Fallback();
        }
    }

    /// <summary>公式无法排版时，用代码字体显示原始 TeX，至少让用户看到内容。</summary>
    private TextBlock Fallback()
    {
        return new TextBlock
        {
            Text = source,
            FontFamily = AppFonts.Code,
            FontSize = Math.Max(12, mathFontSize * .9),
            Foreground = mathForeground,
            TextWrapping = TextWrapping.Wrap
        };
    }
}
