using MokiPad.Core;

namespace MokiPad.Models;

/// <summary>
/// 一个只读文件的快照，同时也是标签列表里的一项。
/// 这里没有编辑缓冲区、撤销栈或“未保存”状态——MokiPad 只负责阅读。
/// </summary>
/// <remarks>
/// 这个类没有实现 <c>INotifyPropertyChanged</c>：标签项在打开文件时整体创建，
/// 之后界面需要更新的部分（标题、路径、解析结果）由窗口代码显式刷新，
/// 不需要让绑定系统监听每一个属性。
/// </remarks>
public sealed class DocumentTab
{
    /// <summary>文件的完整路径。创建标签时必须提供。</summary>
    public required string FilePath { get; init; }

    /// <summary>显示在标签上的文件名（不含目录）。</summary>
    public string Name => Path.GetFileName(FilePath);

    /// <summary>文件内容、编码和换行符；重新加载时整体替换。</summary>
    public required LoadedFile File { get; set; }

    /// <summary>Markdown 解析结果；超大文件可能被清空以释放内存。</summary>
    public MarkdownResult? Parsed { get; set; }

    /// <summary>切换标签时记住的滚动位置，避免回来时跳回顶部。</summary>
    public double ScrollOffset { get; set; }

    /// <summary>是否按 Markdown 解析；否则以纯文本方式阅读。</summary>
    public bool IsMarkdown =>
        new[] { ".md", ".markdown", ".mdown" }.Contains(Path.GetExtension(FilePath).ToLowerInvariant());
}
