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


async def test_listing_a_prefix_names_what_is_there(storage: LocalStorage) -> None:
    await storage.put("person/a.png", b"x", content_type="image/png")
    await storage.put("person/b.png", b"y", content_type="image/png")
    await storage.put("andere/c.png", b"z", content_type="image/png")

    assert await storage.list_names("person") == ["a.png", "b.png"]


async def test_listing_an_untouched_prefix_is_empty_not_an_error(
    storage: LocalStorage,
) -> None:
    # Kein Verzeichnis heißt: da war noch nie etwas.
    assert await storage.list_names("noch-nie") == []


async def test_a_half_written_file_is_not_listed(storage: LocalStorage, tmp_path: Path) -> None:
    """Halbfertige Schreibvorgänge gehören niemandem.

    Sie aufzuführen würde sie als Anhänge ausgeben — und ein Aufräumer, der sie
    für verwaist hält, löscht sie mitten im Schreiben.
    """
    await storage.put("person/a.png", b"x", content_type="image/png")

    def _leave_partial() -> None:
        (tmp_path / "person" / ".b.png.partial").write_bytes(b"halb")

    await asyncio.to_thread(_leave_partial)

    assert await storage.list_names("person") == ["a.png"]


async def _exists(path: Path) -> bool:
    """Im Thread, wie beim rglob oben: ASYNC240 verbietet blockierendes
    pathlib in einer Coroutine, und die Regel gilt hier zu Recht auch für den
    Test."""
    return await asyncio.to_thread(path.exists)


async def _is_dir(path: Path) -> bool:
    return await asyncio.to_thread(path.is_dir)


class TestTheEmptyDirectoryIsASpurToo:
    """Ein leeres Verzeichnis, das nach einer `subject_id` heißt, bleibt eine
    Auskunft: *diesen Menschen gab es hier*. Beim Löschen eines Kontos ist genau
    das die Spur, die man übersieht (ADR-0027 §2).

    Aufgeräumt wird **innerhalb von LocalStorage**, nicht über eine neue
    Port-Methode: „Verzeichnis" ist ein Begriff des Dateisystems, den ein
    Objektspeicher gar nicht kennt — dort verschwindet ein Präfix von selbst,
    sobald das letzte Objekt weg ist. Ein `rmdir` am Port wäre das lokale
    Backend, das in die Naht durchschlägt, die es verbergen soll (ADR-0021).
    """

    async def test_the_last_delete_takes_the_directory_with_it(
        self, storage: LocalStorage, tmp_path: Path
    ) -> None:
        await storage.put("subject/a.png", b"eins", content_type="image/png")
        await storage.put("subject/b.png", b"zwei", content_type="image/png")

        await storage.delete("subject/a.png")
        assert await _is_dir(tmp_path / "subject"), "solange noch etwas drin liegt: bleibt"

        await storage.delete("subject/b.png")

        assert not await _exists(tmp_path / "subject")

    async def test_it_never_removes_the_root(self, storage: LocalStorage, tmp_path: Path) -> None:
        """Sonst wäre der erste Anhang nach dem Aufräumen ein Schreibfehler."""
        await storage.put("lose.png", b"bytes", content_type="image/png")

        await storage.delete("lose.png")

        assert await _exists(tmp_path)

    async def test_a_directory_someone_else_uses_stays(
        self, storage: LocalStorage, tmp_path: Path
    ) -> None:
        await storage.put("a/b/mein.png", b"bytes", content_type="image/png")
        await storage.put("a/deins.png", b"bytes", content_type="image/png")

        await storage.delete("a/b/mein.png")

        assert not await _exists(tmp_path / "a" / "b")
        assert await _is_dir(tmp_path / "a")
