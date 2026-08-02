"""SQLAlchemy-Umsetzung der Profil-Ports."""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from companies_service.domain.company_profile import CompanyProfile
from companies_service.infrastructure.database.models import CompanyProfileModel

__all__ = ["SqlAlchemyCompanyProfileRepository"]


def _to_domain(row: CompanyProfileModel) -> CompanyProfile:
    # Geht durch `create`: die Regeln sollen auch für gespeicherte Zeilen
    # gelten, damit eine von Hand veränderte Zeile nicht unbemerkt durchrutscht.
    profile = CompanyProfile.create(
        row.id,
        slug=row.slug,
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

    async def get_by_slug(self, slug: str) -> CompanyProfile | None:
        stmt = select(CompanyProfileModel).where(CompanyProfileModel.slug == slug)
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _to_domain(row)

    async def free_slug(self, wanted: str) -> str:
        """Das Kürzel, oder das nächste freie mit Zähler.

        Nicht die Tenant-UUID anhängen: die stünde dann in einer URL, die
        weitergegeben wird.
        """
        taken = set(
            (
                await self._session.execute(
                    select(CompanyProfileModel.slug).where(
                        CompanyProfileModel.slug.like(f"{wanted}%")
                    )
                )
            ).scalars()
        )
        if wanted not in taken:
            return wanted
        suffix = 2
        while f"{wanted}-{suffix}" in taken:
            suffix += 1
        return f"{wanted}-{suffix}"

    async def save(self, profile: CompanyProfile) -> None:
        row = await self._session.get(CompanyProfileModel, profile.tenant_id)
        if row is None:
            row = CompanyProfileModel(
                id=profile.tenant_id,
                # Das Kürzel wird nur beim Anlegen geschrieben — es ist die
                # Adresse, und eine Adresse, die sich ändert, ist keine.
                slug=profile.slug,
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
