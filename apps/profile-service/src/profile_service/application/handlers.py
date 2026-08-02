"""Commands, Queries und ihre Handler.

Die Handler treiben die Regel dieses Slices: das eigene Profil gehört einem
ohne Rückfrage, ein fremdes nur mit Einwilligung — und die wird bei jedem
Abruf frisch geholt (ADR-0013, kein Cache).
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from profile_service.domain.profile import Profile, Skills

__all__ = [
    "MAX_PAGE_SIZE",
    "GetProfileQuery",
    "ListProfilesQuery",
    "ProfileNotVisible",
    "SaveMyProfileCommand",
    "handle_get_my_profile",
    "handle_get_visible_profile",
    "handle_list_visible_profiles",
    "handle_save_my_profile",
]

#: Obergrenze je Seite. Jede Zeile kostet eine Ledger-Abfrage; ohne Deckel
#: könnte ein Aufrufer den Consent-Service mit einer einzigen Anfrage fluten.
MAX_PAGE_SIZE = 50
DEFAULT_PAGE_SIZE = 20


class ProfileNotVisible(DomainError):
    """Nicht vorhanden ODER nicht freigegeben — von außen dasselbe.

    Ein eigener Fehler für „existiert, zeigt sich aber nicht" würde genau das
    preisgeben, was die Person zurückhalten wollte (product-scope.md).
    """

    def __init__(self) -> None:
        super().__init__("profile_not_visible", "No such profile")


@dataclass(frozen=True, slots=True)
class SaveMyProfileCommand:
    subject_id: UUID
    headline: str
    bio: str
    location: str
    remote_ok: bool
    skills: list[str]


async def handle_save_my_profile(
    cmd: SaveMyProfileCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Profile]:
    now = deps["clock"].now()
    try:
        skills = Skills(cmd.skills)
        existing: Profile | None = await repos["profiles"].get(cmd.subject_id)
        if existing is None:
            profile = Profile.create(
                subject_id=cmd.subject_id,
                headline=cmd.headline,
                bio=cmd.bio,
                location=cmd.location,
                remote_ok=cmd.remote_ok,
                skills=skills,
                now=now,
            )
        else:
            # update() prüft vollständig, bevor es schreibt — ein abgelehntes
            # Formular hinterlässt kein halb geändertes Aggregat.
            existing.update(
                headline=cmd.headline,
                bio=cmd.bio,
                location=cmd.location,
                remote_ok=cmd.remote_ok,
                skills=skills,
                now=now,
            )
            profile = existing
        await repos["profiles"].save(profile)
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(profile)


async def handle_get_my_profile(subject_id: UUID, *, repos: dict[str, Any]) -> Profile | None:
    """Das eigene Profil — ohne Ledger-Abfrage, ohne Result.

    Die eigene Einwilligung zu prüfen, um sich selbst zu sehen, wäre nicht nur
    ein überflüssiger Round-Trip, sondern falsch: wer nichts freigegeben hat,
    könnte sein Profil sonst nicht mehr bearbeiten.

    Kein `Result`, weil es keinen fachlichen Fehlerfall gibt — „noch keins
    angelegt" ist ein Zustand, kein Fehler. Nebenbei umgeht das eine Falle in
    `worker_core.Result`: `.value` wirft, wenn der Erfolgswert `None` ist, weil
    dort „kein Wert" und „der Wert ist None" nicht unterschieden werden.
    """
    profile: Profile | None = await repos["profiles"].get(subject_id)
    return profile


@dataclass(frozen=True, slots=True)
class GetProfileQuery:
    subject_id: UUID
    #: Das Unternehmen des Aufrufers — aus dem Token, nie aus dem Request.
    tenant_id: UUID
    bearer: str


async def handle_get_visible_profile(
    query: GetProfileQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Profile]:
    profile: Profile | None = await repos["profiles"].get(query.subject_id)
    if profile is None:
        # Kein Ledger-Aufruf für ein Profil, das es nicht gibt: unnötiger
        # Round-Trip, und er meldete dem Ledger geratene Subject-IDs.
        return Result.fail(ProfileNotVisible())
    # ConsentUnavailable fliegt bewusst durch: der Router macht daraus 503.
    # Es hier zu False zu machen hieße zu behaupten, die Person habe nicht
    # eingewilligt — das wissen wir nicht.
    if not await deps["consent"].may_see(
        query.subject_id, tenant_id=query.tenant_id, bearer=query.bearer
    ):
        return Result.fail(ProfileNotVisible())
    return Result.ok(profile)


@dataclass(frozen=True, slots=True)
class ListProfilesQuery:
    limit: int
    cursor: str | None
    tenant_id: UUID
    bearer: str


async def handle_list_visible_profiles(
    query: ListProfilesQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[tuple[list[Profile], str | None]]:
    """Eine Seite Kandidaten, gefiltert auf die freigegebenen.

    Die Seite kann weniger Einträge liefern als angefragt. Nachzuladen, bis sie
    voll ist, würde über die Anzahl der Runden verraten, wie viele Profile NICHT
    freigegeben sind — und genau das ist die Information, die der Ledger
    schützt.
    """
    limit = max(1, min(query.limit or DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE))
    candidates, next_cursor = await repos["profiles"].page(limit=limit, cursor=query.cursor)
    if not candidates:
        return Result.ok(([], next_cursor))

    # Parallel: eine Seite bedeutet `limit` Abfragen an einen Service im selben
    # Netz. Nacheinander wären das aufsummierte Latenzen ohne Grund.
    verdicts = await asyncio.gather(
        *(
            deps["consent"].may_see(p.subject_id, tenant_id=query.tenant_id, bearer=query.bearer)
            for p in candidates
        )
    )
    visible = [profile for profile, allowed in zip(candidates, verdicts, strict=True) if allowed]
    return Result.ok((visible, next_cursor))
