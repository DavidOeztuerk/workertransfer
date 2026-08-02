"""Commands, Queries und ihre Handler.

Der Consent-Ledger kommt nicht vor: ein Arbeitgeberprofil ist eine Aussage des
Unternehmens über sich selbst, und es gibt niemanden, der einwilligen könnte.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from companies_service.domain.company_profile import CompanyProfile, slug_from

__all__ = [
    "ProfileNotFound",
    "SaveCompanyProfileCommand",
    "handle_get_by_slug",
    "handle_get_own_profile",
    "handle_get_public_profile",
    "handle_save_profile",
]


class ProfileNotFound(DomainError):
    def __init__(self) -> None:
        super().__init__("profile_not_found", "No such company profile")


@dataclass(frozen=True, slots=True)
class SaveCompanyProfileCommand:
    tenant_id: UUID
    display_name: str
    about: str
    website: str | None
    locations: list[str]
    benefits: list[str]


async def handle_save_profile(
    cmd: SaveCompanyProfileCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[CompanyProfile]:
    now = deps["clock"].now()
    try:
        existing: CompanyProfile | None = await repos["profiles"].get(cmd.tenant_id)
        if existing is None:
            # Das Kürzel entsteht genau einmal, beim ersten Speichern.
            slug = await repos["profiles"].free_slug(slug_from(cmd.display_name))
            profile = CompanyProfile.create(
                cmd.tenant_id,
                slug=slug,
                display_name=cmd.display_name,
                about=cmd.about,
                website=cmd.website,
                locations=cmd.locations,
                benefits=cmd.benefits,
                now=now,
            )
        else:
            existing.update(
                display_name=cmd.display_name,
                about=cmd.about,
                website=cmd.website,
                locations=cmd.locations,
                benefits=cmd.benefits,
                now=now,
            )
            profile = existing
        await repos["profiles"].save(profile)
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(profile)


async def handle_get_own_profile(
    tenant_id: UUID, *, repos: dict[str, Any]
) -> CompanyProfile | None:
    """Kein `Result`: „noch keins angelegt" ist ein Zustand, kein Fehler."""
    profile: CompanyProfile | None = await repos["profiles"].get(tenant_id)
    return profile


async def handle_get_by_slug(slug: str, *, repos: dict[str, Any]) -> Result[CompanyProfile]:
    """Die Karriere-Seite. `404`, wenn es das Kürzel nicht gibt."""
    profile: CompanyProfile | None = await repos["profiles"].get_by_slug(slug)
    if profile is None:
        return Result.fail(ProfileNotFound())
    return Result.ok(profile)


async def handle_get_public_profile(
    tenant_id: UUID, *, repos: dict[str, Any]
) -> Result[CompanyProfile]:
    """Für die Öffentlichkeit gibt es nichts, solange nichts angelegt wurde.

    Anders als beim eigenen: dort ist „noch keins" ein Formular, hier ist es
    eine Stelle, die anonym bleibt.
    """
    profile: CompanyProfile | None = await repos["profiles"].get(tenant_id)
    if profile is None:
        return Result.fail(ProfileNotFound())
    return Result.ok(profile)
