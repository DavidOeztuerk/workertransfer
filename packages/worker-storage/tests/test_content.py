"""Was hineindarf."""

from __future__ import annotations

import pytest
from worker_storage import UnsupportedContentType, sniff_content_type
from worker_storage.content import ContentTooLarge, assert_within

PNG = b"\x89PNG\r\n\x1a\n" + b"rest"
JPEG = b"\xff\xd8\xff\xe0" + b"rest"
PDF = b"%PDF-1.7\n" + b"rest"


@pytest.mark.parametrize(
    ("data", "expected"),
    [(PNG, "image/png"), (JPEG, "image/jpeg"), (PDF, "application/pdf")],
)
def test_it_recognises_what_it_allows(data: bytes, expected: str) -> None:
    assert sniff_content_type(data) == expected


def test_it_reads_the_bytes_not_the_claim() -> None:
    """Ein Content-Type-Header ist eine Behauptung des Hochladenden.

    Eine als `image/png` deklarierte HTML-Datei ist der klassische Weg, einen
    Speicher zum Ausliefern von Skripten zu bringen.
    """
    with pytest.raises(UnsupportedContentType):
        sniff_content_type(b"<html><script>alert(1)</script></html>")


@pytest.mark.parametrize(
    "hostile",
    [
        b"",
        b"GIF89a",
        b"<?php echo 1; ?>",
        b"\x7fELF",
        b"PK\x03\x04",
        b"\xff\xd8",  # zu kurz für die JPEG-Signatur
    ],
)
def test_everything_else_is_refused(hostile: bytes) -> None:
    with pytest.raises(UnsupportedContentType):
        sniff_content_type(hostile)


def test_the_refusal_does_not_say_what_it_saw() -> None:
    """Für den Hochladenden ist das keine Hilfe, für einen Angreifer schon.

    Die erlaubte Liste steht in der Oberfläche; was der Server erkannt hat, ist
    eine Rückmeldung darüber, wie weit jemand kommt.
    """
    with pytest.raises(UnsupportedContentType) as caught:
        sniff_content_type(b"\x7fELF")

    assert "ELF" not in caught.value.message
    assert "PNG" in caught.value.message


def test_the_size_limit_is_checked_against_the_bytes() -> None:
    assert_within(b"x" * 10, 10)

    with pytest.raises(ContentTooLarge):
        assert_within(b"x" * 11, 10)
