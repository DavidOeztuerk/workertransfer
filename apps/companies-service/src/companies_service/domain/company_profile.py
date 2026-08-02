"""Das Arbeitgeberprofil — wie ein Unternehmen sich zeigt.

`tenants` in identity-service hält Name und Domain: die **Identität**, entstanden
aus einem Domain-Nachweis (ADR-0019). Hier geht es um **Darstellung**. Dieselbe
Trennung wie zwischen identity-service und profile-service bei einer Person: die
eine Seite weiß, wer jemand ist, die andere, wie er sich zeigt.

Der Consent-Ledger kommt nicht vor — hier ist niemand betroffen, der einwilligen
könnte. Ein Unternehmen macht eine Aussage über sich selbst.
"""

from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from datetime import datetime
from urllib.parse import urlparse
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "MAX_ENTRIES",
    "CompanyProfile",
    "InvalidText",
    "InvalidUrl",
    "TooManyEntries",
    "slug_from",
]

MAX_DISPLAY_NAME = 160
MAX_ABOUT = 8000
MAX_URL = 2000
MAX_ENTRY = 120
MAX_ENTRIES = 20

#: Dieselbe Regel wie bei Portfolio-Links (ADR-0021): ein Link wird von fremden
#: Menschen angeklickt, und `javascript:` in einem Feld, das im Browser landet,
#: ist kein Randfall.
ALLOWED_SCHEMES = frozenset({"http", "https"})

MAX_SLUG = 60
_SLUG_STRIP = re.compile(r"[^a-z0-9]+")


def slug_from(display_name: str) -> str:
    """Ein Kürzel für die Karriere-Seite, abgeleitet aus dem Anzeigenamen.

    Abgeleitet und nicht eingegeben: ein freies Feld lädt zum Besetzen fremder
    Namen ein. Umlaute werden zerlegt und ihre Grundbuchstaben behalten — „Grün"
    wird `gruen` nur mit einer Ersetzungstabelle, `grun` ohne; letzteres ist
    ehrlicher als eine Tabelle, die bei der nächsten Sprache falsch liegt.

    Bleibt nichts übrig (etwa bei einem rein chinesischen Namen), fällt das
    Kürzel auf `unternehmen` zurück — der Zähler beim Speichern macht daraus
    `unternehmen-2` und so weiter. Eine leere Adresse wäre schlimmer als eine
    unpersönliche.
    """
    folded = unicodedata.normalize("NFKD", display_name.casefold())
    ascii_only = folded.encode("ascii", "ignore").decode()
    slug = _SLUG_STRIP.sub("-", ascii_only).strip("-")[:MAX_SLUG].strip("-")
    return slug or "unternehmen"


class InvalidText(DomainError):
    def __init__(self, field: str, detail: str) -> None:
        super().__init__("invalid_text", f"{field} {detail}")


class InvalidUrl(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("invalid_url", detail)


class TooManyEntries(DomainError):
    def __init__(self, field: str) -> None:
        super().__init__("too_many_entries", f"At most {MAX_ENTRIES} {field} are allowed")


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
        # Leer und „nicht angegeben" sind dasselbe; ein leerer String würde als
        # Link gerendert und führte ins Nichts.
        return None
    if len(cleaned) > MAX_URL:
        raise InvalidUrl(f"The link exceeds {MAX_URL} characters")
    parsed = urlparse(cleaned)
    if parsed.scheme.lower() not in ALLOWED_SCHEMES:
        raise InvalidUrl("Only http and https links are allowed")
    if not parsed.netloc:
        raise InvalidUrl("The link is missing a host")
    return cleaned


def _entries(field: str, raw: list[str]) -> tuple[str, ...]:
    """Getrimmt, ohne Leeres, ohne Dubletten — Reihenfolge bleibt.

    Erst entdoppeln, dann zählen: sonst würde jemand mit einundzwanzigmal
    „Homeoffice" abgewiesen, obwohl daraus ein Eintrag wird.
    """
    cleaned: list[str] = []
    seen: set[str] = set()
    for entry in raw:
        item = entry.strip()
        if not item:
            continue
        if len(item) > MAX_ENTRY:
            raise InvalidText(field, f"entry exceeds {MAX_ENTRY} characters")
        key = item.casefold()
        if key in seen:
            continue
        seen.add(key)
        cleaned.append(item)
    if len(cleaned) > MAX_ENTRIES:
        raise TooManyEntries(field)
    return tuple(cleaned)


@dataclass(eq=False, slots=True)
class CompanyProfile:
    """Ein Profil je Unternehmen; `tenant_id` IST der Schlüssel."""

    tenant_id: UUID
    #: Die Adresse der Karriere-Seite. Einmal vergeben und danach
    #: unveränderlich — sie ist ein Versprechen, und ein Kürzel, das dem
    #: Anzeigenamen folgt, bricht jeden geteilten Link.
    slug: str
    display_name: str
    about: str
    website: str | None
    locations: tuple[str, ...]
    benefits: tuple[str, ...]
    created_at: datetime
    updated_at: datetime

    @classmethod
    def create(
        cls,
        tenant_id: UUID,
        *,
        slug: str,
        display_name: str,
        about: str,
        website: str | None,
        locations: list[str],
        benefits: list[str],
        now: datetime,
    ) -> CompanyProfile:
        checked = _validated(display_name, about, website, locations, benefits)
        return cls(
            tenant_id=tenant_id,
            slug=slug,
            display_name=checked.display_name,
            about=checked.about,
            website=checked.website,
            locations=checked.locations,
            benefits=checked.benefits,
            created_at=now,
            updated_at=now,
        )

    def update(
        self,
        *,
        display_name: str,
        about: str,
        website: str | None,
        locations: list[str],
        benefits: list[str],
        now: datetime,
    ) -> None:
        # Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
        # kein halb geändertes Aggregat hinterlassen.
        checked = _validated(display_name, about, website, locations, benefits)
        self.display_name = checked.display_name
        self.about = checked.about
        self.website = checked.website
        self.locations = checked.locations
        self.benefits = checked.benefits
        self.updated_at = now


@dataclass(frozen=True, slots=True)
class _Checked:
    """Die geprüften Werte, getippt.

    Ein `dict[str, object]` wäre kürzer und würde jeden Zuweisungsfehler erst
    zur Laufzeit zeigen — bei fünf Feldern, die alle Strings oder Tupel davon
    sind, ist das genau die Verwechslung, die niemand bemerkt. mypy hat sie
    hier auch prompt gefunden.
    """

    display_name: str
    about: str
    website: str | None
    locations: tuple[str, ...]
    benefits: tuple[str, ...]


def _validated(
    display_name: str,
    about: str,
    website: str | None,
    locations: list[str],
    benefits: list[str],
) -> _Checked:
    """Gemeinsam, damit `create` und `update` nicht auseinanderlaufen können."""
    return _Checked(
        # Der Anzeigename ist NICHT `tenants.name`: der eine ist der Kontoname
        # bei der Anlage, der andere die Marke. Keiner wird aus dem anderen
        # abgeleitet, also gibt es hier keine Kopie, die driften könnte.
        display_name=_text("Display name", display_name, required=True, limit=MAX_DISPLAY_NAME),
        about=_text("About", about, required=False, limit=MAX_ABOUT),
        website=_url(website),
        locations=_entries("locations", locations),
        benefits=_entries("benefits", benefits),
    )
