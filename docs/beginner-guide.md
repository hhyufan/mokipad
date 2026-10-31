# MokiPad 新手入门：从零看懂 WPF 与 C#

> 写给第一次接触 C#、第一次打开 WPF 项目的人。  
> 不要求你会写代码，只要求你愿意按顺序动手。  
> 配套知识树：`docs/beginner-guide.mgtree`，可以用 MokiDown 打开，按章节跳转。

## 怎么读这份文档

- 代码块里的内容是“可以照着写的例子”，不是必须一次全懂。
- 每个代码块下面都有**逐行拆解**：先看代码，再看表格里每一行/每个属性的意思。
- 看到 `...` 表示“这里省略了其他代码”，不是让你输入三个点。
- 命令行里的 `>` 是提示符，不需要输入。
- 术语第一次出现时会用大白话解释，后面再慢慢加深。

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

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `dotnet` | .NET 的命令行工具，装完 SDK 后就能在终端里使用 |
| `--version` | 让工具打印当前安装的 SDK 版本，而不是去构建项目 |
| 输出 `10.0.100` 或 `10.x` | 说明 .NET 10 SDK 已经可用，可以继续 |

如果提示“找不到 dotnet”，说明 SDK 没装好，或者终端没有重新打开。

### 1.2 构建项目

在项目根目录执行：

```powershell
dotnet build .\MokiPad.sln -c Release
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `dotnet build` | 编译整个项目：把 C# 和 XAML 变成可运行的程序 |
| `.\MokiPad.sln` | 要编译的解决方案文件；`.` 表示当前目录，`\` 是 Windows 路径分隔符 |
| `-c Release` | 使用 Release 配置；Release 会做优化，Debug 更适合断点调试 |
| `0 个警告，0 个错误` | 警告是“能编译但可能有隐患”，错误是“编译失败”；这里都为零 |

第一次构建会下载 NuGet 依赖，需要联网。构建产物在 `src/MokiPad/bin/Release/` 等目录，不需要手动打开。

### 1.3 启动阅读器

```powershell
.\scripts\dev.ps1
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `.\` | 从当前目录开始找文件 |
| `scripts` | 存放开发脚本的文件夹 |
| `dev.ps1` | PowerShell 脚本；里面实际执行 `dotnet run --project ...` |
| 不带参数 | 只打开阅读器窗口，不自动打开文件 |

也可以直接传一个文件：

```powershell
.\scripts\dev.ps1 "C:\Notes\长文.md"
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `"C:\Notes\长文.md"` | 传给脚本的文件路径；有空格或中文时用双引号包住 |
| 脚本里的 `$Files` | 接收这些参数，再原样传给阅读器 |
| 阅读器收到路径 | 启动后直接打开这个 Markdown 文件 |

窗口打开后，把 `.md` 文件拖进去，或者点左上角“文件”菜单打开。

### 1.4 验证功能是否正常

```powershell
.\scripts\verify.ps1
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `verify.ps1` | 验证脚本，内部执行验证项目 |
| 不需要手动打开窗口 | 验证程序会创建离屏窗口，不打扰桌面 |
| 生成 `artifacts/verification.json` | 记录通过了哪些检查、耗时、内存等 |

它会检查主题、公式、图片、GIF、链接、脚注、长文虚拟化等。

### 1.5 发布给别人用

```powershell
.\scripts\publish.ps1
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `publish.ps1` | 发布脚本，内部执行 `dotnet publish` |
| 默认输出 `artifacts\app` | 发布后的程序目录 |
| 需要 .NET 10 桌面运行时 | 目标电脑没装运行时就选自包含发布 |

如果对方没装 .NET 运行时，用自包含发布：

```powershell
.\scripts\publish.ps1 -SelfContained
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `-SelfContained` | 把 .NET 运行时一起打包，体积更大，但目标电脑不用另装 |
| 脚本里的 `--self-contained true` | 对应这个开关 |
| 产物仍在 `artifacts\app` | 直接运行里面的 `MokiPad.exe` |

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

**逐行拆解**

| 行 | 含义 |
| --- | --- |
| `mokipad/` | 项目根目录，所有路径都从这里开始 |
| `src/` | 源代码目录，`src` 是 source 的缩写 |
| `MokiPad.Core/` | 不依赖界面的核心库：解析 Markdown、识别编码 |
| `MokiPad/` | WPF 界面项目，生成最终的 `MokiPad.exe` |
| `Controls/` | 自定义控件：阅读列表、图片、GIF、HTML |
| `Models/` | 数据模型，例如一个标签页代表什么 |
| `Rendering/` | 数学公式的显示控件 |
| `Services/` | 设置、主题、字体、界面文案 |
| `Styles/` | 全局 XAML 样式和控件模板 |
| `tests/` | 自动验证项目 |
| `docs/` | 文档、截图、知识树 |
| `scripts/` | 启动、发布、验证脚本 |

先记住三个最常改的地方：

- 界面长什么样：`src/MokiPad/Styles/Controls.xaml` 和 `MainWindow.xaml`；
- 阅读器怎么画块：`src/MokiPad/Controls/MarkdownPreview.cs`；
- Markdown 怎么解析：`src/MokiPad.Core/MarkdownEngine.cs`。

---

## 3. C# 最小必要知识

### 3.1 命名空间、类、方法

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

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `namespace MokiPad.Core;` | 命名空间，像文件夹一样给类分组；分号表示“文件范围命名空间” |
| 3 | `public sealed class Greeting` | 定义一个公开的类；`sealed` 表示不允许别的类继承它 |
| 5 | `public string Hello(string name)` | 方法：公开、返回字符串、参数是一个字符串 `name` |
| 7 | `return "你好，" + name;` | 把固定文字和参数拼起来，返回给调用者 |
| 8 | `}` | 结束方法 |
| 9 | `}` | 结束类 |

- **命名空间**：避免重名，例如两个项目都有 `Greeting` 类也不会冲突。
- **类**：一组数据和行为。
- **方法**：一段可以重复调用的代码。
- `public` 是访问修饰符：公开，外部可以调用；对应的还有 `private`（只在本类可用）。

### 3.2 `var`、类型和可空

```csharp
var title = "MokiPad";
var count = 3;
string? subtitle = null;

if (subtitle is not null)
{
    Console.WriteLine(subtitle.Length);
}
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `var title = "MokiPad";` | 编译器根据右边的字符串推断 `title` 是 `string`；`var` 不是“无类型” |
| 2 | `var count = 3;` | 推断为 `int`（整数） |
| 3 | `string? subtitle = null;` | `string?` 表示这个字符串**可能为空**；`null` 就是“没有值” |
| 5 | `if (subtitle is not null)` | 只有 `subtitle` 不是空值时才进入大括号 |
| 7 | `subtitle.Length` | 字符串长度；因为上面已经判空，这里访问才安全 |

- C# 是强类型语言：每个变量都有确定的类型。
- 打开“可空引用类型”后，`string` 表示“不应该为空”，`string?` 表示“可能为空”。
- 访问可能为空的变量前，要先判断，否则运行时会抛空引用异常。

### 3.3 异步：`async` 和 `await`

```csharp
private static async Task OpenFileAsync(string path)
{
    var text = await File.ReadAllTextAsync(path);
    Console.WriteLine($"读到 {text.Length} 个字符");
}
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `private static async Task` | 私有、静态、异步方法；返回 `Task` 表示“将来完成的工作” |
| 1 | `OpenFileAsync(string path)` | 方法名以 `Async` 结尾是约定；参数是文件路径 |
| 3 | `await File.ReadAllTextAsync(path)` | 异步读取文件；`await` 会等结果，但不会堵住界面线程 |
| 3 | `var text = ...` | 读取完成后，把文件内容赋给 `text` |
| 4 | `$"..."` | 字符串插值；大括号里可以放变量或表达式 |
| 4 | `text.Length` | 文件内容的字符数 |

- WPF 的界面线程只有一个；同步读大文件会让窗口“未响应”。
- `async` / `await` 把等待交给后台，界面继续响应。
- 不要写 `async void`，除非它是界面事件处理器；否则异常没人接收。

### 3.4 记录类型和 LINQ

```csharp
public sealed record PreviewBlock(string Kind, int Line, string Text);

var blocks = new List<PreviewBlock>
{
    new("heading", 1, "标题"),
    new("paragraph", 3, "正文")
};

var headings = blocks.Where(block => block.Kind == "heading").ToList();
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `public sealed record PreviewBlock(...)` | 记录类型：适合表达只读数据；括号里是构造参数 |
| 1 | `string Kind, int Line, string Text` | 三个属性：类型、行号、文本 |
| 3 | `var blocks = new List<PreviewBlock>` | 创建一个列表，元素类型是 `PreviewBlock` |
| 4 | `new("heading", 1, "标题")` | 目标类型已知，省略类型名的 `new`；等价于 `new PreviewBlock(...)` |
| 5 | `new("paragraph", 3, "正文")` | 第二个块 |
| 8 | `blocks.Where(...)` | LINQ 过滤：只保留满足条件的元素 |
| 8 | `block => block.Kind == "heading"` | Lambda 表达式：对每个 `block` 判断类型是不是标题 |
| 8 | `.ToList()` | 把过滤结果变成新列表，方便后续使用 |

- `record` 自动生成构造函数、`Equals`、`ToString`，适合数据载体。
- `List<T>` 是可变列表；`IReadOnlyList<T>` 是只读视图。
- LINQ 把“循环 + 判断 + 收集”写成一条查询，读起来更接近自然语言。

### 3.5 可空模式和模式匹配

```csharp
PreviewBlock? block = FindBlock();

if (block is { Kind: "heading", Line: > 0 } heading)
{
    Console.WriteLine(heading.Text);
}
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `PreviewBlock? block` | 声明一个可能为空的块 |
| 1 | `FindBlock()` | 假设有一个方法会返回块或空值 |
| 3 | `block is { ... } heading` | 模式匹配：同时判断非空、属性值和范围 |
| 3 | `Kind: "heading"` | 要求 `Kind` 等于 `"heading"` |
| 3 | `Line: > 0` | 要求 `Line` 大于 0 |
| 3 | `heading` | 匹配成功后，用这个变量访问属性 |
| 5 | `heading.Text` | 读取匹配到的块的文本 |

- 这种写法比 `if (block != null && block.Kind == "heading" && block.Line > 0)` 更短。
- 模式匹配是 C# 里非常常用的可读性工具，WPF 代码里也会大量出现。

### 3.6 依赖属性：WPF 特有的“属性”

```csharp
public static readonly DependencyProperty ReadingFontSizeProperty =
    DependencyProperty.RegisterAttached(
        "ReadingFontSize",
        typeof(double),
        typeof(MarkdownPreview),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.Inherits));
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `public static readonly` | 公开、静态、只读字段；依赖属性通常这样声明 |
| 1 | `DependencyProperty` | WPF 的属性系统类型，不是普通 C# 属性 |
| 1 | `ReadingFontSizeProperty` | 字段名以 `Property` 结尾是约定 |
| 2 | `DependencyProperty.RegisterAttached` | 注册一个“附加属性”，可以附加到别的控件上 |
| 3 | `"ReadingFontSize"` | 属性名，XAML 里用的名字 |
| 4 | `typeof(double)` | 属性值类型是双精度浮点数 |
| 5 | `typeof(MarkdownPreview)` | 注册者/所有者类型 |
| 6 | `new FrameworkPropertyMetadata(16.0, ...)` | 元数据：默认 16，并设置“属性继承” |
| 6 | `FrameworkPropertyMetadataOptions.Inherits` | 父控件设置后，子控件自动继承这个值 |

- 普通 C# 属性只能存值；依赖属性还能参与绑定、样式、动画、属性继承。
- 实际读写不是 `field = value`，而是 `GetValue` / `SetValue`。
- 阅读器在父控件设置字号，内部的块控件能自动读到，就是靠 `Inherits`。

### 3.7 路由事件和 Dispatcher

```csharp
AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);

Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
{
    // 等布局完成后再做
}));
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `AddHandler(...)` | 给控件注册事件处理器 |
| 1 | `PreviewMouseWheelEvent` | 鼠标滚轮的“隧道”事件，先从父级往下传 |
| 1 | `new MouseWheelEventHandler(OnWheel)` | 事件处理器方法 |
| 1 | `true` | `handledEventsToo`：即使子控件已经处理，父级仍然收到 |
| 3 | `Dispatcher.BeginInvoke` | 把一段工作排到 WPF 的界面线程队列，稍后执行 |
| 3 | `DispatcherPriority.Loaded` | 优先级：等布局完成后再执行 |
| 3 | `new Action(() => { ... })` | 要执行的动作；`Action` 是无返回值委托 |

- WPF 事件有“冒泡”和“隧道”两种传递方式。
- 滚轮事件用隧道，是为了在代码块内部的滚动条吃掉它之前，阅读器先接住。
- 所有界面对象都只能在界面线程操作；`Dispatcher` 就是回到界面线程的入口。

---

## 4. WPF 最小必要知识

### 4.1 XAML 和代码后置

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

**逐行拆解**

| 行/属性 | 含义 |
| --- | --- |
| `<Window ...>` | 窗口的根元素；一个 XAML 文件通常只有一个根 |
| `x:Class="MokiPad.MainWindow"` | 这个 XAML 对应的 C# 类；后台文件是 `MainWindow.xaml.cs` |
| `xmlns="..."` | WPF 控件所在的默认命名空间；不写前缀的标签都从这里找 |
| `xmlns:x="..."` | XAML 语言本身的命名空间；`x:Class`、`x:Name` 来自这里 |
| `Title="MokiPad"` | 窗口标题 |
| `Width="1280" Height="820"` | 初始宽高，单位是设备无关像素 |
| `<Grid>` | 布局容器：把窗口内容分成网格 |
| `<TextBlock Text="你好，MokiPad" />` | 显示一段文字；自闭合标签表示没有子元素 |
| `</Grid>` | 结束网格 |
| `</Window>` | 结束窗口 |

- `.xaml` 负责“长什么样”，`.xaml.cs` 负责“做什么”。
- `x:Name` 给元素起名字，后台代码可以直接用这个名字访问控件。

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

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `<Grid>` | 最外层网格，默认只有一行一列 |
| 2 | `<Grid.RowDefinitions>` | 开始定义“行”；列用 `ColumnDefinitions`，这里不需要 |
| 3 | `<RowDefinition Height="42" />` | 第 0 行固定 42 像素高 |
| 4 | `<RowDefinition Height="*" />` | 第 1 行占满剩余高度；`*` 表示按比例分配剩余空间 |
| 5 | `<RowDefinition Height="32" />` | 第 2 行固定 32 像素高 |
| 6 | `</Grid.RowDefinitions>` | 结束行定义 |
| 8 | `<TextBlock Grid.Row="0" ... />` | 把文字放进第 0 行；`Grid.Row` 是附加属性，默认是 0 |
| 10 | `<StackPanel Grid.Row="1" ...>` | 把堆叠面板放进第 1 行 |
| 10 | `Orientation="Vertical"` | 子元素从上到下排列；`Horizontal` 是从左到右 |
| 11 | `<TextBlock Text="第一行" />` | 堆叠面板的第一个子元素 |
| 12 | `<TextBlock Text="第二行" />` | 第二个子元素，自动排在第一行下面 |
| 15 | `<DockPanel Grid.Row="2">` | 把停靠面板放进第 2 行 |
| 16 | `<TextBlock DockPanel.Dock="Left" ... />` | 这个文字靠左停靠；`Dock` 可取 Left/Top/Right/Bottom |
| 17 | `<TextBlock Text="剩余空间" />` | 没有写 Dock 的子元素；DockPanel 默认 `LastChildFill=True`，最后一个子元素填满剩余区域 |

- `Grid` 适合整体框架：标题栏、内容区、状态栏。
- `StackPanel` 适合“一个接一个”的简单排列。
- `DockPanel` 适合“左边工具栏 + 右边剩余内容”这类布局。
- `Height="*"` 是按比例分配剩余空间；`Height="Auto"` 是按内容高度。
- `Grid.Row`、`DockPanel.Dock` 都是**附加属性**：属性不属于子元素本身，而是由父容器定义、子元素设置。

### 4.3 资源、样式、模板

```xml
<Style x:Key="IconButton" TargetType="Button">
    <Setter Property="Width" Value="32" />
    <Setter Property="Height" Value="32" />
    <Setter Property="Background" Value="Transparent" />
</Style>

<Button Style="{StaticResource IconButton}" Content="打开" />
```

**逐行拆解**

| 行/属性 | 含义 |
| --- | --- |
| `<Style ...>` | 定义一组可复用的控件属性 |
| `x:Key="IconButton"` | 资源键名，后面用它来引用 |
| `TargetType="Button"` | 这个样式只能用于按钮 |
| `<Setter Property="Width" Value="32" />` | 设置按钮宽度为 32 |
| `<Setter Property="Height" Value="32" />` | 设置高度为 32 |
| `<Setter Property="Background" Value="Transparent" />` | 背景透明 |
| `<Button Style="{StaticResource IconButton}" ... />` | 把样式应用到按钮 |
| `{StaticResource IconButton}` | 在资源字典里找 `IconButton`；只在加载时查找一次 |
| `Content="打开"` | 按钮上显示的文字 |

- `StaticResource`：加载时查一次，适合不会变的样式。
- `DynamicResource`：资源变化时重新查找，适合换主题。
- `ControlTemplate` 决定控件的“骨架”，`Style` 决定属性的默认值。

> 这段代码是“资源片段”的写法：实际使用时，`Style` 要放进 `<Window.Resources>` 或 `ResourceDictionary` 里，`Button` 要放在窗口的内容区域中。单独把这两段贴进 XAML 文件是跑不起来的。

### 4.4 绑定和 DataTemplate

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

**逐行拆解**

| 行/属性 | 含义 |
| --- | --- |
| `<ListBox ...>` | 列表控件 |
| `ItemsSource="{Binding Documents}"` | 列表的数据来自当前数据上下文的 `Documents` 属性 |
| `<ListBox.ItemTemplate>` | 开始定义“每一项怎么显示” |
| `<DataTemplate>` | 数据模板：描述一个数据对象的界面 |
| `<StackPanel Orientation="Horizontal">` | 每一项内部横向排列 |
| `<TextBlock Text="{Binding Name}" />` | 显示当前项的 `Name` 属性 |
| `<TextBlock Text="{Binding FilePath}" />` | 显示当前项的 `FilePath` 属性 |
| `</DataTemplate>` | 结束模板 |

- `{Binding Name}` 里的 `Name` 是**当前项对象**的属性，不是窗口的属性。
- 数据模板只负责显示，不负责数据本身。
- WPF 会自动为每一项创建模板里的控件；列表虚拟化时这些控件会被回收复用。

> 这个例子省略了窗口和 `DataContext`。实际使用时，`ListBox` 要放在窗口里，并且它的数据上下文要有一个 `Documents` 属性，列表才有内容。

### 4.5 虚拟化：长文为什么不卡

```csharp
SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
```

**逐行拆解**

| 行 | 代码 | 含义 |
| --- | --- | --- |
| 1 | `IsVirtualizingProperty, true` | 开启虚拟化：只创建屏幕附近的项目 |
| 2 | `VirtualizationModeProperty, Recycling` | 回收模式：滚出屏幕的容器会被复用，而不是反复新建 |
| 3 | `ScrollUnitProperty, Pixel` | 按像素滚动，而不是按“第几项”跳动 |
| 3 | `ScrollUnit.Pixel` | 滚动条和滚轮以像素为单位 |

- 虚拟化只减少“控件数量”，不会减少“解析整篇文档”的成本。
- 容器会被复用，所以控件里不能假设“我只显示一个块”；每次换数据都要重新渲染。
- 回收模式能减少创建/销毁开销，但也会带来高度估算问题，这是滚动优化的难点。

> 这三行是阅读器构造函数里的真实设置；`SetValue` 是 WPF 依赖属性的写法，不是普通字段赋值。

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

**逐项解释**

| 名称 | 大白话 |
| --- | --- |
| `FileService` | 负责把磁盘上的文件读成文字，并判断它是 UTF-8 还是 GB18030 |
| `MarkdownEngine` | 把 Markdown 文字切成“标题、段落、代码块”等块 |
| `PreviewBlock` | 一块内容的描述，不是控件本身 |
| `MarkdownPreview` | 一个会虚拟化的列表，负责滚动和复用 |
| `BlockPresenter` | 真正把一块内容画成 WPF 控件的类 |

---

## 6. 想改东西，从哪里下手

### 6.1 改颜色和主题

- `src/MokiPad/Services/ThemeService.cs`：颜色值；
- `src/MokiPad/Styles/Controls.xaml`：控件样式；
- `src/MokiPad/MainWindow.xaml`：整体布局。

改颜色时先找颜色名，例如 `Accent`、`Surface`，再在 `ThemeService` 里同时改浅色和深色两套值。

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

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `dotnet build` | 编译 |
| `.\MokiPad.sln` | 解决方案文件 |
| `-c Release` | 发布配置，优化后的版本 |

### 7.2 跑验证

```powershell
.\scripts\verify.ps1
```

**逐行拆解**

| 部分 | 含义 |
| --- | --- |
| `verify.ps1` | 运行验证项目 |
| 不打开真实窗口 | 使用离屏窗口，不影响桌面 |
| 结果在 `artifacts` | 报告和截图都写到这里 |

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

**逐行拆解**

| 写法 | 含义 |
| --- | --- |
| `# 一级标题` | 一个 `#` 加空格表示一级标题；`##` 是二级，最多六级 |
| 空行 | 分隔不同段落，避免被当成同一段 |
| `**加粗**` | 两个星号包住的内容加粗 |
| `*斜体*` | 一个星号包住的内容斜体 |
| `~~删除线~~` | 两个波浪线包住的内容加删除线 |
| `` `行内代码` `` | 反引号包住的内容用等宽字体显示 |

### 8.2 列表和任务

```markdown
- 无序列表
  - 子项

1. 有序列表
2. 第二项

- [x] 已完成
- [ ] 未完成
```

**逐行拆解**

| 写法 | 含义 |
| --- | --- |
| `- 无序列表` | 减号加空格表示无序列表项 |
| `  - 子项` | 前面两个空格表示子项，缩进决定层级 |
| `1. 有序列表` | 数字加点表示有序列表；数字不一定要连续 |
| `2. 第二项` | 第二项 |
| `- [x] 已完成` | 任务列表，`x` 表示已勾选 |
| `- [ ] 未完成` | 任务列表，空括号表示未勾选 |

### 8.3 代码围栏

````markdown
```csharp
Console.WriteLine("Hello");
```
````

**逐行拆解**

| 写法 | 含义 |
| --- | --- |
| ` ```csharp ` | 三个反引号开始代码块，`csharp` 是语言名，用于语法高亮 |
| `Console.WriteLine("Hello");` | 代码内容，不会被当成普通 Markdown 解析 |
| ` ``` ` | 三个反引号结束代码块 |
| 外层四个反引号 | 因为示例本身包含三个反引号，所以用四个把它包起来 |

### 8.4 表格

```markdown
| 名称 | 作用 |
| --- | --- |
| Core | 解析 |
| App | 界面 |
```

**逐行拆解**

| 写法 | 含义 |
| --- | --- |
| `\| 名称 \| 作用 \|` | 第一行是表头，竖线分列 |
| `\| --- \| --- \|` | 分隔行，告诉 Markdown 这是表格 |
| `\| Core \| 解析 \|` | 第一行数据 |
| `\| App \| 界面 \|` | 第二行数据 |
| 对齐冒号 | `:---` 左对齐、`:---:` 居中、`---:` 右对齐 |

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

**逐行拆解**

| 写法 | 含义 |
| --- | --- |
| `[链接文字](https://example.com)` | 方括号是显示文字，圆括号是目标地址 |
| `![图片说明](images/demo.png)` | 前面多一个 `!` 表示图片；方括号是替代文字 |
| `$a^2 + b^2 = c^2$` | 两个美元符号包住行内公式 |
| `$$ ... $$` | 两个美元符号单独成行表示块级公式 |
| `\int_0^1 x^2 \, dx` | TeX 语法：积分、上下限、平方 |
| `脚注引用[^1]` | 方括号加 `^` 表示脚注引用 |
| `[^1]: 脚注内容` | 脚注定义，通常放在文末 |

### 8.6 HTML 子集

```markdown
<kbd>Ctrl</kbd> + <kbd>C</kbd>

<mark>高亮</mark>

H<sub>2</sub>O、x<sup>2</sup>
```

**逐行拆解**

| 写法 | 含义 |
| --- | --- |
| `<kbd>Ctrl</kbd>` | 键盘按键样式 |
| `<mark>高亮</mark>` | 高亮背景 |
| `H<sub>2</sub>O` | 下标，显示成 H₂O |
| `x<sup>2</sup>` | 上标，显示成 x² |

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

**逐行拆解**

| 行 | 含义 |
| --- | --- |
| `MokiPad 新手入门` | 根节点，对应 Markdown 的一级标题 |
| `  1. 十分钟跑起来` | 两个空格缩进，表示根节点的子节点，对应二级标题 |
| `    构建项目 >powershell[2]` | 四个空格，再下一层；`>powershell[2]` 表示跳到第 2 个 PowerShell 代码块 |
| `    启动阅读器 >powershell[3]` | 跳到第 3 个 PowerShell 代码块 |
| `  3. C# 最小必要知识` | 另一个二级节点 |
| `    类和命名空间 >csharp[1]` | 跳到第 1 个 C# 代码块 |
| `    异步 >csharp[3]` | 跳到第 3 个 C# 代码块 |

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

**逐行拆解**

| 行 | 含义 |
| --- | --- |
| ` ```tree ` | 开始一个知识树代码块 |
| `@tree(beginner-guide)` | 引用外部树文件，省略 `.mgtree` 后缀；相对当前 Markdown 解析 |
| ` ``` ` | 结束代码块 |

或者直接写树：

````markdown
```tree
MokiPad 新手入门
  先跑起来
  再看结构
```
````

**逐行拆解**

| 行 | 含义 |
| --- | --- |
| ` ```tree ` | 开始内嵌知识树 |
| `MokiPad 新手入门` | 根节点 |
| `  先跑起来` | 两个空格，子节点 |
| `  再看结构` | 另一个子节点 |
| ` ``` ` | 结束 |

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
先看第 3 章的七个例子，再打开 `MainWindow.xaml.cs` 看真实代码。不要一开始就追所有细节。

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
