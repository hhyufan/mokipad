using System.Globalization;
using System.Windows;

namespace MokiPad.Services;

public static class UiText
{
    private static string language = "zh";

    private static readonly Dictionary<string, (string Zh, string En)> Text = new()
    {
        ["New"] = ("新建", "New"), ["Open"] = ("打开", "Open"), ["Save"] = ("保存", "Save"),
        ["SaveAs"] = ("另存为…", "Save as…"), ["File"] = ("文件", "File"), ["EditMenu"] = ("编辑", "Edit"),
        ["View"] = ("视图", "View"), ["Close"] = ("关闭标签页", "Close tab"), ["Exit"] = ("退出", "Exit"),
        ["Rename"] = ("重命名", "Rename"), ["CloseOthers"] = ("关闭其他标签页", "Close other tabs"),
        ["CloseAll"] = ("关闭所有标签页", "Close all tabs"), ["InvalidName"] = ("请输入有效的文件名。", "Enter a valid file name."),
        ["Undo"] = ("撤销", "Undo"), ["Redo"] = ("重做", "Redo"), ["Cut"] = ("剪切", "Cut"),
        ["Copy"] = ("复制", "Copy"), ["Paste"] = ("粘贴", "Paste"), ["SelectAll"] = ("全选", "Select all"),
        ["Source"] = ("编辑", "Edit"), ["Split"] = ("分屏", "Split"), ["Read"] = ("阅读", "Read"),
        ["ShowPreview"] = ("切换到预览", "Switch to preview"), ["ShowEditor"] = ("切换到编辑", "Switch to editor"),
        ["ToLight"] = ("切换到浅色主题", "Switch to light theme"), ["ToDark"] = ("切换到深色主题", "Switch to dark theme"),
        ["AboutMenu"] = ("关于 MokiPad", "About MokiPad"),
        ["Outline"] = ("大纲", "Outline"), ["Recent"] = ("最近", "Recent"), ["Find"] = ("查找", "Find"),
        ["Replace"] = ("替换", "Replace"), ["ReplaceAll"] = ("全部替换", "Replace all"),
        ["Next"] = ("下一个", "Next"), ["Previous"] = ("上一个", "Previous"),
        ["MatchCase"] = ("区分大小写", "Match case"), ["Wrap"] = ("自动换行", "Word wrap"),
        ["Theme"] = ("切换明暗主题", "Switch theme"), ["Background"] = ("显示背景", "Show background"),
        ["ChooseBackground"] = ("选择背景图片…", "Choose background image…"),
        ["ResetBackground"] = ("恢复星光书页背景", "Restore starlight background"),
        ["Language"] = ("English", "简体中文"), ["Pin"] = ("窗口置顶", "Always on top"),
        ["Minimize"] = ("最小化", "Minimize"), ["Maximize"] = ("最大化 / 还原", "Maximize / restore"),
        ["CloseWindow"] = ("关闭窗口", "Close window"), ["Settings"] = ("外观与帮助", "Appearance and help"),
        ["EmptyOutline"] = ("标题会显示在这里", "Headings appear here"), ["EmptyRecent"] = ("还没有最近文件", "No recent files yet"),
        ["Untitled"] = ("未命名", "Untitled"), ["Ready"] = ("就绪", "Ready"),
        ["Opening"] = ("正在打开…", "Opening…"), ["Rendering"] = ("正在更新预览…", "Updating preview…"),
        ["Saved"] = ("已保存", "Saved"), ["SaveQuestion"] = ("保存对“{0}”的修改？", "Save changes to “{0}”?"),
        ["Error"] = ("操作未完成", "Operation failed"),
        ["Conflict"] = ("文件已被其他程序修改。选择“是”覆盖，选择“否”保留当前编辑。",
            "The file changed on disk. Choose Yes to overwrite, or No to keep editing."),
        ["LineColumn"] = ("行 {0}，列 {1}", "Ln {0}, Col {1}"), ["Characters"] = ("{0:N0} 字符", "{0:N0} characters"),
        ["PreviewTime"] = ("{0:N0} 块 · 解析 {1:F0} ms", "{0:N0} blocks · parsed in {1:F0} ms"),
        ["OpenedTime"] = ("已打开 · {0:F0} ms", "Opened · {0:F0} ms"), ["NoMatch"] = ("未找到匹配内容", "No matches"),
        ["Replaced"] = ("已替换 {0} 处", "Replaced {0} matches"), ["PlainText"] = ("纯文本", "Plain text"),
        ["EditHere"] = ("从这里开始书写…", "Start writing here…"),
        ["PreviewHint"] = ("写下 Markdown，然后切换分屏或阅读", "Write Markdown, then switch to Split or Read"),
        ["DoubleEdit"] = ("双击定位到源码", "Double-click to edit source"), ["CopyCode"] = ("复制代码", "Copy code"),
        ["LocalImage"] = ("图片 · {0}", "Image · {0}"), ["ToggleTask"] = ("切换任务状态", "Toggle task"),
        ["About"] = ("MokiPad · MokiDown 的轻量记事本伙伴\n原生 WPF · 本地文件 · 按需预览",
            "MokiPad · MokiDown’s lightweight notepad companion\nNative WPF · Local files · On-demand preview"),
        ["OpenFilter"] = ("Markdown 与文本|*.md;*.markdown;*.mdown;*.txt;*.log;*.json;*.cs;*.js;*.xml|所有文件|*.*",
            "Markdown and text|*.md;*.markdown;*.mdown;*.txt;*.log;*.json;*.cs;*.js;*.xml|All files|*.*"),
        ["ReaderWelcome"] = ("专注阅读 Markdown", "Focus on Markdown"),
        ["ReaderHint"] = ("拖放文件到这里，或按 Ctrl+O 打开。", "Drop a file here, or press Ctrl+O to open."),
        ["OpenMarkdown"] = ("打开 Markdown", "Open Markdown"), ["ReaderReady"] = ("Markdown 查看器", "Markdown viewer"),
        ["Reload"] = ("重新加载", "Reload"), ["CopyDocument"] = ("复制整篇 Markdown", "Copy entire Markdown"),
        ["ReaderAbout"] = ("MokiPad · 专注 Markdown 阅读\n原生 WPF · 本地文件 · 长文虚拟化",
            "MokiPad · Focused Markdown reading\nNative WPF · Local files · Virtualized long documents"),
        ["SaveFilter"] = ("Markdown|*.md|文本文件|*.txt|所有文件|*.*", "Markdown|*.md|Text files|*.txt|All files|*.*"),
        ["ImageFilter"] = ("图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"),
        ["ZoomIn"] = ("放大文字", "Zoom in"), ["ZoomOut"] = ("缩小文字", "Zoom out"),
        ["Footnotes"] = ("脚注", "Footnotes"), ["BackToReference"] = ("返回引用", "Back to reference"),
        ["Image"] = ("图片", "Image"),
        ["CopySelectedMarkdown"] = ("复制所选 Markdown", "Copy selection as Markdown"),
        ["CopyBlockMarkdown"] = ("复制此块 Markdown", "Copy block as Markdown"),
        ["CopyCodeMarkdown"] = ("复制代码块 Markdown（包含围栏）", "Copy code block Markdown (with fences)"),
        ["ClipboardBusy"] = ("剪贴板暂时被占用，请重试复制。", "The clipboard is busy. Please try copying again."),
        ["PlayGif"] = ("在系统查看器中播放 GIF", "Play GIF in the system viewer"),
        ["Fullscreen"] = ("全屏", "Fullscreen"), ["NotMarkdown"] = ("当前文件使用纯文本阅读", "This file opens as plain text")
    };

    public static string Get(string key, params object[] args)
    {
        var text = Text.TryGetValue(key, out var pair) ? language == "en" ? pair.En : pair.Zh : key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    public static void Apply(string value)
    {
        language = value;
        foreach (var key in Text.Keys) Application.Current.Resources["L." + key] = Get(key);
    }
}
