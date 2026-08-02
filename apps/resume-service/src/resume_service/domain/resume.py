"""Der Lebenslauf einer Person — Stationen und Ausbildung.

Anders als das Profil ist das kein Aushang: hier stehen echte Arbeitgeber mit
Zeiträumen. Wer er sehen darf, entscheidet nicht dieses Modul, sondern der
Consent-Ledger, und zwar je Unternehmen einzeln (Design-Spec §„Der Anfrage-Fluss").

Das Aggregat kennt deshalb weder Sichtbarkeit noch Empfänger — ein Feld dafür
wäre eine zweite Wahrheit neben dem Ledger (ADR-0020 §6).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime
from typing import Self
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "MAX_EDUCATION",
    "MAX_POSITIONS",
    "Education",
    "InvalidMonth",
    "InvalidText",
    "MonthDate",
    "Position",
    "Resume",
    "TooManyEntries",
    "TwoOpenPositions",
]

MAX_POSITIONS = 40
MAX_EDUCATION = 20
MAX_NAME = 160
MAX_DESCRIPTION = 2000

_MONTH_RE = re.compile(r"^(\d{4})-(0[1-9]|1[0-2])$")


class InvalidMonth(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("invalid_month", detail)


class InvalidText(DomainError):
    def __init__(self, field: str, detail: str) -> None:
        super().__init__("invalid_text", f"{field} {detail}")


class TooManyEntries(DomainError):
    def __init__(self, detail: str) -> None:
        super().__init__("too_many_entries", detail)


class TwoOpenPositions(DomainError):
    def __init__(self) -> None:
        super().__init__(
            "two_open_positions",
            "Only one position may be left open; an open one means 'still there'",
        )


@dataclass(frozen=True, order=True, slots=True)
class MonthDate:
    """Ein Monat, kein Tag.

    Kein Lebenslauf der Welt nennt den 14. März. Eine Tagesangabe suggeriert eine
    Präzision, die niemand hat, und macht aus einer Lücke von drei Wochen einen
    Rechtfertigungsdruck.

    `order=True` mit (year, month) in dieser Reihenfolge: der erzeugte Vergleich
    ist damit chronologisch.
    """

    year: int
    month: int

    @classmethod
    def parse(cls, value: str) -> Self:
        match = _MONTH_RE.match(value.strip())
        if match is None:
            raise InvalidMonth(f"Expected YYYY-MM, got {value!r}")
        return cls(year=int(match.group(1)), month=int(match.group(2)))

    def __str__(self) -> str:
        return f"{self.year:04d}-{self.month:02d}"


def _text(field: str, value: str, *, required: bool, limit: int) -> str:
    cleaned = value.strip()
    if required and not cleaned:
        raise InvalidText(field, "must not be empty")
    if len(cleaned) > limit:
        raise InvalidText(field, f"exceeds {limit} characters")
    return cleaned


def _check_span(started_on: MonthDate, ended_on: MonthDate | None) -> None:
    # Gleicher Monat ist erlaubt: eine einmonatige Probezeit ist kurz, aber keine
    # Falscheingabe.
    if ended_on is not None and ended_on < started_on:
        raise InvalidMonth(f"End {ended_on} lies before start {started_on}")


@dataclass(frozen=True, slots=True)
class Position:
    employer: str
    title: str
    started_on: MonthDate
    ended_on: MonthDate | None
    description: str = ""

    def __post_init__(self) -> None:
        object.__setattr__(
            self, "employer", _text("Employer", self.employer, required=True, limit=MAX_NAME)
        )
        object.__setattr__(self, "title", _text("Title", self.title, required=True, limit=MAX_NAME))
        object.__setattr__(
            self,
            "description",
            _text("Description", self.description, required=False, limit=MAX_DESCRIPTION),
        )
        _check_span(self.started_on, self.ended_on)

    @property
    def is_current(self) -> bool:
        """`ended_on is None` heißt „läuft noch", nicht „unbekannt"."""
        return self.ended_on is None


@dataclass(frozen=True, slots=True)
class Education:
    institution: str
    qualification: str
    started_on: MonthDate
    ended_on: MonthDate | None = None

    def __post_init__(self) -> None:
        object.__setattr__(
            self,
            "institution",
            _text("Institution", self.institution, required=True, limit=MAX_NAME),
        )
        object.__setattr__(
            self,
            "qualification",
            _text("Qualification", self.qualification, required=False, limit=MAX_NAME),
        )
        _check_span(self.started_on, self.ended_on)


def _sorted_newest_first[T: Position | Education](entries: list[T]) -> tuple[T, ...]:
    """Laufende zuerst, danach absteigend nach Beginn.

    Aus den Daten statt aus einer `sort_order`-Spalte: die müsste bei jeder
    Bearbeitung mitgepflegt werden und wäre irgendwann falsch.
    """
    # `ended_on is None` als erstes Schlüsselelement, damit die laufende Station
    # unter `reverse=True` als True nach vorn wandert — und danach absteigend
    # nach Beginn.
    return tuple(sorted(entries, key=lambda e: (e.ended_on is None, e.started_on), reverse=True))


@dataclass(eq=False, slots=True)
class Resume:
    """Ein Lebenslauf je Person; `subject_id` IST der Schlüssel."""

    subject_id: UUID
    positions: tuple[Position, ...]
    education: tuple[Education, ...]
    created_at: datetime
    updated_at: datetime

    @classmethod
    def create(
        cls,
        subject_id: UUID,
        *,
        positions: list[Position],
        education: list[Education],
        now: datetime,
    ) -> Resume:
        checked_positions, checked_education = _validated(positions, education)
        return cls(
            subject_id=subject_id,
            positions=checked_positions,
            education=checked_education,
            created_at=now,
            updated_at=now,
        )

    def update(
        self, *, positions: list[Position], education: list[Education], now: datetime
    ) -> None:
        # Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
        # kein halb geändertes Aggregat hinterlassen.
        checked_positions, checked_education = _validated(positions, education)
        self.positions = checked_positions
        self.education = checked_education
        self.updated_at = now


def _validated(
    positions: list[Position], education: list[Education]
) -> tuple[tuple[Position, ...], tuple[Education, ...]]:
    """Gemeinsam, damit `create` und `update` nicht auseinanderlaufen können."""
    if len(positions) > MAX_POSITIONS:
        raise TooManyEntries(f"At most {MAX_POSITIONS} positions are allowed")
    if len(education) > MAX_EDUCATION:
        raise TooManyEntries(f"At most {MAX_EDUCATION} education entries are allowed")
    if sum(1 for entry in positions if entry.is_current) > 1:
        raise TwoOpenPositions()
    return _sorted_newest_first(positions), _sorted_newest_first(education)
