from uuid import UUID, uuid4

import pytest
from identity_service.domain.value_objects import (
    Email,
    InvalidEmail,
    PasswordHash,
    TenantId,
    UserId,
)


def test_email_lowercases_and_validates() -> None:
    assert Email("Alice@Example.COM").value == "alice@example.com"
    assert Email("Alice@Example.COM") == Email("alice@example.com")


def test_email_rejects_garbage() -> None:
    with pytest.raises(InvalidEmail):
        Email("not-an-email")
    with pytest.raises(InvalidEmail):
        Email("")


def test_email_rejects_extreme_inputs() -> None:
    with pytest.raises(InvalidEmail):
        Email("a" * 1000 + "@example.com")


def test_password_hash_is_opaque() -> None:
    h = PasswordHash("$2b$12$abc")
    assert h.value == "$2b$12$abc"


def test_user_id_and_tenant_id_wrap_uuid() -> None:
    u = uuid4()
    assert UserId(u).value == u
    assert TenantId(u).value == u


def test_tenant_id_rejects_nil() -> None:
    with pytest.raises(ValueError):
        TenantId(UUID("00000000-0000-0000-0000-000000000000"))
