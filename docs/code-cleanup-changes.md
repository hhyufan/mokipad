# MokiPad 代码清理修改集（按修改类型）

> 范围：从 `MokiPad.sln` 解决方案文档/检查工作开始，到 InspectCode 与 Rider 复查归零为止。
> 基线：`143` 条（其中 95 条 WARNING）→ 全量口径 `155` 条（0 warning，47 SUGGESTION + 108 HINT）→ **0**。
> 结果：`dotnet build` 0 warning / 0 error；验证套件全部 PASS；InspectCode 0 issue。

本文按“修改的性质”分类，而不是按文件罗列。每一类都给出**改前**与**改后**的代码片段（节选），并说明为什么这样改、影响是什么。

---

## 0. 总览

| 阶段 | 处理方式 | 数量 | 结果 |
| --- | --- | ---: | --- |
| 警告清理（143 基线） | 手工修命名、可空性、异步、未使用成员、XAML | 95 | 0 WARNING |
| 风格批处理（155 基线） | ReSharper Full Cleanup | 46 | 尾逗号、`new()`、括号、限定符统一 |
| 语义改写（155 基线） | 逐条修改 | 95 | 模式、switch、异步、可见性、只读性等 |
| 规则抑制（155 基线） | `.editorconfig` 关闭 `SYSLIB1045` | 14 | 说明见 7.4 |
| **两段合计** | | **250 处改动** | **0 warning / 0 issue** |

> 说明：`143 → 47` 是警告阶段；随后按 `--severity=HINT` 取全量口径得到 `155`（47 SUGGESTION + 108 HINT），再清到 0。上表两段基线不同，所以是 95 + 155 = 250 处改动，不是 155 处。Rider 的 Problems 面板默认也统计 HINT，所以两边现在都是 0。

---

## 1. 项目与配置类修改

### 1.1 删除重复指定的基类

**规则**：`Redundant base class` / `CS0108` 类告警。

WPF 的 `App` 与 `MainWindow` 已经在 XAML 根元素里声明了基类，C# 代码里再写一次 `: Application` / `: Window` 属于重复且容易和 XAML 生成代码冲突。

**改前**

```csharp
public partial class App : Application
{
    // ...
}

public partial class MainWindow : Window
{
    // ...
}
```

**改后**

```csharp
public partial class App
{
    // ...
}

public partial class MainWindow
{
    // ...
}
```

**影响**：行为不变；XAML 生成的 `partial` 部分已经提供了基类，编译器不再报“基类重复指定”。

---

### 1.2 私有字段命名规则：用 DotSettings 固化项目风格

**规则**：`Inconsistent Naming`（44 条）。

ReSharper 默认要求私有字段 `_camelCase`，项目现有代码是 `camelCase`。与其重命名 44 个字段，不如把项目约定写进解决方案级设置。

**改前**：没有解决方案级命名配置，ReSharper 按默认 `_camelCase` 检查。

**改后**：新增 `MokiPad.sln.DotSettings`：

```xml
<s:String x:Key="/Default/CodeStyle/Naming/CSharpNaming/PredefinedNamingRules/=PrivateInstanceFields/@EntryIndexedValue">
  <Policy Inspect="True" Prefix="" Suffix="" Style="aaBb" />
</s:String>
<s:String x:Key="/Default/CodeStyle/Naming/CSharpNaming/PredefinedNamingRules/=PrivateStaticFields/@EntryIndexedValue">
  <Policy Inspect="True" Prefix="" Suffix="" Style="aaBb" />
</s:String>
```

`aaBb` 即 camelCase；这样 CLI 与 Rider 都按项目风格检查，44 条命名告警一次消失，不需要改动业务字段名。

> 例外：测试里的 `SourceArray` 是 `static readonly` 且非私有，按 PascalCase 规则命名，见 8.1。

---

### 1.3 XAML：设计时 DataContext、去掉重复样式属性

**规则**：`Unresolved binding path`、`UnusedMember.Global`、重复属性告警。

Tab 模板里的 `{Binding Name}` / `{Binding FilePath}` 在编译期没有设计时类型，Rider 会报“无法解析符号”；另外按钮的 `Width="32"`、`FontSize="16"` 与 `IconButton` 样式重复，`x:Name` 也没有代码引用。

**改前**

```xml
<Window x:Class="MokiPad.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        ...>

<ListBox.ItemTemplate>
  <DataTemplate>
    <StackPanel Orientation="Horizontal">
      <TextBlock Text="{Binding Name}" ... ToolTip="{Binding FilePath}" />
    </StackPanel>
  </DataTemplate>
</ListBox.ItemTemplate>

<Button x:Name="ThemeButton" Style="{StaticResource IconButton}"
        Width="32" Height="28" FontSize="16" Padding="4" ... />
<Button x:Name="SettingsButton" Style="{StaticResource IconButton}"
        Width="32" Height="28" FontSize="16" Padding="4" ... />
```

**改后**

```xml
<Window x:Class="MokiPad.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:models="clr-namespace:MokiPad.Models"
        mc:Ignorable="d" ...>

<ListBox.ItemTemplate>
  <DataTemplate>
    <StackPanel Orientation="Horizontal"
                d:DataContext="{d:DesignInstance models:DocumentTab}">
      <TextBlock Text="{Binding Name}" ... ToolTip="{Binding FilePath}" />
    </StackPanel>
  </DataTemplate>
</ListBox.ItemTemplate>

<Button x:Name="ThemeButton" Style="{StaticResource IconButton}" Height="28" Padding="4" ... />
<Button Style="{StaticResource IconButton}" Height="28" Padding="4" ... />
```

**影响**：

- `d:DataContext` 只影响设计时/IDE 分析，运行时不产生绑定。
- `Width`/`FontSize` 交给 `IconButton` 样式统一，按钮尺寸不再两处维护。
- `SettingsButton` 的 `x:Name` 删除后不影响 `Click="OnSettings"`；`AddTabButton` 同理。

菜单里的 `Check` 图标补了 `Grid.Column="0"`，避免它被默认放到第 2 列：

```xml
<!-- 改前：没有列号，落在 Grid 默认列 -->
<Path x:Name="Check" Data="M2,6 L5,9 11,2" ... />

<!-- 改后 -->
<Path x:Name="Check" Grid.Column="0" Data="M2,6 L5,9 11,2" ... />
```

---

### 1.4 移除多余 `using`

**规则**：`Redundant using directive`。

**改前**

```csharp
using System.Diagnostics;   // MainWindow.xaml.cs，已不再使用 Process/Debug
using MokiPad;              // Verification，命名空间已嵌套在 MokiPad 下
```

**改后**

```csharp
// 两行都删除
```

**影响**：减少无意义的编译依赖；`MokiPad` 命名空间下的类型在 `MokiPad.Verification` 里仍然可见。

---

## 2. 异步与资源生命周期

### 2.1 `async` lambda 传给 `Action` → 明确的 `async Task` 方法

**规则**：`AsyncVoidEventHandlerMethod` / `async void` 风险。

`new Action(async () => ...)` 是 `async void`，异常不会被任何人观察，可能直接终止进程。把它拆成一个返回 `Task` 的静态方法，再用 `_ =` 显式丢弃。

**改前**

```csharp
window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(async () =>
{
    foreach (var arg in e.Args.Where(File.Exists)) await window.OpenFileAsync(arg);
}));
```

**改后**

```csharp
window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
    new Action(() => _ = OpenFilesAsync(window, e.Args)));

private static async Task OpenFilesAsync(MainWindow window, string[] arguments)
{
    foreach (var argument in arguments.Where(File.Exists))
        await window.OpenFileAsync(argument);
}
```

**影响**：异常现在留在 `Task` 里，至少可以被调试器/日志观察；`_ =` 明确表示“故意不等待”。

---

### 2.2 事件处理器 `async void` → `void` 入口 + `async Task` 实现

**规则**：`AsyncVoidEventHandlerMethod`（XAML 事件处理器）。

XAML 的 `Click`/`Drop` 签名必须是 `void`，但方法体不必是 `async void`。拆成薄的同步入口 + 异步实现。

**改前**

```csharp
private async void OnOpen(object sender, RoutedEventArgs e)
{
    var dialog = new OpenFileDialog { Multiselect = true, Filter = UiText.Get("OpenFilter") };
    if (dialog.ShowDialog(this) == true)
        foreach (var path in dialog.FileNames) await OpenFileAsync(path);
}

private async void OnDrop(object sender, DragEventArgs e)
{
    if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        foreach (var path in paths.Where(File.Exists)) await OpenFileAsync(path);
}
```

**改后**

```csharp
private void OnOpen(object sender, RoutedEventArgs e) => _ = OpenFilesAsync();

private async Task OpenFilesAsync()
{
    var dialog = new OpenFileDialog { Multiselect = true, Filter = UiText.Get("OpenFilter") };
    if (dialog.ShowDialog(this) != true) return;
    foreach (var path in dialog.FileNames) await OpenFileAsync(path);
}

private void OnDrop(object sender, DragEventArgs e) => _ = DropFilesAsync(e);

private async Task DropFilesAsync(DragEventArgs e)
{
    if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
    foreach (var path in paths.Where(File.Exists)) await OpenFileAsync(path);
}
```

**影响**：XAML 签名不变；异常进入返回的 `Task`，不会静默冒泡成进程崩溃。

---

### 2.3 `async void OnReload` → `ReloadAsync()` 返回 `Task`

**改前**

```csharp
private async void OnReload(object sender, RoutedEventArgs e)
{
    if (active is not { } tab) return;
    // ...
}

// 调用点
Bind(Key.F5, ModifierKeys.None, () => OnReload(this, new()));
Item(menu, "Reload", () => OnReload(this, new()), "F5").IsEnabled = active is not null;
```

**改后**

```csharp
private async Task ReloadAsync()
{
    if (active is not { } tab) return;
    // ...
}

// 调用点
Bind(Key.F5, ModifierKeys.None, () => _ = ReloadAsync());
Item(menu, "Reload", () => _ = ReloadAsync(), "F5").IsEnabled = active is not null;
```

**影响**：`Reload` 不再吞异常；调用点显式丢弃 `Task`，语义比 `async void` 清楚。

---

### 2.4 `CancellationTokenSource.Cancel()` → `await CancelAsync()`

**规则**：`MethodHasAsyncOverload`（.NET 8+ 的异步重载）。

在 `async Task RenderAsync` 中取消上一次渲染时，同步 `Cancel()` 会同步执行回调；用 `CancelAsync()` 让取消也异步完成。

**改前**

```csharp
renderCancellation?.Cancel();
var cancellation = new CancellationTokenSource();
```

**改后**

```csharp
if (renderCancellation is not null) await renderCancellation.CancelAsync();
var cancellation = new CancellationTokenSource();
```

**注意**：`ShowWelcome()` 是同步方法，里面的 `renderCancellation?.Cancel()` 保持同步调用——只有异步上下文才换成 `CancelAsync()`，避免为了一个取消动作把整个方法改成 `async void`。

---

### 2.5 `using` → `await using`

**规则**：`UseAwaitUsing`。

`FileStream`、`StreamWriter` 实现了 `IAsyncDisposable`，在异步方法里应使用 `await using`，避免同步 `Dispose` 阻塞。

**改前**

```csharp
using (var stream = new FileStream(path, FileMode.Open, ...))
{
    using var reader = new StreamReader(stream, ...);
    text = await reader.ReadToEndAsync(token);
}
```

**改后**

```csharp
await using (var stream = new FileStream(path, FileMode.Open, ...))
{
    using var reader = new StreamReader(stream, ...);
    text = await reader.ReadToEndAsync(token);
}
```

**注意**：`StreamReader` 在 .NET 10 仍只实现 `IDisposable`，所以 reader 保持 `using`；只有 `FileStream`/`StreamWriter` 换成 `await using`。

---

## 3. 空值与模式匹配

### 3.1 `FindName` 返回值：强转 → `as` + 空判断

**规则**：`Possible 'null' assignment to non-nullable entity`。

`FindName` 返回 `object?`；直接 `(TextBlock)` 强转在找不到控件时会抛异常，也触发可空告警。

**改前**

```csharp
Check(((TextBlock)window.FindName("HeaderTitle")).Text == "灵感笔记.md",
    "Header shows the opened file name");

var theme = (Button)window.FindName("ThemeButton");
```

**改后**

```csharp
Check((window.FindName("HeaderTitle") as TextBlock)?.Text == "灵感笔记.md",
    "Header shows the opened file name");

var theme = window.FindName("ThemeButton") as Button
    ?? throw new InvalidOperationException("ThemeButton is missing");
```

**影响**：测试里找不到控件会得到明确的断言失败/异常信息，而不是 `NullReferenceException` 或无效强转。

---

### 3.2 `Application.Current` → 显式宿主检查 `WpfApp.Current`

**规则**：可空性 + 显式依赖。

`Application.Current` 是可空的；散落各处的 `Application.Current.FindResource(...)` 每次都要 `!` 或空判断。集中到一个入口，缺失时给出明确异常。

**改前**

```csharp
var accent = (SolidColorBrush)Application.Current.FindResource("Accent");
Background = (Brush)Application.Current.FindResource("MdSoft");
```

**改后**

```csharp
internal static class WpfApp
{
    public static Application Current => Application.Current
        ?? throw new InvalidOperationException("MokiPad requires a WPF application context.");
}

var accent = WpfApp.Current.FindResource("Accent") as SolidColorBrush ?? Brushes.Transparent;
Background = WpfApp.Current.FindResource("MdSoft") as Brush;
```

**影响**：所有资源查找走同一条路径；资源缺失时不再把 `null` 传进 WPF 属性，而是显式回退或抛异常。

---

### 3.3 空传播：`if (x != null) x.Foo()` → `x?.Foo()`

**规则**：`UseNullPropagation`。

**改前**

```csharp
if (preview is not null) preview.SetBlocks([]);
if (openMenu is not null) openMenu.IsOpen = false;
```

**改后**

```csharp
preview?.SetBlocks([]);
openMenu?.IsOpen = false;
```

**注意**：属性赋值在 C# 14 之前不能写成 `x?.Prop = ...`；本项目是 .NET 10 / C# 14，所以 `openMenu?.IsOpen = false` 合法。对于同步方法里的 `ShowWelcome()`，保留原来的判空写法也没有问题。

---

### 3.4 C# 14 空条件赋值：`preview?.ReadingFontSize = ...`

**规则**：`UseNullPropagation`（属性赋值）。

**改前**

```csharp
private void ApplyReadingSize()
{
    if (preview is not null)
        preview.ReadingFontSize = Math.Clamp(preferences.FontSize, 10, 28);
}
```

**改后**

```csharp
private void ApplyReadingSize()
{
    preview?.ReadingFontSize = Math.Clamp(preferences.FontSize, 10, 28);
}
```

**影响**：行为等价；这是 C# 14 新增的空条件赋值语法，要求 `LangVersion` 至少为 14（本项目 .NET 10 默认满足）。

---

### 3.5 `MergeIntoPattern`：`x != null && x.Prop` → 属性模式

**规则**：`MergeIntoPattern`（14 条）。

**改前**

```csharp
// 锚点查找
if (Items[i] is PreviewBlock block && block.Anchor.Length > 0 &&
    string.Equals(block.Anchor, slug, StringComparison.OrdinalIgnoreCase))

// 段落里的独立公式
if (p.Inline is { FirstChild: MathInline displayMath } &&
    displayMath.NextSibling is null &&
    displayMath.DelimiterCount >= 2)

// 图片像素宽度
.FirstOrDefault(image => image.Source is BitmapSource bitmap && bitmap.PixelWidth == 320)

// 数学布局尺寸
Check(displayMath.All(view => view.ActualWidth > 40 && view.ActualHeight > 10), ...)
```

**改后**

```csharp
if (Items[i] is PreviewBlock { Anchor.Length: > 0 } block &&
    string.Equals(block.Anchor, slug, StringComparison.OrdinalIgnoreCase))

if (p.Inline is { FirstChild: MathInline { NextSibling: null, DelimiterCount: >= 2 } displayMath })

.FirstOrDefault(image => image.Source is BitmapSource { PixelWidth: 320 })

Check(displayMath.All(view => view is { ActualWidth: > 40, ActualHeight: > 10 }), ...)
```

**影响**：判空、取值、比较合并成一次模式匹配；左侧表达式只求值一次，读起来也更接近“这个对象长什么样”。

---

### 3.6 逻辑模式：`x is A || x is B` → `x is A or B`

**规则**：`MergeIntoLogicalPattern`。

**改前**

```csharp
node = node is Visual || node is Visual3D
    ? VisualTreeHelper.GetParent(node)
    : LogicalTreeHelper.GetParent(node);
```

**改后**

```csharp
node = node is Visual or Visual3D
    ? VisualTreeHelper.GetParent(node)
    : LogicalTreeHelper.GetParent(node);
```

---

### 3.7 switch 模式：脚注/图片分支

**规则**：`MergeIntoPattern`（switch case 上的 when 条件）。

**改前**

```csharp
case FootnoteLink note when !note.IsBackLink:
    // ...
case LinkInline imageLink when imageLink.IsImage:
    // ...
```

**改后**

```csharp
case FootnoteLink { IsBackLink: false } note:
    // ...
case LinkInline { IsImage: true } imageLink:
    // ...
```

**影响**：条件从 `when` 子句移到模式里，分支类型和条件在同一处；对 `switch` 的可读性和穷尽性分析都更好。

---

## 4. 控制流与结构

### 4.1 `InvertIf`：反转条件、提前返回

**规则**：`InvertIf`（10 条）。

**改前**

```csharp
private void OnZoomWheel(object sender, MouseWheelEventArgs e)
{
    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
    {
        Zoom(Math.Sign(e.Delta));
        e.Handled = true;
    }
}

private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
{
    if (e.ChangedButton == MouseButton.Middle)
    {
        e.Handled = true;
        CloseTab(TabUnder(e.OriginalSource as DependencyObject));
    }
}
```

**改后**

```csharp
private void OnZoomWheel(object sender, MouseWheelEventArgs e)
{
    if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
    Zoom(Math.Sign(e.Delta));
    e.Handled = true;
}

private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
{
    if (e.ChangedButton != MouseButton.Middle) return;
    e.Handled = true;
    CloseTab(TabUnder(e.OriginalSource as DependencyObject));
}
```

**影响**：主流程少一层缩进；异常/不处理路径尽早退出。所有 `InvertIf` 都确认过没有副作用顺序变化（例如 `e.Handled` 仍在同一条件内设置）。

同类还有：`OnOpen`、`OnDrop`、`ChooseBackground`、`OnTabWheel`、`ImageLoader.Decode/缓存`、`MarkdownPreview` 图片加载、验证程序 `Flatten`。

---

### 4.2 `if/else if` → `switch`

**规则**：`ConvertIfStatementToSwitchStatement`（5 条）。

**改前（标题层级）**

```csharp
if (block.Level == 1)
{
    row.HorizontalAlignment = HorizontalAlignment.Center;
    // 笔记本图标
}
else if (block.Level == 2)
{
    var hash = Text("#", heading.FontSize);
    // ...
}
```

**改后**

```csharp
switch (block.Level)
{
    case 1:
        row.HorizontalAlignment = HorizontalAlignment.Center;
        // 笔记本图标
        break;
    case 2:
        var hash = Text("#", heading.FontSize);
        // ...
        break;
}
```

**改前（强调标记）**

```csharp
if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
else if (emphasis.DelimiterChar == '=') span.Background = Brush("MdSoft");
else if (emphasis.DelimiterChar == '+') span.TextDecorations = TextDecorations.Underline;
else if (emphasis.DelimiterChar == '^') { span.FontSize = fontSize * .75; span.BaselineAlignment = BaselineAlignment.Superscript; }
else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
else span.FontStyle = FontStyles.Italic;
```

**改后**

```csharp
switch (emphasis.DelimiterChar)
{
    case '~': span.TextDecorations = TextDecorations.Strikethrough; break;
    case '=': span.Background = Brush("MdSoft"); break;
    case '+': span.TextDecorations = TextDecorations.Underline; break;
    case '^':
        span.FontSize = fontSize * .75;
        span.BaselineAlignment = BaselineAlignment.Superscript;
        break;
    default:
        if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
        else span.FontStyle = FontStyles.Italic;
        break;
}
```

**改前（链接 scheme）**

```csharp
if (uri.Scheme is "http" or "https" or "mailto" or "tel")
{
    try { Process.Start(...); } catch (Win32Exception) { }
    return;
}
if (uri.Scheme == "file")
{
    preview?.RequestOpenFile(uri.LocalPath);
    return;
}
```

**改后**

```csharp
switch (uri.Scheme)
{
    case "http" or "https" or "mailto" or "tel":
        try { Process.Start(...); } catch (Win32Exception) { }
        return;
    case "file":
        preview?.RequestOpenFile(uri.LocalPath);
        return;
}
```

**改前（HTML 片段）**

```csharp
if (piece.Kind == "pre") text.FontFamily = new FontFamily("Cascadia Code, Consolas, Microsoft YaHei UI");
if (piece.Kind == "li") text.Margin = new Thickness(16, 0, 0, 2);
```

**改后**

```csharp
switch (piece.Kind)
{
    case "pre":
        text.FontFamily = new FontFamily("Cascadia Code, Consolas, Microsoft YaHei UI");
        break;
    case "li":
        text.Margin = new Thickness(16, 0, 0, 2);
        break;
}
```

`CodeSyntax` 里还有一处重复分支：注释与 shell 的 `#` 都被标记为 `Comment`，合并成一个条件。

**改前**

```csharp
if (match.Groups["comment"].Success)
{
    kind = "Comment";
}
else if (match.Groups["hash"].Success &&
         (shell || lang is "python" or "py" or "yaml" or "yml" or "ruby" or "rb"))
{
    kind = "Comment";
}
```

**改后**

```csharp
if (match.Groups["comment"].Success ||
    (match.Groups["hash"].Success &&
     (shell || lang is "python" or "py" or "yaml" or "yml" or "ruby" or "rb")))
{
    kind = "Comment";
}
```

`CodeSyntax` 里的 `if (shell && ...) else if (...)` 改成了基于元组的 switch 表达式：

```csharp
kind = (shell, CommandPosition(source, match.Index),
        match.Index > 0 && source[match.Index - 1] == '-',
        tail < source.Length && source[tail] == '(') switch
{
    (true, true, _, _) => "Command",
    (true, false, true, _) => "Parameter",
    (_, _, _, true) => "Function",
    _ => kind
};
```

---

### 4.3 条件表达式合并：`x != null ? x.Y : null` → `x?.Y`

**规则**：`MergeConditionalExpression`。

**改前**

```csharp
var path = LocalPath(value, documentPath);
return path is null ? null : path.ToLowerInvariant();

var columns = cells is not null ? cells.Max(r => r.Length)
    : rows.Length == 0 ? 0 : rows.Max(r => r.Length);
```

**改后**

```csharp
return LocalPath(value, documentPath)?.ToLowerInvariant();

var columns = cells?.Max(r => r.Length) ?? (rows.Length == 0 ? 0 : rows.Max(r => r.Length));
```

---

### 4.3.1 `if` + `return` 合并（ConvertIfStatementToReturnStatement）

**规则**：`ConvertIfStatementToReturnStatement`。

**改前**

```csharp
var key = Key(source, documentPath);
if (key is null) return Task.FromResult<ImageSource?>(null);
return Cache.GetOrAdd(key, _ => LoadCoreAsync(source, documentPath));
```

**改后**

```csharp
var key = Key(source, documentPath);
return key is null
    ? Task.FromResult<ImageSource?>(null)
    : Cache.GetOrAdd(key, _ => LoadCoreAsync(source, documentPath));
```

**影响**：一个出口，读的人不用在脑内合并“空/非空”两条路径；`LocalPath` 里的 `if (basePath is null) return null;` 也用同样方式合并为 `return basePath is null ? null : ...`。

### 4.4 本地函数移到 `return` 之后

**规则**：`MoveLocalFunctionAfterJumpStatement`（9 条）。

本地函数可以声明在调用之后；把辅助函数放在 `return` 之后，读者先读完主流程，再看实现细节。

**改前（MarkdownEngine.Parse）**

```csharp
public static MarkdownResult Parse(string source, ...)
{
    var blocks = new List<PreviewBlock>();

    string UniqueAnchor(string title) { ... }
    void Add(Block block, ...) { ... }
    void AddFootnotes(FootnoteGroup group) { ... }
    void AddChunks(...) { ... }

    foreach (var block in doc) Add(block);
    return new MarkdownResult(blocks, watch.Elapsed.TotalMilliseconds);
}
```

**改后**

```csharp
public static MarkdownResult Parse(string source, ...)
{
    var blocks = new List<PreviewBlock>();

    foreach (var block in doc) Add(block);
    return new MarkdownResult(blocks, watch.Elapsed.TotalMilliseconds);

    string UniqueAnchor(string title) { ... }
    void Add(Block block, ...) { ... }
    void AddFootnotes(FootnoteGroup group) { ... }
    void AddChunks(...) { ... }
}
```

同类：`HtmlBlocks.Flush`、`ImageLoader.Replace/Attr`、`MainWindow.BindKeys.Bind`、`MarkdownEngine.Plain.Walk`。

> `MainWindow.BindKeys` 是 `void` 方法，ReSharper 要求“用显式 return 分隔本地函数”，所以在最后一个 `Bind(...)` 后加了 `return;`，本地函数仍然不会被“执行”，只是视觉上与主流程分开。

---

## 5. 类型与成员

### 5.1 收紧可见性：`public/internal` → `private`

**规则**：`MemberCanBePrivate.Global`（6 条）。

只在本类/本文件使用的成员不需要暴露给外部；收紧后 API 面更小，也不会被误用。

**改前**

```csharp
public static string? Attribute(string tag, string name)   // HtmlBlocks
public static string? LocalPath(string source, string? documentPath) // ImageLoader
internal void ScrollToIndex(int index)                    // MarkdownPreview
internal void ResolveLink(string? url)                    // MarkdownPreview
public bool HasError { get; private set; }                 // MathView
internal static readonly string[] SourceArray = ...        // Verification
```

**改后**

```csharp
private static string? Attribute(string tag, string name)
private static string? LocalPath(string source, string? documentPath)
private void ScrollToIndex(int index)
private void ResolveLink(string? url)
private bool HasError { get; private set; }
private static readonly string[] SourceArray = ...
```

**影响**：`MathView.HasError` 只在 `Rebuild()` 内部读写；`SourceArray` 只在测试程序内使用。没有外部调用点需要改。

---

### 5.2 删除未使用成员

**规则**：`UnusedMember.Global` / `CS0169`。

**改前**

```csharp
// MarkdownEngine.cs
public sealed record OutlineEntry(int Level, string Text, string Anchor);
public sealed record MarkdownResult(IReadOnlyList<PreviewBlock> Blocks, IReadOnlyList<OutlineEntry> Outline, double ParseMs);
private static void BuildOutline(...) { ... }

// FileService.cs
public sealed record LoadedFile(string Text, Encoding Encoding, string NewLine, long Bytes);
public static async Task SaveAsync(string path, string text, Encoding encoding, ...) { ... }

// ImageLoader.cs
public static string? LastError { get; private set; }

// Motion.cs
public static void Enter(FrameworkElement target) { ... }
```

**改后**

```csharp
public sealed record MarkdownResult(IReadOnlyList<PreviewBlock> Blocks, double ParseMs);

public sealed record LoadedFile(string Text, Encoding Encoding, string NewLine);

// SaveAsync / LastError / Motion.Enter / OutlineEntry 全部删除
```

**影响**：查看器是只读的，`SaveAsync` 没有调用点；`Outline` 在移除大纲面板后不再需要；删除后项目更小，也不会有“看似可用其实没人调用”的 API。

---

### 5.3 `init` 与 `readonly`

**规则**：`PropertyCanBeMadeInitOnly`、`FieldCanBeMadeReadOnly`。

只在初始化阶段赋值的属性/字段改为 `init`/`readonly`，向读代码的人表达“构造后不再变化”。

**改前**

```csharp
// Preferences
public double BackgroundTransparency { get; set; } = 85;

// MathView
public string Source { get => source; set { ... } }
public bool Display { get => display; set { ... } }
private bool display;
private string source = "";
```

**改后**

```csharp
// Preferences
public double BackgroundTransparency { get; init; } = 85;

// MathView
public string Source { get => source; init { ... } }
public bool Display { get => display; init { ... } }
private readonly bool display;
private readonly string source = "";
```

**注意**：`MathFontSize` / `MathForeground` 仍然用普通 `set`，因为它们的 setter 会在对象初始化之后被调用；只有 `Source`/`Display` 是纯 init。`readonly` 字段允许在 `init` 访问器里赋值，这是 C# 9 起的规则。

---

### 5.4 具体返回类型（CA1859）

**规则**：`CA1859`（Roslyn 分析器：尽可能使用具体类型）。

方法始终返回某个具体类型时，返回接口会隐藏能力、增加虚调用。

**改前**

```csharp
private static ImageSource? DecodeSvg(byte[] bytes)      // 实际返回 DrawingImage
private static ImageSource? DecodeBitmap(byte[] bytes)   // 实际返回 BitmapImage
private FrameworkElement HtmlBlock(string source)        // 实际返回 StackPanel
private FrameworkElement Table(PreviewBlock block)       // 实际返回 Border
```

**改后**

```csharp
private static DrawingImage? DecodeSvg(byte[] bytes)
private static BitmapImage? DecodeBitmap(byte[] bytes)
private StackPanel HtmlBlock(string source)
private Border Table(PreviewBlock block)
```

**影响**：调用方仍可隐式转换为 `ImageSource?`/`FrameworkElement`，但方法签名更准确；`DecodeSvg` 的 SVG 分支与 `DecodeBitmap` 的位图分支也更容易区分。

---

### 5.5 静态类 / 静态方法（CA1822、ConvertToStaticClass）

**规则**：`CA1822`、`ClassNeverInstantiated`、`ConvertToStaticClass`。

`MarkdownEngine` 没有任何实例状态，`Parse` 也不访问实例字段；把它改成静态类，调用方不再需要 `new`。

**改前**

```csharp
public sealed class MarkdownEngine
{
    public MarkdownResult Parse(string source, CancellationToken cancellation = default) { ... }
}

// 调用方
var parsed = (markdown ??= new MarkdownEngine()).Parse(text, token);
```

**改后**

```csharp
public static class MarkdownEngine
{
    public static MarkdownResult Parse(string source, CancellationToken cancellation = default) { ... }
}

// 调用方
var parsed = MarkdownEngine.Parse(text, token);
```

**影响**：`MainWindow` 里的 `MarkdownEngine? markdown` 字段删除；测试里的 `new MarkdownEngine()` 也删除。

---

### 5.6 主构造函数与对象初始化器

**规则**：`ConvertToPrimaryConstructor`、`UseObjectOrCollectionInitializer`。

**改前**

```csharp
internal sealed class HtmlContext
{
    public HtmlContext(InlineCollection root)
    {
        Target = root;
    }

    public InlineCollection Target { get; private set; }
    public double FontSize { get; set; } = 16;
}

var view = new MathView
{
    Source = math.Content.ToString(), Display = false, MathFontSize = fontSize,
    MathForeground = Brush("Text"), VerticalAlignment = VerticalAlignment.Center
};
view.Margin = new Thickness(1, 0, 1, 0);
```

**改后**

```csharp
internal sealed class HtmlContext(InlineCollection root)
{
    public InlineCollection Target { get; private set; } = root;
    public double FontSize { get; init; } = 16;
}

var view = new MathView
{
    Source = math.Content.ToString(), Display = false, MathFontSize = fontSize,
    MathForeground = Brush("Text"), VerticalAlignment = VerticalAlignment.Center,
    Margin = new Thickness(1, 0, 1, 0)
};
```

**影响**：构造与配置集中在一处；`Target` 仍需要 `private set`，因为 `PushStyle`/`PopStyle` 会切换当前目标集合。

---

## 6. 语法与集合

### 6.1 集合表达式 `[...]`

**规则**：`UseCollectionExpression`。

**改前**

```csharp
Rows: new[] { rows[0] }.Concat(rows.Skip(index).Take(32)).ToArray(),
Cells: new[] { cells[0] }.Concat(cells.Skip(index).Take(32)).ToArray(),

private static readonly string[] SourceArray = new[]
{
    "Editor", "SourceHost", "PreviewToggleButton", ...
};
```

**改后**

```csharp
Rows: [rows[0], .. rows.Skip(index).Take(32)],
Cells: [cells[0], .. cells.Skip(index).Take(32)],

private static readonly string[] SourceArray =
[
    "Editor", "SourceHost", "PreviewToggleButton", ...
];
```

**影响**：`..` 是 C# 12 的 spread 元素；这里把“首行 + 分页 32 行”表达得更直接，也避免中间 `ToArray()`。

---

### 6.2 `const` 与静态缓存

**规则**：`ConvertToConstant`、`CA1869`、`CA1861`。

**改前**

```csharp
var source = "# 灵感笔记\n\n...";
var readme = @"C:\Code\React\mokidown\README.md";

File.WriteAllText(..., JsonSerializer.Serialize(Metrics, new JsonSerializerOptions { WriteIndented = true }));
```

**改后**

```csharp
const string source = "# 灵感笔记\n\n...";
const string readme = @"C:\Code\React\mokidown\README.md";

private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
File.WriteAllText(..., JsonSerializer.Serialize(Metrics, JsonOptions));
```

**影响**：不会变化的字符串改为编译期常量；`JsonSerializerOptions` 复用同一个实例，避免每次序列化都丢失内部元数据缓存。

---

### 6.3 LINQ：`Count() > 0` → `Any()`，`!Any` → `All`，嵌套循环 → `SelectMany`

**规则**：`CA1827`、`UseMethodAny`、`SimplifyLinqExpressionUseAll`、`LoopCanBeConvertedToQuery`、`ForeachCanBePartlyConvertedToQuery`。

**改前**

```csharp
Check(Descendants(root).OfType<TextBox>().Count() == 0, "...");

Check(!typeof(MainWindow).Assembly.GetReferencedAssemblies()
    .Any(a => a.Name?.Contains("AvalonEdit") == true), "...");

private static IEnumerable<Inline> Inlines(DependencyObject root)
{
    foreach (var text in Descendants(root).OfType<TextBlock>())
    foreach (var inline in text.Inlines)
    foreach (var item in Flatten(inline))
        yield return item;
}
```

**改后**

```csharp
Check(!Descendants(root).OfType<TextBox>().Any(), "...");

Check(typeof(MainWindow).Assembly.GetReferencedAssemblies()
    .All(a => a.Name?.Contains("AvalonEdit") != true), "...");

private static IEnumerable<Inline> Inlines(DependencyObject root)
{
    return Descendants(root).OfType<TextBlock>()
        .SelectMany(text => text.Inlines)
        .SelectMany(Flatten);
}
```

**影响**：`Any()` 找到第一个元素就返回；`All` 表达“全部不引用”；`SelectMany` 把三层循环压成一条查询，语义不变。

`MarkdownEngine.Slugify` 的字符过滤也改成了 LINQ：

```csharp
// 改前
var builder = new StringBuilder(text.Length);
foreach (var ch in text.ToLowerInvariant())
    if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == ' ')
        builder.Append(ch);

// 改后
var builder = new StringBuilder(string.Concat(text.ToLowerInvariant()
    .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')));
```

---

### 6.4 ReSharper Full Cleanup：尾逗号、显式 `new()`、括号、限定符

**规则**：`ArrangeTrailingCommaInMultilineLists`（17）、`ArrangeObjectCreationWhenTypeNotEvident`（24）、`ArrangeRedundantParentheses`（2）、`ArrangeStaticMemberQualifier`（1）。

这一批由 `jb cleanupcode MokiPad.sln` 按项目代码风格统一完成，属于**纯风格**修改，不改变行为。

**尾逗号：改前**

```csharp
"figure", "figcaption",
```

**尾逗号：改后**

```csharp
"figure", "figcaption"
```

**显式类型：改前**

```csharp
var blocks = new List<PreviewBlock>();
blocks.Add(new("heading", ...));
```

**改后**

```csharp
var blocks = new List<PreviewBlock>();
blocks.Add(new PreviewBlock("heading", ...));
```

**静态限定符：改前**

```csharp
context.AddText(HtmlContext.WhitespaceRegex().Replace(html[position..match.Index], " "));
```

**改后**

```csharp
context.AddText(WhitespaceRegex().Replace(html[position..match.Index], " "));
```

> 另有 2 处多余括号由 Full Cleanup 自动删除（`ArrangeRedundantParentheses`），不改变求值顺序。

> 风格批处理只改格式与写法；语义修改都在后续手工批次里单独完成。

---

### 6.5 逐字字符串（UseVerbatimString）

**规则**：`UseVerbatimString`。

正则里包含 `\?` 这类转义时，逐字字符串可读性更好，也少一层转义。

**改前**

```csharp
inner = Regex.Replace(inner, "<\\?xml[^>]*\\?>", "");
```

**改后**

```csharp
inner = Regex.Replace(inner, @"<\?xml[^>]*\?>", "");
```

**影响**：正则字面量保持原样，不再需要双反斜杠；`RegexOptions` 不变。

## 7. 平台、分析器与安全

### 7.1 区域设置：显式 `CultureInfo`

**规则**：Rider 的 globalization 检查（`string.Format` 当前区域相关）。

**改前**

```csharp
return args.Length == 0 ? text : string.Format(text, args);

return Convert.ToInt32(metadata.GetQuery(path));
```

**改后**

```csharp
return args.Length == 0 ? text
    : string.Format(CultureInfo.CurrentCulture, text, args);

return Convert.ToInt32(metadata.GetQuery(path), CultureInfo.InvariantCulture);
```

**影响**：格式化字符串使用当前 UI 区域（符合本地化预期）；GIF 元数据是二进制协议值，固定用 `InvariantCulture` 解析，避免阿拉伯语/德语等区域把数字格式解释错。

---

### 7.2 缓存键哈希：SHA1 → SHA256

**规则**：Rider 的弱加密算法检查。

**改前**

```csharp
var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
```

**改后**

```csharp
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
```

**影响**：缓存文件名长度变化，第一次运行会重新下载图片；之后命中新缓存。SHA256 不再是弱算法，也不会触发安全告警。

---

### 7.3 异常类型具体化

**规则**：Rider 的 `System.Exception` 检查。

**改前**

```csharp
private static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Passed.Add(name);
}
```

**改后**

```csharp
private static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Passed.Add(name);
}
```

**影响**：验证失败现在是一个明确的“操作状态不合法”，而不是笼统的 `Exception`；测试输出也更具体。

---

### 7.4 `[GeneratedRegex]`：尝试、回退与 `.editorconfig`

**规则**：`SYSLIB1045`（14 条）。

**尝试**：把静态 `Regex` 字段改成 `[GeneratedRegex]` 的 `static partial` 方法，类标记 `partial`：

```csharp
public static partial class ImageLoader
{
    [GeneratedRegex("viewBox\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex ViewBoxRegex();

    // 调用
    var viewBox = ViewBoxRegex().Match(inner).Groups[1].Value;
}
```

`dotnet build` 可以通过，但 Rider 的独立分析进程没有稳定运行 regex source generator，Problems 里把每个 partial 方法都报成红色错误「分部方法必须具有实现部分」，共有 11 条；这会让 IDE 一直红着。

**最终回退**：保留手写的 `static readonly Regex`（`RegexOptions.Compiled`）：

```csharp
public static class ImageLoader
{
    private static readonly Regex ViewBoxPattern =
        new("viewBox\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    var viewBox = ViewBoxPattern.Match(inner).Groups[1].Value;
}
```

并在 `.editorconfig` 里关闭该分析器：

```ini
root = true

[*.cs]
# Rider's out-of-process compiler does not run the regex source generator reliably;
# keep hand-written compiled Regex fields instead of [GeneratedRegex] partial methods.
dotnet_diagnostic.SYSLIB1045.severity = none
```

**影响**：Rider 不再出现 11 条红色假错误；CLI 构建与运行时行为不变。代价是失去 source generator 的编译期优化，这是为跨 IDE 一致性做的明确取舍。

---

### 7.5 有意保留的同步写盘（带说明的抑制）

验证程序在 WPF dispatcher 关闭前写 `verification.json`；这里不能用 `await`（回调即将结束、进程马上退出），同步写能保证文件先落盘。

**改前**

```csharp
File.WriteAllText(Path.Combine(output, "verification.json"),
    JsonSerializer.Serialize(Metrics, JsonOptions));
```

**改后**

```csharp
// ReSharper disable once MethodHasAsyncOverload
// The app is shutting down on the dispatcher; a synchronous write guarantees the file is flushed first.
File.WriteAllText(Path.Combine(output, "verification.json"),
    JsonSerializer.Serialize(Metrics, JsonOptions));
```

**影响**：行为不变；用注释说明为什么这里“看起来该异步、但不能异步”，避免后来的人机械替换成 `WriteAllTextAsync` 导致文件还没写完进程就退出。

---

## 8. 测试代码的清理

### 8.1 命名空间与命名

测试项目从全局命名空间改为文件范围命名空间，`sourceArray` 改为 PascalCase 的 `SourceArray`（它是非私有静态字段）。

**改前**

```csharp
using MokiPad;
// ...
internal static class Program
{
    internal static readonly string[] sourceArray = new[]
    {
        "Editor", ...
    };
}
```

**改后**

```csharp
using MokiPad.Controls;
// ...
namespace MokiPad.Verification;

internal static class Program
{
    private static readonly string[] SourceArray =
    [
        "Editor", ...
    ];
}
```

**影响**：`MokiPad` 命名空间内的类型仍然可见（嵌套命名空间），但不再需要 `using MokiPad;`；`MokiPad.Controls` 改为显式导入。

---

### 8.2 测试里的可空强转与未用变量

**改前**

```csharp
Check(((TextBlock)window.FindName("HeaderTitle")).Text == "灵感笔记.md", ...);
var theme = (Button)window.FindName("ThemeButton");
var engine = new MarkdownEngine();
var parsed = MarkdownEngine.Parse(source);
```

**改后**

```csharp
Check((window.FindName("HeaderTitle") as TextBlock)?.Text == "灵感笔记.md", ...);
var theme = window.FindName("ThemeButton") as Button
    ?? throw new InvalidOperationException("ThemeButton is missing");
var parsed = MarkdownEngine.Parse(source);
```

**影响**：测试失败信息更明确；`MarkdownEngine` 改静态后不再需要 `engine` 变量。

---

## 9. 验证结果

| 检查 | 结果 |
| --- | --- |
| `dotnet build MokiPad.sln -c Release` | 0 warning / 0 error |
| `jb inspectcode MokiPad.sln --severity=HINT` | 0 issue（0 error / 0 warning / 0 suggestion / 0 hint） |
| 验证套件 | 全部 PASS（组件、公式、图片/SVG、GIF、链接、脚注、长文虚拟化、编码） |
| Rider 复查 | 0 个 Problems（GeneratedRegex 假错误已随回退消失） |
| 应用 | `artifacts/app/MokiPad.exe` 已重新发布 |

完整逐条记录（每条规则的出现位置、处理阶段、完整 diff）见 [code-quality-report.html](code-quality-report.html)；解决方案结构见 [solution.html](solution.html)。
