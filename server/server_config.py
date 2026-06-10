"""
server_config.py - 服务器参数配置
管理房间上限、从服务器注册等可配置参数，持久化到 JSON 文件
"""

import json
import logging
from pathlib import Path

logger = logging.getLogger(__name__)

CONFIG_FILE = Path(__file__).parent / "server_config.json"

# 默认配置
DEFAULT_CONFIG = {
    "max_concurrent_games": 10,
    # 从服务器注册参数（为空或不存在时为独立模式）
    "master_url": "",
    "slave_name": "萝莉丝扑克服务器",
    "slave_host": "127.0.0.1",
    "slave_port": 0,  # 注册到列表服务器时通告的端口（0=使用监听端口）
    "port": 8050  # 监听端口（如果注册到列表服务器，默认使用此端口）
}


class ServerConfig:
    """服务器配置管理器"""

    def __init__(self):
        self.max_concurrent_games: int = DEFAULT_CONFIG["max_concurrent_games"]
        self.master_url: str = DEFAULT_CONFIG["master_url"]
        self.slave_name: str = DEFAULT_CONFIG["slave_name"]
        self.slave_host: str = DEFAULT_CONFIG["slave_host"]
        self.slave_port: int = DEFAULT_CONFIG["slave_port"]
        self.port: int = DEFAULT_CONFIG["slave_port"]  # 监听端口（如果注册到列表服务器，默认使用此端口）
        self._load()

    def _load(self):
        """从文件加载配置"""
        try:
            if CONFIG_FILE.exists():
                data = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
                self.max_concurrent_games = data.get("max_concurrent_games", DEFAULT_CONFIG["max_concurrent_games"])
                self.master_url = data.get("master_url", DEFAULT_CONFIG["master_url"])
                self.slave_name = data.get("slave_name", DEFAULT_CONFIG["slave_name"])
                self.slave_host = data.get("slave_host", DEFAULT_CONFIG["slave_host"])
                self.slave_port = data.get("slave_port", DEFAULT_CONFIG["slave_port"])
                logger.info(f"已加载配置: 最大房间数 {self.max_concurrent_games}, master_url='{self.master_url}'")
            else:
                self._save()
                logger.info(f"已创建默认配置文件: {CONFIG_FILE}")
        except Exception as e:
            logger.warning(f"加载配置失败，使用默认值: {e}")
            self.max_concurrent_games = DEFAULT_CONFIG["max_concurrent_games"]
            self.master_url = DEFAULT_CONFIG["master_url"]
            self.slave_name = DEFAULT_CONFIG["slave_name"]
            self.slave_host = DEFAULT_CONFIG["slave_host"]
            self.slave_port = DEFAULT_CONFIG["slave_port"]

    def _save(self):
        """保存配置到文件（仅持久化静态参数，不持久化运行时状态）"""
        try:
            data = {
                "max_concurrent_games": self.max_concurrent_games,
                "master_url": self.master_url,
                "slave_name": self.slave_name,
                "slave_host": self.slave_host,
                "slave_port": self.slave_port,
            }
            CONFIG_FILE.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception as e:
            logger.warning(f"保存配置失败: {e}")

    def can_create_room(self, current_room_count: int) -> bool:
        """是否可以创建新房间"""
        return current_room_count < self.max_concurrent_games

    def get_status(self, room_count: int = 0, connected_players: int = 0) -> dict:
        """获取当前状态（房间数和在线人数由外部传入）"""
        return {
            "max_concurrent_games": self.max_concurrent_games,
            "room_count": room_count,
            "connected_players": connected_players,
        }

    def set_max_concurrent_games(self, value: int):
        """修改最大房间数（运行时生效并持久化）"""
        if value < 1:
            value = 1
        self.max_concurrent_games = value
        self._save()
        logger.info(f"最大房间数已更新为: {value}")
