using System.Windows;
using System.Windows.Threading;

namespace MokiPad;

/// <summary>
/// 应用入口。WPF 会在启动时创建这个类；<c>App.xaml</c> 负责合并全局资源字典，
/// 这里只处理“窗口创建”和“启动参数里的文件”。
/// </summary>
public partial class App
{
    /// <summary>
    /// WPF 启动钩子。此时还没有任何窗口，适合创建主窗口；
    /// 但读取文件、解析 Markdown 比较耗时，不能放在这里阻塞首帧。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // WPF 的界面线程叫 Dispatcher。BeginInvoke 把工作排进消息队列，而不是立刻执行。
        // ContextIdle 的优先级很低，表示“等窗口完成第一次布局、绘制并且空闲下来再做”，
        // 这样用户先看到窗口，不会看到白屏或“未响应”。
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => _ = OpenFilesAsync(window, e.Args)));
    }

    /// <summary>
    /// 读取启动参数中的文件。每个文件打开都会切换一次阅读标签，
    /// 所以这里按顺序 await，避免多个文件同时解析抢 UI 线程。
    /// </summary>
    private static async Task OpenFilesAsync(MainWindow window, string[] arguments)
    {
        foreach (var argument in arguments.Where(File.Exists))
            await window.OpenFileAsync(argument);
    }
}
