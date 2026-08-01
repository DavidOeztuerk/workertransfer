"""Das Profil einer Person — was sie selbst über sich einträgt.

Bewusst schmal: Überschrift, Freitext, Ort, Remote-Bereitschaft, Fähigkeiten.
Strukturierte Berufserfahrung ist der Lebenslauf (Sub-step 3.3), verifizierte
Nachweise kommen aus dem Skill-Graph (Phase 6). Beides würde hier nur doppelt
gepflegt.

Das Profil kennt keine Sichtbarkeit. Ob es jemand sehen darf, beantwortet der
Consent-Ledger (ADR-0013) — ein Flag an dieser Stelle wäre eine zweite Wahrheit.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "InvalidBio",
    "InvalidHeadline",
    "InvalidLocation",
    "Profile",
    "Skills",
    "TooManySkills",
]

MAX_HEADLINE = 120
MAX_BIO = 4000
MAX_LOCATION = 120
MAX_SKILLS = 30
MAX_SKILL_LENGTH = 50


class InvalidHeadline(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__("invalid_headline", f"Headline {reason}")


class InvalidBio(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_bio", f"Bio exceeds {MAX_BIO} characters")


class InvalidLocation(DomainError):
    def __init__(self) -> None:
        super().__init__("invalid_location", f"Location exceeds {MAX_LOCATION} characters")


class TooManySkills(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__("invalid_skills", reason)


@dataclass(frozen=True, slots=True)
class Skills:
    """Fähigkeiten als normalisierte, reihenfolgetreue Liste."""

    value: tuple[str, ...]

    def __init__(self, raw: list[str] | tuple[str, ...]) -> None:
        cleaned: list[str] = []
        seen: set[str] = set()
        for entry in raw:
            item = entry.strip()
            if not item:
                continue
            if len(item) > MAX_SKILL_LENGTH:
                raise TooManySkills(f"A skill must not exceed {MAX_SKILL_LENGTH} characters")
            # Groß-/Kleinschreibung ist keine zweite Fähigkeit. Die erste
            # Schreibweise gewinnt — so hat die Person sie eingetragen.
            key = item.casefold()
            if key in seen:
                continue
            seen.add(key)
            cleaned.append(item)
        # Erst entdoppeln, dann zählen: sonst würde jemand mit 31-mal "Python"
        # abgewiesen, obwohl daraus eine einzige Fähigkeit wird.
        if len(cleaned) > MAX_SKILLS:
            raise TooManySkills(f"At most {MAX_SKILLS} skills are allowed")
        object.__setattr__(self, "value", tuple(cleaned))


def _validated(headline: str, bio: str, location: str) -> tuple[str, str, str]:
    """Prüft und normalisiert die Textfelder gemeinsam.

    Gemeinsam, damit `create` und `update` nicht auseinanderlaufen können — und
    damit ein `update` erst schreibt, wenn ALLE Felder gültig sind.
    """
    cleaned_headline = headline.strip()
    if not cleaned_headline:
        raise InvalidHeadline("must not be empty")
    if len(cleaned_headline) > MAX_HEADLINE:
        raise InvalidHeadline(f"exceeds {MAX_HEADLINE} characters")
    cleaned_bio = bio.strip()
    if len(cleaned_bio) > MAX_BIO:
        raise InvalidBio()
    cleaned_location = location.strip()
    if len(cleaned_location) > MAX_LOCATION:
        raise InvalidLocation()
    return cleaned_headline, cleaned_bio, cleaned_location


@dataclass(eq=False, slots=True)
class Profile:
    """Ein Profil pro Person; `subject_id` IST der Schlüssel."""

    subject_id: UUID
    headline: str
    bio: str
    location: str
    remote_ok: bool
    skills: Skills
    created_at: datetime
    updated_at: datetime

    @classmethod
    def create(
        cls,
        *,
        subject_id: UUID,
        headline: str,
        bio: str,
        location: str,
        remote_ok: bool,
        skills: Skills,
        now: datetime,
    ) -> Profile:
        cleaned_headline, cleaned_bio, cleaned_location = _validated(headline, bio, location)
        return cls(
            subject_id=subject_id,
            headline=cleaned_headline,
            bio=cleaned_bio,
            location=cleaned_location,
            remote_ok=remote_ok,
            skills=skills,
            created_at=now,
            updated_at=now,
        )

    def update(
        self,
        *,
        headline: str,
        bio: str,
        location: str,
        remote_ok: bool,
        skills: Skills,
        now: datetime,
    ) -> None:
        # Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
        # kein halb geändertes Aggregat hinterlassen.
        cleaned_headline, cleaned_bio, cleaned_location = _validated(headline, bio, location)
        self.headline = cleaned_headline
        self.bio = cleaned_bio
        self.location = cleaned_location
        self.remote_ok = remote_ok
        self.skills = skills
        self.updated_at = now
