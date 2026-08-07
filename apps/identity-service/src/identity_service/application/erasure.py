"""Der Ursprung der Löschung — und der Nachweis, dass sie überall ankam.

**Warum hier und nicht im consent-service**, obwohl ADR-0013 ihm die
GDPR-Vorgänge zuweist: der Ursprung muss das Konto beenden können, und `users`
liegt hier. Der Präzedenzfall ist der Datenexport — auch der wurde nicht in
einem Sammeldienst gebaut, weil ein Dienst, der über sieben Grenzen liest, genau
das ist, was ADR-0004 ausschließt. Das Ledger bleibt die Anlaufstelle für die
*Auskunft*, was gelöscht wurde; hier ist es ein Empfänger wie die anderen.

**Der Nachweis ist die Menge der Zeilen.** Eine Löschung ist genau dann fertig,
wenn für diese `user_id` keine Outbox-Zeile mehr ohne `delivered_at` steht. Das
ist eine SQL-Abfrage, keine Vermutung, und sie ist je Empfänger einzeln
beantwortbar — man sieht nicht nur *dass* etwas offen ist, sondern *welcher
Dienst*.

**Die Reihenfolge aus §6 ist erzwungen, nicht empfohlen:**

1. Alle sieben fremden Empfänger haben quittiert.
2. Die Abschlussnachricht ist zugestellt — selbst eine Outbox-Zeile.
3. **Erst danach** fallen `users` und die daran hängenden Zeilen.

Der Grund ist unromantisch: die Nachricht braucht die Adresse, und die Adresse
liegt in der Zeile, die gelöscht werden soll. Sie in die Outbox mitzunehmen wäre
die naheliegende Abkürzung und ist ausdrücklich verboten — eine Outbox ist ein
dauerhafter Speicher und landet in jedem Backup (ADR-0025 §5).
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from sqlalchemy import delete, select, update
from sqlalchemy.ext.asyncio import AsyncSession
from worker_outbox import record

from identity_service.domain.company import TenantStatus
from identity_service.domain.membership import MembershipRole
from identity_service.domain.user import AccountStatus
from identity_service.infrastructure.database.models import (
    OUTBOX,
    AuditEventModel,
    InvitationModel,
    NotificationPreferenceModel,
    SessionModel,
    TenantModel,
    UserModel,
    UserTenantMembershipModel,
)

__all__ = [
    "KIND_COMPANY_WITHDRAWAL",
    "KIND_FINAL_NOTICE",
    "KIND_IDENTITY",
    "KIND_PREFIX",
    "RECIPIENTS",
    "erasure_kinds",
    "finish_erasure",
    "open_kinds",
    "request_erasure",
]

#: Die sieben fremden Empfänger. **Nicht** jobs-service und companies-service:
#: sie halten nichts Personenbezogenes (ADR-0027 §2). Ein Löschbefehl an einen
#: Dienst ohne zu löschende Daten wäre ein Endpunkt, der „erledigt" sagt, ohne
#: je etwas zu tun.
#:
#: `tests/test_erasure_recipients.py` fällt rot, sobald irgendein Dienst eine
#: Tabelle mit `subject_id` oder `user_id` bekommt, die hier nicht steht.
RECIPIENTS: tuple[str, ...] = (
    "consent",
    "profile",
    "resume",
    "portfolio",
    "applications",
    "transfer",
    "github",
)

KIND_PREFIX = "erasure:"
#: identity selbst, und zwar **zuletzt**: hier liegt die Adresse, die die
#: Abschlussnachricht braucht.
KIND_IDENTITY = f"{KIND_PREFIX}identity"
KIND_FINAL_NOTICE = f"{KIND_PREFIX}final-notice"
#: Die Absicht aus §7 — über ein **Unternehmen**, nicht über einen Menschen.
#: Sie trägt bewusst kein `erasure:`-Präfix: `open_kinds` filtert danach, und
#: damit kann ein stiller jobs-service die Löschung eines Menschen nicht
#: offenhalten.
KIND_COMPANY_WITHDRAWAL = "company:withdrawal"


def erasure_kinds() -> tuple[str, ...]:
    """Alle neun Zeilen, in der Reihenfolge, in der sie fällig werden.

    Die Reihenfolge *erzwingt* der Zusteller (`Deferred`), nicht diese Liste —
    alle neun entstehen im selben Augenblick und in derselben Transaktion.
    """
    return (
        *(f"{KIND_PREFIX}{name}" for name in RECIPIENTS),
        KIND_FINAL_NOTICE,
        KIND_IDENTITY,
    )


async def open_kinds(session: AsyncSession, user_id: UUID) -> list[str]:
    """Was noch aussteht — der Nachweis aus §4, als Abfrage.

    Leer heißt fertig. Die Absicht an jobs-service zählt ausdrücklich **nicht**
    mit: sonst könnte ein stiller jobs-service die Löschung eines Menschen
    offenhalten, und das persönliche Recht hinge wieder an einer
    Organisationsfrage.
    """
    rows = await session.execute(
        select(OUTBOX.c.kind)
        .where(OUTBOX.c.user_id == user_id)
        .where(OUTBOX.c.delivered_at.is_(None))
        .where(OUTBOX.c.kind.startswith(KIND_PREFIX))
        .order_by(OUTBOX.c.created_at)
    )
    return [row.kind for row in rows]


async def _already_requested(session: AsyncSession, user_id: UUID) -> bool:
    row = await session.execute(
        select(OUTBOX.c.id)
        .where(OUTBOX.c.user_id == user_id)
        .where(OUTBOX.c.kind.startswith(KIND_PREFIX))
        .limit(1)
    )
    return row.first() is not None


async def request_erasure(session: AsyncSession, *, user_id: UUID, now: datetime) -> bool:
    """Nimmt das Verlangen entgegen. `False`, wenn es schon läuft.

    **Sofort und sichtbar:** alle Sitzungen widerrufen, Konto auf `DISABLED`.
    Ab diesem Moment passiert nichts mehr unter diesem Namen, auch wenn die
    Kaskade noch läuft (§6).

    Die neun Absichten entstehen in **derselben Transaktion** wie die
    Zustandsänderung des Kontos (ADR-0025): geht sie durch, liegen sie fest;
    wird sie zurückgerollt, sind auch sie weg. Es gibt keinen Zustand
    „gesperrt, aber niemand wurde beauftragt".
    """
    if await _already_requested(session, user_id):
        # Zweimal zu drücken ist kein Fehler und darf keine zweite Kaskade
        # auslösen — neun weitere Zeilen wären neun weitere Zustellungen für
        # einen Vorgang, den es nur einmal gibt.
        return False

    await session.execute(
        update(UserModel).where(UserModel.id == user_id).values(status=AccountStatus.DISABLED)
    )
    # Nicht erst am Ende: solange die Kaskade läuft, soll sich niemand mehr
    # unter diesem Namen anmelden können.
    await session.execute(
        update(SessionModel)
        .where(SessionModel.user_id == user_id, SessionModel.revoked_at.is_(None))
        .values(revoked_at=now)
    )

    for kind in erasure_kinds():
        await record(session, OUTBOX, user_id=user_id, kind=kind, now=now)
    return True


async def finish_erasure(session: AsyncSession, *, user_id: UUID, now: datetime) -> list[UUID]:
    """Der letzte Schritt: `users` fällt, und was daran hängt.

    Gibt die Unternehmen zurück, die dabei ohne Administrator zurückblieben —
    sie werden stillgelegt (§7).

    Idempotent: ist die Zeile schon weg, war dieser Schritt schon einmal
    erfolgreich, und es gibt nichts mehr zu tun.
    """
    user = await session.get(UserModel, user_id)
    if user is None:
        return []
    email = user.email

    # Einladungen AN diese Adresse: sie tragen sie im Klartext (CITEXT).
    await session.execute(delete(InvitationModel).where(InvitationModel.email == email))

    # `audit_events` bleibt, `metadata` wird geleert — keine Kaskade mit
    # `users`, das ist eine ausdrückliche Entscheidung (ADR-0012). Die
    # Allowlist erlaubt `ip` und `user_agent`; heute schreibt sie zwar niemand,
    # doch die Löschung entscheidet die *Form*, nicht den Tagesstand.
    await session.execute(
        update(AuditEventModel)
        .where((AuditEventModel.actor_id == user_id) | (AuditEventModel.target_id == user_id))
        .values(meta={})
    )

    # **Die Zeile, die man vergisst.** `notification_preferences` hat keinen
    # Fremdschlüssel auf `users` (so dokumentiert im Modell) — ein
    # `DELETE FROM users` lässt sie stehen.
    await session.execute(
        delete(NotificationPreferenceModel).where(NotificationPreferenceModel.user_id == user_id)
    )

    affected = list(
        (
            await session.execute(
                select(UserTenantMembershipModel.tenant_id).where(
                    UserTenantMembershipModel.user_id == user_id
                )
            )
        )
        .scalars()
        .all()
    )

    # Und jetzt die eigentliche Löschung: danach bildet nichts im System mehr
    # eine `subject_id` auf einen Menschen ab. `sessions`,
    # `email_verification_tokens` und `user_tenant_memberships` fallen über
    # `ondelete=CASCADE` mit; `company_invitations.invited_by` wird `NULL`.
    await session.execute(delete(UserModel).where(UserModel.id == user_id))
    await session.flush()

    dormant: list[UUID] = []
    for tenant_id in affected:
        remaining = (
            await session.execute(
                select(UserTenantMembershipModel.id).where(
                    UserTenantMembershipModel.tenant_id == tenant_id,
                    UserTenantMembershipModel.role == str(MembershipRole.ADMIN),
                )
            )
        ).all()
        if remaining:
            continue
        await session.execute(
            update(TenantModel)
            .where(TenantModel.id == tenant_id)
            .values(status=TenantStatus.DORMANT.value)
        )
        # Eigene Absicht, eigene Zeile — und `user_id` trägt hier die
        # **tenant_id**: die Tabelle hat nur diese eine Kennungsspalte
        # (ADR-0025 §5, „ohne eine einzige neue Spalte"). Über das fehlende
        # `erasure:`-Präfix bleibt sie aus dem Vollständigkeitsnachweis heraus.
        await record(session, OUTBOX, user_id=tenant_id, kind=KIND_COMPANY_WITHDRAWAL, now=now)
        dormant.append(tenant_id)
    return dormant
