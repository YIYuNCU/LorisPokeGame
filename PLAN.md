# 萝莉丝扑克 UI 优化计划

## 需求分析

| # | 需求 | 复杂度 |
|---|------|--------|
| 1 | TTS 接口：出牌时将牌内容转为语音播放，播放完毕后继续流程 | 中 |
| 2 | 卡牌整体放大 50%，随窗口缩放 | 高 |
| 3 | 提示文本字号放大 50%，随窗口缩放 | 低 |
| 4 | 预留 BGM 接口，支持指定 BGM 文件 | 低 |
| 5 | 跟牌时手中无能压过的牌，点击提示显示提示文本 | 低 |

---

## 优化 1：TTS 接口

### 设计思路

定义 `ITtsService` 接口，允许外部注入实现。GameViewModel 在出牌后调用 TTS 将牌型描述转为语音，等待播放完成后才推进游戏流程（AI 回合）。

### 需修改的文件

| 文件 | 修改内容 |
|------|---------|
| `LolitaPoker.Core/Audio/ITtsService.cs` | **新建** - TTS 服务接口 |
| `LolitaPoker.Core/Audio/NullTtsService.cs` | **新建** - 默认空实现（不播放，立即返回） |
| `LolitaPoker.Core/ViewModels/GameViewModel.cs` | 构造函数接收 `ITtsService`；`OnPlayerPlayed` 中出牌后调用 TTS；延迟 AI 回合直到 TTS 完成 |
| `LolitaPoker.Core/ViewModels/MainViewModel.cs` | 创建 GameViewModel 时注入 TTS 实现 |

### 接口定义

```csharp
public interface ITtsService
{
    /// <summary>将文本转为语音并播放，返回 Task 表示播放完成。</summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>是否可用。</summary>
    bool IsAvailable { get; }
}
```

### 出牌流程改动

当前流程（人机模式）：
```
玩家出牌 → SubmitPlay → PlayerPlayed 事件 → UI 显示出牌
         → CardsChanged → 刷新手牌
         → TurnChanged → 调度 AI（600-1200ms 延时）
```

改动后：
```
玩家出牌 → SubmitPlay → PlayerPlayed 事件 → UI 显示出牌 + 异步 TTS
         → CardsChanged → 刷新手牌
         → TurnChanged → TTS 完成后再开始 AI 倒计时
```

- `OnPlayerPlayed` 中异步调用 `_ttsService.SpeakAsync(combo.GetDescription())`
- TTS 与 AI 延时并行，AI 实际出牌在 TTS 完成后
- 使用 `_ttsCts` 在 Cleanup/新游戏时取消
- `NullTtsService` 默认 `IsAvailable = false`，不影响现有逻辑

---

## 优化 2：卡牌放大 50% 并随窗口缩放

### 设计思路

在 `GameTableControl.xaml` 内部用 `Viewbox(Stretch="Uniform")` 包裹固定尺寸的 Grid，所有尺寸 ×1.5，ViewBox 自动等比缩放。不影响其他页面。

### 尺寸变化

| 元素 | 当前 | 放大后 |
|------|------|--------|
| 手牌 | 70×100 | 105×150 |
| 玩家出牌 | 58×82 | 87×123 |
| AI 出牌 | 55×78 | 82.5×117 |
| 底牌 | 40×56 | 60×84 |
| 手牌负边距 | -35 | -52.5 |
| 玩家出牌负边距 | -25 | -37.5 |
| AI 出牌负边距 | -20 | -30 |
| 窗口默认 | 1000×680 | 1500×1020 |
| 窗口最小 | 800×560 | 1200×840 |

### 需修改的文件

| 文件 | 修改内容 |
|------|---------|
| `Views/DoudizhuMainWindow.xaml` | 窗口默认/最小尺寸增大 |
| `Views/GameTableControl.xaml` | 外层加 Viewbox；Grid 设固定 Width=1500 Height=1020；所有卡牌尺寸 ×1.5；负边距 ×1.5；ScrollViewer MaxHeight ×1.5；Grid 行高调整 |
| `Views/GameTableControl.xaml.cs` | `CardNegativeMargin` → -52.5；`CardWidth` → 105；洗牌动画 cardW/cardH → 127.5/180 |
| `Views/CardControl.xaml.cs` | `ScaleTransform CenterX/Y` → (52.5, 75)；选中上移量 → -22.5 |

### ViewBox 结构

```xml
<UserControl ...>
    <Viewbox Stretch="Uniform" UseLayoutRounding="True">
        <Grid x:Name="GameGrid" Width="1500" Height="1020" UseLayoutRounding="True">
            <!-- 所有内容使用放大后的固定尺寸 -->
        </Grid>
    </Viewbox>
</UserControl>
```

拖拽选牌 `FindCardAtPoint` 使用 `e.GetPosition(stackPanel)` 本地坐标，不受 ViewBox 影响。

---

## 优化 3：提示文本字号放大 50%

采用 ViewBox 方案后，字号在设计稿中直接 ×1.5，ViewBox 自动缩放：

| 元素 | 当前 | 放大后 |
|------|------|--------|
| StatusMessage（叫分提示） | 14 | 21 |
| StatusMessage（游戏结束） | 22 | 33 |
| LastAction（出牌动作） | 13 | 20 |
| 按钮 FontSize | 16 | 24 |
| 新游戏按钮 FontSize | 18 | 27 |

随优化 2 一起在 `GameTableControl.xaml` 中修改。

---

## 优化 4：预留 BGM 接口

### 需修改的文件

| 文件 | 修改内容 |
|------|---------|
| `LolitaPoker.Core/Audio/IBgmService.cs` | **新建** - BGM 服务接口 |
| `LolitaPoker.Core/Audio/NullBgmService.cs` | **新建** - 默认空实现 |

### 接口定义

```csharp
public interface IBgmService
{
    Task PlayAsync(string bgmFilePath, CancellationToken cancellationToken = default);
    void Stop();
    double Volume { get; set; }
    bool IsPlaying { get; }
}
```

GameViewModel 构造函数预留注入参数，当前不主动调用。

---

## 优化 5：跟牌无牌可出时提示

### 场景

玩家跟牌时手中没有能压过桌面的牌，点击"提示"显示提示文本。

### 当前问题

`GameViewModel.cs:669-672` 已有逻辑 `StatusMessage = "没有可以出的牌，请选择不出"`，但 StatusMessage TextBlock 仅在叫分面板和游戏结束面板内可见，出牌阶段看不到。

### 需修改的文件

| 文件 | 修改内容 |
|------|---------|
| `Views/GameTableControl.xaml` | 在手牌上方添加出牌阶段专用的 StatusMessage TextBlock |

### 具体方案

在 Grid.Row 4 的 StackPanel 内，手牌上方添加：

```xml
<TextBlock Text="{Binding StatusMessage}" Foreground="#FFD700" FontSize="21"
           HorizontalAlignment="Center" Margin="0,0,0,5"
           Visibility="{Binding IsPlayPanelVisible, Converter={StaticResource BoolToVis}}"/>
```

---

## 实施顺序

1. **优化 5**（无牌提示）— 最小改动，独立于其他优化
2. **优化 4**（BGM 接口）— 仅新建接口文件，不影响现有代码
3. **优化 1**（TTS 接口）— 新建接口 + 修改 GameViewModel 出牌流程
4. **优化 2 + 3**（卡牌放大 + 字号放大）— ViewBox 方案，改动面最大，最后实施

## 测试计划

- `dotnet build LolitaPoker.sln` 确保编译通过
- `dotnet test LolitaPoker.Tests/LolitaPoker.Tests.csproj` 确保现有测试不被破坏

## 潜在风险

| 风险 | 缓解措施 |
|------|---------|
| ViewBox 导致模糊 | `SnapsToDevicePixels` + `UseLayoutRounding` |
| TTS 阻塞 UI | async/await，不阻塞 UI 线程 |
| 窗口最小尺寸偏大 | ViewBox 自动缩小，可适当降低 MinWidth/MinHeight |
