"""Ablage im Dateisystem."""

from __future__ import annotations

import asyncio
from pathlib import Path

import pytest
from worker_storage import LocalStorage
from worker_storage.local import InvalidKey


@pytest.fixture
def storage(tmp_path: Path) -> LocalStorage:
    return LocalStorage(tmp_path)


async def test_what_goes_in_comes_out(storage: LocalStorage) -> None:
    stored = await storage.put("a/b/file.png", b"bytes", content_type="image/png")

    assert stored.size == 5
    assert stored.content_type == "image/png"
    assert await storage.get("a/b/file.png") == b"bytes"


async def test_a_missing_key_is_none_not_an_exception(storage: LocalStorage) -> None:
    # „Gibt es nicht" ist beim Abrufen ein normaler Ausgang.
    assert await storage.get("nichts/da.png") is None


async def test_deleting_twice_is_fine(storage: LocalStorage) -> None:
    await storage.put("x.png", b"bytes", content_type="image/png")

    await storage.delete("x.png")
    await storage.delete("x.png")

    assert await storage.get("x.png") is None


async def test_a_second_put_replaces(storage: LocalStorage) -> None:
    await storage.put("x.png", b"alt", content_type="image/png")
    await storage.put("x.png", b"neu", content_type="image/png")

    assert await storage.get("x.png") == b"neu"


async def test_no_partial_file_is_left_under_the_real_name(
    storage: LocalStorage, tmp_path: Path
) -> None:
    """Geschrieben wird daneben, dann umbenannt.

    Ein Absturz mitten im Schreiben hinterlässt sonst eine halbe Datei unter dem
    richtigen Namen — und die sieht für jeden Leser gültig aus.
    """
    await storage.put("x.png", b"bytes", content_type="image/png")

    # rglob im Thread: ASYNC240 verbietet blockierendes pathlib in einer
    # Coroutine, und die Regel gilt hier zu Recht auch für den Test.
    leftovers = await asyncio.to_thread(lambda: list(tmp_path.rglob(".*.partial")))
    assert leftovers == []


@pytest.mark.parametrize(
    "hostile",
    ["../draussen.png", "a/../../draussen.png", "/etc/passwd", "", "a/b/../../../x"],
)
async def test_a_key_may_not_leave_the_root(storage: LocalStorage, hostile: str) -> None:
    # Ein Schlüssel wird zu einem Pfad. Alles, was `..` oder einen führenden
    # Schrägstrich enthält, führt aus dem Verzeichnis heraus.
    with pytest.raises(InvalidKey):
        await storage.put(hostile, b"bytes", content_type="image/png")

    with pytest.raises(InvalidKey):
        await storage.get(hostile)


async def test_a_symlinked_root_cannot_be_used_to_escape(tmp_path: Path) -> None:
    """Gürtel und Hosenträger: das Muster schließt `..` aus, ein Symlink nicht."""
    real = tmp_path / "real"
    real.mkdir()
    outside = tmp_path / "outside"
    outside.mkdir()
    (real / "link").symlink_to(outside)

    storage = LocalStorage(real)

    with pytest.raises(InvalidKey):
        await storage.put("link/../../outside/x.png", b"bytes", content_type="image/png")
