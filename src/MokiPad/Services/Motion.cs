using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MokiPad.Services;

/// <summary>
/// 一些很小的界面动画。WPF 的动画不是“定时器改属性”，而是由渲染线程按时间插值，
/// 所以这里用 <see cref="ScaleTransform"/> + <see cref="DoubleAnimation"/> 做图标切换。
/// </summary>
public static class Motion
{
    /// <summary>
    /// 切换按钮图标，并可选择做一个轻微的缩放动画。
    /// </summary>
    /// <param name="button">要换图标的按钮。</param>
    /// <param name="icon">新的图标对象，通常是资源里的 <c>Geometry</c>。</param>
    /// <param name="animate">是否播放动画；系统关闭“动画效果”时会自动跳过。</param>
    public static void SwapIcon(Button button, object icon, bool animate = true)
    {
        if (Equals(button.Content, icon)) return;

        button.Content = icon;

        // SystemParameters.ClientAreaAnimation 是 Windows 的“在控件和元素中播放动画”设置。
        // 用户关闭系统动画时，我们也尊重这个选择，直接切换图标。
        if (!animate || !SystemParameters.ClientAreaAnimation) return;

        // RenderTransform 只影响绘制，不参与布局；用它做缩放不会让按钮重新排版。
        var scale = new ScaleTransform();
        button.RenderTransformOrigin = new Point(.5, .5);
        button.RenderTransform = scale;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // ScaleX/ScaleY 同时从 0.82 回到 1，产生一个很轻的“弹一下”的效果。
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }
}
