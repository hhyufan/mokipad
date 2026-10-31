# MokiPad 新手入门：从零看懂 WPF 与 C#

> 写给第一次接触 C#、第一次打开 WPF 项目的人。  
> 不要求你会写代码，只要求你愿意按顺序动手。  
> 配套知识树：`docs/beginner-guide.mgtree`，可以用 MokiDown 打开，按章节跳转。

---

## 0. 先认识这个项目

MokiPad 是一个 **Windows 上的 Markdown 阅读器**。

它只做阅读，不修改文件：

- 打开 `.md` 文件；
- 把 Markdown 解析成标题、段落、代码块、表格、图片、公式、脚注；
- 用 WPF 画成可滚动的阅读界面；
- 你可以换主题、调字号、复制原文，但不能编辑。

它和 MokiDown 是互补关系：

- MokiDown 是完整的知识工作台，能编辑、能跑代码、有知识树；
- MokiPad 是安静的原生阅读器，启动快、界面轻。

项目主要分三块：

| 目录 | 作用 | 你可以把它想成 |
| --- | --- | --- |
| `src/MokiPad.Core` | 解析 Markdown、识别文件编码 | 大脑，不画界面 |
| `src/MokiPad` | WPF 窗口、阅读界面、图片、主题 | 身体和脸 |
| `tests/MokiPad.Verification` | 自动检查功能是否正常 | 体检报告 |

---

## 1. 十分钟跑起来

### 1.1 需要什么

- Windows 10 或 Windows 11；
- .NET 10 SDK；
- 一个代码编辑器（Visual Studio、Rider 或 VS Code 都行）。

先确认 SDK 装好了：

```powershell
dotnet --version
```

如果输出 `10.0.100` 或更高的 `10.x`，说明可以继续。

### 1.2 构建项目

在项目根目录执行：

```powershell
dotnet build .\MokiPad.sln -c Release
```

第一次构建会下载 NuGet 依赖，需要联网。看到 `0 个警告`、`0 个错误` 就成功了。

### 1.3 启动阅读器

```powershell
.\scripts\dev.ps1
```

也可以直接传一个文件：

```powershell
.\scripts\dev.ps1 "C:\Notes\长文.md"
```

窗口打开后，把 `.md` 文件拖进去，或者点左上角“文件”菜单打开。

### 1.4 验证功能是否正常

```powershell
.\scripts\verify.ps1
```

它会打开离屏窗口，检查主题、公式、图片、GIF、链接、脚注、长文虚拟化等，最后生成报告。

### 1.5 发布给别人用

```powershell
.\scripts\publish.ps1
```

产物在 `artifacts\app\MokiPad.exe`。如果对方没装 .NET 运行时，用自包含发布：

```powershell
.\scripts\publish.ps1 -SelfContained
```

---

## 2. 目录地图

```text
mokipad/
  src/
    MokiPad.Core/           Markdown 解析、文件编码、源码复制
    MokiPad/                WPF 窗口、阅读器、图片、主题
      Controls/             阅读控件、图片加载、GIF、HTML
      Models/               标签页数据
      Rendering/            数学公式控件
      Services/             设置、主题、字体、本地化
      Styles/               全局样式和控件模板
  tests/
    MokiPad.Verification/   离屏验证程序
  docs/                     文档、截图、知识树
  scripts/                  启动、发布、验证脚本
```

先记住三个最常改的地方：

- 界面长什么样：`src/MokiPad/Styles/Controls.xaml` 和 `MainWindow.xaml`；
- 阅读器怎么画块：`src/MokiPad/Controls/MarkdownPreview.cs`；
- Markdown 怎么解析：`src/MokiPad.Core/MarkdownEngine.cs`。

---

## 3. C# 最小必要知识

### 3.1 命名空间、类、方法

C# 代码由“类”和“方法”组成：

- 命名空间：给类分组，类似文件夹；
- 类：一组数据和行为的集合；
- 方法：一段可以执行的代码。

```csharp
namespace MokiPad.Core;

public sealed class Greeting
{
    public string Hello(string name)
    {
        return "你好，" + name;
    }
}
```

- `namespace` 是分组；
- `class` 是类；
- `public` 表示外部可以访问；
- `string` 是文本类型；
- `return` 返回结果。

### 3.2 `var`、类型和可空

C# 是强类型语言，但可以用 `var` 让编译器推断类型。

```csharp
var title = "MokiPad";
var count = 3;
string? subtitle = null;

if (subtitle is not null)
{
    Console.WriteLine(subtitle.Length);
}
```

- `var` 不是“随便什么类型”，编译器会在编译时确定；
- `string?` 表示“可能是空值”；
- `is not null` 是空值判断；
- 打开可空检查后，编译器会提醒你可能出现的空引用。

### 3.3 异步：`async` 和 `await`

WPF 的界面线程只有一个。读文件、解析长文、下载图片如果直接做，窗口会卡住。

C# 用 `async` / `await` 把耗时工作放到后台，同时不阻塞界面：

```csharp
private static async Task OpenFileAsync(string path)
{
    var text = await File.ReadAllTextAsync(path);
    Console.WriteLine($"读到 {text.Length} 个字符");
}
```

- `Task` 表示“将来会完成的工作”；
- `await` 表示“先等它完成，但不要堵住界面线程”；
- 方法签名要加 `async`；
- 不要写 `async void`，除非它是界面事件处理器。

### 3.4 记录类型和 LINQ

`record` 适合表达“只读数据”：

```csharp
public sealed record PreviewBlock(string Kind, int Line, string Text);

var blocks = new List<PreviewBlock>
{
    new("heading", 1, "标题"),
    new("paragraph", 3, "正文")
};

var headings = blocks.Where(block => block.Kind == "heading").ToList();
```

- `record` 自动生成构造函数、相等比较；
- `List<T>` 是列表；
- `Where` / `Select` / `ToList` 是 LINQ，用查询的方式处理集合。

### 3.5 可空模式和模式匹配

C# 常用“模式”判断对象长什么样：

```csharp
PreviewBlock? block = FindBlock();

if (block is { Kind: "heading", Line: > 0 } heading)
{
    Console.WriteLine(heading.Text);
}
```

- `is { ... }` 同时判断非空和属性；
- `heading` 是匹配成功后的变量；
- 比一层层 `if (block != null && block.Kind == ...)` 更短、更安全。

### 3.6 依赖属性：WPF 特有的“属性”

普通 C# 属性只能存值；WPF 的依赖属性还能参与绑定、样式、动画和属性继承。

```csharp
public static readonly DependencyProperty ReadingFontSizeProperty =
    DependencyProperty.RegisterAttached(
        "ReadingFontSize",
        typeof(double),
        typeof(MarkdownPreview),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.Inherits));
```

- `DependencyProperty` 是 WPF 的属性系统；
- `RegisterAttached` 表示这个属性可以“附加”到别的控件；
- `Inherits` 表示父控件设置后，子控件能自动读到；
- 实际读写用 `GetValue` / `SetValue`，不是普通字段。

### 3.7 路由事件和 Dispatcher

WPF 的事件会沿着可视树传递，这叫“路由事件”。

```csharp
AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);

Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
{
    // 等布局完成后再做
}));
```

- `PreviewMouseWheel` 是隧道事件，先从父级往下传；
- `handledEventsToo: true` 表示即使子控件处理过，父级也能收到；
- `Dispatcher` 是 WPF 的界面线程队列；
- `BeginInvoke` 把工作排到稍后执行，避免阻塞当前布局。

---

## 4. WPF 最小必要知识

### 4.1 XAML 和代码后置

WPF 的界面用 XAML 描述，逻辑写在 `.xaml.cs` 里。

```xml
<Window x:Class="MokiPad.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MokiPad" Width="1280" Height="820">
    <Grid>
        <TextBlock Text="你好，MokiPad" />
    </Grid>
</Window>
```

- `x:Class` 把 XAML 和后台类关联；
- `xmlns` 是命名空间；
- `Grid` 是布局容器；
- `TextBlock` 显示文字。

### 4.2 布局：Grid、StackPanel、DockPanel

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="42" />
        <RowDefinition Height="*" />
        <RowDefinition Height="32" />
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0" Text="标题栏" />

    <StackPanel Grid.Row="1" Orientation="Vertical">
        <TextBlock Text="第一行" />
        <TextBlock Text="第二行" />
    </StackPanel>

    <DockPanel Grid.Row="2">
        <TextBlock DockPanel.Dock="Left" Text="左边" />
        <TextBlock Text="剩余空间" />
    </DockPanel>
</Grid>
```

- `Grid`：按行/列摆放，适合整体框架；
- `StackPanel`：横着或竖着依次排列；
- `DockPanel`：靠边停靠，最后一个填满剩余空间；
- `Height="*"` 表示占满剩余高度。

### 4.3 资源、样式、模板

WPF 把颜色、图标、样式放在资源字典里，控件通过 `StaticResource` 或 `DynamicResource` 引用。

```xml
<Style x:Key="IconButton" TargetType="Button">
    <Setter Property="Width" Value="32" />
    <Setter Property="Height" Value="32" />
    <Setter Property="Background" Value="Transparent" />
</Style>

<Button Style="{StaticResource IconButton}" Content="打开" />
```

- `x:Key` 是资源名字；
- `StaticResource` 只在加载时查找一次；
- `DynamicResource` 会在资源变化时重新查找，适合换主题；
- `ControlTemplate` 决定控件“长什么样”，`Style` 决定属性默认值。

### 4.4 绑定和 DataTemplate

绑定把“数据”接到“界面”上。

```xml
<ListBox ItemsSource="{Binding Documents}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding Name}" />
                <TextBlock Text="{Binding FilePath}" />
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

- `ItemsSource` 指定列表数据；
- `ItemTemplate` 指定每一项怎么显示；
- `{Binding Name}` 表示读取当前对象的 `Name` 属性；
- `DataTemplate` 是“数据的脸”。

### 4.5 虚拟化：长文为什么不卡

阅读器里的 `ListBox` 不会一次创建几万个控件，只创建屏幕附近的项。

```csharp
SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
```

- `IsVirtualizing` 开启虚拟化；
- `Recycling` 回收滚出屏幕的容器；
- `Pixel` 按像素滚动；
- 容器会被复用，所以控件里不能假设“我只显示一个块”。

---

## 5. 一次打开文件发生了什么

把流程想成一条流水线：

1. 用户选择文件；
2. `FileService` 读取字节，识别编码（UTF-8 / UTF-16 / GB18030）；
3. `MarkdownEngine` 用 Markdig 解析，生成 `PreviewBlock` 列表；
4. `MarkdownPreview` 把块列表交给 `ListBox`；
5. 列表只为可见块创建 `BlockPresenter`；
6. `BlockPresenter` 根据块的类型画标题、段落、代码、表格、图片或公式。

关键对象：

| 对象 | 作用 |
| --- | --- |
| `LoadedFile` | 文件文本、编码、换行风格 |
| `PreviewBlock` | 一个可显示的块 |
| `MarkdownResult` | 解析后的块列表和耗时 |
| `MarkdownPreview` | 虚拟化阅读列表 |
| `BlockPresenter` | 单个块的渲染器 |

---

## 6. 想改东西，从哪里下手

### 6.1 改颜色和主题

- `src/MokiPad/Services/ThemeService.cs`：颜色值；
- `src/MokiPad/Styles/Controls.xaml`：控件样式；
- `src/MokiPad/MainWindow.xaml`：整体布局。

### 6.2 改字号和快捷键

- 字号：`ApplyReadingSize`、`Zoom`；
- 快捷键：`BindKeys`；
- 菜单位置：`CreateFileMenu` / `CreateSettingsMenu`。

### 6.3 加一种新的 Markdown 块

1. 在 `MarkdownEngine` 里识别新语法，生成新的 `Kind`；
2. 在 `BlockPresenter.Render` 里为这个 `Kind` 画 WPF 元素；
3. 在 `MokiPad.Verification` 里加一条检查。

### 6.4 改图片加载

- `ImageLoader.cs`：本地、网络、data URI、SVG；
- `BoundedImageCache.cs`：内存上限；
- `GifAnimation.cs`：GIF 逐帧播放。

---

## 7. 验证与调试

### 7.1 先构建，再运行

```powershell
dotnet build .\MokiPad.sln -c Release
```

### 7.2 跑验证

```powershell
.\scripts\verify.ps1
```

验证不打开真实窗口，而是创建离屏窗口，检查控件树、截图和长文虚拟化。

### 7.3 代码检查

InspectCode 可以检查代码风格和潜在问题。注意：带构建运行有时会卡在构建阶段，可以直接分析已有产物。

### 7.4 常见错误

| 现象 | 可能原因 | 处理 |
| --- | --- | --- |
| 窗口打不开 | 没装 .NET 10 桌面运行时 | 安装 SDK/运行时，或自包含发布 |
| 图片不显示 | 相对路径不对 | 图片相对当前 Markdown 文件解析 |
| 字体不对 | 字体没有随程序打包 | 检查 `Assets/Fonts` 和 `AppFonts` |
| 长文卡顿 | 文档太大或布局过重 | 看验证报告里的块数和内存 |
| 修改 XAML 不生效 | 没有重新构建 | 重新运行 `dotnet build` |

---

## 8. Markdown 语法速查

### 8.1 标题和强调

```markdown
# 一级标题
## 二级标题

**加粗**、*斜体*、~~删除线~~、`行内代码`
```

### 8.2 列表和任务

```markdown
- 无序列表
  - 子项

1. 有序列表
2. 第二项

- [x] 已完成
- [ ] 未完成
```

### 8.3 代码围栏

````markdown
```csharp
Console.WriteLine("Hello");
```
````

### 8.4 表格

```markdown
| 名称 | 作用 |
| --- | --- |
| Core | 解析 |
| App | 界面 |
```

### 8.5 链接、图片、公式、脚注

```markdown
[链接文字](https://example.com)
![图片说明](images/demo.png)

行内公式：$a^2 + b^2 = c^2$

块级公式：

$$
\int_0^1 x^2 \, dx
$$

脚注引用[^1]

[^1]: 脚注内容
```

### 8.6 HTML 子集

```markdown
<kbd>Ctrl</kbd> + <kbd>C</kbd>

<mark>高亮</mark>

H<sub>2</sub>O、x<sup>2</sup>
```

MokiPad 只支持安全的 HTML 子集，脚本和样式会被丢弃。

---

## 9. 知识树 mgtree

### 9.1 它是什么

`mgtree` 是 MokiDown 使用的纯文本知识树格式：

- 用缩进表示父子关系；
- 一行一个节点；
- 可以给节点加“跳到某个代码块”的标记；
- 和 Markdown 文件放在一起，用来生成侧边知识树。

MokiPad 当前只做阅读，不显示知识树；这份 mgtree 主要给 MokiDown 或后续功能使用。

### 9.2 和 Markdown 怎么对应

推荐一一对应：

- Markdown 的一级标题 → 知识树根节点；
- 二级标题 → 根节点的子节点；
- 三级标题 → 再下一层；
- 节点文字尽量和标题一致，方便搜索；
- 需要跳代码时，在节点末尾加 `>语言[序号]`。

例如：

```tree
MokiPad 新手入门
  1. 十分钟跑起来
    构建项目 >powershell[2]
    启动阅读器 >powershell[3]
  3. C# 最小必要知识
    类和命名空间 >csharp[1]
    异步 >csharp[3]
```

### 9.3 代码跳转规则

- 序号从 1 开始，按语言分别计数；
- `>csharp[2]` 表示跳到第 2 个 C# 代码块；
- `>csharp++` 表示在当前序号上加一；
- `>csharp+=3` 表示加三；
- `>csharp` 表示复用当前序号；
- 语言别名会归一化，例如 `js`/`javascript`、`py`/`python`、`cs`/`csharp`。

### 9.4 文件怎么关联

对 `docs/beginner-guide.md`，MokiDown 会依次找：

1. `docs/beginner-guide.mgtree`；
2. `docs/trees/beginner-guide.mgtree`；
3. 以 Markdown 一级标题命名的同名文件。

也可以在 Markdown 里嵌入：

````markdown
```tree
@tree(beginner-guide)
```
````

或者直接写树：

````markdown
```tree
MokiPad 新手入门
  先跑起来
  再看结构
```
````

### 9.5 这份文档的配套树

本目录下的 `beginner-guide.mgtree` 就是按上面的规则写的：

- 根节点对应本文标题；
- 一级子节点对应各章；
- 关键代码节点带 `>语言[序号]`，可以在 MokiDown 里直接跳到示例。

---

## 10. 术语表

| 术语 | 解释 |
| --- | --- |
| WPF | Windows 的桌面界面框架，用 XAML 描述界面 |
| XAML | 一种 XML，用来写 WPF 界面 |
| 依赖属性 | WPF 的属性系统，支持绑定、样式、继承 |
| 路由事件 | 会沿可视树传递的事件 |
| Dispatcher | WPF 的界面线程消息队列 |
| 虚拟化 | 只创建屏幕附近控件的技术 |
| 容器 | 列表为每一项生成的外层控件 |
| 绑定 | 把数据属性接到界面属性 |
| 资源字典 | 存放颜色、样式、模板的集合 |
| mgtree | MokiDown 的纯文本知识树格式 |

---

## 11. 常见问题

**Q：我完全不会 C#，从哪看起？**  
先看第 3 章的五个例子，再打开 `MainWindow.xaml.cs` 看真实代码。不要一开始就追所有细节。

**Q：为什么改了 XAML，界面没变？**  
WPF 的 XAML 会编译成代码，改完要重新构建，不能只刷新窗口。

**Q：为什么阅读器里没有编辑功能？**  
这是产品定位：MokiPad 只读。编辑请用 MokiDown。

**Q：mgtree 在 MokiPad 里看不到？**  
当前版本不支持知识树显示，用 MokiDown 打开；MokiPad 只负责阅读 Markdown 正文。

**Q：长文很卡怎么办？**  
先看验证报告里的块数和内存；阅读器已经使用虚拟化，但解析仍然会读完整篇文档。

---

## 12. 下一步

- 想改界面：从 `Styles/Controls.xaml` 和 `MainWindow.xaml` 开始；
- 想改解析：从 `MarkdownEngine.cs` 开始；
- 想加功能：先在 `MokiPad.Verification` 写一条会失败的检查，再实现；
- 想理解知识树：用 MokiDown 打开 `beginner-guide.mgtree`，对照本文标题看节点。

祝你读得开心，也改得开心。
