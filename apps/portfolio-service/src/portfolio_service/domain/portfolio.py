"""Das Portfolio einer Person — was sie gemacht hat.

Ein Schaufenster, kein Aktenschrank: freigegeben wird es als Ganzes, für alle
Unternehmen (`portfolio.visibility:public`). Die Feinheit steckt in der
Entscheidung, was hineinkommt, nicht in einer Sichtbarkeitsstufe je Eintrag —
was nicht gezeigt werden darf, gehört nicht ins Portfolio.

Wer es sehen darf, entscheidet der Consent-Ledger, nicht dieses Modul
(ADR-0020 §6).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime
from urllib.parse import urlparse
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "MAX_ITEMS",
    "InvalidAttachment",
    "InvalidText",
    "InvalidUrl",
    "InvalidYear",
    "Portfolio",
    "PortfolioItem",
    "TooManyItems",
]

MAX_ITEMS = 30
MAX_TITLE = 160
MAX_SUMMARY = 1000
MAX_ROLE = 160
MAX_URL = 2000
MIN_YEAR = 1900
MAX_ATTACHMENT_NAME = 80

#: Der Name einer hochgeladenen Datei, wie ihn der Server vergeben hat. Kein
#: Pfad und keine URL: er wird beim Ausliefern mit der subject_id zu einem
#: Ablageschlüssel zusammengesetzt, und diese Struktur ist es, die verhindert,
#: dass jemand mit einem fremden Namen an eine fremde Datei kommt.
_ATTACHMENT_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")

#: Nur diese beiden. Ein Portfolio-Link wird von fremden Menschen angeklickt;
#: `javascript:` und `data:` sind in einem Feld, das später in einem Browser
#: landet, kein exotischer Randfall, sondern der Normalfall eines Angriffs.
ALLOWED_SCHEMES = frozenset({"http", "https"})


class InvalidText(DomainError):
    def __init__(self, field: str, detail: str) -> None:
        super().__init__("invalid_text", f"{field} {detail}")


class InvalidUrl(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("invalid_url", detail)


class InvalidYear(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("invalid_year", detail)


class InvalidAttachment(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_attachment", "That is not a valid attachment name")


class TooManyItems(DomainError):
    def __init__(self) -> None:
        super().__init__("too_many_items", f"At most {MAX_ITEMS} items are allowed")


def _text(field: str, value: str, *, required: bool, limit: int) -> str:
    cleaned = value.strip()
    if required and not cleaned:
        raise InvalidText(field, "must not be empty")
    if len(cleaned) > limit:
        raise InvalidText(field, f"exceeds {limit} characters")
    return cleaned


def _url(value: str | None) -> str | None:
    if value is None:
        return None
    cleaned = value.strip()
    if not cleaned:
        # Leer und „nicht angegeben" sind dasselbe; ein leerer String im Feld
        # würde später als Link gerendert und ins Nichts führen.
        return None
    if len(cleaned) > MAX_URL:
        raise InvalidUrl(f"URL exceeds {MAX_URL} characters")
    parsed = urlparse(cleaned)
    if parsed.scheme.lower() not in ALLOWED_SCHEMES:
        raise InvalidUrl("Only http and https links are allowed")
    if not parsed.netloc:
        raise InvalidUrl("The link is missing a host")
    return cleaned


def _attachment(value: str | None) -> str | None:
    """Nur ein Name, kein Pfad.

    Der Client bekommt ihn vom Upload zurück und schickt ihn beim Speichern
    mit. Ließe man einen Pfad zu, könnte jemand mit `../` aus seinem eigenen
    Verzeichnis herauszeigen — die Ablage würde das zwar auch abfangen, aber
    eine Prüfung an der Grenze ist billiger als eine Ausnahme in der Tiefe.
    """
    if value is None:
        return None
    cleaned = value.strip()
    if not cleaned:
        return None
    if len(cleaned) > MAX_ATTACHMENT_NAME or not _ATTACHMENT_RE.match(cleaned):
        raise InvalidAttachment()
    return cleaned


def _year(value: int | None, *, now: datetime) -> int | None:
    if value is None:
        return None
    # Das nächste Jahr ist erlaubt: etwas kann gerade erscheinen. Weiter in die
    # Zukunft ist ein Tippfehler, kein Plan.
    if value < MIN_YEAR or value > now.year + 1:
        raise InvalidYear(f"A year must lie between {MIN_YEAR} and {now.year + 1}")
    return value


@dataclass(frozen=True, slots=True)
class PortfolioItem:
    title: str
    summary: str = ""
    url: str | None = None
    role: str = ""
    year: int | None = None
    #: Name einer hochgeladenen Datei, vom Server vergeben. `None` heißt „keine".
    attachment: str | None = None

    def __post_init__(self) -> None:
        object.__setattr__(
            self, "title", _text("Title", self.title, required=True, limit=MAX_TITLE)
        )
        object.__setattr__(
            self, "summary", _text("Summary", self.summary, required=False, limit=MAX_SUMMARY)
        )
        object.__setattr__(self, "role", _text("Role", self.role, required=False, limit=MAX_ROLE))
        object.__setattr__(self, "url", _url(self.url))
        object.__setattr__(self, "attachment", _attachment(self.attachment))
        # Die Obergrenze des Jahres braucht die Gegenwart, und ein Wertobjekt
        # hat keine Uhr: sie hineinzureichen wäre eine versteckte Abhängigkeit,
        # sich `datetime.now()` zu holen dasselbe, nur unsichtbarer. Der Eintrag
        # prüft deshalb nur, was er wissen kann; das Aggregat prüft den Rest,
        # und durch das Aggregat führt der einzige Weg in ein Portfolio.
        if self.year is not None and self.year < MIN_YEAR:
            raise InvalidYear(f"A year must not lie before {MIN_YEAR}")


@dataclass(eq=False, slots=True)
class Portfolio:
    """Ein Portfolio je Person; `subject_id` IST der Schlüssel."""

    subject_id: UUID
    items: tuple[PortfolioItem, ...]
    created_at: datetime
    updated_at: datetime

    @classmethod
    def create(cls, subject_id: UUID, *, items: list[PortfolioItem], now: datetime) -> Portfolio:
        checked = _validated(items, now=now)
        return cls(subject_id=subject_id, items=checked, created_at=now, updated_at=now)

    def update(self, *, items: list[PortfolioItem], now: datetime) -> None:
        # Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
        # kein halb geändertes Aggregat hinterlassen.
        checked = _validated(items, now=now)
        self.items = checked
        self.updated_at = now


def _validated(items: list[PortfolioItem], *, now: datetime) -> tuple[PortfolioItem, ...]:
    """Gemeinsam, damit `create` und `update` nicht auseinanderlaufen können.

    Die Reihenfolge bleibt, wie sie kam: ein Portfolio hat keine natürliche
    Ordnung, „das hier zuerst" ist eine Entscheidung der Person.
    """
    if len(items) > MAX_ITEMS:
        raise TooManyItems()
    for entry in items:
        _year(entry.year, now=now)
    return tuple(items)
