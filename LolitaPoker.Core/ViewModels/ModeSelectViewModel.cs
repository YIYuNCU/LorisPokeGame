// -----------------------------------------------------------------------
// ModeSelectViewModel.cs - 模式选择视图模型
// 提供人机模式、P2P联网、服务器模式三种选择
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Input;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Network;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 模式选择视图模型（简化版，仅负责模式导航）
/// </summary>
public class ModeSelectViewModel : ViewModelBase
{
    private readonly Action<GameMode, INetworkAdapter?, string, string, int, string> _navigateToGame;
    private readonly Action<GameMode> _navigateToSettings;

    // ========== 命令 ==========
    public ICommand StartAIGameCommand { get; }
    public ICommand OpenVPetLanInfoCommand { get; }
    public ICommand OpenServerSettingsCommand { get; }

    public ModeSelectViewModel(
        Action<GameMode, INetworkAdapter?, string, string, int, string> navigateToGame,
        Action<GameMode> navigateToSettings)
    {
        _navigateToGame = navigateToGame;
        _navigateToSettings = navigateToSettings;

        StartAIGameCommand = new RelayCommand(_ => OnStartAIGame());
        OpenVPetLanInfoCommand = new RelayCommand(_ => OnShowVPetLanInfo());
        OpenServerSettingsCommand = new RelayCommand(_ => _navigateToSettings(GameMode.Server));
    }

    private void OnStartAIGame()
    {
        _navigateToGame(GameMode.HumanVsAI, null, "玩家", "", 0, "");
    }

    private void OnShowVPetLanInfo()
    {
        MessageBox.Show(
            "VPet 联机模式通过 VPet 桌宠的访客表进行联机对战。\n\n" +
            "使用方法：\n" +
            "1. 在 VPet 桌宠中打开访客表\n" +
            "2. 在访客表的「萝莉丝扑克」标签页中准备\n" +
            "3. 房主点击「开始游戏」即可开始对战\n\n" +
            "请确保已安装萝莉丝扑克 VPet 插件。",
            "VPet 联机 - 使用说明",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
