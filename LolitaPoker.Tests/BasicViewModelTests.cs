using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using LolitaPoker.Core;
using LolitaPoker.Core.Converters;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.ViewModels;
using Xunit;

namespace LolitaPoker.Tests;

public class BasicViewModelTests
{
    [Fact]
    public void RelayCommand_ThrowsWhenExecuteIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
    }

    [Fact]
    public void RelayCommand_CanExecute_ReturnsTrueWithoutPredicate()
    {
        var command = new RelayCommand(_ => { });

        Assert.True(command.CanExecute((object?)null));
    }

    [Fact]
    public void RelayCommand_CanExecute_UsesPredicate()
    {
        var command = new RelayCommand(_ => { }, parameter => parameter is int value && value > 3);

        Assert.False(command.CanExecute(3));
        Assert.True(command.CanExecute(4));
    }

    [Fact]
    public void RelayCommand_Execute_PassesParameterToAction()
    {
        string? received = null;
        var command = new RelayCommand(parameter => received = parameter?.ToString());

        command.Execute(123);

        Assert.Equal("123", received);
    }

    [Fact]
    public void BoolToVisibilityConverter_Convert_ReturnsExpectedVisibility()
    {
        var converter = new BoolToVisibilityConverter();

        Assert.Equal(Visibility.Visible, converter.Convert(true, typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert(false, typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert("not a bool", typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BoolToVisibilityConverter_Convert_InverseParameterFlipsResult()
    {
        var converter = new BoolToVisibilityConverter();

        Assert.Equal(Visibility.Collapsed, converter.Convert(true, typeof(Visibility), "Inverse", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, converter.Convert(false, typeof(Visibility), "Inverse", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BoolToVisibilityConverter_ConvertBack_ReturnsExpectedBool()
    {
        var converter = new BoolToVisibilityConverter();

        Assert.True((bool)converter.ConvertBack(Visibility.Visible, typeof(bool), string.Empty, CultureInfo.InvariantCulture));
        Assert.False((bool)converter.ConvertBack(Visibility.Collapsed, typeof(bool), string.Empty, CultureInfo.InvariantCulture));
        Assert.False((bool)converter.ConvertBack(Visibility.Visible, typeof(bool), "Inverse", CultureInfo.InvariantCulture));
        Assert.True((bool)converter.ConvertBack(Visibility.Collapsed, typeof(bool), "Inverse", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ViewModelBase_SetProperty_RaisesOnlyWhenValueChanges()
    {
        var vm = new TestViewModel();
        int fireCount = 0;
        string? lastPropertyName = null;
        vm.PropertyChanged += (_, args) =>
        {
            fireCount++;
            lastPropertyName = args.PropertyName;
        };

        vm.Name = "Alice";
        vm.Name = "Alice";
        vm.Name = "Bob";

        Assert.Equal(2, fireCount);
        Assert.Equal(nameof(TestViewModel.Name), lastPropertyName);
        Assert.Equal("Bob", vm.Name);
    }

    [Fact]
    public void PlayerViewModel_RoleChange_UpdatesDisplayAndRaisesNotification()
    {
        var vm = new PlayerViewModel();
        var notifiedProperties = new List<string?>();
        vm.PropertyChanged += (_, args) => notifiedProperties.Add(args.PropertyName);

        Assert.Equal(PlayerRole.Farmer, vm.Role);
        Assert.Equal("农民", vm.RoleDisplay);

        vm.Role = PlayerRole.Landlord;

        Assert.Equal(PlayerRole.Landlord, vm.Role);
        Assert.Equal("地主", vm.RoleDisplay);
        Assert.Contains(nameof(PlayerViewModel.Role), notifiedProperties);
        Assert.Contains(nameof(PlayerViewModel.RoleDisplay), notifiedProperties);
    }

    [Fact]
    public void PlayerViewModel_DefaultCollectionsStartEmpty()
    {
        var vm = new PlayerViewModel();

        Assert.Empty(vm.Hand);
        Assert.Empty(vm.PlayedCards);
        Assert.False(vm.IsCurrentTurn);
        Assert.False(vm.IsHuman);
        Assert.False(vm.IsThinking);
        Assert.Equal(0, vm.CardCount);
        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(string.Empty, vm.LastAction);
    }

    [Fact]
    public void CardViewModel_InitializesFromCardModel()
    {
        var card = new Card(Suit.Spades, Rank.Ace);
        var vm = new CardViewModel(card, true);

        Assert.Equal(card, vm.Model);
        Assert.Equal(card.DisplayName, vm.DisplayName);
        Assert.True(vm.IsFaceUp);
        Assert.True(vm.IsPlayable);
        Assert.Equal(CardAnimation.Idle, vm.AnimationState);
        Assert.Equal(0, vm.GatherOffset);
        Assert.False(vm.IsSelected);
    }

    [Fact]
    public void CardViewModel_IsSelected_RaisesSelectionStateChanged()
    {
        var card = new Card(Suit.Hearts, Rank.Five);
        var vm = new CardViewModel(card, true);
        CardViewModel? observedVm = null;
        bool observedValue = false;

        void Handler(CardViewModel sender, bool selected)
        {
            observedVm = sender;
            observedValue = selected;
        }

        CardViewModel.SelectionStateChanged += Handler;
        try
        {
            vm.IsSelected = true;

            Assert.Same(vm, observedVm);
            Assert.True(observedValue);
        }
        finally
        {
            CardViewModel.SelectionStateChanged -= Handler;
        }
    }

    [Fact]
    public void MainViewModel_InitialViewIsModeSelectViewModel()
    {
        var vm = new MainViewModel();

        Assert.IsType<ModeSelectViewModel>(vm.CurrentViewModel);
    }

    [Fact]
    public void MainViewModel_CurrentViewModelSetter_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        string? propertyName = null;
        vm.PropertyChanged += (_, args) => propertyName = args.PropertyName;

        vm.CurrentViewModel = new TestViewModel();

        Assert.Equal(nameof(MainViewModel.CurrentViewModel), propertyName);
        Assert.IsType<TestViewModel>(vm.CurrentViewModel);
    }

    [Theory]
    [InlineData("abc", 30)]
    [InlineData("5", 10)]
    [InlineData("999", 120)]
    [InlineData("45", 45)]
    public void NetworkSettingsViewModel_TurnTimeoutSeconds_ParsesAndClamps(string text, int expected)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"game_config_vm_{Guid.NewGuid():N}.json");
        SetConfigFilePath(tempPath);

        try
        {
            var vm = new NetworkSettingsViewModel(GameMode.Server, (_, _, _, _, _, _) => { }, () => { });

            vm.TurnTimeoutText = text;

            Assert.Equal(expected, vm.TurnTimeoutSeconds);
        }
        finally
        {
            ResetConfigFilePath();
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void GameConfig_Load_InvalidJson_ReturnsDefaults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"game_config_invalid_{Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{ this is not valid json");

        try
        {
            SetConfigFilePath(tempPath);

            var config = GameConfig.Load();

            Assert.Equal("ws://127.0.0.1:8000/ws", config.ServerUrl);
            Assert.Equal("127.0.0.1", config.P2pIpAddress);
            Assert.Equal(9000, config.P2pPort);
        }
        finally
        {
            ResetConfigFilePath();
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void SetConfigFilePath(string path)
    {
        var field = typeof(GameConfig).GetField("_configFilePath", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, path);
    }

    private static void ResetConfigFilePath()
    {
        var field = typeof(GameConfig).GetField("_configFilePath", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }

    private sealed class TestViewModel : ViewModelBase
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
    }
}
