from datetime import UTC, datetime
from uuid import uuid4

import pytest
from identity_service.domain.audit import (
    AUDIT_METADATA_ALLOWLIST,
    AuditAction,
    AuditEvent,
    AuditMetadataError,
)


def test_audit_action_values() -> None:
    assert AuditAction.REGISTER == "register"
    assert AuditAction.LOGIN_SUCCESS == "login_success"
    assert AuditAction.LOGIN_FAILURE == "login_failure"
    assert AuditAction.TOKEN_REFRESH == "token_refresh"
    assert AuditAction.TOKEN_REVOKE == "token_revoke"


def test_audit_event_allows_only_allowlist_metadata_keys() -> None:
    ev = AuditEvent(
        occurred_at=datetime.now(UTC),
        actor_id=uuid4(),
        tenant_id=uuid4(),
        action=AuditAction.LOGIN_SUCCESS,
        target_id=None,
        correlation_id="corr",
        metadata={"reason": "ok", "ip": "127.0.0.1"},
    )
    assert ev.metadata == {"reason": "ok", "ip": "127.0.0.1"}


def test_audit_event_rejects_unknown_metadata_key() -> None:
    with pytest.raises(AuditMetadataError):
        AuditEvent(
            occurred_at=datetime.now(UTC),
            actor_id=None,
            tenant_id=None,
            action=AuditAction.LOGIN_FAILURE,
            target_id=None,
            correlation_id=None,
            metadata={"email": "pii@example.com"},  # PII sneak-in attempt, rejected
        )


def test_audit_event_actor_nullable_for_unknown_login() -> None:
    ev = AuditEvent(
        occurred_at=datetime.now(UTC),
        actor_id=None,
        tenant_id=None,
        action=AuditAction.LOGIN_FAILURE,
        target_id=None,
        correlation_id=None,
        metadata={"reason": "unknown_user"},
    )
    assert ev.actor_id is None
    assert "user_agent" in AUDIT_METADATA_ALLOWLIST
