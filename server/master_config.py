"""
master_config.py - 列表服务器配置
管理列表服务器的端口、清理间隔等参数
"""

import json
import logging
from pathlib import Path

logger = logging.getLogger(__name__)

CONFIG_FILE = Path(__file__).parent / "master_config.json"

DEFAULT_CONFIG = {
    "port": 8000,
    "cleanup_interval": 30,   # 清理间隔（秒）
    "dead_timeout": 60,       # 从服务器超时判定（秒）
    "api_key": "",            # 管理接口密钥（空=不验证）
}


class MasterConfig:
    """列表服务器配置管理器"""

    def __init__(self):
        self.port: int = DEFAULT_CONFIG["port"]
        self.cleanup_interval: int = DEFAULT_CONFIG["cleanup_interval"]
        self.dead_timeout: int = DEFAULT_CONFIG["dead_timeout"]
        self.api_key: str = DEFAULT_CONFIG["api_key"]
        self._load()

    def _load(self):
        try:
            if CONFIG_FILE.exists():
                data = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
                self.port = data.get("port", DEFAULT_CONFIG["port"])
                self.cleanup_interval = data.get("cleanup_interval", DEFAULT_CONFIG["cleanup_interval"])
                self.dead_timeout = data.get("dead_timeout", DEFAULT_CONFIG["dead_timeout"])
                self.api_key = data.get("api_key", DEFAULT_CONFIG["api_key"])
                logger.info(f"已加载列表服务器配置: port={self.port}, cleanup_interval={self.cleanup_interval}s, api_key={'已设置' if self.api_key else '未设置'}")
            else:
                self._save()
                logger.info(f"已创建默认列表服务器配置: {CONFIG_FILE}")
        except Exception as e:
            logger.warning(f"加载列表服务器配置失败，使用默认值: {e}")

    def _save(self):
        try:
            data = {
                "port": self.port,
                "cleanup_interval": self.cleanup_interval,
                "dead_timeout": self.dead_timeout,
                "api_key": self.api_key,
            }
            CONFIG_FILE.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception as e:
            logger.warning(f"保存列表服务器配置失败: {e}")
