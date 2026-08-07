"""Was „löschen" hier heißt — im einzigen Dienst mit Dateien (ADR-0027 §2).

`portfolios.id` **ist** die `subject_id`; die Zeile fällt samt Einträgen. Dazu
kommen die Anhänge im Speicher unter `<subject_id>/…`, über `list_names` und
`delete` je Name — genau der Zweck, für den `list_names` dokumentiert ist.

**Über den Speicher, nicht über die Einträge.** Aufgezählt wird, was im Speicher
liegt, nicht, worauf die Einträge zeigen. Eine Waise — hochgeladen, dann aus dem
Portfolio entfernt, aber nie aufgeräumt — trägt genauso den Namen dieses
Menschen. Wer nur die referenzierten Namen löscht, lässt sie liegen.

**Reihenfolge umgekehrt zum Hochladen.** Der Upload committet zuerst und räumt
danach auf, weil ein fehlgeschlagener Commit sonst Dateien löscht, auf die
gültige Einträge zeigen. Beim Löschen gilt das Gegenteil: **erst die Dateien,
dann die Zeile.** Bricht es dazwischen ab, zeigen Einträge ins Leere, und der
nächste Zustellversuch räumt sie weg — andersherum bliebe der Inhalt liegen,
den niemand mehr referenziert und deshalb auch niemand mehr findet.

Das leere Verzeichnis verschwindet innerhalb von `LocalStorage` (ADR-0021):
„Verzeichnis" ist ein Begriff des Dateisystems, den ein Objektspeicher nicht
kennt — ein `rmdir` am Port wäre das lokale Backend, das in die Naht
durchschlägt, die es verbergen soll.
"""

from __future__ import annotations

from typing import Any
from uuid import UUID

from sqlalchemy import delete
from sqlalchemy.ext.asyncio import AsyncSession

from portfolio_service.infrastructure.database.models import PortfolioModel

__all__ = ["erase_subject"]


async def erase_subject(session: AsyncSession, storage: Any, subject_id: UUID) -> int:
    """Erst jede Datei, dann die Zeile. Gibt zurück, was stehen blieb: nichts."""
    prefix = str(subject_id)
    for name in await storage.list_names(prefix):
        await storage.delete(f"{prefix}/{name}")

    await session.execute(delete(PortfolioModel).where(PortfolioModel.id == subject_id))
    return 0
