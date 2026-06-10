# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 交互规范

**重要：** 
- 对自己的称呼从"我"改成"本喵"。
- 每次修改代码文件后，必须运行 `dotnet build LolitaPoker.sln` 检查编译是否通过，编译通过的话说"编译通过了喵"。
- 每次修改代码文件后，必须更新此文档的描述。
- 每次更新代码后，需运行或补充后运行与本次修改部分代码相关的测试文件以检查修改后的部分是否存在漏洞。
- 每次更新测试文件后，考虑原本的测试文件是否需要同步更新。

## 项目概述

**萝莉丝扑克** - 基于 WPF (.NET 8) 和 Flutter 的斗地主游戏，支持单机对战AI、VPet访客表联机和服务器模式。

## 技术栈

### WPF 版（原始版本）
- .NET 8.0 + WPF
- MVVM 架构模式（ContentControl + 隐式 DataTemplate 视图切换）
- 异步事件驱动的游戏状态机
- Python FastAPI 服务器（WebSocket 实时通信）

### Flutter 版（跨平台版本）
- Flutter 3.41+ / Dart 3.11+
- Riverpod 状态管理（StateNotifier 对应 WPF ViewModel）
- domain / data / presentation 三层架构
- 97 个单元测试覆盖 domain + data + presentation 层
- 与现有 Python FastAPI 服务器完全兼容（零服务器改动）

## 构建命令

```bash
# 构建 C# 项目
dotnet build LolitaPoker.sln

# 构建发布版本
dotnet build LolitaPoker.sln -c Release

# 运行应用
dotnet run --project LolitaPoker.App

# 清理构建产物
dotnet clean

# 运行 C# 测试
dotnet test LolitaPoker.Tests/LolitaPoker.Tests.csproj

# 运行 Python 测试
cd server && python -m pytest tests/ -v

# 启动 FastAPI 游戏服务器
cd server && pip install -r requirements.txt && python main.py

# === Flutter 版构建命令 ===
# 进入 Flutter 项目目录
cd ../lolita_poker

# 安装依赖
flutter pub get

# 运行应用（桌面/手机/Web）
flutter run -d windows
flutter run -d chrome

# 静态分析
flutter analyze

# 运行全部测试（97个）
flutter test
```

## 发布流程

```bash
# 发布框架依赖版本（需要用户安装 .NET 8 运行时）
dotnet publish LolitaPoker.App/LolitaPoker.App.csproj -c Release -o publish/framework-dependent --self-contained false -r win-x64 -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false

# 发布自包含版本（无需安装 .NET 运行时，体积较大）
dotnet publish LolitaPoker.App/LolitaPoker.App.csproj -c Release -o publish/self-contained --self-contained true -r win-x64 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 自包含单文件模式下，ExcludeFromSingleFile 配置可能不生效，需手动复制 pics 文件夹
cp -r LolitaPoker.App/bin/Release/net8.0-windows/pics publish/self-contained/

# 打包为 zip 文件
Compress-Archive -Path publish/framework-dependent/* -DestinationPath publish/LolitaPoker-framework-dependent.zip -Force
Compress-Archive -Path publish/self-contained/* -DestinationPath publish/LolitaPoker-self-contained.zip -Force
```

**发布产物：**
- `publish/framework-dependent/` - 框架依赖版本（需用户安装 .NET 8 运行时，体积小，约 150KB exe + pics）
- `publish/self-contained/` - 自包含单文件版本（无需 .NET 运行时，体积大，约 320MB exe + pics）
- `publish/LolitaPoker-framework-dependent.zip` - 框架依赖版打包
- `publish/LolitaPoker-self-contained.zip` - 自包含版打包

**注意事项：**
- 自包含单文件模式下，`ExcludeFromSingleFile="true"` 配置可能不会自动复制外部资源文件夹，需要手动复制 `pics/` 目录
- 框架依赖版本会自动包含 `pics/` 文件夹（通过 .csproj 中的 Content 配置）

## 游戏配置文件

配置文件 `game_config.json` 位于应用运行目录下（`bin/Debug/net8.0-windows/` 或 `bin/Release/net8.0-windows/`），应用启动时自动加载，设置变更时自动保存。

```json
{
  "server_url": "ws://127.0.0.1:8000/ws",
  "p2p_ip": "127.0.0.1",
  "p2p_port": 9000
}
```

- 配置文件不存在时使用上述默认值，无需手动创建
- 可用文本编辑器直接修改，下次启动时生效
- 也可通过 `GameConfig.LoadFrom(path)` / `config.SaveTo(path)` 编程读写

## 架构设计

### 五项目结构 + 服务器

- **LolitaPoker.App** - 启动项目，负责初始化图片资源和启动主窗口（Debug 为框架依赖，Release 为自包含单文件）
- **LolitaPoker.Core** - 核心逻辑库，包含游戏规则、AI、UI和网络模块
- **LolitaPoker.Plugin** - VPet 桌宠插件，通过访客表（Steam P2P）实现联机对战（继承 MainPlugin，注入 Tab + 独立游戏窗口）
- **LolitaPoker.Tests** - xUnit 单元测试项目（262个测试，覆盖规则引擎、卡牌工具、发牌器、GameManager、AI决策、提示系统、配置持久化、Card/CardCombo/PlayerInfo数据模型、CardSelectionCache选牌缓存、NetworkGameManager联机管理器、基础ViewModel/命令/转换器/MainViewModel测试、**压力测试**、**性能测试**、**内存管理测试**、**反作弊测试**）
- **server/** - Python FastAPI 游戏服务器（WebSocket 实时通信，含 210 个 pytest 测试，支持主从服务器架构）
- **lolita_poker/** - Flutter 跨平台版本（97个测试，domain/data/presentation 三层架构）

### Flutter 项目结构

```
lolita_poker/
├── lib/
│   ├── main.dart                    # 入口：图片缓存初始化 + ProviderScope + runApp
│   ├── domain/                      # 纯逻辑层（0 Flutter 依赖）
│   │   ├── enums/                   # 6 个枚举（Suit, Rank, CardComboType, GamePhase, PlayerRole, GameMode）
│   │   ├── models/                  # Card + CardHelper
│   │   ├── game/                    # RulesEngine + CardCombo + Deck + GameManager + PlayerInfo
│   │   └── ai/                      # AiPlayer 接口 + SimpleAiPlayer + CardComboFinder
│   ├── data/                        # 网络层
│   │   ├── models/                  # NetworkMessage
│   │   ├── network_adapter.dart     # abstract class NetworkAdapter
│   │   ├── websocket_adapter.dart   # WebSocketAdapter（web_socket_channel）
│   │   └── network_game_manager.dart
│   ├── presentation/                # UI 层
│   │   ├── theme/app_theme.dart     # 暗色主题（#1a1a2e 等颜色常量）
│   │   ├── providers/               # Riverpod 状态管理
│   │   │   ├── main_provider.dart   # 导航状态（AppPage 枚举切换）
│   │   │   ├── game_provider.dart   # GameNotifier（核心，对应 GameViewModel）
│   │   │   ├── player_state.dart    # PlayerState + CardState
│   │   │   └── card_selection_provider.dart  # 选牌状态
│   │   ├── screens/                 # 页面
│   │   │   ├── main_shell.dart      # Scaffold + 条件渲染
│   │   │   ├── mode_select_screen.dart      # 模式选择
│   │   │   ├── network_settings_screen.dart # 网络设置（含大厅二级页）
│   │   │   └── game_table_screen.dart       # 游戏桌面（5行布局）
│   │   └── widgets/                 # 可复用组件
│   │       ├── card_widget.dart     # 卡牌控件（正/反面 + 选中高亮）
│   │       ├── card_hand_row.dart   # 手牌横排（负边距重叠 + 点击选中）
│   │       ├── action_buttons.dart  # 出牌/不出/提示/叫分按钮
│   │       ├── player_info_bar.dart # 玩家信息栏（名字 + 思考动画）
│   │       ├── shuffle_animation.dart # 洗牌 Canvas 动画
│   │       └── vote_overlay.dart    # 断线投票叠加层
│   └── utils/
│       └── card_image_cache.dart    # 图片预加载缓存
├── assets/images/                   # 55 张卡牌 PNG（中文命名）
└── test/                            # 97 个单元测试
    ├── domain/                      # domain 层测试（68个）
    ├── data/                        # 网络层测试（12个）
    └── presentation/                # Provider 测试（17个）
```

### 核心模块分层

```
LolitaPoker.Core/
├── GameConfig.cs    # 游戏配置持久化管理（JSON文件，支持外部读写）
├── Models/          # 数据模型层（Card, CardHelper）
├── Enums/           # 枚举定义（Suit, Rank, CardComboType, GamePhase, PlayerRole, GameMode）
├── Game/            # 游戏逻辑层
│   ├── GameManager.cs     # 状态机管理器，驱动整个游戏流程
│   ├── RulesEngine.cs     # 规则引擎，牌型识别和合法性判定
│   ├── CardCombo.cs       # 牌型组合表示
│   ├── Deck.cs            # 牌组管理和发牌
│   └── PlayerInfo.cs      # 玩家信息实体
├── AI/              # AI模块
│   ├── SimpleAIPlayer.cs  # 带队友感知的规则AI
│   └── CardComboFinder.cs # 出牌组合搜索器
├── ViewModels/      # MVVM视图模型层
│   ├── MainViewModel.cs   # 应用壳视图模型，管理三级视图切换
│   ├── ModeSelectViewModel.cs # 模式选择逻辑（人机直接开始/P2P和服务器导航到设置页）
│   ├── NetworkSettingsViewModel.cs # 网络连接设置（P2P和服务器共用，含列表服务器浏览和服务器浏览器）
│   ├── GameViewModel.cs   # 游戏主视图模型，连接GameManager和UI
│   ├── PlayerViewModel.cs # 玩家UI状态
│   └── CardViewModel.cs   # 卡牌UI状态
├── Views/           # WPF视图层
│   ├── DoudizhuMainWindow.xaml  # 主窗口（ContentControl + DataTemplate 模式）
│   ├── ModeSelectView.xaml      # 模式选择界面（三个简洁卡片）
│   ├── NetworkSettingsView.xaml # 网络连接设置页面（P2P/服务器共用，服务器含大厅二级页面）
│   ├── GameTableControl.xaml    # 游戏桌面控件（含洗牌动画Canvas叠加层）
│   └── CardControl.xaml        # 卡牌控件（code-behind驱动聚拢/翻牌/铺开动画）
├── Network/         # 联机网络模块
│   ├── INetworkAdapter.cs       # 网络适配器接口
│   ├── NetworkGameManager.cs    # 网络游戏管理器（P2P消息路由+服务器消息转发）
│   ├── P2pNetworkAdapter.cs     # P2P局域网适配器（占位实现）
│   └── WebSocketNetworkAdapter.cs # WebSocket客户端适配器（连接FastAPI服务器，含大厅房间列表+PlayersText）
└── Assets/          # 资源管理
    ├── CardImageProvider.cs     # 图片缓存和加载
    └── FallbackCardRenderer.cs  # 备用卡牌渲染

LolitaPoker.Core/Audio/  # 音频服务接口
├── ITtsService.cs       # TTS 服务接口（SpeakAsync + IsAvailable）
├── NullTtsService.cs    # 默认空 TTS 实现（IsAvailable=false，立即返回）
├── IBgmService.cs       # BGM 服务接口（PlayAsync/Stop/Volume/IsPlaying）
├── NullBgmService.cs    # 默认空 BGM 实现（不播放任何音频）
├── ISoundEffectService.cs    # 音效服务接口（PlayAsync）
├── NullSoundEffectService.cs # 默认空音效实现（不播放任何音频）
└── SoundEffectMapper.cs      # 音效文件名映射（牌型→文件名，含 VictoryFileName/DefeatFileName/PassFileName 常量）

LolitaPoker.App/           # 启动项目
├── App.cs                 # 启动项目，初始化图片资源、TTS、BGM 并启动主窗口
├── WhiteVoiceTtsService.cs # 基于 MediaPlayer 的 TTS 实现，播放 WhiteVoice.mp3
└── BgmServiceImpl.cs      # 基于 MediaPlayer 的 BGM 实现，循环播放 Background.mp3

LolitaPoker.Plugin/        # VPet 桌宠插件项目
├── LolitaPokerPlugin.cs   # 插件入口，继承 MainPlugin，订阅 MutiPlayerHandle
├── VPetNetworkAdapter.cs  # 实现 INetworkAdapter，桥接 IMPWindows Steam P2P 消息
├── HostGameManager.cs     # 房主端游戏权威，封装本地 GameManager，广播操作结果
├── VpetMpTypes.cs         # 自定义 MPMessage Type 常量（-100~-150）
├── VpetPayloads.cs        # 消息载荷 POCO 类（带 [Line] 特性，LPSConvert 序列化）
├── LolitaPokerTab.xaml/cs # 注入到访客表 TabControl 的 UI（准备按钮+状态显示）
└── GameHostWindow.xaml/cs # 承载 GameTableControl 的独立游戏窗口

LolitaPoker.Tests/   # xUnit 单元测试项目
├── CardHelperTests.cs           # 卡牌工具类测试（CreateFullDeck, GetDisplayName, SortHand）
├── RulesEngineTests.cs          # 规则引擎测试（14种牌型识别 + CanBeat 比较逻辑）
├── DeckTests.cs                 # 发牌器测试（发牌数量、无重复、排序、缓存池）
├── GameManagerTests.cs          # 游戏状态机测试（出牌流程、叫分、炸弹翻倍、胜负判定）
├── CardComboFinderTests.cs      # AI出牌搜索+提示系统测试（自由出牌、跟牌、代价排序、炸弹/火箭候选、带牌选择、复杂牌型压牌）
├── SimpleAIPlayerTests.cs       # AI决策测试（叫分阈值、队友感知、自由出牌策略、一次性出完）
├── GameConfigTests.cs           # 配置持久化测试（往返序列化、容错、JSON字段名）
├── CardTests.cs                 # Card记录结构体测试（Strength、IsJoker、CompareTo、记录相等性）
├── CardComboTests.cs            # 牌型组合测试（GetDescription全14种、ToString、Invalid单例、IsValid）
├── PlayerInfoTests.cs           # 玩家信息实体测试（构造函数、Role默认值、Hand管理）
├── NetworkGameManagerTests.cs   # 联机游戏管理器测试（消息收发Stub、事件触发、自身消息过滤）
├── CardSelectionCacheTests.cs   # 选牌缓存测试（Toggle/Set/IsSelected、SyncFromHand、ApplyToHand、事件）
├── StressTests.cs               # 压力测试（CardComboFinder万次搜索、RulesEngine十万次分类、GameManager千局对战、DeckPool百线程并发、SimpleAI完整对局、NetworkGameManager万条消息）
├── PerformanceTests.cs          # 性能测试（FindAllPlayableCombos延迟基准、ClassifyPlay/CanBeat吞吐、完整AI对局性能、DeckPool/AI决策延迟）
├── MemoryManagementTests.cs     # 内存管理测试（分配压力Gen0监控、内存增长有界、防御性拷贝验证、CardViewModel静态事件泄漏检测、GameManager事件退订、DeckPool池上限、NetworkGameManager事件订阅）
└── AntiCheatTests.cs            # 反作弊测试（回合/牌型/跳过/叫分防护验证、重复牌伪装炸弹漏洞金丝雀、无手牌所有权漏洞金丝雀、叫分无上限漏洞金丝雀、CanBeat正确性、炸弹翻倍、清桌机制）

server/              # FastAPI 游戏服务器
├── main.py          # FastAPI 应用入口 + WebSocket 端点
├── card_models.py   # 扑克牌模型和规则引擎（与 C# RulesEngine 一致）
├── game_logic.py    # 服务端游戏状态机（ServerGameManager）
├── room_manager.py  # 房间管理（创建/加入/断线处理/大厅房间列表）
├── models.py        # Pydantic 消息模型（含 SetRoomVisibilityPayload）
├── server_config.py # 服务器参数配置（房间数上限 + 从服务器注册参数，持久化到 server_config.json）
├── master_config.py  # 列表服务器配置（端口、清理间隔、超时判定，持久化到 master_config.json）
├── master.py         # 列表服务器（主服务器）：从服务器注册/发现、服务器列表、启停管理、大厅 WebSocket 推送
├── slave_config.py   # 从服务器注册客户端：向列表服务器注册、周期心跳、接收启停指令
├── requirements.txt # 依赖：fastapi, uvicorn, websockets, pydantic, pytest, psutil
└── tests/           # pytest 单元测试（181个测试，覆盖规则引擎、游戏逻辑、房间管理、断线重连、服务器配置、服务器集成测试、**压力测试**（RoomManager/ServerGameManager）、**性能测试**（classify_play/can_beat/完整对局/deal_cards）、**内存管理测试**（GC压力/RoomManager字典增长/ServerGameManager引用泄漏）、**反作弊测试**（回合/牌型/跳过/叫分防护验证、重复牌拒绝验证、手牌所有权拒绝验证、叫分范围拒绝验证、牌值范围Pydantic验证、房间管理防护））
    ├── conftest.py              # 测试 fixtures 和辅助函数
    ├── test_card_models.py      # 卡牌模型和规则引擎测试
    ├── test_game_logic.py       # 服务端游戏状态机测试
    ├── test_reconnect_vote.py   # 断线重连和投票流程测试（令牌管理、状态恢复、弱网场景）
    ├── test_room_manager.py     # 房间管理器测试
    ├── test_server_config.py    # 服务器配置测试
    ├── test_models.py           # Pydantic 消息模型测试（含JoinRoomPayload、SetRoomVisibilityPayload）
    ├── test_main.py             # 服务器集成测试（HTTP端点、WebSocket房间管理、准备/游戏流程、错误处理）
    ├── test_stress_performance.py # 压力/性能/内存管理测试（RoomManager压力、ServerGameManager千局对战、classify_play/can_beat性能基准、GC压力监控、引用泄漏检测）
    ├── test_anti_cheat.py       # 反作弊测试（回合/牌型/跳过/叫分防护验证、重复牌炸弹漏洞、手牌所有权漏洞、叫分无上限漏洞、牌值范围漏洞、Pydantic模型验证、房间管理防护）
    ├── test_server_load.py      # 2H2G 服务器负载测试（15个用例：空闲基线、并发连接、完整对局、消息延迟/吞吐、资源清理、持续运行约束）
    └── test_master_slave.py     # 主从服务器测试（14个用例：列表服务器基础、从服务器注册、心跳更新、启停管理、大厅 WebSocket 推送、端到端浏览+连接）

LolitaPoker.Tests/   # xUnit 单元测试项目
├── GlobalUsings.cs           # 全局命名空间引用（System, System.Collections.Generic, System.Linq）
├── CardHelperTests.cs        # 卡牌工具类测试（创建牌组、显示名称、排序）
├── RulesEngineTests.cs       # 规则引擎测试（14种牌型识别和合法性判定）
├── DeckTests.cs              # 牌组管理测试（发牌、洗牌）
├── GameManagerTests.cs       # 游戏管理器测试（状态机、叫分、出牌、胜负判定）
├── CardComboFinderTests.cs   # 出牌组合搜索器测试（自由出牌、压牌、炸弹/火箭、代价排序、复杂牌型压牌）
├── SimpleAIPlayerTests.cs    # AI玩家测试（叫分阈值、队友感知、自由出牌偏好、空手牌处理、一次性出完）
├── GameConfigTests.cs        # 配置持久化测试（默认值、序列化往返、缺失文件处理、JSON字段名）
├── CardTests.cs              # Card记录结构体测试（Strength、IsJoker、CompareTo、记录相等性）
├── CardComboTests.cs         # 牌型组合测试（GetDescription全14种、ToString、Invalid单例、IsValid）
├── PlayerInfoTests.cs        # 玩家信息实体测试（构造函数、Role默认值、Hand管理）
├── NetworkGameManagerTests.cs # 联机游戏管理器测试（消息收发Stub、事件触发、自身消息过滤）
├── CardSelectionCacheTests.cs # 选牌缓存测试（Toggle/Set/IsSelected、SyncFromHand、ApplyToHand、事件）
├── StressTests.cs             # 压力测试（CardComboFinder万次搜索、RulesEngine十万次分类、GameManager千局对战、DeckPool百线程并发、SimpleAI完整对局、NetworkGameManager万条消息）
├── PerformanceTests.cs        # 性能测试（FindAllPlayableCombos延迟基准、ClassifyPlay/CanBeat吞吐、完整AI对局性能、DeckPool/AI决策延迟）
├── MemoryManagementTests.cs   # 内存管理测试（分配压力Gen0监控、内存增长有界、防御性拷贝验证、CardViewModel静态事件泄漏检测、GameManager事件退订、DeckPool池上限、NetworkGameManager事件订阅）
└── AntiCheatTests.cs          # 反作弊测试（回合/牌型/跳过/叫分防护验证、重复牌伪装炸弹漏洞金丝雀、无手牌所有权漏洞金丝雀、叫分无上限漏洞金丝雀、CanBeat正确性、炸弹翻倍、清桌机制）
```

### 关键类职责

**GameConfig** (GameConfig.cs)
- 游戏配置持久化管理器，使用 JSON 文件（`game_config.json`）存储配置
- 配置文件位于 `AppDomain.CurrentDomain.BaseDirectory` 下（`bin/Debug/net8.0-windows/` 或 `bin/Release/net8.0-windows/`），可被外部程序直接读写
- `Load()` / `Save()` 方法：自动加载/保存到默认路径
- `LoadFrom(path)` / `SaveTo(path)` 方法：供外部工具指定路径读写
- 持久化字段：`server_url`（服务器地址）、`p2p_ip`（P2P IP）、`p2p_port`（P2P 端口）、`master_url`（列表服务器地址）
- `NetworkSettingsViewModel` 构造时加载配置，任何设置变更时自动保存

**MainViewModel** (ViewModels/MainViewModel.cs)
- 应用壳视图模型，持有 `CurrentViewModel` 属性
- 通过 WPF 隐式 DataTemplate 自动切换 `ModeSelectView` ↔ `NetworkSettingsView` ↔ `GameTableControl`
- 三级导航：模式选择 → 网络设置（P2P/服务器各一页） → 游戏桌面
- **音频服务注入**：构造函数接收 `ITtsService?` 和 `IBgmService?`，传递给 GameViewModel
- 创建 `GameViewModel` 时注入 `GameMode`、`INetworkAdapter`、返回菜单回调和音频服务
- `NavigateToGame` 中从 `WebSocketNetworkAdapter.IsPublicRoom` 同步房间可见性到 `GameViewModel`
- `NavigateToSettings` 和 `NavigateToMenu` 中调用 `NetworkSettingsViewModel.Cleanup()` 断开大厅适配器

**ModeSelectViewModel** (ViewModels/ModeSelectViewModel.cs)
- 模式选择界面逻辑，提供三种模式入口
- 人机模式：直接导航到游戏桌面
- VPet联机：显示使用说明（需通过 VPet 访客表使用）
- 服务器模式：导航到对应的 `NetworkSettingsViewModel` 设置页面

**NetworkSettingsViewModel** (ViewModels/NetworkSettingsViewModel.cs)
- P2P 和服务器共用的连接设置视图模型
- 通过 `Mode` 属性区分 P2P/服务器，动态显示对应配置项
- P2P：IP 地址 + 端口 + 创建/加入房间
- 服务器：列表服务器地址 + 游戏服务器地址（直连）+ 三种连接方式卡片（创建房间 / 加入房间 / 查找公开房间）+ 浏览可用服务器
- **配置持久化**：构造时通过 `GameConfig.Load()` 加载保存的服务器地址和 P2P 设置；`ServerUrl`、`MasterUrl`、`IpAddress`、`PortText` 属性变更时自动通过 `SaveConfig()` 持久化到 `game_config.json`
- **三级页面**：`IsShowingMainPage`（主页面）↔ `IsShowingLobby`（大厅房间列表）↔ `IsShowingServerBrowser`（服务器浏览器）
- **主页面**：列表服务器地址输入 + 浏览可用服务器入口 + 游戏服务器地址输入（直连）+ 创建房间卡片（含 `IsPublicRoom` 复选框）+ 加入房间卡片（含房间号输入）+ 查找公开房间入口
- **服务器浏览器页面**：连接列表服务器获取可用游戏服务器列表，显示服务器名称、在线状态、对局容量（`ActiveGames/MaxConcurrentGames`）、可创建对局数；点击「选择」自动填入游戏服务器地址
- **大厅二级页面**：点击「查找公开房间」进入，显示公开房间列表 + 刷新按钮，点击「加入」直接加入
- **`_lobbyAdapter`**：独立的 WebSocket 连接用于浏览大厅，与游戏 adapter 分离；加入/创建房间时自动 dispose
- **`_serverBrowserAdapter`**：独立的 WebSocket 连接用于浏览列表服务器，通过 `ConnectToLobbyAsync` 连接 `/ws/lobby` 端点
- **`IsPublicRoom`**：创建房间时是否公开（默认 true），传递给 `WebSocketNetworkAdapter.IsPublicRoom`
- **`TurnTimeoutText`**：出牌超时秒数输入（默认 "30"，范围 10~120），传递给 `WebSocketNetworkAdapter.CreateRoomAsync`
- **`BackCommand`**：智能返回——服务器浏览器/大厅返回主页面，主页面返回模式选择
- **`Cleanup()`**：断开大厅 adapter 和服务器浏览器 adapter 连接，取消事件订阅

**GameManager** (Game/GameManager.cs)
- 游戏状态机，管理 Idle → Dealing → Bidding → Playing → GameOver 生命周期
- 支持快速模式（轮流地主）和传统叫分模式
- 通过事件通知UI层状态变化

**RulesEngine** (Game/RulesEngine.cs)
- 静态规则引擎，识别14种牌型（单张、对子、三条、三带一、顺子、飞机等）
- 实现牌型大小比较逻辑（火箭>炸弹>同类型比点数）
- **Python 移植版**在 `server/card_models.py`，逻辑完全一致

**SimpleAIPlayer** (AI/SimpleAIPlayer.cs)
- 基于规则的AI，带队友感知机制
- 农民AI会配合队友，优先攻击地主
- 通过EvaluateHandStrength评估手牌强度决定叫分

**CardComboFinder** (AI/CardComboFinder.cs)
- 枚举所有合法出牌组合
- 供AI决策和玩家提示系统使用
- 按"代价"排序，优先推荐小牌
- **带牌选择**：所有三带一/三带二/飞机/四带二的带牌均按 Rank 升序选取最小牌，避免浪费大牌

**GameViewModel** (ViewModels/GameViewModel.cs)
- 游戏主视图模型，连接GameManager、AI和UI
- 管理选牌状态、提示系统和AI延时
- 支持三种模式：`GameMode.HumanVsAI`、`GameMode.VPetLan`、`GameMode.Server`
- **音频服务注入**：构造函数接收 `ITtsService?`、`IBgmService?` 和 `ISoundEffectService?`（可选，默认 NullTtsService/NullBgmService/NullSoundEffectService）
- **TTS 播报**：出牌时异步调用 `_ttsService.SpeakAsync(combo.GetDescription())`，AI 出牌等待 TTS 完成后再执行
- **音效播放**：出牌时通过 `SoundEffectMapper.GetCardPlaySoundFileName` 播放牌型音效；不出/跳过时播放 `pass.mp3`；游戏结束时播放 `victory.mp3`/`defeat.mp3`
- **BGM 控制**：`_bgmService` 预留注入，Cleanup 时自动 Stop
- **服务器模式 TTS 延迟**：`HandleServerCardsPlayed` 启动 TTS，`DispatchServerMessage` 中 TurnChange 检测 TTS 未完成时暂存到 `_deferredTurnChangeType/Payload`，TTS 完成后 `FlushDeferredTurnChange()` 重新分发
- `RoomCode` 属性：服务器模式下存储房间号，绑定到顶部显示
- `IsPublicRoom` 属性：服务器模式下房间是否公开，绑定到顶部可见性标签
- `RoomVisibilityText` 计算属性：返回 "🟢 公开" 或 "🔴 私密"
- `ToggleRoomVisibility()` 方法：仅创建者 + 游戏未开始时可用
- `IsRoomCreator` 属性：是否为房间创建者，由 `WebSocketNetworkAdapter.IsRoomCreator` 同步
- `IsRoomVisibilityToggleEnabled` 计算属性：`IsRoomCreator && (Idle || GameOver)`
- **出牌倒计时**：服务器模式下出牌回合启动 `TurnTimeoutSeconds`（默认 30s）倒计时，超时由服务端处理（牌权在手出最小单牌，跟牌自动不出）；`TurnCountdownText`/`IsCountdownVisible` 驱动 UI 显示
- `IsBackToMenuVisible` 属性：由 `CurrentPhase` setter 自动更新，仅 Idle 阶段为 true
- **断线投票**：`IsVoteVisible`/`VoteMessage`/`VoteStatusMessage` 控制投票面板；`VoteEndCommand`/`VoteContinueCommand` 发送投票
- **重连处理**：`HandleServerReconnected` 恢复手牌、对手牌数、地主信息、当前回合等完整游戏状态
- **服务器模式**：客户端为薄客户端，所有操作发送给服务器，通过 `OnServerMessageReceived` 处理服务器消息更新UI
- **线程安全**：`OnServerMessageReceived` 使用 `Application.Current.Dispatcher.InvokeAsync()` 将所有服务端消息分发调度到UI线程，确保 `ObservableCollection` 修改安全；`OnNetworkPlayerJoined` / `OnNetworkPlayerLeft` 同样调度到UI线程
- **发牌动画**：通过 `ShuffleRequested` 事件通知 View 播放洗牌动画；阶段2结束后通过 `HandLayoutRequested` 事件通知 View 调用 `UpdateLayout()` 强制创建所有 CardControl 实例
- **资源清理**：`Cleanup()` 方法取消所有事件订阅、停止定时器、断开网络连接
- `SyncLobbyFromAdapter()` (internal)：从 `WebSocketNetworkAdapter.LobbyPlayers` 同步大厅玩家列表到UI

**WebSocketNetworkAdapter** (Network/WebSocketNetworkAdapter.cs)
- 使用 `ClientWebSocket` 连接 FastAPI 服务器
- 提供 `ConnectAsync()`、`CreateRoomAsync(isPublic, turnTimeout)`、`JoinRoomAsync()` 方法
- **`RequestRoomListAsync()`**：发送 `list_rooms` 请求，解析 `room_list` 响应填充 `RoomList`
- **`SetRoomVisibilityAsync(isPublic)`**：发送 `set_room_visibility` 消息切换房间可见性
- **`RoomList`** 属性：`List<RoomListEntry>`，存储公开房间列表（由 `ParseRoomList` 更新）
- **`OnRoomListUpdated`** 事件：收到 `room_list_updated` 推送时触发
- **`ConnectToLobbyAsync(masterUrl)`**：连接列表服务器 `/ws/lobby` 端点，用于服务器浏览器
- **`RequestServerListAsync()`**：发送 `list_servers` 请求，解析 `server_list` 响应填充 `ServerBrowserList`
- **`ServerBrowserList`** 属性：`List<ServerBrowserEntry>`，存储可用游戏服务器列表（含名称、地址、对局容量、在线状态）
- **`OnServerListUpdated`** 事件：收到 `server_list_updated` 推送时触发
- **`IsPublicRoom`** 属性：创建房间时的可见性设置，由 `NetworkSettingsViewModel` 写入，`MainViewModel` 读取同步给 `GameViewModel`
- **`SendReconnectVoteAsync(choice)`**：发送断线投票（"end"/"continue"）
- **断线重连**：`_reconnectPlayerId` 存储玩家ID；`ReceiveLoopAsync` 断线时自动调用 `TryReconnectAsync`（最多6次，间隔10秒）；通过 `?reconnect_player_id=xxx` 查询参数告知服务端
- **重连消息处理**：`reconnected` 更新本地状态并通知 GameViewModel；`vote_start`/`vote_update`/`reconnect_waiting`/`player_reconnected`/`game_ended` 转发给 GameViewModel
- 接收循环解析 JSON 消息，转换为 `NetworkMessage` 格式
- 服务器消息通过 `OnMessageReceived` 事件分发
- `LobbyPlayers` 属性：持久化大厅玩家列表（由 `ParseLobbyPlayers` 更新，`player_joined`、`player_ready`、`player_left` 消息均会触发同步）
- `CreateRoomAsync`：收到 `room_created` 后用创建者自身信息初始化 `LobbyPlayers`
- `JoinRoomAsync`：收到 `room_joined` 后调用 `ParseLobbyPlayers` 读取已有玩家

**NetworkGameManager** (Network/NetworkGameManager.cs)
- 封装 GameManager + INetworkAdapter
- P2P模式：处理 NewGame/Bid/Play/Pass 消息，驱动本地 GameManager
- 服务器模式：通过 `ServerMessageReceived` 事件转发未处理的消息给 GameViewModel

**DoudizhuMainWindow** (Views/DoudizhuMainWindow.xaml)
- 主窗口使用 ContentControl + 隐式 DataTemplate
- 窗口尺寸 1000×680，最小 800×560
- `ModeSelectViewModel` → `ModeSelectView`（模式选择）
- `NetworkSettingsViewModel` → `NetworkSettingsView`（连接设置）
- `GameViewModel` → `GameTableControl`（游戏桌面）

**CardControl** (Views/CardControl.xaml)
- 卡牌控件，支持选中高亮、正反面切换
- **自适应尺寸**：Width/Height 由 `GameTableControl.CardWidth`/`CardHeight` 静态属性控制，通过 `CardSizeChanged` 事件同步更新 `ScaleTransform` 中心
- **选中上移量**：由 `GameTableControl.SelectionLift` 动态计算（约为卡牌宽度的 22%）
- **动画系统**：所有动画由 code-behind 中的 Storyboard 驱动，响应 `CardViewModel.AnimationState` 变化
  - Gathering：TranslateX 收拢动画（CubicEase）
  - Revealing：ScaleX 翻牌（1→0→1）+ 背面/正面淡变；动画完成后通过 `BeginAnimation(null)` 清除 HoldEnd
  - Revealed：TranslateX 铺开（ElasticEase 弹性）；动画完成后重置 `AnimationState = Idle`
  - `UpdateFaceState` 在 Revealing 动画期间跳过直接切换，避免脸在翻牌前就显示
- 使用 `TransformGroup`（ScaleTransform + TranslateTransform）实现复合变换

**CardViewModel** (ViewModels/CardViewModel.cs)
- `CardAnimation` 枚举：Idle / Gathering / Revealing / Revealed
- `AnimationState` 属性：驱动 CardControl 的动画状态机
- `GatherOffset` 属性：聚拢时的 TranslateX 偏移量
- 静态事件 `SelectionStateChanged`：跨 ViewModel 同步选中状态

**GameTableControl** (Views/GameTableControl.xaml)
- 游戏桌面控件，4行 Grid 布局：菜单栏 / AI信息+底牌 / 出牌区域（三人叠加） / 手牌+操作
- **卡牌自适应**：手牌尺寸由 `RecalcCardSize` 根据窗口宽度阻尼缩放（基准 89px@1000w，阻尼 0.5），`CardWidth`/`CardHeight`/`CardNegativeMargin`/`SelectionLift` 为静态属性，CardControl 通过 `CardSizeChanged` 事件响应
- **按钮在手牌上方**：操作按钮行（出牌/不出/提示）位于手牌展示区上方，手牌区顶部留有选牌上移空间
- **菜单栏**（Row 0）：左侧「← 返回主页」按钮（游戏开始后禁用变灰），右侧服务器模式显示房间号 + **可见性标签**（点击切换公开/私密）
- **洗牌动画**：通过 `ShuffleCanvas` 叠加层展示牌背交错洗牌效果（卡牌 89×126），完成后淡出并触发发牌
- **断线投票面板**：全屏半透明叠加层，显示断线玩家名、「结束对局」和「等待重连」按钮、投票状态
- **出牌阶段提示**：手牌上方 StatusMessage TextBlock，仅出牌阶段可见，用于显示"没有可以出的牌"等提示

### 服务器模块

**ServerGameManager** (server/game_logic.py)
- 服务端游戏状态机，与 C# GameManager 逻辑一致
- 不使用事件，返回消息列表（GameAction.messages）
- 管理发牌、叫地主、出牌校验、胜负判定
- **`get_reconnect_state(seat)`**：返回重连玩家所需的游戏状态（手牌、对手牌数、当前回合、地主信息等）

**RoomManager** (server/room_manager.py)
- 房间管理：6位大写字母数字房间码
- 玩家座位分配（0-2）
- **`Room.is_public`** 字段：房间是否公开（默认 true），公开且未开始且未满的房间出现在大厅列表中
- **`Room.reconnect_tokens`**：断线玩家重连令牌（player_id → ReconnectToken）
- **`Room.vote_state`**：断线投票状态（player_id → "end"/"continue"）
- **`Room.game_paused`**：游戏是否因断线投票而暂停
- **`Room.creator_id`**：房间创建者 player_id（仅创建者可修改可见性）
- **`Room.turn_timeout`**：出牌超时秒数（默认 30），牌权在手超时自动出最小单牌，跟牌超时自动不出
- **`Room.last_activity`**：最后一次人数/状态变化时间，用于超时清理
- **`cleanup_stale_rooms()`**：清理超时空房间（30分钟）和单人等待超时房间（5分钟）
- **`get_public_rooms()`**：返回公开房间列表（含房间码、人数、玩家名）
- **`set_room_visibility(player_id, is_public)`**：设置房间可见性（仅创建者+游戏未开始时允许）
- **`store_reconnect_token(player_id)`**：游戏中断线时存储重连令牌，移除玩家连接但保留座位
- **`try_reconnect(player_id, websocket)`**：尝试用原玩家ID重连，恢复座位和连接
- **`has_reconnect_token(player_id)`**：检查是否有待恢复的重连令牌
- 断线处理和超时房间清理（断线广播含更新后的完整玩家列表）

**消息协议**（JSON over WebSocket）
- 客户端→服务端：`create_room`（含 `is_public`、`turn_timeout`）、`join_room`、`ready`、`cancel_ready`、`list_rooms`、`set_room_visibility`、`reconnect_vote`、`dealing_complete`、`bid`、`play`、`pass`
- 服务端→客户端：`room_created`、`room_joined`、`player_joined`、`player_left`、`player_ready`、`room_list`、`room_list_updated`、`visibility_changed`、`vote_start`、`vote_update`、`reconnect_waiting`、`player_reconnected`、`game_ended`、`reconnected`、`game_start`、`bid_update`、`landlord_assigned`、`turn_change`、`cards_played`、`player_passed`、`game_over`、`error`
- **大厅系统**：`list_rooms` 请求当前公开房间列表，`room_list` 返回结果；`room_list_updated` 推送给不在房间内的客户端；`set_room_visibility` 切换房间公开/私密
- **断线重连**：`_reconnect_player_id` 查询参数重连；服务端恢复座位并发送 `reconnected`；游戏已结束时发送 `game_ended`
- **断线投票**：`vote_start`→`vote_update`→`reconnect_waiting`；30秒无人投票自动结束；两人均选"继续"则等60秒重连超时
- **发牌→叫分同步**：服务器发送 `game_start` 后暂存 `turn_change`，等所有客户端发送 `dealing_complete` 确认动画完成后再下发叫分指令
- **HTTP 端点**：`GET /`（服务器信息+统计）、`GET /health`（健康检查）、`GET /stats`（对局统计）

**ServerConfig** (server/server_config.py)
- 服务器参数配置管理器，持久化到 `server_config.json`
- `max_concurrent_games`：最大房间数（默认 10），创建房间时检查上限，修改后立即生效并保存
- `master_url`：列表服务器地址（空字符串=独立模式，非空=从服务器模式）
- `slave_name`：从服务器名称（注册时显示）
- `slave_host`：从服务器外部可达地址（注册时告知客户端）
- `can_create_room(current_room_count)`：检查当前房间数是否未达上限
- `get_status(room_count, connected_players)`：返回当前状态（上限+房间数+在线人数）
- `set_max_concurrent_games(value)`：运行时修改上限并持久化
- **优先级**：环境变量 `MASTER_URL`/`SLAVE_NAME`/`SLAVE_HOST`/`SERVER_PORT` 覆盖配置文件

**MasterConfig** (server/master_config.py)
- 列表服务器配置管理器，持久化到 `master_config.json`
- `port`：监听端口（默认 8000），环境变量 `MASTER_PORT` 覆盖
- `cleanup_interval`：清理超时从服务器的间隔秒数（默认 30）
- `dead_timeout`：从服务器心跳超时判定秒数（默认 60）
- `api_key`：管理接口密钥（空=不验证），配置后 enable/disable/remove 端点需要 `X-API-Key` 请求头

### 主从服务器架构（模式1：直连模式）

**列表服务器** (`master.py`)
- 独立运行的轻量级服务（默认端口 8000）
- 管理从服务器注册表（`ServerRegistry`）
- WebSocket 端点 `/ws/slave`：从服务器注册 + 心跳
- WebSocket 端点 `/ws/lobby`：客户端浏览服务器列表 + 实时推送
- HTTP 端点 `/api/servers`：查询服务器列表
- HTTP 端点 `/api/servers/{id}/enable|disable`：启停从服务器
- 30 秒心跳超时自动清理失联从服务器

**从服务器注册** (`slave_config.py`)
- `SlaveConfig`：从服务器配置（名称、地址、端口、统计）
- `SlaveRegistration`：注册客户端，启动后自动连接列表服务器
- 10 秒心跳周期，发送 `active_games`（房间数）/`connected_players`/`room_count`
- 接收 `set_enabled` 指令，支持远程启停
- 断线自动重连（5 秒间隔）

**启动方式**
```bash
# 启动列表服务器（端口由 master_config.json 或 MASTER_PORT 环境变量决定）
python master.py

# 启动从服务器（独立模式，不注册）
python main.py

# 启动从服务器（通过配置文件注册）
# 编辑 server_config.json，设置 "master_url": "ws://127.0.0.1:8000/ws/slave"
python main.py

# 启动从服务器（通过环境变量注册，覆盖配置文件）
MASTER_URL=ws://127.0.0.1:8000/ws/slave python main.py
SLAVE_NAME=服务器A SLAVE_HOST=192.168.1.100 MASTER_URL=ws://192.168.1.1:8000/ws/slave python main.py
```

**配置文件**
```json
// server_config.json（从服务器）
{
  "max_concurrent_games": 10,
  "master_url": "ws://127.0.0.1:8000/ws/slave",
  "slave_name": "萝莉丝扑克服务器",
  "slave_host": "127.0.0.1"
}

// master_config.json（列表服务器）
{
  "port": 8000,
  "cleanup_interval": 30,
  "dead_timeout": 60
}
```

## 游戏流程

1. **模式选择** - 启动后显示模式选择界面（人机/P2P/服务器）
2. **洗牌** - 牌背在牌桌中央交错叠放，展示洗牌动画（约1.2秒）
3. **发牌** - 51张牌逐张轮转发给三位玩家（全部背面），间隔60ms
4. **理牌** - 玩家的17张牌聚拢（300ms），然后从左到右依次翻成正面（每张20ms间隔），最后铺开（弹性300ms）
5. **发牌确认**（服务器模式）- 所有客户端完成动画后发送 `dealing_complete`，服务器确认后才下发叫分指令
6. **叫地主** - 轮流叫分(1-3分)或快速模式(轮流当地主)；底牌在叫分阶段倒扣
6. **出牌** - 地主先出，农民配合压制地主
7. **胜负判定** - 先出完手牌的一方获胜
8. **返回菜单** - 游戏结束后可返回模式选择界面

## 数据流

```
人机模式:
  用户操作 → GameViewModel → GameManager → RulesEngine
                  ↓
          AI (SimpleAIPlayer) → CardComboFinder
                  ↓
          UI更新 ← 事件通知 ← GameManager

服务器模式:
  用户操作 → GameViewModel → WebSocketNetworkAdapter → FastAPI服务器
                  ↑                                              ↓
          UI更新 ← 服务器消息 ← WebSocketNetworkAdapter ← 游戏逻辑校验
```

## 图片资源

卡牌图片存放在 `pics/` 目录，通过CardImageProvider缓存加载，命名规则如 "方片A.png"、"大王.png"。
