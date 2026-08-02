"""SQLAlchemy-Umsetzung der Profil-Ports."""

from __future__ import annotations

from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession

from companies_service.domain.company_profile import CompanyProfile
from companies_service.infrastructure.database.models import CompanyProfileModel

__all__ = ["SqlAlchemyCompanyProfileRepository"]


def _to_domain(row: CompanyProfileModel) -> CompanyProfile:
    # Geht durch `create`: die Regeln sollen auch für gespeicherte Zeilen
    # gelten, damit eine von Hand veränderte Zeile nicht unbemerkt durchrutscht.
    profile = CompanyProfile.create(
        row.id,
        display_name=row.display_name,
        about=row.about,
        website=row.website,
        locations=list(row.locations),
        benefits=list(row.benefits),
        now=row.updated_at,
    )
    profile.created_at = row.created_at
    return profile


class SqlAlchemyCompanyProfileRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, tenant_id: UUID) -> CompanyProfile | None:
        row = await self._session.get(CompanyProfileModel, tenant_id)
        return None if row is None else _to_domain(row)

    async def save(self, profile: CompanyProfile) -> None:
        row = await self._session.get(CompanyProfileModel, profile.tenant_id)
        if row is None:
            row = CompanyProfileModel(
                id=profile.tenant_id,
                display_name=profile.display_name,
                created_at=profile.created_at,
                updated_at=profile.updated_at,
            )
            self._session.add(row)
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.display_name = profile.display_name
        row.about = profile.about
        row.website = profile.website
        row.locations = list(profile.locations)
        row.benefits = list(profile.benefits)
        row.updated_at = profile.updated_at
