"""Was hineindarf — Typ und Größe.

Bewusst ohne python-magic: das braucht libmagic als Systembibliothek und war
einer der Gründe, warum dieses Paket nicht baubar war. Für eine kurze Liste
erlaubter Typen genügen die ersten Bytes, und die Liste bleibt kurz, weil jeder
zusätzliche Typ eine eigene Entscheidung ist.
"""

from __future__ import annotations

from worker_core import DomainError

__all__ = [
    "ALLOWED_TYPES",
    "ContentTooLarge",
    "UnsupportedContentType",
    "sniff_content_type",
]

#: Genau diese drei. Ein Portfolio zeigt Bilder und Dokumente; alles Weitere ist
#: eine Entscheidung, die jemand treffen und begründen muss.
ALLOWED_TYPES: frozenset[str] = frozenset({"image/png", "image/jpeg", "application/pdf"})

_SIGNATURES: tuple[tuple[bytes, str], ...] = (
    (b"\x89PNG\r\n\x1a\n", "image/png"),
    (b"\xff\xd8\xff", "image/jpeg"),
    (b"%PDF-", "application/pdf"),
)


class UnsupportedContentType(DomainError):
    def __init__(self) -> None:
        # Ohne Detail, welcher Typ erkannt wurde: die Meldung geht an jemanden,
        # der gerade etwas hochlädt, und die erlaubte Liste steht in der
        # Oberfläche. Was wir erkannt haben, ist für ihn keine Hilfe, für einen
        # Angreifer aber eine Rückmeldung darüber, wie weit er kommt.
        super().__init__(
            "unsupported_content_type",
            "Only PNG, JPEG and PDF files are accepted",
        )


class ContentTooLarge(DomainError):
    def __init__(self, limit: int) -> None:
        super().__init__("content_too_large", f"The file exceeds {limit} bytes")


def sniff_content_type(data: bytes) -> str:
    """Der Typ aus den ersten Bytes — nicht aus dem, was der Client behauptet.

    Ein `Content-Type`-Header ist eine Behauptung des Hochladenden, und die
    Endung eines Dateinamens ebenso. Beides ist frei wählbar; die Signatur nicht.
    """
    for signature, content_type in _SIGNATURES:
        if data.startswith(signature):
            return content_type
    raise UnsupportedContentType()


def assert_within(data: bytes, limit: int) -> None:
    if len(data) > limit:
        raise ContentTooLarge(limit)
