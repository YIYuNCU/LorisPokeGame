# 列表服务器（主服务器）部署指南

## 概述

列表服务器是轻量级服务，负责管理游戏服务器的注册/发现、提供服务器列表、启停管理。
不涉及任何游戏逻辑，资源消耗极低。

```
客户端 ──WebSocket──→ 列表服务器 ──WebSocket──→ 游戏服务器A（注册/心跳）
(浏览服务器列表)         ↕                      游戏服务器B
                   管理员 HTTP API              游戏服务器C
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

编辑 `master_config.json`：

```json
{
  "port": 8000,
  "cleanup_interval": 30,
  "dead_timeout": 60,
  "api_key": ""
}
```

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `port` | 监听端口 | 8000 |
| `cleanup_interval` | 清理超时从服务器的间隔（秒） | 30 |
| `dead_timeout` | 从服务器心跳超时判定（秒） | 60 |
| `api_key` | 管理接口密钥（空=不验证） | `""` |

环境变量 `MASTER_PORT` 可覆盖 `port`。

## 启动

```bash
python master.py
```

## 验证

```bash
# 健康检查
curl http://127.0.0.1:8000/health
# → {"status": "ok"}

# 服务器信息
curl http://127.0.0.1:8000/
# → {"message": "萝莉丝扑克 - 列表服务器", "version": "1.0.0", "server_count": 0}

# 查看已注册的从服务器
curl http://127.0.0.1:8000/api/servers
# → {"servers": []}
```

## API 端点

### 公开端点（无需密钥）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/` | 服务器信息 |
| GET | `/health` | 健康检查 |
| GET | `/api/servers` | 从服务器列表 |

### 管理端点（配置了 `api_key` 后需要 `X-API-Key` 头）

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/servers/{id}/enable` | 启用从服务器 |
| POST | `/api/servers/{id}/disable` | 禁用从服务器 |
| DELETE | `/api/servers/{id}` | 移除从服务器 |

### WebSocket 端点

| 路径 | 说明 |
|------|------|
| `/ws/slave` | 从服务器注册 + 心跳（从服务器连接） |
| `/ws/lobby` | 客户端浏览服务器列表 + 实时推送 |

## 管理接口认证

编辑 `master_config.json`，设置 `api_key`：

```json
{
  "api_key": "your-secret-key-here"
}
```

重启后，管理端点需要在请求头中携带密钥：

```bash
# 无密钥 → 401
curl -X POST http://127.0.0.1:8000/api/servers/abc123/disable
# → {"error": "需要有效的管理密钥（X-API-Key 头）"}

# 有密钥 → 200
curl -X POST http://127.0.0.1:8000/api/servers/abc123/disable \
     -H "X-API-Key: your-secret-key-here"
# → {"status": "ok", "server_id": "abc123", "enabled": false}
```

`api_key` 为空时（默认），所有管理端点不需要密钥，适合本地开发。

## 协议

### 从服务器注册流程

```
从服务器                              列表服务器
   │                                     │
   │──── WebSocket /ws/slave ───────────→│
   │──── register {server_name, ...} ───→│
   │←─── registered {server_id} ─────────│
   │                                     │
   │     （每 10 秒）                     │
   │──── heartbeat {active_games, ...} ─→│
   │                                     │
```

### 客户端浏览流程

```
客户端                                列表服务器
   │                                     │
   │──── WebSocket /ws/lobby ────────────→│
   │←─── server_list {servers: [...]} ───│
   │                                     │
   │     （从服务器变化时自动推送）         │
   │←─── server_list_updated ───────────│
```

## 后台运行

### Linux (systemd)

```ini
# /etc/systemd/system/lolita-master.service
[Unit]
Description=LolitaPoker Listing Server
After=network.target

[Service]
WorkingDirectory=/opt/lolita-poker/server
ExecStart=/usr/bin/python3 master.py
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable lolita-master
sudo systemctl start lolita-master
sudo journalctl -u lolita-master -f
```

### Windows (NSSM)

```powershell
nssm install LolitaMaster "C:\Python312\python.exe" "master.py"
nssm set LolitaMaster AppDirectory "C:\lolita-poker\server"
nssm start LolitaMaster
```

## 防火墙

开放 TCP 端口（默认 8000）。

## 资源消耗

列表服务器本身不运行游戏逻辑，资源消耗极低：
- 内存：~30MB（Python + FastAPI 空载）
- CPU：接近 0（仅心跳处理）
- 带宽：每从服务器每 10 秒一次心跳（~200 bytes）
