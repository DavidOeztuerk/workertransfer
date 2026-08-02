"""Database infrastructure for consent-service."""

from __future__ import annotations

from consent_service.infrastructure.database.models import AuditEventModel, ConsentEventModel
from consent_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemyConsentEventRepository,
)

__all__ = [
    "AuditEventModel",
    "ConsentEventModel",
    "SqlAlchemyAuditRepository",
    "SqlAlchemyConsentEventRepository",
]
