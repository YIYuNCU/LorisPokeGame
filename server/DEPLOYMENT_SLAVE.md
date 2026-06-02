# 游戏服务器（从服务器）部署指南

## 概述

游戏服务器处理全部游戏逻辑和客户端 WebSocket 通信。
可以独立运行，也可以注册到列表服务器供客户端发现。

```
模式一（独立）：
  客户端 ──WebSocket──→ 游戏服务器（端口 8050）

模式二（主从）：
  客户端 → 列表服务器(浏览) → 游戏服务器(直连)
              (端口 8000)         (端口 8050+)
```

## 环境要求

| 组件 | 最低版本 |
|------|---------|
| Python | 3.10+ |

## 安装

```bash
cd server
pip install -r requirements.txt
```

## 配置

编辑 `server_config.json`：

```json
{
  "max_concurrent_games": 10,
  "master_url": "",
  "slave_name": "萝莉丝扑克服务器",
  "slave_host": "127.0.0.1",
  "slave_port": 0
}
```

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `max_concurrent_games` | 最大并发对局数 | 10 |
| `master_url` | 列表服务器地址（空=独立模式） | `""` |
| `slave_name` | 注册到列表服务器时显示的名称 | `"萝莉丝扑克服务器"` |
| `slave_host` | 客户端连接此服务器时使用的地址 | `"127.0.0.1"` |
| `slave_port` | 注册到列表服务器时通告的端口（0=使用监听端口） | `0` |

环境变量覆盖：`SERVER_PORT`、`MASTER_URL`、`SLAVE_NAME`、`SLAVE_HOST`、`SLAVE_PORT`

**优先级：环境变量 > 配置文件 > 代码默认值**

## 模式一：独立运行

最简部署，不依赖列表服务器。

```bash
python main.py
```

默认监听 `0.0.0.0:8050`。

客户端直连：`ws://你的IP:8050/ws`

### 验证

```bash
curl http://127.0.0.1:8050/health
# → {"status": "ok"}

curl http://127.0.0.1:8050/stats
# → {"max_concurrent_games": 10, "active_games": 0}
```

## 模式二：注册到列表服务器

### 步骤 1：确保列表服务器已运行

参见 `DEPLOYMENT_MASTER.md`。

### 步骤 2：配置

编辑 `server_config.json`：

```json
{
  "max_concurrent_games": 10,
  "master_url": "ws://列表服务器IP:8000/ws/slave",
  "slave_name": "服务器A",
  "slave_host": "本机公网IP或域名"
}
```

> **重要**：`slave_host` 必须是客户端能直接访问的地址（公网 IP 或域名），不能是 `127.0.0.1`。

### 步骤 3：启动

```bash
python main.py
```

或用环境变量覆盖：

```bash
MASTER_URL=ws://192.168.1.1:8000/ws/slave \
SLAVE_NAME=服务器A \
SLAVE_HOST=192.168.1.100 \
SERVER_PORT=8051 \
python main.py
```

### 步骤 4：验证注册

```bash
# 在列表服务器上查看
curl http://列表服务器IP:8000/api/servers
# → {"servers": [{"server_name": "服务器A", "active_games": 0, ...}]}
```

日志中应显示：
```
从服务器模式: 注册到 ws://192.168.1.1:8000/ws/slave
```

## 多实例部署

同一台机器运行多个游戏服务器（不同端口）：

```bash
# 服务器 A
SERVER_PORT=8051 SLAVE_NAME=服务器A python main.py &

# 服务器 B
SERVER_PORT=8052 SLAVE_NAME=服务器B python main.py &

# 服务器 C
SERVER_PORT=8053 SLAVE_NAME=服务器C python main.py &
```

每台机器一个 `server_config.json`，或用环境变量区分。

## 协议

### 客户端连接流程

```
客户端                              游戏服务器
   │                                    │
   │──── WebSocket /ws ────────────────→│
   │                                    │
   │──── create_room {player_name} ────→│
   │←─── room_created {room_code} ─────│
   │                                    │
   │     （其他玩家加入）                 │
   │←─── player_joined ────────────────│
   │                                    │
   │     （3人准备好 → 游戏开始）         │
   │←─── game_start {hand} ────────────│
   │──── dealing_complete ─────────────→│
   │←─── turn_change {current_player} ─│
   │                                    │
   │     （叫分、出牌循环）               │
   │──── bid {amount} ─────────────────→│
   │──── play {cards} ─────────────────→│
   │──── pass {} ──────────────────────→│
```

### 消息类型

#### 客户端 → 服务器

| 类型 | 负载 | 说明 |
|------|------|------|
| `create_room` | `{player_name, is_public}` | 创建房间 |
| `join_room` | `{room_code, player_name}` | 加入房间 |
| `ready` | `{}` | 准备 |
| `cancel_ready` | `{}` | 取消准备 |
| `dealing_complete` | `{}` | 发牌动画完成 |
| `bid` | `{amount: 0-3}` | 叫分（0=不叫） |
| `play` | `{cards: [{suit, rank}]}` | 出牌 |
| `pass` | `{}` | 不出 |
| `list_rooms` | `{}` | 查询公开房间 |
| `set_room_visibility` | `{is_public}` | 切换房间可见性 |
| `reconnect_vote` | `{choice: "end"/"continue"}` | 断线投票 |

#### 服务器 → 客户端

| 类型 | 说明 |
|------|------|
| `room_created` | 房间创建成功 |
| `room_joined` | 加入房间成功 |
| `player_joined` | 玩家加入 |
| `player_left` | 玩家离开 |
| `player_ready` | 准备状态更新 |
| `game_start` | 游戏开始（含手牌） |
| `turn_change` | 回合切换 |
| `bid_update` | 叫分更新 |
| `landlord_assigned` | 地主分配 |
| `cards_played` | 出牌 |
| `player_passed` | 跳过 |
| `game_over` | 游戏结束 |
| `error` | 错误消息 |

## 断线重连

游戏中断线后：
1. 服务器保存重连令牌（60 秒有效）
2. 剩余玩家收到投票：结束对局 / 等待重连
3. 断线玩家通过 `?reconnect_player_id=xxx` 重连参数恢复

## 资源参考（2H2G VPS 实测）

| 指标 | 1 局 | 5 局并发 | 10 局并发 |
|------|------|---------|----------|
| 内存 (RSS) | 49 MB | 52 MB | 55 MB |
| CPU | 7% | 23% | 31% |
| 每回合流量 | 60 B | 562 B | 869 B |

50Mbps 带宽可支撑 ~72 局并发。**CPU 是瓶颈**，推荐 2H2G 运行 5-8 局并发。

## 后台运行

### Linux (systemd)

```ini
# /etc/systemd/system/lolita-poker.service
[Unit]
Description=LolitaPoker Game Server
After=network.target

[Service]
WorkingDirectory=/opt/lolita-poker/server
ExecStart=/usr/bin/python3 main.py
Restart=always
RestartSec=5
Environment=SERVER_PORT=8050

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable lolita-poker
sudo systemctl start lolita-poker
sudo journalctl -u lolita-poker -f
```

### Windows (NSSM)

```powershell
nssm install LolitaPoker "C:\Python312\python.exe" "main.py"
nssm set LolitaPoker AppDirectory "C:\lolita-poker\server"
nssm start LolitaPoker
```

## 防火墙

开放游戏服务器端口（默认 8050），协议 TCP。

## 常见问题

**Q: 如何从独立模式切换到主从模式？**

编辑 `server_config.json`，设置 `master_url`，重启即可。

**Q: `slave_host` 应该填什么？**

填客户端能直接访问的地址。局域网填内网 IP（如 `192.168.1.100`），公网填公网 IP 或域名。不能填 `127.0.0.1`。

**Q: `slave_port` 什么时候需要设置？**

当外部可访问端口与内部监听端口不同时。例如服务器监听 8050，但路由器端口转发为 9050，或 Nginx 反代到其他端口：

```json
{
  "slave_port": 9050
}
```

这样客户端从列表服务器获取的连接地址是 `ws://你的IP:9050/ws`，而非 `ws://你的IP:8050/ws`。默认值 `0` 表示使用监听端口。

**Q: 列表服务器挂了怎么办？**

游戏服务器独立运行不受影响，已连接的玩家可以继续游戏。只是新玩家无法通过列表服务器发现。
