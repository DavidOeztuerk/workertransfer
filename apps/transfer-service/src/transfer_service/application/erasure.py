"""Was „löschen" hier heißt — und der zweite Schalter, der auf AUS steht.

**Die Voreinstellung löscht auch bezahlte Transfers** (ADR-0027 §3). Ein Betrag,
auf den sich zwei Unternehmen geeinigt haben, macht die Zeile nicht zur
Unterlage eines Vermittlers: die Plattform führt kein Geld, sie hält eine Zahl
fest, damit beide Seiten dieselbe im Blick haben.

Vier Fälle, und zwei laufen in verschiedene Richtungen:

| Zeile | Was passiert |
|---|---|
| `market_status` (`id` IST die `subject_id`) | fällt, samt `note` |
| `market_requests` mit `subject_id` = Person | fällt — die Zeile IST eine Aussage über sie |
| `market_requests` mit `requested_by` = Person | bleibt, `requested_by` wird NULL |
| `transfers` mit `subject_id` = Person | fällt, samt `message` und `offer_note` |
| `outbox` mit `user_id` = Person | fällt |
"""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import ColumnElement, delete, func, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from transfer_service.domain.transfer import TransferStatus
from transfer_service.infrastructure.database.models import (
    OUTBOX,
    MarketRequestModel,
    MarketStatusModel,
    TransferModel,
)

__all__ = ["RETAIN_PAID_TRANSFERS", "erase_subject"]

#: Endzustände, an denen ein bezahlter Vorgang hängen könnte. **Nicht**
#: `TransferStatus`' eigenes `_FINAL` — das enthält `declined` und `withdrawn`,
#: und ein abgesagter Vorgang begründet nichts. Ausgeschrieben, weil das
#: Abgrenzen der ganze Punkt ist (ADR-0027 §3.2).
_CLOSED_DEALS = (TransferStatus.ACCEPTED.value, TransferStatus.COMPLETED.value)

#: **AUS.** Die Voreinstellung löscht auch bezahlte Transfers (ADR-0027 §3).
#:
#: Eine benannte Konstante und **kein Konfigurationswert**: bei einem
#: Löschversprechen wäre „in Produktion anders als im Test" der schlimmste
#: denkbare Zustand.
#:
#: Er schaltet **genau eine Zeilenklasse in diesem Dienst**:
#:
#:     transfers.status IN ('accepted','completed')
#:       AND transfers.offer_fee_cents IS NOT NULL
#:
#: Keine Ausdehnung auf `interested`/`talking`/`offered` — ein Gespräch ist kein
#: Vertrag. Kein „laufender Vorgang" als Gummiwort. Und ohne Vergütung ist kein
#: Handelsvorgang entstanden, an dem etwas hängen könnte.
#:
#: Und keine Frist, in keiner Richtung: kein Nachlauf, der später aufräumt. Wird
#: der Schalter je umgelegt, kommt die Frist *zusammen mit der Antwort*.
RETAIN_PAID_TRANSFERS = False


async def erase_subject(session: AsyncSession, subject_id: UUID) -> int:
    """Löscht alles über diesen Menschen und gibt zurück, was stehen blieb."""
    await session.execute(delete(MarketStatusModel).where(MarketStatusModel.id == subject_id))

    # Was ÜBER die Person gesagt wurde, fällt.
    await session.execute(
        delete(MarketRequestModel).where(MarketRequestModel.subject_id == subject_id)
    )
    # Was die Person FÜR ihr Unternehmen tat, bleibt — ohne ihren Namen. Die
    # Anfrage gehört dem Unternehmen und handelt von einem Dritten.
    await session.execute(
        update(MarketRequestModel)
        .where(MarketRequestModel.requested_by == subject_id)
        .values(requested_by=None)
    )

    mine = TransferModel.subject_id == subject_id
    # Zur Laufzeit gelesen, nicht beim Import gebunden: sonst wäre der Schalter
    # im Test nicht umlegbar, und die Abgrenzung könnte niemand prüfen.
    keep_paid = RETAIN_PAID_TRANSFERS
    paid_deal = TransferModel.status.in_(_CLOSED_DEALS) & TransferModel.offer_fee_cents.is_not(None)

    retained = 0
    condition: ColumnElement[bool]
    if keep_paid:
        retained = (
            await session.execute(
                select(func.count()).select_from(TransferModel).where(mine, paid_deal)
            )
        ).scalar_one()

    # Ausgeschrieben statt als Ternär in der `where`: das ist die Zeile, die
    # entscheidet, ob ein bezahlter Transfer fällt. Wer sie überfliegt, soll
    # sehen, was sie tut — nicht Operatorvorrang nachschlagen müssen.
    if keep_paid:
        condition = mine & ~paid_deal
    else:
        condition = mine
    await session.execute(delete(TransferModel).where(condition))

    # Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.
    await session.execute(delete(OUTBOX).where(OUTBOX.c.user_id == subject_id))
    return int(retained)
