# 音效清单

音效会按文件名从 `Audio/Sfx` 加载。缺失文件会静默跳过，因此可以逐步补充音频素材，不会影响游戏运行。

## 必需胜负音效

- `victory.mp3` - 本地玩家在正常完成的对局中获胜时播放。
- `defeat.mp3` - 本地玩家在正常完成的对局中失败时播放。

主动退出、断线结束和异常强制结束不会播放胜负音效。

## 出牌音效

单张音效：

- `single_3.mp3`
- `single_4.mp3`
- `single_5.mp3`
- `single_6.mp3`
- `single_7.mp3`
- `single_8.mp3`
- `single_9.mp3`
- `single_10.mp3`
- `single_j.mp3`
- `single_q.mp3`
- `single_k.mp3`
- `single_a.mp3`
- `single_2.mp3`
- `single_small_joker.mp3`
- `single_big_joker.mp3`

对子音效：

- `pair_3.mp3`
- `pair_4.mp3`
- `pair_5.mp3`
- `pair_6.mp3`
- `pair_7.mp3`
- `pair_8.mp3`
- `pair_9.mp3`
- `pair_10.mp3`
- `pair_j.mp3`
- `pair_q.mp3`
- `pair_k.mp3`
- `pair_a.mp3`
- `pair_2.mp3`

通用牌型音效：

- `triple.mp3`
- `triple_plus_one.mp3`
- `triple_plus_pair.mp3`
- `straight.mp3`
- `consecutive_pairs.mp3`
- `airplane.mp3`
- `airplane_with_singles.mp3`
- `airplane_with_pairs.mp3`
- `four_plus_two.mp3`
- `four_plus_two_pairs.mp3`
- `bomb.mp3`
- `rocket.mp3`

跳过音效：

- `pass.mp3` - 任何玩家选择”不出/跳过”时播放（本地、P2P 和服务器模式均生效）。

## 触发规则

- 本地和 P2P 的有效出牌会使用 `RulesEngine.ClassifyPlay(cards)` 识别牌型，并播放一次对应文件。
- 服务器 `cards_played` 消息使用相同的牌型识别和音效映射。
- 无效出牌不会播放出牌音效。
- “不出/跳过”事件播放 `pass.mp3`（本地 `OnPlayerPlayed` combo==null 和服务器 `PlayerPassed` 消息均触发）。
- 胜负音效只会在已知胜者的正常游戏结束消息中播放。
