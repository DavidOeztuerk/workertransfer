"""Die Löschung im transfer-service — hier liegt der zweite Konflikt.

Vier Tabellenfälle, und zwei davon sind nicht offensichtlich:

* `market_requests` mit `subject_id` = Person **fällt** — die Zeile IST die
  Aussage „Unternehmen X hat nach diesem Menschen gefragt".
* `market_requests` mit `requested_by` = Person **bleibt**, der Name fällt weg:
  ein Recruiter löscht sein privates Konto, die Anfrage gehört dem Unternehmen
  und handelt von einem *Dritten*.

Und der Konflikt selbst: **auch bezahlte Transfers fallen** (ADR-0027 §3). Der
umgelegte Schalter hält genau eine Zeilenklasse in diesem Dienst — `accepted`
oder `completed` **und** `offer_fee_cents IS NOT NULL` — und keine dritte.
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest
import pytest_asyncio
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from transfer_service.application import erasure

_SERVICE_DIR = Path(__file__).resolve().parents[2]
SECRET = "erasure-secret-with-at-least-thirty-two-bytes"
JWT_SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"

pytestmark = pytest.mark.asyncio(loop_scope="module")


@pytest.fixture(scope="module")
def app(postgres_url: str) -> Iterator[Any]:
    patch = pytest.MonkeyPatch()
    try:
        cfg = Config()
        cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        patch.setenv("WORKER_JWT_SECRET", JWT_SECRET)
        patch.setenv("WORKER_ERASURE_SECRET", SECRET)
        command.downgrade(cfg, "base")
        command.upgrade(cfg, "head")

        from transfer_service.configuration import TransferServiceSettings
        from transfer_service.presentation.compose_api import build_app

        yield build_app(TransferServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(text("TRUNCATE market_status, market_requests, transfers, outbox"))
        await s.commit()
        yield s
    await engine.dispose()


async def _market_status(session: AsyncSession, subject: UUID) -> None:
    await session.execute(
        text(
            "INSERT INTO market_status (id, availability, employed, note, created_at, "
            "updated_at) VALUES (:id, 'listening', true, 'Ich höre zu', now(), now())"
        ),
        {"id": str(subject)},
    )
    await session.commit()


async def _request(
    session: AsyncSession, *, subject: UUID, requested_by: UUID, tenant: UUID | None = None
) -> UUID:
    request_id = uuid4()
    await session.execute(
        text(
            "INSERT INTO market_requests (id, subject_id, tenant_id, requested_by, status, "
            "created_at) VALUES (:id, :subject, :tenant, :by, 'PENDING', now())"
        ),
        {
            "id": str(request_id),
            "subject": str(subject),
            "tenant": str(tenant or uuid4()),
            "by": str(requested_by),
        },
    )
    await session.commit()
    return request_id


async def _transfer(
    session: AsyncSession, subject: UUID, *, status: str, fee_cents: int | None
) -> UUID:
    transfer_id = uuid4()
    await session.execute(
        text(
            "INSERT INTO transfers (id, subject_id, tenant_id, status, requires_release, "
            "release_confirmed, message, offer_note, offer_fee_cents, created_at, "
            "updated_at) VALUES (:id, :subject, :tenant, :status, false, false, "
            "'Mein Text', 'Angebot', :fee, now(), now())"
        ),
        {
            "id": str(transfer_id),
            "subject": str(subject),
            "tenant": str(uuid4()),
            "status": status,
            "fee": fee_cents,
        },
    )
    await session.commit()
    return transfer_id


async def _transfer_shape(session: AsyncSession, subject: UUID) -> set[tuple[str, int | None]]:
    rows = await session.execute(
        text("SELECT status, offer_fee_cents FROM transfers WHERE subject_id = :s"),
        {"s": str(subject)},
    )
    return {(row[0], row[1]) for row in rows}


async def _erase(app: Any, subject: UUID, *, secret: str = SECRET) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post(
            "/internal/erasure",
            json={"user_id": str(subject)},
            headers={"X-Erasure-Secret": secret},
        )


async def test_the_market_status_falls_with_its_note(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _market_status(session, subject)

    response = await _erase(app, subject)

    assert response.status_code == 200, response.text
    rows = await session.execute(
        text("SELECT count(*) FROM market_status WHERE id = :s"), {"s": str(subject)}
    )
    assert rows.scalar_one() == 0


async def test_a_request_about_this_person_falls(app: Any, session: AsyncSession) -> None:
    """Die Zeile IST die Aussage „Unternehmen X hat nach diesem Menschen
    gefragt" — eine Aussage über ihn."""
    subject = uuid4()
    await _request(session, subject=subject, requested_by=uuid4())

    await _erase(app, subject)

    rows = await session.execute(
        text("SELECT count(*) FROM market_requests WHERE subject_id = :s"), {"s": str(subject)}
    )
    assert rows.scalar_one() == 0


async def test_a_request_this_person_sent_stays_but_loses_their_name(
    app: Any, session: AsyncSession
) -> None:
    """Ein Recruiter löscht sein PRIVATES Konto.

    Die offene Anfrage seines Arbeitgebers verschwindet nicht — sie gehört dem
    Unternehmen und handelt von einem Dritten. Was fällt, ist der Name daran.
    """
    recruiter, other_person = uuid4(), uuid4()
    request_id = await _request(session, subject=other_person, requested_by=recruiter)

    await _erase(app, recruiter)

    row = (
        await session.execute(
            text("SELECT subject_id, requested_by FROM market_requests WHERE id = :i"),
            {"i": str(request_id)},
        )
    ).one()
    assert row[0] == other_person, "die Anfrage steht noch"
    assert row[1] is None, "aber ohne den Namen der Person, die sie stellte"


async def test_by_default_a_paid_completed_transfer_falls(app: Any, session: AsyncSession) -> None:
    """Die Zeilenklasse, um die es geht (ADR-0027 §3).

    Fällt sie nicht, ist die tragende Entscheidung der ADR gebrochen — und ein
    Mensch, der sein Konto gelöscht hat, steht weiter in einer Vorgangsliste.
    """
    subject = uuid4()
    await _transfer(session, subject, status="completed", fee_cents=250_000)
    await _transfer(session, subject, status="accepted", fee_cents=100_000)

    response = await _erase(app, subject)

    assert await _transfer_shape(session, subject) == set()
    assert response.json()["retained"] == 0


async def test_by_default_every_transfer_falls_with_its_texts(
    app: Any, session: AsyncSession
) -> None:
    subject = uuid4()
    for status in ("interested", "talking", "offered", "accepted", "completed", "declined"):
        await _transfer(session, subject, status=status, fee_cents=None)

    await _erase(app, subject)

    assert await _transfer_shape(session, subject) == set()


async def test_the_pending_notification_falls_too(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await session.execute(
        text(
            "INSERT INTO outbox (id, user_id, kind, created_at, attempts, last_error) "
            "VALUES (:id, :user, 'transfer_update', now(), 0, '')"
        ),
        {"id": str(uuid4()), "user": str(subject)},
    )
    await session.commit()

    await _erase(app, subject)

    rows = await session.execute(
        text("SELECT count(*) FROM outbox WHERE user_id = :u"), {"u": str(subject)}
    )
    assert rows.scalar_one() == 0


async def test_delivering_twice_changes_nothing(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _market_status(session, subject)
    await _transfer(session, subject, status="completed", fee_cents=250_000)

    first = await _erase(app, subject)
    second = await _erase(app, subject)

    assert (first.status_code, second.status_code) == (200, 200)
    assert await _transfer_shape(session, subject) == set()


async def test_a_wrong_secret_looks_like_no_endpoint(app: Any, session: AsyncSession) -> None:
    subject = uuid4()
    await _market_status(session, subject)

    response = await _erase(app, subject, secret="falsch")

    assert response.status_code == 404
    rows = await session.execute(
        text("SELECT count(*) FROM market_status WHERE id = :s"), {"s": str(subject)}
    )
    assert rows.scalar_one() == 1


class TestTheFlippedSwitchHoldsExactlyOneRowClass:
    """Umgelegt wird er **nur hier**, nie in der Voreinstellung (ADR-0027 §3.2).

    Ausgeschrieben, weil das Abgrenzen der ganze Punkt ist:

        transfers.status IN ('accepted','completed')
          AND transfers.offer_fee_cents IS NOT NULL

    Kein `interested`/`talking`/`offered` — ein Gespräch ist kein Vertrag. Und
    ohne Vergütung ist kein Handelsvorgang entstanden, an dem etwas hängen
    könnte.
    """

    @pytest.fixture(autouse=True)
    def flipped(self, monkeypatch: pytest.MonkeyPatch) -> None:
        monkeypatch.setattr(erasure, "RETAIN_PAID_TRANSFERS", True)

    async def test_only_a_paid_final_transfer_survives(
        self, app: Any, session: AsyncSession
    ) -> None:
        subject = uuid4()
        await _transfer(session, subject, status="completed", fee_cents=250_000)
        await _transfer(session, subject, status="accepted", fee_cents=100_000)
        # Die drei, die NICHT gehalten werden dürfen:
        await _transfer(session, subject, status="completed", fee_cents=None)
        await _transfer(session, subject, status="offered", fee_cents=90_000)
        await _transfer(session, subject, status="declined", fee_cents=90_000)

        response = await _erase(app, subject)

        assert await _transfer_shape(session, subject) == {
            ("completed", 250_000),
            ("accepted", 100_000),
        }
        assert response.json()["retained"] == 2

    async def test_a_final_transfer_without_a_fee_still_falls(
        self, app: Any, session: AsyncSession
    ) -> None:
        """Ohne Vergütung ist kein Handelsvorgang entstanden."""
        subject = uuid4()
        await _transfer(session, subject, status="completed", fee_cents=None)

        await _erase(app, subject)

        assert await _transfer_shape(session, subject) == set()

    async def test_the_market_status_falls_even_then(self, app: Any, session: AsyncSession) -> None:
        """Der Schalter schaltet Transfers, nicht den ganzen Dienst.

        Er auf eine zweite Tabelle auszudehnen wäre genau das Ausfransen, gegen
        das die Abgrenzung in §3.2 geschrieben ist.
        """
        subject = uuid4()
        await _market_status(session, subject)
        await _transfer(session, subject, status="completed", fee_cents=250_000)

        await _erase(app, subject)

        rows = await session.execute(
            text("SELECT count(*) FROM market_status WHERE id = :s"), {"s": str(subject)}
        )
        assert rows.scalar_one() == 0
