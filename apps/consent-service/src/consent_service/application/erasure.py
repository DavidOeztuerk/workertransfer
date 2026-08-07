"""Der Empfänger, bei dem etwas STEHEN BLEIBT (ADR-0027 §5).

Das Ledger ist der **Beleg**, dass gelöscht wurde. Es mitzulöschen hieße, die
Löschung unbeweisbar zu machen — und die Behauptung „wir haben gelöscht" gegen
nichts mehr prüfbar.

**Es bleibt:** `event_id`, `subject_id`, `capability`, `action`, `recorded_at`,
`actor_id` — die vollständige Kette aus Erteilungen, Widerrufen und, am Ende, je
einer `DELETE`-Zeile.

**Es fällt heraus:** `reason` (→ `NULL`) und `metadata` (→ `{}`), in
`consent_events` wie in `audit_events`. Der Grund ist Freitext, den ein Mensch
über sich selbst geschrieben hat — das einzige wirklich personenbezogene Feld
hier. Der Beleg braucht ihn nicht: *dass* widerrufen wurde, steht in `action`.

**Warum das mit Art. 17 vereinbar ist — und wo das Argument endet.** Es lautet
nicht „wir dürfen aufbewahren", sondern: was übrigbleibt, ist keine Auskunft
über einen Menschen mehr. Nach der Löschung gibt es im System keine Abbildung
`subject_id → Mensch`; Adresse, Name und Passwort-Hash lagen ausschließlich in
`identity_service.users`, und die Zeile fällt. Zurück bleiben UUIDs,
Capability-Namen und Zeitstempel. In der Voreinstellung trägt das vollständig —
ein umgelegter Aufbewahrungsschalter (§3) hielte den Schlüssel am Leben, und das
Übrige wäre dann pseudonym statt anonym.

**Warum hier `UPDATE` steht, wo die Repositories keines anbieten.** Dass
`SqlAlchemyConsentEventRepository` weder `update` noch `delete` kennt, ist
Absicht: Append-only ist eine Eigenschaft der verfügbaren API, keine Regel, die
sich jemand merken muss. Diese Datei bricht das nicht auf — sie umgeht es an
genau einer Stelle, unter einem Namen, der sagt warum, und erreichbar nur über
den Löschendpunkt. Die Alternative wäre eine `clear_reasons`-Methode am
Repository gewesen: die stünde dann jedem Aufrufer offen, und die Zusage
„niemand kann Geschichte umschreiben" wäre für alle weg.
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID, uuid4

from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from consent_service.domain.value_objects import ConsentAction
from consent_service.infrastructure.database.models import AuditEventModel, ConsentEventModel

__all__ = ["erase_subject"]


async def _capabilities_still_open(session: AsyncSession, subject_id: UUID) -> list[str]:
    """Jede Capability, die die Person je hielt — außer den schon gelöschten.

    Die Ausnahme ist die Idempotenz (ADR-0027 §4.2). Löschen ist von Natur aus
    idempotent, **Anhängen nicht**: eine zweite Zustellung würde sonst eine
    zweite `DELETE`-Zeile je Capability schreiben. Kein Schaden, aber eine
    Unwahrheit — die Person hat einmal gelöscht, nicht zweimal.

    `DISTINCT ON` mit derselben Ordnung wie `latest_per_capability`: weicht sie
    ab, sagt diese Abfrage bei zwei Ereignissen im selben Zeittakt etwas anderes
    als `/check`.
    """
    stmt = (
        select(ConsentEventModel.capability, ConsentEventModel.action)
        .where(ConsentEventModel.subject_id == subject_id)
        .distinct(ConsentEventModel.subject_id, ConsentEventModel.capability)
        .order_by(
            ConsentEventModel.subject_id,
            ConsentEventModel.capability,
            ConsentEventModel.recorded_at.desc(),
            ConsentEventModel.event_id.desc(),
        )
    )
    rows = (await session.execute(stmt)).all()
    return [row.capability for row in rows if row.action != ConsentAction.DELETE.value]


async def erase_subject(session: AsyncSession, subject_id: UUID, *, now: datetime) -> int:
    """Setzt den Schlusspunkt und räumt den Freitext weg. Behält: alles andere."""
    for capability in await _capabilities_still_open(session, subject_id):
        session.add(
            ConsentEventModel(
                event_id=uuid4(),
                subject_id=subject_id,
                capability=capability,
                action=ConsentAction.DELETE.value,
                # Die Person selbst — es gibt kein Delegationsmodell, und das
                # ist Absicht (ADR-0013). Niemand außer ihr löscht ihr Konto.
                actor_id=subject_id,
                reason=None,
                meta={},
                recorded_at=now,
            )
        )
    await session.flush()

    # Danach, nicht davor: so trifft es auch die eben geschriebenen Zeilen, und
    # es bleibt eine einzige Stelle, an der „kein Freitext mehr" durchgesetzt
    # wird — statt zweier, die auseinanderlaufen können.
    await session.execute(
        update(ConsentEventModel)
        .where(ConsentEventModel.subject_id == subject_id)
        .values(reason=None, meta={})
    )

    # Die Allowlist erlaubt `ip` und `user_agent` (`domain/audit.py`); heute
    # schreibt sie zwar niemand, doch die Löschung entscheidet die *Form*, nicht
    # den Tagesstand. Die Zeile selbst bleibt — keine Kaskade, ADR-0012.
    await session.execute(
        update(AuditEventModel)
        .where((AuditEventModel.target_id == subject_id) | (AuditEventModel.actor_id == subject_id))
        .values(meta={})
    )
    return 0
