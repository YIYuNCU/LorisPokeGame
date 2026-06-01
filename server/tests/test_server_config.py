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

    def test_can_start_game_below_max(self):
        cfg = ServerConfig()
        cfg._active_games = cfg.max_concurrent_games - 1
        if cfg._active_games < 0:
            cfg._active_games = 0
        assert cfg.can_start_game() is True

    def test_cannot_start_game_at_max(self):
        cfg = ServerConfig()
        cfg._active_games = cfg.max_concurrent_games
        assert cfg.can_start_game() is False

    def test_on_game_start_increments(self):
        cfg = ServerConfig()
        cfg._active_games = 0
        cfg.on_game_start()
        assert cfg.active_games == 1
        cfg.on_game_start()
        assert cfg.active_games == 2

    def test_on_game_end_decrements(self):
        cfg = ServerConfig()
        cfg._active_games = 3
        cfg.on_game_end()
        assert cfg.active_games == 2

    def test_on_game_end_no_underflow(self):
        cfg = ServerConfig()
        cfg._active_games = 0
        cfg.on_game_end()
        assert cfg.active_games == 0

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
        cfg._active_games = 3
        status = cfg.get_status()
        assert status["max_concurrent_games"] == cfg.max_concurrent_games
        assert status["active_games"] == 3
