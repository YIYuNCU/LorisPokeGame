using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LolitaPoker.Core.Assets;
using LolitaPoker.Core.Views;

namespace LolitaPoker.App;

public partial class App : Application
{
    private WhiteVoiceTtsService? _tts;
    private BgmServiceImpl? _bgm;
    private SoundEffectServiceImpl? _soundEffects;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册日志文件输出
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string logPath = Path.Combine(exeDir, "game.log");
        Trace.Listeners.Add(new TextWriterTraceListener(logPath, "fileListener"));
        Trace.AutoFlush = true;
        Trace.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] === 萝莉丝扑克启动 ===");

        // 初始化图片缓存
        string basePath = Path.Combine(exeDir, "pics");
        CardImageProvider.Initialize(basePath);

        // 初始化 TTS 和 BGM 服务
        _tts = new WhiteVoiceTtsService();
        _bgm = new BgmServiceImpl();
        _soundEffects = new SoundEffectServiceImpl();
        Trace.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TTS 可用: {_tts.IsAvailable}");

        // 创建并显示主窗口（注入 TTS + BGM）
        var mainWindow = new DoudizhuMainWindow(_tts, _bgm, _soundEffects);

        // 启动背景音乐
        _bgm.PlayAsync("");
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tts?.Dispose();
        _bgm?.Dispose();
        _soundEffects?.Dispose();
        base.OnExit(e);
        Environment.Exit(0);
    }
}
