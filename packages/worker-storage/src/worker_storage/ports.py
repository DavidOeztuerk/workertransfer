"""Der Port. Alles andere in diesem Paket ist eine Umsetzung davon."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol

__all__ = ["Storage", "StoredObject"]


@dataclass(frozen=True, slots=True)
class StoredObject:
    """Was nach dem Ablegen bekannt ist.

    `key` ist die einzige Kennung, die der Aufrufer behalten muss — bewusst kein
    Pfad und keine URL: beides bindet an ein Backend, und ein Pfad in der
    Datenbank überlebt keinen Umzug in einen Objektspeicher.
    """

    key: str
    content_type: str
    size: int


class Storage(Protocol):
    async def put(self, key: str, data: bytes, *, content_type: str) -> StoredObject: ...

    async def get(self, key: str) -> bytes | None:
        """Die Bytes, oder `None` wenn es den Schlüssel nicht gibt.

        `None` statt einer Ausnahme: „gibt es nicht" ist beim Abrufen einer
        Datei ein normaler Ausgang, kein Fehlerfall.
        """
        ...

    async def delete(self, key: str) -> None:
        """Löscht, und schweigt über einen Schlüssel, den es nicht gibt.

        Löschen ist idempotent: ein zweiter Aufruf soll nicht scheitern, sonst
        muss jeder Aufräumpfad den Unterschied behandeln, der ihn nicht
        interessiert.
        """
        ...
