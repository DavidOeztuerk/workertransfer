"""Ablage im Dateisystem.

Das Backend, das im Entwicklungs- und Testbetrieb wirklich läuft — und in einer
kleinen Installation auch in Produktion, mit einem Volume darunter. Ein
S3-Backend kommt, wenn eine Umgebung es braucht; `Storage` ist die Naht dafür.
"""

from __future__ import annotations

import asyncio
import re
from pathlib import Path

from worker_storage.ports import StoredObject

__all__ = ["InvalidKey", "LocalStorage"]

#: Buchstaben, Ziffern, Bindestrich, Unterstrich, Punkt und Schrägstrich.
#: Bewusst eng: ein Schlüssel wird zu einem Pfad, und alles, was `..` oder einen
#: führenden Schrägstrich enthält, führt aus dem Verzeichnis heraus.
_KEY_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]*$")


class InvalidKey(ValueError):
    """Der Schlüssel taugt nicht als Pfad.

    Kein DomainError: Schlüssel werden vom Dienst gebildet, nicht von einem
    Menschen eingegeben. Kommt hier etwas Unpassendes an, ist das ein
    Programmfehler und keine Eingabe, die man höflich ablehnt.
    """


def _safe_relative(key: str) -> Path:
    if not _KEY_RE.match(key) or ".." in key.split("/"):
        raise InvalidKey(f"Unsafe storage key: {key!r}")
    return Path(key)


class LocalStorage:
    def __init__(self, root: Path) -> None:
        self._root = root

    def _path(self, key: str) -> Path:
        path = (self._root / _safe_relative(key)).resolve()
        root = self._root.resolve()
        # Gürtel und Hosenträger: das Muster oben schließt `..` bereits aus,
        # aber ein Symlink im Wurzelverzeichnis könnte trotzdem hinausführen.
        if not path.is_relative_to(root):
            raise InvalidKey(f"Storage key escapes the root: {key!r}")
        return path

    async def put(self, key: str, data: bytes, *, content_type: str) -> StoredObject:
        path = self._path(key)

        def _write() -> None:
            path.parent.mkdir(parents=True, exist_ok=True)
            # Erst daneben schreiben, dann umbenennen: ein Absturz mittendrin
            # hinterlässt sonst eine halbe Datei unter dem richtigen Namen, und
            # die sieht für jeden Leser gültig aus.
            temporary = path.with_name(f".{path.name}.partial")
            temporary.write_bytes(data)
            temporary.replace(path)

        await asyncio.to_thread(_write)
        return StoredObject(key=key, content_type=content_type, size=len(data))

    async def get(self, key: str) -> bytes | None:
        path = self._path(key)

        def _read() -> bytes | None:
            try:
                return path.read_bytes()
            except FileNotFoundError:
                return None

        return await asyncio.to_thread(_read)

    async def delete(self, key: str) -> None:
        path = self._path(key)
        # missing_ok: Löschen ist idempotent.
        await asyncio.to_thread(lambda: path.unlink(missing_ok=True))
