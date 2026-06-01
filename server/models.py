"""
models.py - Pydantic 消息模型
用于客户端/服务端消息验证
"""

from pydantic import BaseModel, Field
from typing import Any, Optional


class ClientMessage(BaseModel):
    """客户端消息"""
    type: str
    payload: dict[str, Any] = {}


class CardData(BaseModel):
    """卡牌数据"""
    suit: int = Field(ge=0, le=4)   # 0-4 (Diamonds/Spades/Hearts/Clubs/None)
    rank: int = Field(ge=3, le=17)  # 3-17 (Three~Two/SmallJoker/BigJoker)


class CreateRoomPayload(BaseModel):
    """创建房间"""
    player_name: str = "玩家"
    is_public: bool = True


class JoinRoomPayload(BaseModel):
    """加入房间"""
    room_code: str
    player_name: str = "玩家"


class SetRoomVisibilityPayload(BaseModel):
    """设置房间可见性"""
    is_public: bool


class BidPayload(BaseModel):
    """叫分"""
    amount: int = Field(ge=0, le=3)  # 0-3


class PlayPayload(BaseModel):
    """出牌"""
    cards: list[CardData]
