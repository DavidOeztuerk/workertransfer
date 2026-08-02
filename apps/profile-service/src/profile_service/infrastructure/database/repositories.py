"""SQLAlchemy-Umsetzung der Profil-Ports."""

from __future__ import annotations

import base64
from datetime import datetime
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from profile_service.domain.profile import Profile, Skills
from profile_service.infrastructure.database.models import ProfileModel

__all__ = ["SqlAlchemyProfileRepository", "decode_cursor", "encode_cursor"]


def encode_cursor(updated_at: datetime, subject_id: UUID) -> str:
    """Der Cursor trägt beide Sortierschlüssel.

    `updated_at` allein reicht nicht: zwei Profile in derselben Sekunde würden
    sich beim Blättern gegenseitig überspringen oder doppelt erscheinen.
    """
    raw = f"{updated_at.isoformat()}|{subject_id}"
    return base64.urlsafe_b64encode(raw.encode()).decode()


def decode_cursor(cursor: str) -> tuple[datetime, UUID] | None:
    """Ein unlesbarer Cursor ist kein Fehler, sondern der Anfang.

    Er kommt aus einer URL und wird kopiert, gekürzt und weitergereicht; darauf
    mit einem 400 zu antworten hilft niemandem.
    """
    try:
        raw = base64.urlsafe_b64decode(cursor.encode()).decode()
        stamp, subject = raw.split("|", 1)
        return datetime.fromisoformat(stamp), UUID(subject)
    except Exception:
        return None


def _to_domain(row: ProfileModel) -> Profile:
    return Profile(
        subject_id=row.id,
        headline=row.headline,
        bio=row.bio,
        location=row.location,
        remote_ok=row.remote_ok,
        # Skills normalisiert erneut; eine gespeicherte Zeile ist bereits
        # gültig, aber der Konstruktor ist die einzige Stelle, die das Tupel baut.
        skills=Skills(list(row.skills)),
        created_at=row.created_at,
        updated_at=row.updated_at,
    )


class SqlAlchemyProfileRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, subject_id: UUID) -> Profile | None:
        row = await self._session.get(ProfileModel, subject_id)
        return _to_domain(row) if row is not None else None

    async def save(self, profile: Profile) -> None:
        """Anlegen oder aktualisieren — ein Profil je Person.

        Das Aggregat kommt losgelöst aus `get()`; ohne dieses Zurückschreiben
        bliebe jede Änderung im Arbeitsspeicher (die Falle aus ADR-0019).
        """
        row = await self._session.get(ProfileModel, profile.subject_id)
        if row is None:
            self._session.add(
                ProfileModel(
                    id=profile.subject_id,
                    headline=profile.headline,
                    bio=profile.bio,
                    location=profile.location,
                    remote_ok=profile.remote_ok,
                    skills=list(profile.skills.value),
                    created_at=profile.created_at,
                    updated_at=profile.updated_at,
                )
            )
        else:
            row.headline = profile.headline
            row.bio = profile.bio
            row.location = profile.location
            row.remote_ok = profile.remote_ok
            row.skills = list(profile.skills.value)
            row.updated_at = profile.updated_at
        await self._session.flush()

    async def page(self, *, limit: int, cursor: str | None) -> tuple[list[Profile], str | None]:
        """Eine Seite, zuletzt geänderte zuerst.

        Liefert `limit + 1` Zeilen, um ohne zweite Abfrage zu wissen, ob es
        weitergeht. Die Consent-Filterung passiert danach in der Application-
        Schicht — das Repository kennt keine Sichtbarkeit.
        """
        stmt = select(ProfileModel).order_by(ProfileModel.updated_at.desc(), ProfileModel.id.desc())
        position = decode_cursor(cursor) if cursor else None
        if position is not None:
            stamp, subject = position
            stmt = stmt.where(
                (ProfileModel.updated_at < stamp)
                | ((ProfileModel.updated_at == stamp) & (ProfileModel.id < subject))
            )
        rows = list((await self._session.execute(stmt.limit(limit + 1))).scalars().all())
        has_more = len(rows) > limit
        rows = rows[:limit]
        next_cursor = encode_cursor(rows[-1].updated_at, rows[-1].id) if has_more and rows else None
        return [_to_domain(r) for r in rows], next_cursor
