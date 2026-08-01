# Registrierung & Unternehmens-Onboarding — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eine Person registriert sich im Browser, bestätigt ihre E-Mail und kann — wenn sie eine Arbeitsadresse benutzt — ein Unternehmen anlegen und hineinwechseln, ohne dass irgendwo von Hand in die Datenbank geschrieben wird.

**Architecture:** Alles in `apps/identity-service` (Clean Architecture wie bestehend) plus vier Seiten in `apps/web`. Der Kern ist ein einziger Mechanismus: ein gehashter Einmal-Token bestätigt die E-Mail einer Person, und genau diese bestätigte Adresse beweist beim Anlegen eines Unternehmens dessen Domain. Die Domain wird serverseitig abgeleitet, nie aus dem Request gelesen.

**Tech Stack:** Python 3.14, FastAPI, SQLAlchemy 2, Alembic, pydantic-settings, stdlib `smtplib`, Mailpit (Compose), React 19 + TanStack Router/Query, Vitest.

## Global Constraints

- Paketmanager ist **`uv`** (Python) und **`pnpm`** (Frontend) — niemals `pip`, `poetry`, `npm`, `yarn`.
- Gate-Reihenfolge: `make check` = ruff format → ruff check → mypy → pytest → `pnpm check` → `pnpm test`, fail-fast.
- mypy läuft **strict** über `packages` und `apps`, schließt `tests/` aus. ruff `line-length=100`, `target-version=py314`.
- Tests nutzen `asyncio_mode = "auto"` — **kein** `@pytest.mark.asyncio`.
- Integrationstests self-skippen ohne Docker (ADR-0011). **Docker muss lokal laufen** (`open -a Docker`), sonst bleiben sie unbemerkt übersprungen und CI wird rot.
- Keine Secrets, Tokens, CVs oder Verträge ins Repo oder in Logs.
- Tenant kommt **nie** aus Header oder Request-Body (ADR-0017/0018, product-scope.md). Das gilt in diesem Plan auch für die Firmendomain.
- Domänensprache nach außen: **`company`**. Datenbank und JWT-Claim behalten **`tenant`** (Spec §2, „Zwei Wörter für eine Sache").
- Registrierung ist für **private** E-Mail-Adressen uneingeschränkt offen. Die Freemail-Sperrliste greift **ausschließlich** beim Anlegen eines Unternehmens.

## File Structure

**Neu in `apps/identity-service/src/identity_service/`:**

| Datei | Verantwortung |
|---|---|
| `domain/verification.py` | Token-Wertobjekt, Zweck-Enum, Token-Fehler |
| `domain/company.py` | `Company`-Aggregat, `EmailDomain`, Freemail-Sperrliste, Fehler |
| `infrastructure/mail.py` | `SmtpMailer` (stdlib), `NullMailer` |
| `infrastructure/tokens.py` | Token erzeugen + hashen |
| `migrations/versions/0003_verification_and_companies.py` | Schema |

**Geändert:** `domain/user.py`, `domain/membership.py`, `domain/audit.py`, `application/ports.py`, `application/commands.py`, `infrastructure/database/models.py`, `infrastructure/database/repositories.py`, `infrastructure/compose.py`, `configuration.py`, `presentation/http/router.py`, `presentation/compose_api.py`.

**Neu in `packages/worker-contracts/src/worker_contracts/`:** `identity.py` (Register/Verify/Resend/Company/Membership V1-DTOs).

**Neu in `apps/web/src/routes/`:** `register.tsx`, `verify.tsx`, `company-new.tsx`.

**Geändert:** `docker-compose.yml` (Mailpit), `apps/web/src/auth/client.ts`, `apps/web/src/router.tsx`.

---

### Task 1: Mailer-Port und Adapter

**Files:**
- Create: `apps/identity-service/src/identity_service/infrastructure/mail.py`
- Modify: `apps/identity-service/src/identity_service/application/ports.py`
- Modify: `apps/identity-service/src/identity_service/configuration.py`
- Test: `apps/identity-service/tests/unit/test_mail.py`

**Interfaces:**
- Produces: `Mailer` Protocol mit `async def send(self, *, to: str, subject: str, body: str) -> None`; `NullMailer` (sammelt in `.sent: list[tuple[str, str, str]]`); `SmtpMailer(host, port, mail_from, username=None, password=None, use_tls=False)`.
- Settings-Felder: `smtp_host: str = "localhost"`, `smtp_port: int = 1025`, `smtp_username: str | None = None`, `smtp_password: SecretStr | None = None`, `smtp_use_tls: bool = False`, `mail_from: str = "noreply@workertransfer.local"`, `public_web_url: str = "http://localhost:5173"`.

**Warum nicht `worker-email`:** dessen `SMTPBackend.send()` ruft immer `server.login()` auf — Mailpit hat keine Authentifizierung, das schlägt fehl. Zusätzlich schluckt es jede Exception und gibt nur `False` zurück, und es zieht `boto3`, `sendgrid` und `aiohttp` ins Image. Für eine Textmail genügt `smtplib` aus der Standardbibliothek.

- [ ] **Step 1: Write the failing test**

```python
# apps/identity-service/tests/unit/test_mail.py
"""Der Mailer-Adapter baut eine korrekte Nachricht und meldet Fehler ehrlich."""

from __future__ import annotations

import pytest
from identity_service.infrastructure.mail import NullMailer, SmtpMailer


async def test_null_mailer_records_instead_of_sending() -> None:
    mailer = NullMailer()

    await mailer.send(to="a@b.com", subject="Hallo", body="Text")

    assert mailer.sent == [("a@b.com", "Hallo", "Text")]


def test_smtp_mailer_builds_a_plaintext_message() -> None:
    mailer = SmtpMailer(host="h", port=1025, mail_from="noreply@x.de")

    message = mailer.build_message(to="a@b.com", subject="Betreff", body="Zeile")

    assert message["To"] == "a@b.com"
    assert message["From"] == "noreply@x.de"
    assert message["Subject"] == "Betreff"
    assert message.get_content_type() == "text/plain"
    assert message.get_content().strip() == "Zeile"


async def test_smtp_mailer_raises_when_the_server_is_unreachable() -> None:
    # Ehrlich scheitern statt False zurückgeben: der Aufrufer entscheidet,
    # ob ein Fehlschlag die Registrierung kippen darf (er darf es nicht, §5).
    mailer = SmtpMailer(host="127.0.0.1", port=1, mail_from="noreply@x.de")

    with pytest.raises(OSError):
        await mailer.send(to="a@b.com", subject="s", body="b")
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest apps/identity-service/tests/unit/test_mail.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'identity_service.infrastructure.mail'`

- [ ] **Step 3: Write the implementation**

```python
# apps/identity-service/src/identity_service/infrastructure/mail.py
"""SMTP-Adapter für den Mailer-Port.

Bewusst `smtplib` aus der Standardbibliothek statt `worker-email`: dessen
SMTPBackend ruft unbedingt `server.login()` auf (Mailpit hat keine
Authentifizierung), schluckt jede Exception zu einem nackten `False` und zieht
boto3, sendgrid und aiohttp in ein Image, das eine Textmail verschicken soll.
"""

from __future__ import annotations

import asyncio
import smtplib
from email.message import EmailMessage

__all__ = ["NullMailer", "SmtpMailer"]


class NullMailer:
    """Sammelt statt zu senden — für Tests und für Läufe ohne Mailcatcher."""

    def __init__(self) -> None:
        self.sent: list[tuple[str, str, str]] = []

    async def send(self, *, to: str, subject: str, body: str) -> None:
        self.sent.append((to, subject, body))


class SmtpMailer:
    def __init__(
        self,
        *,
        host: str,
        port: int,
        mail_from: str,
        username: str | None = None,
        password: str | None = None,
        use_tls: bool = False,
    ) -> None:
        self._host = host
        self._port = port
        self._mail_from = mail_from
        self._username = username
        self._password = password
        self._use_tls = use_tls

    def build_message(self, *, to: str, subject: str, body: str) -> EmailMessage:
        message = EmailMessage()
        message["From"] = self._mail_from
        message["To"] = to
        message["Subject"] = subject
        message.set_content(body)
        return message

    async def send(self, *, to: str, subject: str, body: str) -> None:
        message = self.build_message(to=to, subject=subject, body=body)
        # smtplib ist blockierend; im Thread ausführen, damit der Event-Loop
        # nicht für die Dauer der SMTP-Konversation steht.
        await asyncio.to_thread(self._send_blocking, message)

    def _send_blocking(self, message: EmailMessage) -> None:
        with smtplib.SMTP(self._host, self._port, timeout=10) as server:
            if self._use_tls:
                server.starttls()
            # Nur anmelden, wenn Zugangsdaten gesetzt sind: Mailpit und die
            # meisten Entwicklungs-Catcher kennen AUTH nicht.
            if self._username and self._password:
                server.login(self._username, self._password)
            server.send_message(message)
```

- [ ] **Step 4: Add the port**

```python
# apps/identity-service/src/identity_service/application/ports.py
# in __all__ ergänzen: "Mailer"

class Mailer(Protocol):
    """Versand ist ein Port, damit die Application kein SMTP kennt."""

    async def send(self, *, to: str, subject: str, body: str) -> None: ...
```

- [ ] **Step 5: Add the settings**

```python
# apps/identity-service/src/identity_service/configuration.py
# innerhalb IdentityServiceSettings ergänzen:

    smtp_host: str = "localhost"
    smtp_port: int = 1025
    smtp_username: str | None = None
    smtp_password: SecretStr | None = None
    smtp_use_tls: bool = False
    mail_from: str = "noreply@workertransfer.local"
    # Basis für den Bestätigungslink in der Mail. Muss die Adresse sein, die der
    # Browser sieht — nicht der Compose-Servicename.
    public_web_url: str = "http://localhost:5173"
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `uv run pytest apps/identity-service/tests/unit/test_mail.py -v`
Expected: PASS (3 Tests)

- [ ] **Step 7: Commit**

```bash
git add apps/identity-service/src/identity_service/infrastructure/mail.py \
        apps/identity-service/src/identity_service/application/ports.py \
        apps/identity-service/src/identity_service/configuration.py \
        apps/identity-service/tests/unit/test_mail.py
git commit -m "feat(identity): Mailer-Port und stdlib-SMTP-Adapter"
```

---

### Task 2: Verifikations-Token (Domäne + Erzeugung)

**Files:**
- Create: `apps/identity-service/src/identity_service/domain/verification.py`
- Create: `apps/identity-service/src/identity_service/infrastructure/tokens.py`
- Test: `apps/identity-service/tests/unit/test_verification.py`

**Interfaces:**
- Consumes: nichts.
- Produces: `TokenPurpose.EMAIL_VERIFY`; `VerificationToken` (frozen dataclass: `token_id: UUID`, `user_id: UUID`, `token_hash: str`, `purpose: TokenPurpose`, `expires_at: datetime`, `consumed_at: datetime | None`) mit `is_expired(now) -> bool` und `is_consumed() -> bool`; Fehler `TokenInvalid`, `TokenExpired`; `generate_token() -> tuple[str, str]` (Klartext, Hash) und `hash_token(raw) -> str`.

- [ ] **Step 1: Write the failing test**

```python
# apps/identity-service/tests/unit/test_verification.py
"""Token-Regeln: einmalig, befristet, nur als Hash gespeichert."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

from identity_service.domain.verification import (
    TokenPurpose,
    VerificationToken,
)
from identity_service.infrastructure.tokens import generate_token, hash_token

NOW = datetime(2026, 8, 1, 12, 0, tzinfo=UTC)


def _token(**overrides: object) -> VerificationToken:
    defaults: dict[str, object] = {
        "token_id": uuid4(),
        "user_id": uuid4(),
        "token_hash": "x" * 64,
        "purpose": TokenPurpose.EMAIL_VERIFY,
        "expires_at": NOW + timedelta(hours=24),
        "consumed_at": None,
    }
    defaults.update(overrides)
    return VerificationToken(**defaults)  # type: ignore[arg-type]


def test_generate_returns_plaintext_and_its_hash() -> None:
    raw, hashed = generate_token()

    assert len(raw) >= 32
    assert hashed == hash_token(raw)
    # Der Klartext darf nirgends aus dem Hash rekonstruierbar sein.
    assert raw not in hashed


def test_hash_is_stable_and_hex_sha256() -> None:
    assert hash_token("abc") == hash_token("abc")
    assert len(hash_token("abc")) == 64
    assert int(hash_token("abc"), 16) >= 0


def test_two_tokens_differ() -> None:
    assert generate_token()[0] != generate_token()[0]


def test_expiry_is_evaluated_against_the_given_moment() -> None:
    token = _token(expires_at=NOW + timedelta(seconds=1))

    assert token.is_expired(NOW) is False
    assert token.is_expired(NOW + timedelta(seconds=2)) is True


def test_consumed_token_reports_itself_as_used() -> None:
    assert _token().is_consumed() is False
    assert _token(consumed_at=NOW).is_consumed() is True
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest apps/identity-service/tests/unit/test_verification.py -v`
Expected: FAIL — `ModuleNotFoundError: identity_service.domain.verification`

- [ ] **Step 3: Write the domain**

```python
# apps/identity-service/src/identity_service/domain/verification.py
"""Einmal-Token für die E-Mail-Bestätigung.

Nur der Hash wird gespeichert. Eine geleakte Datenbankzeile darf keine
Kontoübernahme sein — mit dem Hash allein lässt sich kein Link bauen.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "TokenExpired",
    "TokenInvalid",
    "TokenPurpose",
    "VerificationToken",
]


class TokenPurpose(StrEnum):
    EMAIL_VERIFY = "email_verify"


class TokenInvalid(DomainError):
    def __init__(self) -> None:
        # Bewusst ohne Detail: unbekannt und bereits verbraucht sind von außen
        # nicht unterscheidbar, sonst wird der Endpunkt ein Orakel.
        super().__init__("token_invalid", "This confirmation link is not valid")


class TokenExpired(DomainError):
    def __init__(self) -> None:
        super().__init__("token_expired", "This confirmation link has expired")


@dataclass(frozen=True, slots=True)
class VerificationToken:
    token_id: UUID
    user_id: UUID
    token_hash: str
    purpose: TokenPurpose
    expires_at: datetime
    consumed_at: datetime | None

    def is_expired(self, now: datetime) -> bool:
        return self.expires_at <= now

    def is_consumed(self) -> bool:
        return self.consumed_at is not None
```

- [ ] **Step 4: Write the token factory**

```python
# apps/identity-service/src/identity_service/infrastructure/tokens.py
"""Token-Erzeugung: Klartext geht in die Mail, Hash in die Datenbank."""

from __future__ import annotations

import hashlib
import secrets

__all__ = ["generate_token", "hash_token"]

#: 32 Bytes urlsafe ≈ 43 Zeichen. Raten ist damit kein Angriffsweg.
_TOKEN_BYTES = 32


def hash_token(raw: str) -> str:
    """SHA-256 hex. Kein Salt: der Klartext ist bereits hochentropisch, und ein
    Salt würde die Suche über den Hash unmöglich machen."""
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def generate_token() -> tuple[str, str]:
    raw = secrets.token_urlsafe(_TOKEN_BYTES)
    return raw, hash_token(raw)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `uv run pytest apps/identity-service/tests/unit/test_verification.py -v`
Expected: PASS (5 Tests)

- [ ] **Step 6: Commit**

```bash
git add apps/identity-service/src/identity_service/domain/verification.py \
        apps/identity-service/src/identity_service/infrastructure/tokens.py \
        apps/identity-service/tests/unit/test_verification.py
git commit -m "feat(identity): Verifikations-Token, nur als Hash gespeichert"
```

---

### Task 3: Company-Domäne und Freemail-Sperrliste

**Files:**
- Create: `apps/identity-service/src/identity_service/domain/company.py`
- Modify: `apps/identity-service/src/identity_service/domain/membership.py`
- Test: `apps/identity-service/tests/unit/test_company.py`

**Interfaces:**
- Consumes: `Email` aus `domain/value_objects.py`.
- Produces: `EmailDomain(raw: str)` mit `.value: str` und Klassenmethode `from_email(email: Email) -> EmailDomain`; `EmailDomain.is_public() -> bool`; `Company` (frozen: `id: UUID`, `name: str`, `domain: EmailDomain`) mit `Company.create(name, domain) -> Company`; Fehler `PublicEmailDomain`, `DomainAlreadyClaimed`, `AccountNotConfirmed`, `InvalidCompanyName`; `MembershipRole` (`ADMIN`/`MEMBER`) in `membership.py`.

- [ ] **Step 1: Write the failing test**

```python
# apps/identity-service/tests/unit/test_company.py
"""Die Firmendomain wird abgeleitet, nicht entgegengenommen."""

from __future__ import annotations

import pytest
from identity_service.domain.company import (
    Company,
    EmailDomain,
    InvalidCompanyName,
    PublicEmailDomain,
)
from identity_service.domain.value_objects import Email


def test_domain_is_derived_from_the_address() -> None:
    assert EmailDomain.from_email(Email("Anna@Firma.DE")).value == "firma.de"


def test_domain_is_lowercased_and_stripped() -> None:
    assert EmailDomain("  Firma.DE ").value == "firma.de"


@pytest.mark.parametrize("raw", ["gmail.com", "GMX.de", "web.de", "outlook.com"])
def test_public_providers_are_recognised(raw: str) -> None:
    assert EmailDomain(raw).is_public() is True


@pytest.mark.parametrize("raw", ["firma.de", "siemens.com", "mail.firma.de"])
def test_company_domains_are_not_public(raw: str) -> None:
    assert EmailDomain(raw).is_public() is False


def test_creating_a_company_on_a_public_domain_is_refused() -> None:
    with pytest.raises(PublicEmailDomain):
        Company.create(name="Nicht Google", domain=EmailDomain("gmail.com"))


def test_company_name_must_not_be_blank() -> None:
    with pytest.raises(InvalidCompanyName):
        Company.create(name="   ", domain=EmailDomain("firma.de"))


def test_company_name_is_trimmed() -> None:
    company = Company.create(name="  Firma GmbH  ", domain=EmailDomain("firma.de"))

    assert company.name == "Firma GmbH"
    assert company.domain.value == "firma.de"
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest apps/identity-service/tests/unit/test_company.py -v`
Expected: FAIL — `ModuleNotFoundError: identity_service.domain.company`

- [ ] **Step 3: Write the domain**

```python
# apps/identity-service/src/identity_service/domain/company.py
"""Unternehmen als Identität — Name und bewiesene Domain, sonst nichts.

Das Employer-Profil (Kultur, Benefits, Team, Karriereseite) gehört in den
companies-service aus Phase 4. Hier liegt nur, was der Tenant-Wechsel synchron
braucht.

Ein Unternehmen entsteht ausschließlich bewiesen: die Domain stammt aus der
bestätigten Adresse des Erstellers, nie aus einem Request. Deshalb gibt es
keinen unverifizierten Zustand.
"""

from __future__ import annotations

from dataclasses import dataclass
from uuid import UUID, uuid4

from worker_core import DomainError

from identity_service.domain.value_objects import Email

__all__ = [
    "PUBLIC_EMAIL_DOMAINS",
    "AccountNotConfirmed",
    "Company",
    "DomainAlreadyClaimed",
    "EmailDomain",
    "InvalidCompanyName",
    "PublicEmailDomain",
]

#: Absichtlich kurz und erweiterbar. Vollständigkeit ist nicht erreichbar; die
#: Liste verhindert die offensichtlichen Fälle, in denen jemand einen
#: Massenanbieter als Unternehmen beansprucht.
PUBLIC_EMAIL_DOMAINS: frozenset[str] = frozenset(
    {
        "aol.com",
        "freenet.de",
        "gmail.com",
        "googlemail.com",
        "gmx.at",
        "gmx.ch",
        "gmx.de",
        "gmx.net",
        "hotmail.com",
        "icloud.com",
        "mail.com",
        "me.com",
        "outlook.com",
        "proton.me",
        "protonmail.com",
        "t-online.de",
        "web.de",
        "yahoo.com",
        "yahoo.de",
        "yandex.com",
        "zoho.com",
    }
)


class PublicEmailDomain(DomainError):
    def __init__(self, domain: str) -> None:
        super().__init__(
            "public_email_domain",
            f"{domain!r} is a public email provider and cannot be claimed as a company",
        )


class DomainAlreadyClaimed(DomainError):
    def __init__(self, domain: str) -> None:
        super().__init__("domain_already_claimed", f"{domain!r} already belongs to a company")


class AccountNotConfirmed(DomainError):
    def __init__(self) -> None:
        super().__init__(
            "account_not_confirmed",
            "Confirm your email address before creating a company",
        )


class InvalidCompanyName(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_company_name", "A company name must not be empty")


@dataclass(frozen=True, slots=True)
class EmailDomain:
    value: str

    def __init__(self, raw: str) -> None:
        object.__setattr__(self, "value", raw.strip().lower())

    @classmethod
    def from_email(cls, email: Email) -> EmailDomain:
        # Email normalisiert bereits auf Kleinschreibung und garantiert genau
        # ein '@' über sein Muster.
        return cls(email.value.split("@", 1)[1])

    def is_public(self) -> bool:
        return self.value in PUBLIC_EMAIL_DOMAINS


@dataclass(frozen=True, slots=True)
class Company:
    id: UUID
    name: str
    domain: EmailDomain

    @classmethod
    def create(cls, *, name: str, domain: EmailDomain) -> Company:
        cleaned = name.strip()
        if not cleaned:
            raise InvalidCompanyName()
        if domain.is_public():
            raise PublicEmailDomain(domain.value)
        return cls(id=uuid4(), name=cleaned, domain=domain)
```

- [ ] **Step 4: Add the membership role**

```python
# apps/identity-service/src/identity_service/domain/membership.py
# oben ergänzen:
from enum import StrEnum

# in __all__ ergänzen: "MembershipRole"

class MembershipRole(StrEnum):
    """Durchgesetzt wird der Unterschied erst in Scheibe C (Einladungen); wer
    ein Unternehmen anlegt, ist ab jetzt aber bereits als ADMIN vermerkt."""

    ADMIN = "admin"
    MEMBER = "member"


# TenantMembership um das Feld erweitern:
#     role: MembershipRole
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `uv run pytest apps/identity-service/tests/unit/test_company.py -v`
Expected: PASS (7 Tests, inkl. parametrisierter)

- [ ] **Step 6: Commit**

```bash
git add apps/identity-service/src/identity_service/domain/company.py \
        apps/identity-service/src/identity_service/domain/membership.py \
        apps/identity-service/tests/unit/test_company.py
git commit -m "feat(identity): Company-Aggregat, EmailDomain, Freemail-Sperrliste"
```

---

### Task 4: `User` startet als PENDING und lässt sich aktivieren

**Files:**
- Modify: `apps/identity-service/src/identity_service/domain/user.py`
- Modify: `apps/identity-service/src/identity_service/domain/audit.py`
- Modify: `apps/identity-service/tests/unit/test_user.py`

**Interfaces:**
- Consumes: nichts.
- Produces: `User.register(...)` liefert `status = AccountStatus.PENDING`; `User.activate(now)` setzt `ACTIVE` und hängt `EmailVerified` an; `AlreadyActive` (DomainError); `AuditAction.EMAIL_VERIFIED`, `AuditAction.COMPANY_CREATED`.

- [ ] **Step 1: Write the failing test**

```python
# apps/identity-service/tests/unit/test_user.py — anhängen

async def test_registration_starts_pending() -> None:
    user = User.register(
        email=Email("p@example.com"),
        password_hash=PasswordHash("$2b$12$p"),
        display_name="P",
        now=_now(),
    )

    assert user.status is AccountStatus.PENDING


async def test_activate_makes_the_account_usable_and_emits_an_event() -> None:
    user = User.register(
        email=Email("q@example.com"),
        password_hash=PasswordHash("$2b$12$q"),
        display_name="Q",
        now=_now(),
    )
    user.pull_events()

    user.activate(now=_now())

    assert user.status is AccountStatus.ACTIVE
    events = user.pull_events()
    assert [type(e).__name__ for e in events] == ["EmailVerified"]


async def test_activating_twice_is_refused() -> None:
    user = User.register(
        email=Email("r@example.com"),
        password_hash=PasswordHash("$2b$12$r"),
        display_name="R",
        now=_now(),
    )
    user.activate(now=_now())

    with pytest.raises(AlreadyActive):
        user.activate(now=_now())


async def test_a_pending_account_is_refused_with_a_distinguishable_error() -> None:
    # EmailNotConfirmed statt AccountDisabled: der Router braucht den
    # Unterschied, um 403 statt 401 zu antworten (Spec §4.4). Ein gesperrtes
    # Konto und ein unbestätigtes sind nicht dasselbe Problem.
    user = User.register(
        email=Email("s@example.com"),
        password_hash=PasswordHash("$2b$12$s"),
        display_name="S",
        now=_now(),
    )

    with pytest.raises(EmailNotConfirmed):
        user.assert_can_log_in()


async def test_a_disabled_account_stays_generic() -> None:
    user = User.register(
        email=Email("t@example.com"),
        password_hash=PasswordHash("$2b$12$t"),
        display_name="T",
        now=_now(),
    )
    user.activate(now=_now())
    user.status = AccountStatus.DISABLED

    with pytest.raises(AccountDisabled):
        user.assert_can_log_in()
```

Imports in der Datei ergänzen: `import pytest`, `AlreadyActive`, `AccountDisabled`, `EmailNotConfirmed`.

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest apps/identity-service/tests/unit/test_user.py -v`
Expected: FAIL — `ImportError: cannot import name 'AlreadyActive'` und `assert PENDING is ACTIVE`

- [ ] **Step 3: Change the aggregate**

```python
# apps/identity-service/src/identity_service/domain/user.py

# in __all__ ergänzen: "AlreadyActive", "EmailNotConfirmed", "EmailVerified"

class AlreadyActive(DomainError):
    def __init__(self) -> None:
        super().__init__("already_active", "This account is already confirmed")


class EmailNotConfirmed(DomainError):
    """Unbestätigt ist nicht gesperrt. Der Router bildet das auf 403 ab, damit
    jemand, der nur die Mail übersehen hat, nicht in einer Sackgasse landet."""

    def __init__(self) -> None:
        super().__init__("email_not_confirmed", "Confirm your email address to sign in")


# assert_can_log_in ersetzen:
#
#     def assert_can_log_in(self) -> None:
#         if self.status is AccountStatus.PENDING:
#             raise EmailNotConfirmed()
#         if self.status is not AccountStatus.ACTIVE:
#             raise AccountDisabled()


@dataclass(frozen=True, slots=True)
class EmailVerified(DomainEvent):
    user_id: UUID = field(kw_only=True)

    def to_dict(self) -> dict[str, object]:
        base = _event_dict(self)
        base["event_id"] = str(self.event_id)
        base["user_id"] = str(self.user_id)
        base["occurred_at"] = self.occurred_at.isoformat()
        return base


# in User.register(...) den Status ändern:
#     status=AccountStatus.PENDING,  # bestätigt wird per E-Mail-Token
#
# und die Methode ergänzen:

    def activate(self, *, now: datetime) -> None:
        """Schaltet das Konto nach bestätigter E-Mail frei."""
        if self.status is AccountStatus.ACTIVE:
            raise AlreadyActive()
        self.status = AccountStatus.ACTIVE
        self._events.append(EmailVerified(user_id=self.id.value, occurred_at=now))
```

- [ ] **Step 4: Add the audit actions**

```python
# apps/identity-service/src/identity_service/domain/audit.py
# in AuditAction ergänzen:

    EMAIL_VERIFIED = "email_verified"
    COMPANY_CREATED = "company_created"
```

- [ ] **Step 5: Repair the tests this change breaks — in this task**

`handle_register` liefert ab jetzt ein `PENDING`-Konto, also scheitert jeder
bestehende Test in `test_commands.py`, der nach dem Registrieren sofort
anmeldet (Login-, Refresh- und Revoke-Tests, rund ein halbes Dutzend).

**Jeder Commit bleibt grün** — ein rot committeter Task würde CI brechen und den
nächsten Task auf einer kaputten Basis starten. Deshalb hier ein Helfer in
`test_commands.py` einführen und an allen betroffenen Stellen einsetzen:

```python
async def _register_active(repos: dict[str, Any], deps: dict[str, Any], email: str) -> User:
    """Registrieren und sofort freischalten.

    Die E-Mail-Bestätigung hat ihre eigenen Tests; diese hier prüfen Login,
    Refresh und Revoke und wollen nur ein benutzbares Konto.
    """
    res = await handle_register(
        RegisterUserCommand(email=email, password="strongpassword1", display_name="X"),
        deps=deps,
        repos=repos,
    )
    user: User = res.value
    user.activate(now=deps["clock"].now())
    return user
```

Die betroffenen Tests rufen statt `handle_register(...)` künftig
`_register_active(...)` auf. **Nicht** anpassen: die Registrierungstests selbst —
die sollen `PENDING` sehen.

- [ ] **Step 6: Run the identity unit tests**

Run: `uv run pytest apps/identity-service/tests/unit -v`
Expected: PASS, **keine** Fehler. Wenn hier etwas rot ist, ist der Task nicht fertig.

- [ ] **Step 7: Commit**

```bash
git add apps/identity-service/src/identity_service/domain/user.py \
        apps/identity-service/src/identity_service/domain/audit.py \
        apps/identity-service/tests/unit/test_user.py \
        apps/identity-service/tests/unit/test_commands.py
git commit -m "feat(identity): Konten starten PENDING und werden per activate() freigeschaltet"
```

---

### Task 5: Migration 0003

**Files:**
- Create: `apps/identity-service/migrations/versions/0003_verification_and_companies.py`
- Modify: `apps/identity-service/src/identity_service/infrastructure/database/models.py`
- Test: `apps/identity-service/tests/integration/test_migrations.py` (erweitern)

**Interfaces:**
- Produces: Tabellen `tenants(id, name, domain, created_at)` und `email_verification_tokens(...)`; `user_tenant_memberships.role` + Fremdschlüssel auf `tenants(id)`; Modelle `TenantModel`, `EmailVerificationTokenModel`.

**Achtung:** Der Fremdschlüssel kann an bestehenden Daten scheitern — in Entwicklung wurden `user_tenant_memberships`-Zeilen von Hand eingefügt, zu denen es keine `tenants`-Zeile gibt. Die Migration legt vorher Platzhalter an, statt zu löschen.

- [ ] **Step 1: Write the migration**

```python
# apps/identity-service/migrations/versions/0003_verification_and_companies.py
"""email verification tokens, tenants table, membership role + fk

Revision ID: 0003_verification_and_companies
Revises: 0002_tenant_optional_memberships
Create Date: 2026-08-01
"""

from __future__ import annotations

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects.postgresql import CITEXT
from sqlalchemy.dialects.postgresql import UUID as PG_UUID

revision: str = "0003_verification_and_companies"
down_revision: str | None = "0002_tenant_optional_memberships"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'email_verified'")
    op.execute("ALTER TYPE audit_action ADD VALUE IF NOT EXISTS 'company_created'")

    op.create_table(
        "tenants",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column("name", sa.Text, nullable=False),
        # Die Domain IST der Nachweis: eindeutig, damit sie nur einmal
        # beansprucht werden kann. CITEXT, weil Domains case-insensitiv sind.
        sa.Column("domain", CITEXT, nullable=False, unique=True),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )

    op.create_table(
        "email_verification_tokens",
        sa.Column("id", PG_UUID(as_uuid=True), primary_key=True),
        sa.Column(
            "user_id",
            PG_UUID(as_uuid=True),
            sa.ForeignKey("users.id", ondelete="CASCADE"),
            nullable=False,
        ),
        # sha256 hex — nie der Klartext.
        sa.Column("token_hash", sa.String(64), nullable=False, unique=True),
        sa.Column("purpose", sa.String(32), nullable=False),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("consumed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()
        ),
    )
    op.create_index(
        "ix_verification_user_purpose", "email_verification_tokens", ["user_id", "purpose"]
    )

    op.add_column(
        "user_tenant_memberships",
        sa.Column("role", sa.String(16), nullable=False, server_default="member"),
    )

    # Vor dem Constraint: für jede verwaiste tenant_id eine Platzhalter-Zeile.
    # .invalid ist per RFC 2606 garantiert nicht auflösbar und kollidiert daher
    # nie mit einer echten Domain. Nichts wird stillschweigend gelöscht.
    op.execute(
        """
        INSERT INTO tenants (id, name, domain, created_at)
        SELECT DISTINCT m.tenant_id, 'Unbekannt (migriert)', m.tenant_id || '.invalid', now()
        FROM user_tenant_memberships m
        WHERE NOT EXISTS (SELECT 1 FROM tenants t WHERE t.id = m.tenant_id)
        """
    )
    op.create_foreign_key(
        "fk_membership_tenant",
        "user_tenant_memberships",
        "tenants",
        ["tenant_id"],
        ["id"],
        ondelete="CASCADE",
    )


def downgrade() -> None:
    op.drop_constraint("fk_membership_tenant", "user_tenant_memberships", type_="foreignkey")
    op.drop_column("user_tenant_memberships", "role")
    op.drop_index("ix_verification_user_purpose", table_name="email_verification_tokens")
    op.drop_table("email_verification_tokens")
    op.drop_table("tenants")
    # audit_action behält die neuen Labels: PostgreSQL kann Enum-Werte nicht
    # entfernen, ohne den Typ neu zu bauen, und ungenutzte Labels schaden nicht.
```

- [ ] **Step 2: Add the models**

```python
# apps/identity-service/src/identity_service/infrastructure/database/models.py
# in __all__ ergänzen: "EmailVerificationTokenModel", "TenantModel"
# Import ergänzen: from sqlalchemy.dialects.postgresql import CITEXT (bereits vorhanden)

class TenantModel(Base):
    __tablename__ = "tenants"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True, default=uuid4)
    name: Mapped[str] = mapped_column(Text, nullable=False)
    domain: Mapped[str] = mapped_column(CITEXT, nullable=False, unique=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utc_now
    )


class EmailVerificationTokenModel(Base):
    __tablename__ = "email_verification_tokens"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True, default=uuid4)
    user_id: Mapped[UUID] = mapped_column(
        PG_UUID(as_uuid=True), ForeignKey("users.id", ondelete="CASCADE"), nullable=False
    )
    token_hash: Mapped[str] = mapped_column(String(64), nullable=False, unique=True)
    purpose: Mapped[str] = mapped_column(String(32), nullable=False)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    consumed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utc_now
    )


# In UserTenantMembershipModel ergänzen:
#     role: Mapped[str] = mapped_column(String(16), nullable=False, default="member")
# und tenant_id um den ForeignKey erweitern:
#     tenant_id: Mapped[UUID] = mapped_column(
#         PG_UUID(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"),
#         nullable=False, index=True,
#     )
```

- [ ] **Step 3: Start Docker and run the migration test**

```bash
open -a Docker
until docker info >/dev/null 2>&1; do sleep 2; done
uv run pytest apps/identity-service/tests/integration/test_migrations.py -v
```

Expected: PASS. **Wenn die Zahl der übersprungenen Tests steigt statt der bestandenen, läuft Docker nicht — dann ist nichts verifiziert.**

- [ ] **Step 4: Add a migration test for the orphan case**

```python
# apps/identity-service/tests/integration/test_migrations.py — anhängen

async def test_orphaned_membership_gets_a_placeholder_tenant(postgres_url: str) -> None:
    """Von Hand eingefügte Mitgliedschaften dürfen die Migration nicht sprengen
    und nicht stillschweigend verschwinden.

    Genau dieser Fall existiert in jeder Entwicklungsdatenbank: der Smoke-Test
    hat Mitgliedschaften per INSERT angelegt, bevor es `tenants` gab.
    """
    from uuid import uuid4

    from alembic import command
    from alembic.config import Config
    from sqlalchemy import text
    from sqlalchemy.ext.asyncio import create_async_engine

    cfg = Config(str(_MIGRATIONS_ROOT / "alembic.ini"))
    cfg.set_main_option("script_location", str(_MIGRATIONS_ROOT / "migrations"))
    cfg.set_main_option("sqlalchemy.url", postgres_url)

    command.downgrade(cfg, "base")
    command.upgrade(cfg, "0002_tenant_optional_memberships")

    user_id, orphan_tenant = uuid4(), uuid4()
    engine = create_async_engine(postgres_url)
    try:
        async with engine.begin() as conn:
            await conn.execute(
                text(
                    "INSERT INTO users (id, email, password_hash, display_name, status, roles) "
                    "VALUES (:u, 'orphan@example.com', 'x', 'O', 'active', '[]'::jsonb)"
                ),
                {"u": str(user_id)},
            )
            await conn.execute(
                text(
                    "INSERT INTO user_tenant_memberships (id, user_id, tenant_id) "
                    "VALUES (gen_random_uuid(), :u, :t)"
                ),
                {"u": str(user_id), "t": str(orphan_tenant)},
            )

        command.upgrade(cfg, "head")

        async with engine.connect() as conn:
            kept = (
                await conn.execute(
                    text("SELECT count(*) FROM user_tenant_memberships WHERE tenant_id = :t"),
                    {"t": str(orphan_tenant)},
                )
            ).scalar_one()
            placeholder = (
                await conn.execute(
                    text("SELECT domain FROM tenants WHERE id = :t"), {"t": str(orphan_tenant)}
                )
            ).scalar_one()
    finally:
        await engine.dispose()

    assert kept == 1, "die Mitgliedschaft wurde stillschweigend gelöscht"
    assert placeholder.endswith(".invalid")
```

`_MIGRATIONS_ROOT` ist der bereits in dieser Datei vorhandene Pfad auf
`apps/identity-service` — die bestehenden Migrationstests benutzen ihn; falls er
dort anders heißt, den vorhandenen Namen verwenden statt einen neuen einzuführen.

- [ ] **Step 5: Commit**

```bash
git add apps/identity-service/migrations/versions/0003_verification_and_companies.py \
        apps/identity-service/src/identity_service/infrastructure/database/models.py \
        apps/identity-service/tests/integration/test_migrations.py
git commit -m "feat(identity-db): tenants, Verifikations-Token, Membership-Rolle und FK"
```

---

### Task 6: Repositories

**Files:**
- Modify: `apps/identity-service/src/identity_service/application/ports.py`
- Modify: `apps/identity-service/src/identity_service/infrastructure/database/repositories.py`
- Modify: `apps/identity-service/src/identity_service/infrastructure/compose.py`
- Test: `apps/identity-service/tests/integration/test_repository_roundtrip.py` (erweitern)

**Interfaces:**
- Produces:
  - `VerificationTokenRepository`: `add(token: VerificationToken) -> None`, `get_by_hash(token_hash: str) -> VerificationToken | None`, `consume(token_id: UUID, at: datetime) -> None`, `consume_open_for(user_id: UUID, purpose: TokenPurpose, at: datetime) -> None`
  - `CompanyRepository`: `add(company: Company) -> None`, `get_by_domain(domain: str) -> Company | None`, `get_by_id(company_id: UUID) -> Company | None`
  - `MembershipRepository` erweitert: `add(user_id: UUID, tenant_id: UUID, role: MembershipRole) -> None`, `list_for_user_detailed(user_id: UUID) -> list[MembershipView]`
  - `MembershipView` (frozen dataclass in `domain/membership.py`: `tenant_id: UUID`, `name: str`, `domain: str`, `role: MembershipRole`)
  - `repos`-Schlüssel: `"tokens"`, `"companies"` zusätzlich zu den bestehenden.

- [ ] **Step 1: Write the failing integration test**

```python
# apps/identity-service/tests/integration/test_repository_roundtrip.py — anhängen

async def test_verification_token_roundtrip_and_single_use(session: object) -> None:
    from datetime import UTC, datetime, timedelta

    from identity_service.domain.verification import TokenPurpose, VerificationToken
    from identity_service.infrastructure.database.repositories import (
        SqlAlchemyVerificationTokenRepository,
    )

    user_repo = SqlAlchemyUserRepository(session)  # type: ignore[arg-type]
    token_repo = SqlAlchemyVerificationTokenRepository(session)  # type: ignore[arg-type]
    user = User.register(
        email=Email("tok@example.com"),
        password_hash=PasswordHash("$2b$12$t"),
        display_name="Tok",
        now=datetime.now(UTC),
    )
    await user_repo.add(user)
    token = VerificationToken(
        token_id=uuid4(),
        user_id=user.id.value,
        token_hash="a" * 64,
        purpose=TokenPurpose.EMAIL_VERIFY,
        expires_at=datetime.now(UTC) + timedelta(hours=24),
        consumed_at=None,
    )
    await token_repo.add(token)
    await session.commit()  # type: ignore[attr-defined]

    found = await token_repo.get_by_hash("a" * 64)
    assert found is not None and found.is_consumed() is False

    await token_repo.consume(found.token_id, datetime.now(UTC))
    await session.commit()  # type: ignore[attr-defined]

    again = await token_repo.get_by_hash("a" * 64)
    assert again is not None and again.is_consumed() is True


async def test_company_roundtrip_by_domain(session: object) -> None:
    from identity_service.domain.company import Company, EmailDomain
    from identity_service.infrastructure.database.repositories import SqlAlchemyCompanyRepository

    repo = SqlAlchemyCompanyRepository(session)  # type: ignore[arg-type]
    company = Company.create(name="Firma GmbH", domain=EmailDomain("firma-roundtrip.de"))

    await repo.add(company)
    await session.commit()  # type: ignore[attr-defined]

    # CITEXT: die Suche ist unabhängig von der Schreibweise.
    found = await repo.get_by_domain("FIRMA-ROUNDTRIP.DE")
    assert found is not None and found.name == "Firma GmbH"
```

- [ ] **Step 2: Run to verify it fails**

Run: `uv run pytest apps/identity-service/tests/integration/test_repository_roundtrip.py -v`
Expected: FAIL — `ImportError: cannot import name 'SqlAlchemyVerificationTokenRepository'`

- [ ] **Step 3: Implement the repositories**

```python
# apps/identity-service/src/identity_service/infrastructure/database/repositories.py

class SqlAlchemyVerificationTokenRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(self, token: VerificationToken) -> None:
        self._session.add(
            EmailVerificationTokenModel(
                id=token.token_id,
                user_id=token.user_id,
                token_hash=token.token_hash,
                purpose=token.purpose.value,
                expires_at=token.expires_at,
                consumed_at=token.consumed_at,
            )
        )
        await self._session.flush()

    async def get_by_hash(self, token_hash: str) -> VerificationToken | None:
        stmt = select(EmailVerificationTokenModel).where(
            EmailVerificationTokenModel.token_hash == token_hash
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        if row is None:
            return None
        return VerificationToken(
            token_id=row.id,
            user_id=row.user_id,
            token_hash=row.token_hash,
            purpose=TokenPurpose(row.purpose),
            expires_at=row.expires_at,
            consumed_at=row.consumed_at,
        )

    async def consume(self, token_id: UUID, at: datetime) -> None:
        row = await self._session.get(EmailVerificationTokenModel, token_id)
        if row is not None and row.consumed_at is None:
            row.consumed_at = at
            await self._session.flush()

    async def consume_open_for(self, user_id: UUID, purpose: TokenPurpose, at: datetime) -> None:
        """Entwertet offene Tokens, bevor ein neues ausgestellt wird — sonst
        blieben beliebig viele gültige Links gleichzeitig in Umlauf."""
        stmt = select(EmailVerificationTokenModel).where(
            EmailVerificationTokenModel.user_id == user_id,
            EmailVerificationTokenModel.purpose == purpose.value,
            EmailVerificationTokenModel.consumed_at.is_(None),
        )
        for row in (await self._session.execute(stmt)).scalars().all():
            row.consumed_at = at
        await self._session.flush()


class SqlAlchemyCompanyRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def add(self, company: Company) -> None:
        self._session.add(
            TenantModel(id=company.id, name=company.name, domain=company.domain.value)
        )
        await self._session.flush()

    async def get_by_domain(self, domain: str) -> Company | None:
        stmt = select(TenantModel).where(TenantModel.domain == domain)
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return self._to_domain(row) if row is not None else None

    async def get_by_id(self, company_id: UUID) -> Company | None:
        row = await self._session.get(TenantModel, company_id)
        return self._to_domain(row) if row is not None else None

    @staticmethod
    def _to_domain(row: TenantModel) -> Company:
        return Company(id=row.id, name=row.name, domain=EmailDomain(row.domain))
```

`SqlAlchemyMembershipRepository` um `add` und `list_for_user_detailed` erweitern (JOIN auf `tenants`, liefert `MembershipView`).

- [ ] **Step 4: Wire them into the composition root**

```python
# apps/identity-service/src/identity_service/infrastructure/compose.py — in request_scope:
            "tokens": SqlAlchemyVerificationTokenRepository(uow.session),
            "companies": SqlAlchemyCompanyRepository(uow.session),

# in compose_infrastructure den Mailer ergänzen:
#     "mailer": SmtpMailer(host=settings.smtp_host, port=settings.smtp_port,
#                          mail_from=settings.mail_from,
#                          username=settings.smtp_username,
#                          password=settings.smtp_password.get_secret_value()
#                                   if settings.smtp_password else None,
#                          use_tls=settings.smtp_use_tls)
```

- [ ] **Step 5: Run tests**

Run: `uv run pytest apps/identity-service/tests/integration/test_repository_roundtrip.py -v`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add apps/identity-service/src/identity_service/
git commit -m "feat(identity): Repositories für Token und Unternehmen"
```

---

### Task 7: Registrierung mit Token und Mail

**Files:**
- Modify: `apps/identity-service/src/identity_service/application/commands.py`
- Modify: `apps/identity-service/tests/unit/test_commands.py`

**Interfaces:**
- Consumes: `Mailer`, `VerificationTokenRepository`, `generate_token`, `TokenPurpose`.
- Produces: `handle_register` legt Token an und beauftragt die Mail; **antwortet auch bei bekannter Adresse erfolgreich**; `ResendVerificationCommand { email }` + `handle_resend`; `VerifyEmailCommand { token }` + `handle_verify_email`; `deps["mailer"]`, `deps["settings"].public_web_url`.

- [ ] **Step 1: Write the failing tests**

```python
# apps/identity-service/tests/unit/test_commands.py — anhängen

class _FakeMailer:
    def __init__(self) -> None:
        self.sent: list[tuple[str, str, str]] = []

    async def send(self, *, to: str, subject: str, body: str) -> None:
        self.sent.append((to, subject, body))


class _FakeTokens_:
    def __init__(self) -> None:
        self.rows: dict[str, Any] = {}

    async def add(self, token: Any) -> None:
        self.rows[token.token_hash] = token

    async def get_by_hash(self, token_hash: str) -> Any:
        return self.rows.get(token_hash)

    async def consume(self, token_id: Any, at: Any) -> None:
        for h, t in list(self.rows.items()):
            if t.token_id == token_id:
                self.rows[h] = replace(t, consumed_at=at)

    async def consume_open_for(self, user_id: Any, purpose: Any, at: Any) -> None:
        for h, t in list(self.rows.items()):
            if t.user_id == user_id and t.consumed_at is None:
                self.rows[h] = replace(t, consumed_at=at)


async def test_registration_creates_a_token_and_sends_a_mail() -> None:
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)

    res = await handle_register(
        RegisterUserCommand(email="neu@example.com", password="strongpassword1", display_name="N"),
        deps=deps,
        repos=repos,
    )

    assert is_success(res)
    assert len(repos["tokens"].rows) == 1
    assert len(mailer.sent) == 1
    to, _subject, body = mailer.sent[0]
    assert to == "neu@example.com"
    # Der Klartext-Token steht in der Mail, nicht in der Datenbank.
    assert "/verify?token=" in body


async def test_registering_a_known_address_reports_success_and_warns_the_owner() -> None:
    # Kein Enumerationskanal: die Antwort ist identisch (product-scope.md,
    # Discoverability liegt bei der Person, nicht beim Anfragenden).
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)
    cmd = RegisterUserCommand(
        email="doppelt@example.com", password="strongpassword1", display_name="D"
    )
    await handle_register(cmd, deps=deps, repos=repos)
    mailer.sent.clear()

    second = await handle_register(cmd, deps=deps, repos=repos)

    assert is_success(second)
    assert len(repos["users"].by_email) == 1
    assert len(mailer.sent) == 1
    assert "versucht" in mailer.sent[0][2].lower()


def _raw_token_from(mailer: _FakeMailer) -> str:
    """Der Klartext existiert nur in der Mail — genau wie in Produktion."""
    body = mailer.sent[-1][2]
    return body.split("/verify?token=", 1)[1].split()[0]


async def test_verify_activates_the_account_and_consumes_the_token() -> None:
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)
    await handle_register(
        RegisterUserCommand(email="v@example.com", password="strongpassword1", display_name="V"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(mailer)
    repos["audit"].events.clear()

    res = await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    assert is_success(res)
    user = await repos["users"].get_by_email("v@example.com")
    assert user is not None and user.status is AccountStatus.ACTIVE
    assert repos["tokens"].rows[hash_token(raw)].is_consumed() is True
    assert repos["audit"].events[-1].action is AuditAction.EMAIL_VERIFIED


async def test_a_consumed_token_cannot_be_reused() -> None:
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)
    await handle_register(
        RegisterUserCommand(email="w@example.com", password="strongpassword1", display_name="W"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(mailer)
    await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    second = await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    assert not is_success(second)
    assert fail_err(second).code == "token_invalid"


async def test_an_expired_token_is_refused() -> None:
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    clock = _Clock()
    deps = _deps_with(mailer=mailer, clock=clock)
    await handle_register(
        RegisterUserCommand(email="x@example.com", password="strongpassword1", display_name="X"),
        deps=deps,
        repos=repos,
    )
    raw = _raw_token_from(mailer)
    clock.advance(timedelta(hours=25))

    res = await handle_verify_email(VerifyEmailCommand(token=raw), deps=deps, repos=repos)

    assert not is_success(res)
    assert fail_err(res).code == "token_expired"


async def test_an_unknown_token_is_refused() -> None:
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=_FakeMailer())

    res = await handle_verify_email(
        VerifyEmailCommand(token="niemals-ausgestellt"), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "token_invalid"


async def test_resend_invalidates_the_previous_token() -> None:
    # Sonst blieben beliebig viele gültige Links gleichzeitig in Umlauf, und der
    # älteste — womöglich fehlgeleitete — funktionierte weiter (Spec §4.3).
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)
    await handle_register(
        RegisterUserCommand(email="y@example.com", password="strongpassword1", display_name="Y"),
        deps=deps,
        repos=repos,
    )
    first = _raw_token_from(mailer)

    await handle_resend(ResendVerificationCommand(email="y@example.com"), deps=deps, repos=repos)
    second = _raw_token_from(mailer)

    assert first != second
    assert repos["tokens"].rows[hash_token(first)].is_consumed() is True
    assert repos["tokens"].rows[hash_token(second)].is_consumed() is False


async def test_resend_for_an_unknown_address_reports_success_without_sending() -> None:
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)

    res = await handle_resend(
        ResendVerificationCommand(email="niemand@example.com"), deps=deps, repos=repos
    )

    assert is_success(res)
    assert mailer.sent == []


async def test_resend_for_an_already_active_account_sends_nothing() -> None:
    mailer = _FakeMailer()
    repos = _repos_with(tokens=_FakeTokens_())
    deps = _deps_with(mailer=mailer)
    await handle_register(
        RegisterUserCommand(email="z@example.com", password="strongpassword1", display_name="Z"),
        deps=deps,
        repos=repos,
    )
    await handle_verify_email(
        VerifyEmailCommand(token=_raw_token_from(mailer)), deps=deps, repos=repos
    )
    mailer.sent.clear()

    res = await handle_resend(ResendVerificationCommand(email="z@example.com"), deps=deps, repos=repos)

    assert is_success(res)
    assert mailer.sent == []
```

**Die Helfer**, einmal oben in der Datei zu definieren:

```python
_USER_ID = UUID("00000000-0000-0000-0000-0000000000a1")
_OTHER_USER_ID = UUID("00000000-0000-0000-0000-0000000000a2")


def _repos_with(**overrides: Any) -> dict[str, Any]:
    repos: dict[str, Any] = {
        "users": _FakeUsers(),
        "memberships": _FakeMemberships(),
        "sessions": _FakeSessions(),
        "audit": _FakeAudit(),
        "tokens": _FakeTokens_(),
        "companies": _FakeCompanies(),
    }
    repos.update(overrides)
    return repos


def _deps_with(**overrides: Any) -> dict[str, Any]:
    deps: dict[str, Any] = {
        "hasher": _StupidHasher(),
        "tokens": _FakeTokens(),        # der JWT-Fake, nicht das Token-Repo
        "clock": _Clock(),
        "eventbus": _Bus(),
        "settings": _Settings(),
        "mailer": _FakeMailer(),
    }
    deps.update(overrides)
    return deps
```

`_Settings` erhält zusätzlich `public_web_url = "http://localhost:5173"`.
`_FakeCompanies` spiegelt `CompanyRepository` über ein `dict[str, Company]` nach
Domain, `_FakeMemberships` erhält ein `added: list[TenantMembership]`.

**Namenskollision beachten:** `_FakeTokens` ist bereits der JWT-Aussteller-Fake.
Das Token-*Repository* heißt deshalb `_FakeTokens_`; beim Umsetzen beide sauber
benennen (`_FakeJwt` und `_FakeTokenRepo` wären besser — dann konsequent
umbenennen).

- [ ] **Step 2: Run to verify they fail**

Run: `uv run pytest apps/identity-service/tests/unit/test_commands.py -v -k "token or resend or verify or known_address"`
Expected: FAIL

- [ ] **Step 3: Implement the handlers**

`handle_register` erhält nach dem bestehenden Ablauf:

```python
    existing = await repos["users"].get_by_email(cmd.email)
    if existing is not None:
        # Kein zweites Konto — aber dieselbe Antwort wie im Normalfall, damit
        # der Endpunkt nicht verrät, wer hier ein Konto hat. Der echte Besitzer
        # erfährt von dem Versuch.
        await _send_duplicate_notice(existing, deps)
        return Result.ok(None)
```

und für den Normalfall nach dem Anlegen des Users:

```python
    raw, hashed = generate_token()
    await repos["tokens"].add(
        VerificationToken(
            token_id=uuid4(),
            user_id=user.id.value,
            token_hash=hashed,
            purpose=TokenPurpose.EMAIL_VERIFY,
            expires_at=now + timedelta(hours=24),
            consumed_at=None,
        )
    )
```

Der Versand passiert **nach** dem `return` der UoW — der Router ruft dafür einen zweiten, transaktionsfreien Schritt auf (`deps["mailer"].send(...)`), gekapselt in `_dispatch_mail(...)` mit `try/except` und `logger.exception`. Begründung steht in Spec §5.

- [ ] **Step 4: Run tests**

Run: `uv run pytest apps/identity-service/tests/unit -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/identity-service/src/identity_service/application/commands.py \
        apps/identity-service/tests/unit/test_commands.py
git commit -m "feat(identity): Registrierung erzeugt Token und Mail, ohne Adressen preiszugeben"
```

---

### Task 8: Contracts-DTOs

**Files:**
- Create: `packages/worker-contracts/src/worker_contracts/identity.py`
- Modify: `packages/worker-contracts/src/worker_contracts/__init__.py`
- Test: `packages/worker-contracts/tests/test_identity_contracts.py`

**Interfaces:**
- Produces: `RegisterUserV1 {email, password, display_name}`, `VerifyEmailV1 {token}`, `ResendVerificationV1 {email}`, `CreateCompanyV1 {name}`, `CompanyV1 {id, name, domain}`, `MembershipV1 {id, name, domain, role}`.

- [ ] **Step 1: Write the failing test**

```python
# packages/worker-contracts/tests/test_identity_contracts.py
"""Die Auth-DTOs sind ein versionierter Vertrag (ADR-0004 §1)."""

from __future__ import annotations

import pytest
from pydantic import ValidationError
from worker_contracts import CreateCompanyV1, RegisterUserV1, VerifyEmailV1


def test_register_has_no_tenant_field() -> None:
    # Ein Tenant ist ein Unternehmen und wird nie vom Client geliefert (ADR-0017).
    assert "tenant_id" not in RegisterUserV1.model_fields


def test_create_company_takes_only_a_name() -> None:
    # Die Domain wird serverseitig abgeleitet — sie darf gar nicht sendbar sein.
    assert set(CreateCompanyV1.model_fields) == {"name"}


def test_company_name_length_is_bounded() -> None:
    with pytest.raises(ValidationError):
        CreateCompanyV1(name="")
    with pytest.raises(ValidationError):
        CreateCompanyV1(name="x" * 201)


def test_verify_requires_a_token() -> None:
    with pytest.raises(ValidationError):
        VerifyEmailV1(token="")
```

- [ ] **Step 2: Run to verify it fails**

Run: `uv run pytest packages/worker-contracts/tests/test_identity_contracts.py -v`
Expected: FAIL — `ImportError: cannot import name 'CreateCompanyV1'`

- [ ] **Step 3: Write the DTOs**

```python
# packages/worker-contracts/src/worker_contracts/identity.py
"""Versionierte Boundary-DTOs für identity-service (ADR-0004 §1).

`CreateCompanyV1` trägt bewusst **kein** Domain-Feld: die Firmendomain wird aus
der bestätigten Adresse des Erstellers abgeleitet. Was der Client nicht senden
kann, kann er nicht fälschen.
"""

from __future__ import annotations

from uuid import UUID

from pydantic import BaseModel, Field

__all__ = [
    "CompanyV1",
    "CreateCompanyV1",
    "MembershipV1",
    "RegisterUserV1",
    "ResendVerificationV1",
    "VerifyEmailV1",
]


class RegisterUserV1(BaseModel):
    email: str = Field(..., min_length=3, max_length=320)
    password: str = Field(..., min_length=1, max_length=1024)
    display_name: str = Field(..., min_length=1, max_length=255)


class VerifyEmailV1(BaseModel):
    token: str = Field(..., min_length=1, max_length=512)


class ResendVerificationV1(BaseModel):
    email: str = Field(..., min_length=3, max_length=320)


class CreateCompanyV1(BaseModel):
    name: str = Field(..., min_length=1, max_length=200)


class CompanyV1(BaseModel):
    id: UUID
    name: str
    domain: str


class MembershipV1(BaseModel):
    id: UUID
    name: str
    domain: str
    role: str
```

- [ ] **Step 4: Export them**

In `worker_contracts/__init__.py` importieren und in `__all__` alphabetisch einsortieren (ruff `RUF022` erzwingt die Reihenfolge — `uv run ruff check --fix` erledigt es).

- [ ] **Step 5: Run tests**

Run: `uv run pytest packages/worker-contracts -v`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add packages/worker-contracts/
git commit -m "feat(contracts): Identity-DTOs für Registrierung, Bestätigung und Unternehmen"
```

---

### Task 9: Unternehmen anlegen (Handler)

**Files:**
- Modify: `apps/identity-service/src/identity_service/application/commands.py`
- Modify: `apps/identity-service/tests/unit/test_commands.py`

**Interfaces:**
- Consumes: `CompanyRepository`, `MembershipRepository.add`, `Company`, `EmailDomain`, `MembershipRole`.
- Produces: `CreateCompanyCommand { user_id: UUID, name: str }` → `Result[Company]`; `ListMembershipsQuery { user_id: UUID }` → `Result[list[MembershipView]]`.

- [ ] **Step 1: Write the failing tests**

```python
# apps/identity-service/tests/unit/test_commands.py — anhängen

async def test_creating_a_company_derives_the_domain_and_makes_the_creator_admin() -> None:
    repos, deps = _company_fixture(email="anna@firma.de", status=AccountStatus.ACTIVE)

    res = await handle_create_company(
        CreateCompanyCommand(user_id=_USER_ID, name="Firma GmbH"), deps=deps, repos=repos
    )

    assert is_success(res)
    assert res.value.domain.value == "firma.de"
    membership = repos["memberships"].added[0]
    assert membership.role is MembershipRole.ADMIN
    audit = repos["audit"].events[-1]
    assert audit.action is AuditAction.COMPANY_CREATED
    # Die Handlung betrifft ein Unternehmen, also trägt die Audit-Zeile es auch.
    assert audit.tenant_id == res.value.id


async def test_a_pending_account_cannot_create_a_company() -> None:
    repos, deps = _company_fixture(email="anna@firma.de", status=AccountStatus.PENDING)

    res = await handle_create_company(
        CreateCompanyCommand(user_id=_USER_ID, name="Firma"), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "account_not_confirmed"


async def test_a_public_domain_cannot_be_claimed() -> None:
    repos, deps = _company_fixture(email="max@gmail.com", status=AccountStatus.ACTIVE)

    res = await handle_create_company(
        CreateCompanyCommand(user_id=_USER_ID, name="Nicht Google"), deps=deps, repos=repos
    )

    assert not is_success(res)
    assert fail_err(res).code == "public_email_domain"


async def test_a_taken_domain_is_refused() -> None:
    repos, deps = _company_fixture(email="bob@firma.de", status=AccountStatus.ACTIVE)
    await handle_create_company(
        CreateCompanyCommand(user_id=_USER_ID, name="Erste"), deps=deps, repos=repos
    )

    second = await handle_create_company(
        CreateCompanyCommand(user_id=_OTHER_USER_ID, name="Zweite"), deps=deps, repos=repos
    )

    assert not is_success(second)
    assert fail_err(second).code == "domain_already_claimed"
```

- [ ] **Step 2: Run to verify they fail**

Run: `uv run pytest apps/identity-service/tests/unit/test_commands.py -v -k company`
Expected: FAIL — `ImportError: cannot import name 'CreateCompanyCommand'`

- [ ] **Step 3: Implement the handler**

```python
@dataclass(frozen=True, slots=True)
class CreateCompanyCommand:
    user_id: UUID
    name: str


async def handle_create_company(
    cmd: CreateCompanyCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Company]:
    """Legt ein Unternehmen an, dessen Domain bereits bewiesen ist.

    Die Domain stammt aus der bestätigten Adresse des Erstellers — sie steht
    nicht im Request und kann daher nicht gefälscht werden (ADR-0017/0018).
    """
    try:
        user = await repos["users"].get_by_id(cmd.user_id)
        if user is None:
            raise InvalidCredentials()
        if user.status is not AccountStatus.ACTIVE:
            raise AccountNotConfirmed()

        domain = EmailDomain.from_email(user.email)
        if await repos["companies"].get_by_domain(domain.value) is not None:
            raise DomainAlreadyClaimed(domain.value)

        company = Company.create(name=cmd.name, domain=domain)  # prüft Freemail + Name
        await repos["companies"].add(company)
        await repos["memberships"].add(
            user_id=cmd.user_id, tenant_id=company.id, role=MembershipRole.ADMIN
        )
        await repos["audit"].append(
            AuditEvent(
                occurred_at=deps["clock"].now(),
                actor_id=cmd.user_id,
                tenant_id=company.id,
                action=AuditAction.COMPANY_CREATED,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(company)
```

- [ ] **Step 4: Run tests**

Run: `uv run pytest apps/identity-service/tests/unit -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/identity-service/src/identity_service/application/commands.py \
        apps/identity-service/tests/unit/test_commands.py
git commit -m "feat(identity): Unternehmen anlegen mit abgeleiteter, bewiesener Domain"
```

---

### Task 10: HTTP-Endpunkte

**Files:**
- Modify: `apps/identity-service/src/identity_service/presentation/http/router.py`
- Create: `apps/identity-service/src/identity_service/presentation/http/company_router.py`
- Modify: `apps/identity-service/src/identity_service/presentation/compose_api.py`
- Modify: `apps/identity-service/tests/test_app.py`
- Modify: `apps/identity-service/tests/integration/test_auth_endpoints.py`, `test_tenant_source.py`

**Interfaces:**
- Produces: `POST /auth/verify-email`, `POST /auth/resend-verification`, `POST /companies`, `GET /me/companies`, `POST /auth/company/{id}` (ersetzt `/auth/tenant/{id}`); `RegisterBody`/`LoginBody` weichen `RegisterUserV1`/`LoginV1`.

- [ ] **Step 1: Write the failing app test**

```python
# apps/identity-service/tests/test_app.py — anhängen

def _schema() -> dict:
    return create_app(IdentityServiceSettings()).openapi()


def test_the_new_auth_and_company_routes_exist() -> None:
    paths = _schema()["paths"]

    assert "/auth/verify-email" in paths
    assert "/auth/resend-verification" in paths
    assert "/companies" in paths
    assert "/me/companies" in paths
    assert "/auth/company/{tenant_id}" in paths
    # Umbenannt: das Infrastrukturwort verlässt die öffentliche Grenze.
    assert "/auth/tenant/{tenant_id}" not in paths


def test_create_company_body_cannot_carry_a_domain() -> None:
    schema = _schema()
    ref = schema["paths"]["/companies"]["post"]["requestBody"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    props = schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]

    assert set(props) == {"name"}
```

- [ ] **Step 2: Run to verify it fails**

Run: `uv run pytest apps/identity-service/tests/test_app.py -v -k "routes or domain"`
Expected: FAIL — `KeyError: '/companies'`

- [ ] **Step 3: Implement the endpoints**

Im bestehenden `build_auth_router`:

```python
    @router.post("/verify-email")
    async def verify_email(body: VerifyEmailV1) -> dict[str, str]:
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_verify_email(
                VerifyEmailCommand(token=body.token), deps=deps, repos=repos
            )
        if not result.is_success:
            err = result.error
            if isinstance(err, TokenExpired):
                # 410, damit die Oberfläche gezielt "erneut senden" anbieten kann.
                raise HTTPException(status.HTTP_410_GONE, "confirmation link expired")
            raise HTTPException(status.HTTP_400_BAD_REQUEST, "invalid confirmation link")
        return {"status": "ok"}

    @router.post("/resend-verification", status_code=status.HTTP_202_ACCEPTED)
    async def resend(body: ResendVerificationV1) -> dict[str, str]:
        # Immer 202 — sonst wäre dieser Endpunkt der Enumerationskanal, den
        # /auth/register gerade schließt.
        async with request_scope(session_factory) as (_uow, repos):
            await handle_resend(ResendVerificationCommand(email=body.email), deps=deps, repos=repos)
        return {"status": "accepted"}
```

Im `login`-Handler den neuen Fall ergänzen:

```python
        if isinstance(err, EmailNotConfirmed):
            # Bei korrektem Passwort verrät das nichts, was das Passwort nicht
            # ohnehin beweist — und ohne diesen Fall ist das Konto eine Sackgasse.
            raise HTTPException(status.HTTP_403_FORBIDDEN, "email not confirmed")
```

`switch_tenant` von `@router.post("/tenant/{tenant_id}")` auf `@router.post("/company/{tenant_id}")` umstellen.

Neu `company_router.py` mit `POST /companies` und `GET /me/companies`, gebaut wie `build_auth_router`, registriert in `compose_api.py`.

- [ ] **Step 4: Update the integration tests**

`test_tenant_source.py`: die beiden `POST /auth/tenant/{...}`-Aufrufe auf `/auth/company/{...}` umstellen. Der Hand-`INSERT` in `user_tenant_memberships` bleibt vorerst — Task 12 ersetzt ihn.

- [ ] **Step 5: Run the full backend suite**

Run: `uv run pytest -q`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add apps/identity-service/
git commit -m "feat(identity): Endpunkte für Bestätigung, erneutes Senden und Unternehmen"
```

---

### Task 11: Mailpit in Compose

**Files:**
- Modify: `docker-compose.yml`
- Modify: `.env.example`
- Modify: `README.md`, `CLAUDE.md`

**Interfaces:**
- Produces: Service `mailpit`, SMTP auf `1025`, Web-Oberfläche auf `8025`; Services erhalten `WORKER_SMTP_HOST: mailpit`.

- [ ] **Step 1: Add the service**

```yaml
  mailpit:
    image: axllent/mailpit:latest
    container_name: workertransfer-mailpit
    restart: unless-stopped
    ports:
      - "1025:1025"   # SMTP — die Services sprechen hier hinein
      - "8025:8025"   # Weboberfläche — hier liest man die Mails
    healthcheck:
      test: ["CMD", "/mailpit", "readyz"]
      interval: 5s
      timeout: 3s
      retries: 20
```

In `x-service-env` ergänzen:

```yaml
  WORKER_SMTP_HOST: mailpit
  WORKER_SMTP_PORT: "1025"
  WORKER_MAIL_FROM: noreply@workertransfer.local
  WORKER_PUBLIC_WEB_URL: http://localhost:5173
```

und in `x-service` bei `depends_on` `mailpit: {condition: service_healthy}` ergänzen.

- [ ] **Step 2: Bring the stack up and verify**

```bash
docker compose up --build -d
docker compose ps
curl -sf http://localhost:8025 >/dev/null && echo "Mailpit erreichbar"
```

Expected: alle Container `healthy`, Mailpit antwortet.

- [ ] **Step 3: Register through the running stack**

```bash
curl -s -X POST http://localhost:8001/auth/register \
  -H 'content-type: application/json' \
  -d '{"email":"mail-test@firma.de","password":"strongpassword1","display_name":"M"}'
curl -s http://localhost:8025/api/v1/messages | head -c 400
```

Expected: `201`, und die Nachricht taucht in Mailpit auf.

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml .env.example README.md CLAUDE.md
git commit -m "feat(dev): Mailpit im Stack, Services versenden dorthin"
```

---

### Task 12: Integrationstest über den ganzen Weg

**Files:**
- Create: `apps/identity-service/tests/integration/test_registration_flow.py`

**Interfaces:**
- Consumes: alles Vorherige.

Dieser Test ist der eigentliche Beweis: **kein einziger SQL-`INSERT`**.

- [ ] **Step 1: Write the test**

```python
# apps/identity-service/tests/integration/test_registration_flow.py
"""Der ganze Weg ohne Handarbeit an der Datenbank.

Bisher ließ sich eine Mitgliedschaft nur per INSERT anlegen — genau diese Lücke
schließt dieser Test.
"""

from __future__ import annotations

import pytest
from httpx import ASGITransport, AsyncClient

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


async def test_person_registers_confirms_creates_company_and_switches(
    postgres_url: str, migrated_schema: None, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes-xx")

    from identity_service.configuration import IdentityServiceSettings
    from identity_service.presentation.compose_api import build_app

    # NullMailer statt SMTP: der Token wird aus dem Repository gelesen, nicht
    # aus einer Mail — der Versandweg hat seinen eigenen Test.
    settings = IdentityServiceSettings()
    app = build_app(settings)
    transport = ASGITransport(app=app)

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        reg = await client.post(
            "/auth/register",
            json={
                "email": "anna@flow-firma.de",
                "password": "strongpassword1",
                "display_name": "Anna",
            },
        )
        assert reg.status_code == 201, reg.text

        # Unbestätigt: Anmeldung wird mit 403 abgewiesen, nicht mit 401.
        blocked = await client.post(
            "/auth/login", json={"email": "anna@flow-firma.de", "password": "strongpassword1"}
        )
        assert blocked.status_code == 403, blocked.text

    token = await _read_plaintext_token(postgres_url, "anna@flow-firma.de")

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        confirmed = await client.post("/auth/verify-email", json={"token": token})
        assert confirmed.status_code == 200, confirmed.text

        login = await client.post(
            "/auth/login", json={"email": "anna@flow-firma.de", "password": "strongpassword1"}
        )
        assert login.status_code == 200, login.text
        access = login.cookies.get("access")

        created = await client.post(
            "/companies",
            json={"name": "Flow Firma GmbH"},
            headers={"Authorization": f"Bearer {access}"},
        )
        assert created.status_code == 201, created.text
        company_id = created.json()["id"]
        assert created.json()["domain"] == "flow-firma.de"

        mine = await client.get("/me/companies", headers={"Authorization": f"Bearer {access}"})
        assert [m["role"] for m in mine.json()] == ["admin"]

        switched = await client.post(
            f"/auth/company/{company_id}", headers={"Authorization": f"Bearer {access}"}
        )
        assert switched.status_code == 200, switched.text
        tenant_access = switched.cookies.get("access")

        me = await client.get("/me", headers={"Authorization": f"Bearer {tenant_access}"})
        assert me.json()["tenant_id"] == company_id
```

`_read_plaintext_token` gibt es nicht — der Klartext existiert nur in der Mail. Deshalb: den Mailer in `deps` gegen einen `NullMailer` tauschen (über eine Settings- oder Fixture-Naht) und den Token aus `mailer.sent[0][2]` per Regex ziehen. **Das ist der einzige zulässige Weg; ein Griff in die Datenbank würde den Zweck des Tests aufheben.**

- [ ] **Step 2: Run it**

```bash
open -a Docker
until docker info >/dev/null 2>&1; do sleep 2; done
uv run pytest apps/identity-service/tests/integration/test_registration_flow.py -v
```

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add apps/identity-service/tests/integration/test_registration_flow.py
git commit -m "test(identity): der ganze Onboarding-Weg ohne SQL-Handarbeit"
```

---

### Task 13: Frontend — Registrierung und Bestätigung

**Files:**
- Modify: `apps/web/src/auth/client.ts`
- Create: `apps/web/src/routes/register.tsx`, `apps/web/src/routes/verify.tsx`
- Modify: `apps/web/src/router.tsx`
- Test: `apps/web/src/routes/register.test.tsx`, `apps/web/src/routes/verify.test.tsx`

**Interfaces:**
- Produces: `registerUser(input: {email, password, displayName}): Promise<RegisterResult>`; `verifyEmail(token: string): Promise<VerifyResult>`; `resendVerification(email: string): Promise<void>`. `RegisterResult` und `VerifyResult` sind diskriminierte Unions wie `LoginResult` — sie werfen nicht.

- [ ] **Step 1: Write the failing tests**

```tsx
// apps/web/src/routes/register.test.tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { RegisterRoute } from "./register";

describe("RegisterRoute", () => {
  it("registers with a private address and shows the confirmation hint", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 201 })));
    render(<RegisterRoute />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("E-Mail"), "max@gmail.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "Max");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));

    // Private Adressen sind der Normalfall, nicht die Ausnahme.
    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
  });

  it("asks for no company id", () => {
    render(<RegisterRoute />);

    expect(screen.queryByLabelText("Mandant-ID")).toBeNull();
    expect(screen.queryByLabelText("Firma")).toBeNull();
  });
});
```

```tsx
// apps/web/src/routes/verify.test.tsx — Erfolg, sowie 410 mit "erneut senden"
```

- [ ] **Step 2: Run to verify they fail**

Run: `pnpm --filter @workertransfer/web exec vitest run src/routes/register.test.tsx`
Expected: FAIL — Modul nicht gefunden

- [ ] **Step 3: Implement client + routes**

`client.ts` um die drei Funktionen erweitern, im Muster von `login` (kein `throw`, diskriminierte Union, `credentials: "include"`). Routen im Stil von `login.tsx`, deutschsprachig.

- [ ] **Step 4: Run frontend gates**

```bash
pnpm check && pnpm test
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/web/
git commit -m "feat(web): Registrierung und E-Mail-Bestätigung im Browser"
```

---

### Task 14: Frontend — Unternehmen anlegen und umschalten

**Files:**
- Modify: `apps/web/src/auth/client.ts`, `apps/web/src/auth/session.tsx`
- Create: `apps/web/src/routes/company-new.tsx`
- Modify: `apps/web/src/router.tsx`
- Test: `apps/web/src/routes/company-new.test.tsx`

**Interfaces:**
- Produces: `createCompany(name: string)`, `listCompanies(): Promise<Membership[]>`, `switchCompany(id: string)`; `isPublicEmailDomain(email: string): boolean` (dieselbe Liste wie im Backend, für die reine Sichtbarkeitssteuerung — die Entscheidung fällt weiterhin serverseitig).

- [ ] **Step 1: Write the failing test**

```tsx
// apps/web/src/routes/company-new.test.tsx
it("is not offered to a private address", () => {
  // Sichtbarkeit ist Bequemlichkeit; die Ablehnung kommt vom Server (422).
  render(<CompanyNewRoute principal={{ user_id: "u", tenant_id: null, roles: [], email: "max@gmail.com" }} />);

  expect(screen.getByText(/private Adresse/i)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Unternehmen anlegen" })).toBeNull();
});

it("shows the derived domain instead of asking for it", () => {
  render(<CompanyNewRoute principal={{ user_id: "u", tenant_id: null, roles: [], email: "anna@firma.de" }} />);

  expect(screen.getByText("firma.de")).toBeInTheDocument();
  expect(screen.queryByLabelText("Domain")).toBeNull();
});
```

**Hinweis:** `/me` liefert heute keine `email`. Diese Task erweitert die Antwort um `email`, damit die Oberfläche die Domain anzeigen kann — Test in `test_app.py` ergänzen.

- [ ] **Step 2: Run to verify it fails**

Run: `pnpm --filter @workertransfer/web exec vitest run src/routes/company-new.test.tsx`
Expected: FAIL

- [ ] **Step 3: Implement**

Route, Client-Funktionen, Umschalter in der Navigation aus `listCompanies()`; „als Person" ist der Standardzustand.

- [ ] **Step 4: Run all gates**

```bash
make check
```

Expected: alles grün, Skips ≤ 3.

- [ ] **Step 5: Commit**

```bash
git add apps/web/ apps/identity-service/
git commit -m "feat(web): Unternehmen anlegen und zwischen Rollen umschalten"
```

---

### Task 15: ADR-0019 und Dokumentation

**Files:**
- Create: `docs/adr/0019-email-domain-verification.md`
- Modify: `CLAUDE.md`, `docs/ROADMAP.md`, `docs/adr/0018-membership-and-tenant-switch.md`

- [ ] **Step 1: Write ADR-0019**

Inhalt: warum die Domain abgeleitet statt entgegengenommen wird; warum es keinen unverifizierten Unternehmenszustand gibt; warum Personen- und Domain-Verifikation derselbe Mechanismus sind; warum `POST /auth/register` bei bekannter Adresse `201` antwortet statt `409` (Umgehung des Consent-Gates, Transfermarkt-Risiko); warum `worker-email` nicht verwendet wird.

- [ ] **Step 2: Update CLAUDE.md**

Registrierungsfluss, die neuen Endpunkte, Mailpit, und der Satz: *Registrierung ist für private Adressen offen; die Freemail-Sperrliste gilt nur beim Beanspruchen einer Domain.*

- [ ] **Step 3: Update ADR-0018**

Ein Satz: Mitgliedschaften entstehen jetzt beim Anlegen eines Unternehmens; weitere Mitglieder bleiben Scheibe C.

- [ ] **Step 4: Run all gates and commit**

```bash
make check
git add docs/ CLAUDE.md
git commit -m "docs(adr-0019): Verifikation über die E-Mail-Domain"
```

---

## Definition of Done

- Eine Person registriert sich im Browser mit einer **privaten** Adresse, bestätigt sie über die Mail in Mailpit, meldet sich an, `/me` zeigt `tenant_id: null`.
- Eine Person mit Arbeitsadresse legt zusätzlich ein Unternehmen an, wechselt hinein, `/me` zeigt die `tenant_id` — **ohne Datenbankzugriff von Hand**.
- Eine zweite Person kann dieselbe Domain nicht beanspruchen (`409`).
- Ein unbestätigtes Konto bekommt bei korrektem Passwort `403 email_not_confirmed`, bei falschem `401`.
- `POST /auth/register` antwortet bei bekannter Adresse `201` und schickt die Warnmail.
- `docker compose up --build` bringt Mailpit mit hoch, `:8025` zeigt die Mails.
- `make check` grün mit **≤ 3 Skips** (Docker läuft).
- ADR-0019 geschrieben, `CLAUDE.md` und `docs/ROADMAP.md` nachgezogen.
