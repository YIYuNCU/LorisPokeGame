import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import pytest
from card_models import Card, Suit, Rank


def pytest_configure(config):
    config.addinivalue_line("markers", "stress: 压力测试（高负载、大量迭代）")
    config.addinivalue_line("markers", "performance: 性能测试（延迟基准、吞吐量）")
    config.addinivalue_line("markers", "memory: 内存管理测试（分配压力、泄漏检测）")
    config.addinivalue_line("markers", "anti_cheat: 反作弊测试（防护验证、漏洞暴露）")
    config.addinivalue_line("markers", "server_load: 服务器负载测试（2H2G 资源约束模拟）")


@pytest.fixture
def full_deck():
    from card_models import create_full_deck
    return create_full_deck()


def c(suit, rank):
    """Shorthand helper to create a Card."""
    return Card(suit=suit, rank=rank)
