"""Tests for server_config.py: ServerConfig."""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import tempfile
import json
from unittest.mock import patch
from pathlib import Path

import server_config
from server_config import ServerConfig, DEFAULT_CONFIG


class TestServerConfig:
    def test_default_max_concurrent_games(self):
        """Default max is 10 when no config file exists."""
        with tempfile.TemporaryDirectory() as tmp:
            fake_path = Path(tmp) / "server_config.json"
            with patch.object(server_config, "CONFIG_FILE", fake_path):
                cfg = ServerConfig()
                assert cfg.max_concurrent_games == DEFAULT_CONFIG["max_concurrent_games"]
                assert cfg.max_concurrent_games == 10

    def test_can_create_room_below_max(self):
        cfg = ServerConfig()
        assert cfg.can_create_room(cfg.max_concurrent_games - 1) is True

    def test_can_create_room_at_zero(self):
        cfg = ServerConfig()
        assert cfg.can_create_room(0) is True

    def test_cannot_create_room_at_max(self):
        cfg = ServerConfig()
        assert cfg.can_create_room(cfg.max_concurrent_games) is False

    def test_cannot_create_room_above_max(self):
        cfg = ServerConfig()
        assert cfg.can_create_room(cfg.max_concurrent_games + 1) is False

    def test_set_max_clamps_minimum(self):
        cfg = ServerConfig()
        cfg.set_max_concurrent_games(0)
        assert cfg.max_concurrent_games == 1

    def test_set_max_negative_clamps(self):
        cfg = ServerConfig()
        cfg.set_max_concurrent_games(-5)
        assert cfg.max_concurrent_games == 1

    def test_get_status_returns_dict(self):
        cfg = ServerConfig()
        status = cfg.get_status(room_count=5, connected_players=3)
        assert status["max_concurrent_games"] == cfg.max_concurrent_games
        assert status["room_count"] == 5
        assert status["connected_players"] == 3

    def test_get_status_defaults(self):
        cfg = ServerConfig()
        status = cfg.get_status()
        assert status["room_count"] == 0
        assert status["connected_players"] == 0
