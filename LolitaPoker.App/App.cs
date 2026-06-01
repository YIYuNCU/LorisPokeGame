using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LolitaPoker.Core.Assets;
using LolitaPoker.Core.Views;

namespace LolitaPoker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册日志文件输出（Trace.WriteLine 会同时写入文件和调试器）
        // 优先 BaseDirectory（debug 时 Environment.ProcessPath 指向 dotnet.exe）
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string logPath = Path.Combine(exeDir, "game.log");
        Trace.Listeners.Add(new TextWriterTraceListener(logPath, "fileListener"));
        Trace.AutoFlush = true;
        Trace.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] === 萝莉丝扑克启动 ===");

        // 初始化图片缓存（加载 pics/ 目录下所有图片）
        string basePath = Path.Combine(exeDir, "pics");
        CardImageProvider.Initialize(basePath);

        // 创建并显示主窗口
        var mainWindow = new DoudizhuMainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        // 确保进程完全退出
        Environment.Exit(0);
    }
}
