"""Die Kaskade — belegt an der Datenbank, nicht an einem 202 (ADR-0027 §4, §6, §7).

Der Zusteller läuft hier nicht als Hintergrundaufgabe mit: die Tests sprechen
die App über `ASGITransport` an, ohne Lifespan. Das ist sogar nützlich — so
lässt sich jede Stufe der erzwungenen Reihenfolge einzeln ansehen, statt zu
hoffen, dass sie in der richtigen abläuft.

**Die Empfänger sind echte HTTP-Empfänger**, nur mit einem Transport, der in den
Prozess statt ins Netz zeigt. Das ist nicht Zierde: ADR-0027 verlangt
ausdrücklich, dass der Test für den toten Empfänger gegen einen Zusteller läuft,
der sich wie der produktive verhält — nicht gegen eine Attrappe, die wirft. Der
Fehler, den er finden soll, ist genau, dass der produktive Adapter zu freundlich
ist.
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx
import pytest
import pytest_asyncio
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from identity_service.application.erasure import (
    KIND_COMPANY_WITHDRAWAL,
    KIND_FINAL_NOTICE,
    KIND_IDENTITY,
    RECIPIENTS,
    erasure_kinds,
)
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from worker_outbox import MAX_ATTEMPTS

_SERVICE_DIR = Path(__file__).resolve().parents[2]
SECRET = "erasure-secret-with-at-least-thirty-two-bytes"
JWT_SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"

pytestmark = pytest.mark.asyncio(loop_scope="module")


class Recipients:
    """Die sieben Empfänger als *ein* HTTP-Gegenüber, das man steuern kann."""

    def __init__(self) -> None:
        self.seen: list[tuple[str, str]] = []
        self.dead: set[str] = set()
        self.status: dict[str, int] = {}

    def handle(self, request: httpx.Request) -> httpx.Response:
        service = request.url.host.removesuffix("-service")
        self.seen.append((service, request.content.decode()))
        if service in self.dead:
            raise httpx.ConnectError("Dienst antwortet nicht")
        return httpx.Response(self.status.get(service, 200), json={"retained": 0})

    def reset(self) -> None:
        self.seen.clear()
        self.dead.clear()
        self.status.clear()


class Postbox:
    def __init__(self) -> None:
        self.sent: list[tuple[str, str, str]] = []
        self.broken = False

    async def send(self, *, to: str, subject: str, body: str) -> None:
        if self.broken:
            raise RuntimeError("SMTP nicht erreichbar")
        self.sent.append((to, subject, body))


RECIPIENT_STUB = Recipients()
POSTBOX = Postbox()


@pytest.fixture(scope="module")
def app(postgres_url: str) -> Iterator[Any]:
    patch = pytest.MonkeyPatch()
    try:
        cfg = Config()
        cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        patch.setenv("WORKER_JWT_SECRET", JWT_SECRET)
        patch.setenv("WORKER_ERASURE_SECRET", SECRET)
        for name in (*RECIPIENTS, "jobs"):
            patch.setenv(f"WORKER_{name.upper()}_BASE_URL", f"http://{name}-service:8000")
        command.downgrade(cfg, "base")
        command.upgrade(cfg, "head")

        import identity_service.infrastructure.compose as compose_module
        from identity_service.infrastructure.erasure import HttpErasureDelivery

        original = HttpErasureDelivery

        def _in_process(**kwargs: Any) -> HttpErasureDelivery:
            # DER PRODUKTIVE ADAPTER, nur mit einem Transport in den Prozess.
            return original(**kwargs, transport=httpx.MockTransport(RECIPIENT_STUB.handle))

        patch.setattr(compose_module, "HttpErasureDelivery", _in_process)
        patch.setattr(compose_module, "SmtpMailer", lambda **_kwargs: POSTBOX)

        from identity_service.configuration import IdentityServiceSettings
        from identity_service.presentation.compose_api import build_app

        yield build_app(IdentityServiceSettings())
    finally:
        patch.undo()


@pytest_asyncio.fixture(loop_scope="module")
async def session(postgres_url: str, app: Any) -> AsyncIterator[AsyncSession]:
    engine = create_async_engine(postgres_url)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as s:
        await s.execute(
            text(
                "TRUNCATE users, tenants, user_tenant_memberships, sessions, "
                "audit_events, company_invitations, notification_preferences, "
                "email_verification_tokens, outbox CASCADE"
            )
        )
        await s.commit()
        RECIPIENT_STUB.reset()
        POSTBOX.sent.clear()
        POSTBOX.broken = False
        yield s
    await engine.dispose()


async def _person(session: AsyncSession, *, email: str = "anna@example.com") -> UUID:
    user_id = uuid4()
    await session.execute(
        text(
            "INSERT INTO users (id, email, password_hash, display_name, status, roles, "
            "created_at, updated_at, version) VALUES (:id, :email, 'hash', 'Anna', "
            "'active', '[]', now(), now(), 1)"
        ),
        {"id": str(user_id), "email": email},
    )
    await session.commit()
    return user_id


def _token(user_id: UUID) -> dict[str, str]:
    from identity_service.infrastructure.auth.jwt_service import JwTokenService

    token = JwTokenService(JWT_SECRET).issue_access_token(user_id, None, [], [])
    return {"Authorization": f"Bearer {token}"}


async def _ask_for_erasure(app: Any, user_id: UUID) -> httpx.Response:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post("/account/erasure", headers=_token(user_id))


async def _drain(postgres_url: str, app: Any, times: int = 1) -> None:
    """Ein Takt des Zustellers — genau der, den `create_api_app` im Betrieb startet."""
    from identity_service.configuration import IdentityServiceSettings
    from identity_service.infrastructure.compose import compose_infrastructure
    from identity_service.infrastructure.database.models import OUTBOX
    from identity_service.infrastructure.erasure_dispatch import ErasureDispatch
    from worker_outbox import OutboxDispatcher

    engine = create_async_engine(postgres_url)
    try:
        deps = compose_infrastructure(IdentityServiceSettings(), engine)
        dispatcher = OutboxDispatcher(
            session_factory=deps["session_factory"],
            table=OUTBOX,
            delivery=ErasureDispatch(
                session_factory=deps["session_factory"],
                delivery=deps["erasure_delivery"],
                mailer=deps["mailer"],
                clock=deps["clock"],
            ),
            max_attempts=None,
        )
        for _ in range(times):
            await dispatcher.drain_once()
    finally:
        await engine.dispose()


async def _outbox(session: AsyncSession, user_id: UUID) -> dict[str, Any]:
    rows = await session.execute(
        text("SELECT kind, delivered_at, attempts FROM outbox WHERE user_id = :u"),
        {"u": str(user_id)},
    )
    return {row.kind: row for row in rows}


async def _user_exists(session: AsyncSession, user_id: UUID) -> bool:
    rows = await session.execute(
        text("SELECT count(*) FROM users WHERE id = :u"), {"u": str(user_id)}
    )
    return bool(rows.scalar_one())


class TestTheRequestItself:
    async def test_it_disables_the_account_and_revokes_every_session(
        self, app: Any, session: AsyncSession
    ) -> None:
        """Sofort und sichtbar (§6): ab hier passiert nichts mehr unter diesem
        Namen, auch wenn die Kaskade noch läuft."""
        user_id = await _person(session)
        await session.execute(
            text(
                "INSERT INTO sessions (id, user_id, refresh_jti, expires_at, created_at, "
                "updated_at) VALUES (:id, :u, 'jti-1', now() + interval '1 day', now(), now())"
            ),
            {"id": str(uuid4()), "u": str(user_id)},
        )
        await session.commit()

        response = await _ask_for_erasure(app, user_id)

        assert response.status_code == 202, response.text
        status_row = await session.execute(
            text("SELECT status FROM users WHERE id = :u"), {"u": str(user_id)}
        )
        assert status_row.scalar_one() == "disabled"
        revoked = await session.execute(
            text("SELECT count(*) FROM sessions WHERE user_id = :u AND revoked_at IS NULL"),
            {"u": str(user_id)},
        )
        assert revoked.scalar_one() == 0

    async def test_nine_intents_land_in_one_transaction(
        self, app: Any, session: AsyncSession
    ) -> None:
        """Sieben fremde Empfänger, die Abschlussnachricht und identity selbst.

        Alle im selben Augenblick: es gibt keinen Zustand „gesperrt, aber
        niemand wurde beauftragt".
        """
        user_id = await _person(session)

        await _ask_for_erasure(app, user_id)

        rows = await _outbox(session, user_id)
        assert set(rows) == set(erasure_kinds())
        assert all(row.delivered_at is None for row in rows.values())

    async def test_asking_twice_starts_no_second_cascade(
        self, app: Any, session: AsyncSession
    ) -> None:
        user_id = await _person(session)

        first = await _ask_for_erasure(app, user_id)
        second = await _ask_for_erasure(app, user_id)

        assert (first.status_code, second.status_code) == (202, 202)
        rows = await session.execute(
            text("SELECT count(*) FROM outbox WHERE user_id = :u"), {"u": str(user_id)}
        )
        assert rows.scalar_one() == len(erasure_kinds())

    async def test_an_anonymous_caller_cannot_erase_anyone(self, app: Any) -> None:
        """Es gibt keinen Parameter für jemand anderen — und auch kein
        Schlupfloch für niemanden."""
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
            response = await client.post("/account/erasure")

        assert response.status_code == 401


class TestADeadRecipientHoldsTheErasureOpen:
    """§4.1 und §4.3 — der Kern des Nachweises.

    Gegen den **produktiven** Adapter: der Fehler, den diese Tests finden
    sollen, ist gerade, dass ein schluckender Zusteller `delivered_at` setzt,
    ohne dass jemand gelöscht hat.
    """

    async def test_a_transport_error_never_counts_as_delivered(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)
        RECIPIENT_STUB.dead.add("profile")

        await _drain(postgres_url, app, times=3)

        rows = await _outbox(session, user_id)
        assert rows["erasure:profile"].delivered_at is None
        assert rows["erasure:profile"].attempts == 3
        # Die anderen sind längst durch — ein toter Empfänger blockiert nicht
        # die übrigen, nur den Abschluss.
        assert rows["erasure:consent"].delivered_at is not None

    async def test_a_non_2xx_answer_never_counts_as_delivered(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Der Fall, den `HttpNotifier` nicht einmal ansieht."""
        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)
        RECIPIENT_STUB.status["transfer"] = 500

        await _drain(postgres_url, app, times=2)

        rows = await _outbox(session, user_id)
        assert rows["erasure:transfer"].delivered_at is None
        assert rows["erasure:transfer"].attempts == 2

    async def test_it_never_gives_up(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """`MAX_ATTEMPTS` würde die Zeile liegenlassen — für eine Mail richtig,
        hier das stille Scheitern, gegen das die Konstruktion antritt."""
        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)
        RECIPIENT_STUB.dead.add("github")

        await _drain(postgres_url, app, times=MAX_ATTEMPTS + 3)

        rows = await _outbox(session, user_id)
        assert rows["erasure:github"].attempts == MAX_ATTEMPTS + 3

    async def test_no_final_notice_and_no_deleted_account(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Die einzige ehrliche Antwort: eine Löschung, die einen Dienst nicht
        erreicht hat, **ist** nicht fertig — und keine Zeitüberschreitung macht
        sie fertig."""
        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)
        RECIPIENT_STUB.dead.add("resume")

        await _drain(postgres_url, app, times=5)

        assert POSTBOX.sent == []
        assert await _user_exists(session, user_id)
        rows = await _outbox(session, user_id)
        assert rows[KIND_FINAL_NOTICE].delivered_at is None
        assert rows[KIND_IDENTITY].delivered_at is None
        # Und die Reihenfolge kostet keine Versuche: Zurückstellen ist kein
        # Fehlschlag.
        assert rows[KIND_FINAL_NOTICE].attempts == 0

    async def test_the_open_rest_names_the_service(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Man sieht nicht nur, DASS etwas offen ist, sondern WELCHER Dienst."""
        from identity_service.application.erasure import open_kinds

        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)
        RECIPIENT_STUB.dead.add("portfolio")

        await _drain(postgres_url, app, times=3)

        assert await open_kinds(session, user_id) == [
            "erasure:portfolio",
            KIND_FINAL_NOTICE,
            KIND_IDENTITY,
        ]


class TestTheOrderIsEnforced:
    async def test_the_account_falls_last_and_only_after_the_notice(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Der Grund ist unromantisch: die Nachricht braucht die Adresse, und
        die Adresse liegt in der Zeile, die gelöscht werden soll."""
        user_id = await _person(session, email="anna@example.com")
        await _ask_for_erasure(app, user_id)

        # Takt 1: die sieben Empfänger. Nachricht und Konto sind noch nicht dran.
        await _drain(postgres_url, app)
        assert POSTBOX.sent == []
        assert await _user_exists(session, user_id)

        # Takt 2: die Abschlussnachricht.
        await _drain(postgres_url, app)
        assert [to for to, _s, _b in POSTBOX.sent] == ["anna@example.com"]
        assert await _user_exists(session, user_id), "das Konto lebt, bis die Mail durch ist"

        # Takt 3: und erst jetzt fällt das Konto.
        await _drain(postgres_url, app)
        assert not await _user_exists(session, user_id)

    async def test_a_broken_mailer_stops_the_account_from_falling(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Sonst wäre die Adresse weg, bevor jemand erfahren hat, dass es
        fertig ist — und die Nachricht ginge für immer verloren."""
        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)
        POSTBOX.broken = True

        await _drain(postgres_url, app, times=4)

        assert await _user_exists(session, user_id)

        POSTBOX.broken = False
        await _drain(postgres_url, app, times=2)
        assert not await _user_exists(session, user_id)

    async def test_the_notice_says_that_it_is_done_and_nothing_else(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Keine Aufstellung dessen, was die Person hatte: das wäre eine Kopie
        der Daten in einem Postfach, das womöglich nicht nur ihr gehört."""
        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)

        await _drain(postgres_url, app, times=2)

        _to, _subject, body = POSTBOX.sent[0]
        assert "gelöscht" in body
        # „Transfer" steht hier bewusst nicht in der Liste: es ist der
        # Produktname („WorkerTransfer"), keine Datenkategorie. Geprüft wird,
        # dass die Mail nicht AUFZÄHLT, was dieser Mensch hatte.
        for leak in ("Profil", "Lebenslauf", "Bewerbung", "Portfolio", "Transfervorgang"):
            assert leak not in body, f"{leak!r} gehört nicht in eine Abschiedsmail"

    async def test_when_everything_arrived_nothing_is_open(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        from identity_service.application.erasure import open_kinds

        user_id = await _person(session)
        await _ask_for_erasure(app, user_id)

        await _drain(postgres_url, app, times=3)

        assert await open_kinds(session, user_id) == []


class TestWhatFallsWithTheAccount:
    async def test_the_preferences_row_without_a_foreign_key_falls_too(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """**Die Zeile, die man vergisst.** `notification_preferences` hat
        keinen Fremdschlüssel auf `users` — ein `DELETE FROM users` lässt sie
        stehen."""
        user_id = await _person(session)
        await session.execute(
            text("INSERT INTO notification_preferences (user_id) VALUES (:u)"),
            {"u": str(user_id)},
        )
        await session.commit()

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(
            text("SELECT count(*) FROM notification_preferences WHERE user_id = :u"),
            {"u": str(user_id)},
        )
        assert rows.scalar_one() == 0

    async def test_the_audit_row_stays_but_loses_its_metadata(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        user_id = await _person(session)
        await session.execute(
            text(
                "INSERT INTO audit_events (id, actor_id, action, occurred_at, metadata, "
                "created_at, updated_at) VALUES (:id, :u, 'login_success', now(), "
                '\'{"ip": "10.0.0.1"}\', now(), now())'
            ),
            {"id": str(uuid4()), "u": str(user_id)},
        )
        await session.commit()

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(
            text("SELECT action, metadata FROM audit_events WHERE actor_id = :u"),
            {"u": str(user_id)},
        )
        entries = list(rows)
        assert [row.action for row in entries] == ["login_success"]
        assert [row.metadata for row in entries] == [{}]

    async def test_an_invitation_to_this_address_falls(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Sie trägt die Adresse im Klartext."""
        user_id = await _person(session, email="anna@example.com")
        tenant_id = await _company(session)
        inviter = await _person(session, email="chef@example.com")
        await _invitation(session, tenant_id, "anna@example.com", inviter)

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(text("SELECT count(*) FROM company_invitations"))
        assert rows.scalar_one() == 0

    async def test_an_invitation_this_person_sent_stays_without_their_name(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Heute stand dort `ondelete=CASCADE`: löschte ein Recruiter sein
        **privates** Konto, verschwanden die offenen Einladungen seines
        Arbeitgebers. Die Einladung gehört aber dem Unternehmen."""
        recruiter = await _person(session, email="recruiter@example.com")
        tenant_id = await _company(session)
        await _invitation(session, tenant_id, "wer-anders@example.com", recruiter)

        await _ask_for_erasure(app, recruiter)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(text("SELECT email, invited_by FROM company_invitations"))
        entries = list(rows)
        assert [row.email for row in entries] == ["wer-anders@example.com"]
        assert [row.invited_by for row in entries] == [None]


async def _company(session: AsyncSession, *, name: str = "Beispiel GmbH") -> UUID:
    tenant_id = uuid4()
    await session.execute(
        text(
            "INSERT INTO tenants (id, name, domain, status, created_at) VALUES "
            "(:id, :name, :domain, 'active', now())"
        ),
        {"id": str(tenant_id), "name": name, "domain": f"{tenant_id.hex[:8]}.example"},
    )
    await session.commit()
    return tenant_id


async def _invitation(session: AsyncSession, tenant_id: UUID, email: str, inviter: UUID) -> None:
    await session.execute(
        text(
            "INSERT INTO company_invitations (id, tenant_id, email, role, invited_by, "
            "status, token_hash, created_at, expires_at) VALUES (:id, :t, :e, 'member', "
            ":by, 'pending', :hash, now(), now() + interval '7 days')"
        ),
        {
            "id": str(uuid4()),
            "t": str(tenant_id),
            "e": email,
            "by": str(inviter),
            "hash": uuid4().hex,
        },
    )
    await session.commit()


async def _membership(session: AsyncSession, user_id: UUID, tenant_id: UUID, role: str) -> None:
    await session.execute(
        text(
            "INSERT INTO user_tenant_memberships (id, user_id, tenant_id, role, granted_at) "
            "VALUES (:id, :u, :t, :r, now())"
        ),
        {"id": str(uuid4()), "u": str(user_id), "t": str(tenant_id), "r": role},
    )
    await session.commit()


class TestTheLastAdmin:
    async def test_the_company_goes_dormant(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Nicht: die Löschung blockieren, bis jemand anderes Admin ist. Ein
        persönliches Recht darf nicht an einer Organisationsfrage hängen."""
        user_id = await _person(session)
        tenant_id = await _company(session)
        await _membership(session, user_id, tenant_id, "admin")

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(
            text("SELECT status FROM tenants WHERE id = :t"), {"t": str(tenant_id)}
        )
        assert rows.scalar_one() == "dormant"

    async def test_a_company_with_another_admin_stays_active(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        user_id = await _person(session)
        colleague = await _person(session, email="kollegin@example.com")
        tenant_id = await _company(session)
        await _membership(session, user_id, tenant_id, "admin")
        await _membership(session, colleague, tenant_id, "admin")

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(
            text("SELECT status FROM tenants WHERE id = :t"), {"t": str(tenant_id)}
        )
        assert rows.scalar_one() == "active"

    async def test_a_remaining_member_is_not_an_admin(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """Ein `member` kann das Unternehmen nicht führen — die Anzeigen wären
        genauso unbeaufsichtigt."""
        user_id = await _person(session)
        colleague = await _person(session, email="kollegin@example.com")
        tenant_id = await _company(session)
        await _membership(session, user_id, tenant_id, "admin")
        await _membership(session, colleague, tenant_id, "member")

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=3)

        rows = await session.execute(
            text("SELECT status FROM tenants WHERE id = :t"), {"t": str(tenant_id)}
        )
        assert rows.scalar_one() == "dormant"

    async def test_the_withdrawal_intent_goes_to_jobs_service(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        user_id = await _person(session)
        tenant_id = await _company(session)
        await _membership(session, user_id, tenant_id, "admin")

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=4)

        assert ("jobs", f'{{"tenant_id":"{tenant_id}"}}') in RECIPIENT_STUB.seen

    async def test_a_silent_jobs_service_does_not_hold_the_erasure_open(
        self, app: Any, session: AsyncSession, postgres_url: str
    ) -> None:
        """**Die Kopplung, die §7 ausschließt.**

        Sonst könnte ein stiller jobs-service die Löschung eines Menschen
        offenhalten — und das persönliche Recht hinge wieder an einer
        Organisationsfrage.
        """
        from identity_service.application.erasure import open_kinds

        user_id = await _person(session)
        tenant_id = await _company(session)
        await _membership(session, user_id, tenant_id, "admin")
        RECIPIENT_STUB.dead.add("jobs")

        await _ask_for_erasure(app, user_id)
        await _drain(postgres_url, app, times=5)

        # Der Mensch ist gelöscht, die Nachricht ist draußen, nichts ist offen.
        assert not await _user_exists(session, user_id)
        assert POSTBOX.sent != []
        assert await open_kinds(session, user_id) == []
        # Die Absicht ans jobs-service liegt weiter da und wird weiter versucht
        # — sie zählt nur nicht in den Nachweis.
        rows = await session.execute(
            text("SELECT kind, delivered_at FROM outbox WHERE user_id = :t"),
            {"t": str(tenant_id)},
        )
        pending = list(rows)
        assert [row.kind for row in pending] == [KIND_COMPANY_WITHDRAWAL]
        assert pending[0].delivered_at is None
