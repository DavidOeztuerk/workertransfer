"""Ports der Application-Schicht."""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from portfolio_service.domain.portfolio import Portfolio

__all__ = ["ConsentGate", "PortfolioRepository"]

#: Die eine Capability dieses Slices. Getrennt von `profile.visibility:public`,
#: damit sie einzeln widerrufbar ist — auch wenn der Schalter dafür in der
#: Oberfläche neben dem des Profils sitzt.
VISIBILITY_CAPABILITY = "portfolio.visibility:public"


class PortfolioRepository(Protocol):
    async def get(self, subject_id: UUID) -> Portfolio | None: ...
    async def save(self, portfolio: Portfolio) -> None: ...


class ConsentGate(Protocol):
    async def may_see(self, subject_id: UUID, *, bearer: str) -> bool: ...
