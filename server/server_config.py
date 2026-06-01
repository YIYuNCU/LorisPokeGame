"""
server_config.py - 服务器参数配置
管理并发对局上限等可配置参数，持久化到 JSON 文件
"""

import json
import logging
from pathlib import Path

logger = logging.getLogger(__name__)

CONFIG_FILE = Path(__file__).parent / "server_config.json"

# 默认配置
DEFAULT_CONFIG = {
    "max_concurrent_games": 10,
}


class ServerConfig:
    """服务器配置管理器"""

    def __init__(self):
        self.max_concurrent_games: int = DEFAULT_CONFIG["max_concurrent_games"]
        self._active_games: int = 0  # 当前进行中的对局数（内存，不持久化）
        self._load()

    def _load(self):
        """从文件加载配置"""
        try:
            if CONFIG_FILE.exists():
                data = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
                self.max_concurrent_games = data.get("max_concurrent_games", DEFAULT_CONFIG["max_concurrent_games"])
                logger.info(f"已加载配置: 最大并发对局 {self.max_concurrent_games}")
            else:
                # 首次运行，写入默认配置
                self._save()
                logger.info(f"已创建默认配置文件: {CONFIG_FILE}")
        except Exception as e:
            logger.warning(f"加载配置失败，使用默认值: {e}")
            self.max_concurrent_games = DEFAULT_CONFIG["max_concurrent_games"]

    def _save(self):
        """保存配置到文件"""
        try:
            data = {
                "max_concurrent_games": self.max_concurrent_games,
            }
            CONFIG_FILE.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception as e:
            logger.warning(f"保存配置失败: {e}")

    @property
    def active_games(self) -> int:
        return self._active_games

    def can_start_game(self) -> bool:
        """是否可以开始新对局"""
        return self._active_games < self.max_concurrent_games

    def on_game_start(self):
        """对局开始时调用"""
        self._active_games += 1
        logger.info(f"对局开始，当前进行中: {self._active_games}/{self.max_concurrent_games}")

    def on_game_end(self):
        """对局结束时调用"""
        self._active_games = max(0, self._active_games - 1)
        logger.info(f"对局结束，当前进行中: {self._active_games}/{self.max_concurrent_games}")

    def get_status(self) -> dict:
        """获取当前状态"""
        return {
            "max_concurrent_games": self.max_concurrent_games,
            "active_games": self._active_games,
        }

    def set_max_concurrent_games(self, value: int):
        """修改最大并发对局数（运行时生效并持久化）"""
        if value < 1:
            value = 1
        self.max_concurrent_games = value
        self._save()
        logger.info(f"最大并发对局数已更新为: {value}")
