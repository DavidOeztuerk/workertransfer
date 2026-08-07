"""Ports der Application-Schicht."""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from portfolio_service.domain.portfolio import Portfolio

__all__ = ["ConsentGate", "PortfolioRepository", "tenant_capability"]

#: „Für alle Unternehmen" — der Schalter auf /portfolio. Getrennt von
#: `profile.visibility:public`, damit sie einzeln widerrufbar ist.
VISIBILITY_CAPABILITY = "portfolio.visibility:public"


def tenant_capability(tenant_id: UUID) -> str:
    """„Für dieses eine Unternehmen" — entsteht mit einer Bewerbung (4.2).

    Die additive Verfeinerung aus ADR-0020: `:public` bleibt, was es war.
    """
    return f"portfolio.visibility:tenant:{tenant_id}"


class PortfolioRepository(Protocol):
    async def get(self, subject_id: UUID) -> Portfolio | None: ...
    async def save(self, portfolio: Portfolio) -> None: ...


class ConsentGate(Protocol):
    async def may_see(self, subject_id: UUID, *, tenant_id: UUID, bearer: str) -> bool: ...
