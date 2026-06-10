// -----------------------------------------------------------------------
// MainViewModel.cs - 应用主壳视图模型
// 管理视图切换：模式选择 ↔ 网络设置 ↔ 游戏桌面
// -----------------------------------------------------------------------

using LolitaPoker.Core.Audio;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Network;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 应用主壳视图模型，持有 CurrentViewModel 并负责视图切换
/// </summary>
public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    private readonly ITtsService _ttsService;
    private readonly IBgmService _bgmService;
    private readonly ISoundEffectService _soundEffectService;

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public MainViewModel(
        ITtsService? ttsService = null,
        IBgmService? bgmService = null,
        ISoundEffectService? soundEffectService = null)
    {
        _ttsService = ttsService ?? NullTtsService.Instance;
        _bgmService = bgmService ?? NullBgmService.Instance;
        _soundEffectService = soundEffectService ?? NullSoundEffectService.Instance;
        _currentViewModel = CreateModeSelectViewModel();
    }

    private ModeSelectViewModel CreateModeSelectViewModel()
    {
        return new ModeSelectViewModel(NavigateToGame, NavigateToSettings);
    }

    /// <summary>
    /// 导航到网络设置页面
    /// </summary>
    private void NavigateToSettings(GameMode mode)
    {
        if (CurrentViewModel is NetworkSettingsViewModel oldSettings)
            oldSettings.Cleanup();
        CurrentViewModel = new NetworkSettingsViewModel(mode, NavigateToGame, NavigateToMenu);
    }

    /// <summary>
    /// 导航到游戏桌面
    /// </summary>
    private void NavigateToGame(GameMode mode, INetworkAdapter? adapter, string playerName, string address, int port, string roomCode)
    {
        var gameVm = new GameViewModel(_ttsService, _bgmService, _soundEffectService);
        gameVm.GameMode = mode;

        switch (mode)
        {
            case GameMode.HumanVsAI:
                gameVm.InitializePlayers(playerName, "电脑A", "电脑B");
                break;
            case GameMode.VPetLan:
            case GameMode.Server:
                gameVm.InitializePlayers(playerName, "等待加入...", "等待加入...");
                break;
        }

        // 服务器模式：保存房间号用于顶部显示
        if (mode == GameMode.Server && !string.IsNullOrEmpty(roomCode))
        {
            gameVm.RoomCode = roomCode;
        }

        // 服务器模式：从适配器同步房间可见性和创建者身份
        if (mode == GameMode.Server && adapter is WebSocketNetworkAdapter wsAdapterInit)
        {
            gameVm.IsPublicRoom = wsAdapterInit.IsPublicRoom;
            gameVm.IsRoomCreator = wsAdapterInit.IsRoomCreator;
        }

        if (adapter != null)
        {
            gameVm.InitializeNetworkMode(adapter);
        }

        // 服务器模式：同步大厅玩家列表和名称（修复后加入玩家看不到已有玩家的问题）
        if (mode == GameMode.Server && adapter is WebSocketNetworkAdapter wsAdapter)
        {
            // 用真实玩家名替换占位符 "等待加入..."
            foreach (var lp in wsAdapter.LobbyPlayers)
            {
                int localSeat = (lp.Seat - wsAdapter.AssignedSeat + 3) % 3;
                if (localSeat >= 0 && localSeat < 3)
                    gameVm.AllPlayers[localSeat].Name = lp.Name;
            }
            gameVm.SyncLobbyFromAdapter();
            gameVm.IsLobbyVisible = true;
        }

        gameVm.RequestNavigateToMenu = NavigateToMenu;
        CurrentViewModel = gameVm;
    }

    /// <summary>
    /// 导航回模式选择界面
    /// </summary>
    private void NavigateToMenu()
    {
        if (CurrentViewModel is GameViewModel gameVm)
        {
            gameVm.Cleanup();
        }
        else if (CurrentViewModel is NetworkSettingsViewModel settingsVm)
        {
            settingsVm.Cleanup();
        }

        CurrentViewModel = CreateModeSelectViewModel();
    }

    /// <summary>
    /// 清理资源（窗口关闭时调用）
    /// </summary>
    public void Cleanup()
    {
        if (CurrentViewModel is GameViewModel gameVm)
        {
            gameVm.Cleanup();
        }
    }
}
