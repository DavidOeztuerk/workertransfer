"""The ledger end-to-end over HTTP, against a real Postgres.

Covers the product promise directly: grant makes a capability true, revoke makes
it false on the very next read, and the audit trail lands in the same transaction
as the fact.
"""

from __future__ import annotations

import os
from pathlib import Path
from uuid import uuid4

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine
from worker_auth import TokenManager

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

_SERVICE_DIR = Path(__file__).resolve().parents[2]  # apps/consent-service
SECRET = "integration-secret-with-at-least-thirty-two-bytes"
CAPABILITY = "profile.visibility:public"


@pytest.fixture(scope="module")
def migrated_schema(postgres_url: str) -> None:
    cfg = Config()
    cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    command.upgrade(cfg, "head")


def _client(postgres_url: str) -> tuple[AsyncClient, str, str]:
    os.environ["WORKER_DATABASE_URL"] = postgres_url
    os.environ["WORKER_JWT_SECRET"] = SECRET

    from consent_service.configuration import ConsentServiceSettings
    from consent_service.presentation.compose_api import build_app

    app = build_app(ConsentServiceSettings())
    subject = uuid4()
    # The ledger issues no tokens of its own — it verifies identity-service's
    # (ADR-0015). Minting one here with the shared secret is exactly what that
    # trust relationship means.
    token = TokenManager(SECRET).create_access_token(subject, uuid4(), ["user"], [])
    client = AsyncClient(transport=ASGITransport(app=app), base_url="http://test")
    return client, str(subject), token


async def test_grant_then_check_then_revoke_is_immediate(
    postgres_url: str, migrated_schema: None
) -> None:
    client, subject, token = _client(postgres_url)
    auth = {"Authorization": f"Bearer {token}"}
    body = {"subject_id": subject, "capability": CAPABILITY}

    async with client:
        granted = await client.post("/consent/grant", json=body, headers=auth)
        assert granted.status_code == 200, granted.text
        assert granted.json()["granted"] is True

        checked = await client.post("/consent/check", json=body, headers=auth)
        assert checked.json()["granted"] is True

        revoked = await client.post(
            "/consent/revoke",
            json={**body, "reason": "subject withdrew consent"},
            headers=auth,
        )
        assert revoked.status_code == 200, revoked.text

        # The heart of the product promise: no cache, no eventual window.
        after = await client.post("/consent/check", json=body, headers=auth)
        assert after.json()["granted"] is False
        assert after.json()["reason"] == "subject withdrew consent"


async def test_check_on_an_untouched_pair_is_false_not_404(
    postgres_url: str, migrated_schema: None
) -> None:
    client, _subject, token = _client(postgres_url)
    async with client:
        response = await client.post(
            "/consent/check",
            json={"subject_id": str(uuid4()), "capability": "never.touched:x"},
            headers={"Authorization": f"Bearer {token}"},
        )
    assert response.status_code == 200
    assert response.json() == {
        "subject_id": response.json()["subject_id"],
        "capability": "never.touched:x",
        "granted": False,
        "deleted": False,
        "reason": "no consent event",
    }


async def test_a_subject_cannot_change_someone_elses_consent(
    postgres_url: str, migrated_schema: None
) -> None:
    client, _subject, token = _client(postgres_url)
    async with client:
        response = await client.post(
            "/consent/grant",
            json={"subject_id": str(uuid4()), "capability": CAPABILITY},
            headers={"Authorization": f"Bearer {token}"},
        )
    assert response.status_code == 403


async def test_revoke_without_a_reason_is_rejected(
    postgres_url: str, migrated_schema: None
) -> None:
    client, subject, token = _client(postgres_url)
    async with client:
        response = await client.post(
            "/consent/revoke",
            json={"subject_id": subject, "capability": CAPABILITY},
            headers={"Authorization": f"Bearer {token}"},
        )
    assert response.status_code == 422


async def test_write_and_audit_land_in_one_transaction(
    postgres_url: str, migrated_schema: None
) -> None:
    """ADR-0012 atomicity: a consent fact never exists without its audit row."""
    client, subject, token = _client(postgres_url)
    async with client:
        await client.post(
            "/consent/grant",
            json={"subject_id": subject, "capability": CAPABILITY},
            headers={"Authorization": f"Bearer {token}"},
        )

    eng = create_async_engine(postgres_url)
    try:
        async with eng.connect() as conn:
            facts = (
                await conn.execute(
                    text("SELECT action FROM consent_events WHERE subject_id = :s"),
                    {"s": subject},
                )
            ).all()
            audits = (
                await conn.execute(
                    text("SELECT action, metadata FROM audit_events WHERE target_id = :s"),
                    {"s": subject},
                )
            ).all()
        assert [row[0] for row in facts] == ["GRANT"]
        assert [row[0] for row in audits] == ["consent_grant"]
        # The capability name is allowlisted; the data it governs is never logged.
        assert audits[0][1] == {"capability": CAPABILITY}
    finally:
        await eng.dispose()


async def test_audit_metadata_never_contains_pii(postgres_url: str, migrated_schema: None) -> None:
    client, subject, token = _client(postgres_url)
    async with client:
        await client.post(
            "/consent/grant",
            json={"subject_id": subject, "capability": CAPABILITY},
            headers={"Authorization": f"Bearer {token}"},
        )

    eng = create_async_engine(postgres_url)
    try:
        async with eng.connect() as conn:
            rows = (await conn.execute(text("SELECT metadata::text FROM audit_events"))).all()
    finally:
        await eng.dispose()

    blob = " ".join(row[0] for row in rows).lower()
    for forbidden in ("email", "password", "token", "@"):
        assert forbidden not in blob, f"{forbidden!r} leaked into audit metadata"
