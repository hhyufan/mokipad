using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MokiPad.Services;

/// <summary>
/// 主题切换。WPF 的 <c>DynamicResource</c> 会在资源字典里按名字查找画刷，
/// 所以换主题不需要遍历控件，只要替换 <see cref="Application.Current"/> 资源里的画刷即可。
/// </summary>
public static class ThemeService
{
    /// <summary>
    /// 应用浅色/深色主题。
    /// </summary>
    /// <param name="dark">是否使用深色。</param>
    /// <param name="animate">是否让颜色平滑过渡；系统关闭动画时会自动跳过。</param>
    public static void Apply(bool dark, bool animate = false)
    {
        // 颜色写成 #AARRGGBB（A=透明度，R/G/B=红绿蓝）。
        // 这些名字与 Styles/Controls.xaml 里的 DynamicResource 一一对应。
        var values = new Dictionary<string, string>
        {
            ["Surface"] = dark ? "#1E1E1E" : "#FFFFFF",
            ["Canvas"] = dark ? "#181818" : "#F5F5F7",
            ["BackgroundBase"] = dark ? "#181818" : "#FFFFFF",
            ["Text"] = dark ? "#F5F5F7" : "#2C2C2E",
            ["Secondary"] = dark ? "#98989D" : "#6D6D6F",
            ["Border"] = dark ? "#3A3A3C" : "#E5E5E7",
            ["Accent"] = dark ? "#5A9FFF" : "#4091FF",
            ["Tint"] = dark ? "#265A9FFF" : "#184091FF",
            ["Hover"] = dark ? "#18FFFFFF" : "#10000000",
            ["Selected"] = dark ? "#345A9FFF" : "#264091FF",
            ["Code"] = dark ? "#38141418" : "#70F5F7FA",
            ["Popup"] = dark ? "#FA242428" : "#FAFFFFFF",
            ["Thumb"] = dark ? "#605A9FFF" : "#504091FF",
            ["SyntaxKeyword"] = dark ? "#C678DD" : "#A626A4",
            ["SyntaxString"] = dark ? "#98C379" : "#50A14F",
            ["SyntaxText"] = dark ? "#ABB2BF" : "#383A42",
            ["SyntaxComment"] = dark ? "#7F848E" : "#A0A1A7",
            ["SyntaxNumber"] = dark ? "#D19A66" : "#E45649",
            ["SyntaxVariable"] = dark ? "#E06C75" : "#E45649",
            ["SyntaxCommand"] = dark ? "#61AFEF" : "#0184BC",
            ["SyntaxParameter"] = dark ? "#D19A66" : "#C18401",
            ["SyntaxFunction"] = dark ? "#61AFEF" : "#4078F2",
            ["SyntaxType"] = dark ? "#E5C07B" : "#C18401",
            ["SyntaxOperator"] = dark ? "#56B6C2" : "#0184BC",
            ["MdHash"] = "#F56C6C",
            ["MdSoft"] = dark ? "#525A9FFF" : "#2EA9CAFF",
            ["MdQuote"] = dark ? "#4D5A9FFF" : "#29A9CAFF",
            ["MdCode"] = dark ? "#1BC5DCFD" : "#1B89A1C9",
            ["MdLine"] = dark ? "#24F5F5F7" : "#242C2C2E",
            ["MdTableHead"] = dark ? "#3DB5D4FF" : "#3DA9CAFF",
            ["MdRowAlt"] = dark ? "#12B5D4FF" : "#12A9CAFF"
        };

        foreach (var (name, value) in values)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);

            // 找到旧画刷，动画需要从旧颜色开始；没有旧画刷时直接设置新颜色。
            var previous = Application.Current.Resources[name] as SolidColorBrush;
            var brush = new SolidColorBrush(animate && previous is not null ? previous.Color : color);

            if (animate && previous is not null && SystemParameters.ClientAreaAnimation)
            {
                // 动画只能作用于“未冻结”的画刷；因此动画分支不调用 Freeze。
                // ColorAnimation 从当前颜色渐变到目标颜色，持续 220 毫秒。
                brush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(color, TimeSpan.FromMilliseconds(220))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                // 不播放动画时把画刷冻结：WPF 可以对冻结对象做优化，跨线程读取也更安全。
                brush.Color = color;
                brush.Freeze();
            }

            // 替换资源后，所有使用 DynamicResource 的控件会自动重新取画刷。
            Application.Current.Resources[name] = brush;
        }
    }
}
