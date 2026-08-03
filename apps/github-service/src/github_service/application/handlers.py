"""Commands, Queries und ihre Handler."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from github_service.domain.connection import GitHubConnection

__all__ = [
    "ConnectCommand",
    "NoConnection",
    "NotProven",
    "handle_connect",
    "handle_disconnect",
    "handle_get_mine",
    "handle_get_visible",
    "handle_refresh",
    "handle_verify",
]


class NoConnection(DomainError):
    """Keine Verbindung — oder nicht freigegeben. Von außen dasselbe."""

    def __init__(self) -> None:
        super().__init__("no_connection", "No such connection")


class NotProven(DomainError):
    """Der Gist mit der Einmalzeichenfolge war nicht zu finden."""

    def __init__(self) -> None:
        super().__init__("not_proven", "The verification gist was not found")


@dataclass(frozen=True, slots=True)
class ConnectCommand:
    subject_id: UUID
    login: str


async def handle_connect(
    cmd: ConnectCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[GitHubConnection]:
    """Einen Benutzernamen nennen — mehr passiert hier nicht.

    Kein Abruf bei GitHub: solange nichts bewiesen ist, gibt es nichts zu
    holen, und ein Abruf würde nur verraten, dass jemand nach diesem Konto
    gefragt hat.
    """
    _ = deps
    existing: GitHubConnection | None = await repos["connections"].get(cmd.subject_id)
    try:
        if existing is None:
            connection = GitHubConnection.open(subject_id=cmd.subject_id, login=cmd.login)
        else:
            connection = existing
            connection.relink(cmd.login)
    except DomainError as exc:
        return Result.fail(exc)
    await repos["connections"].save(connection)
    return Result.ok(connection)


async def handle_verify(
    subject_id: UUID, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[GitHubConnection]:
    """Nachweis prüfen und im selben Zug den Abzug holen.

    Zusammen, weil eine bewiesene Verbindung ohne Inhalt für niemanden etwas
    tut — und ein zweiter Knopf „jetzt auch laden" nur eine Gelegenheit wäre,
    ihn nicht zu drücken.
    """
    connection: GitHubConnection | None = await repos["connections"].get(subject_id)
    if connection is None:
        return Result.fail(NoConnection())
    if not connection.is_verified:
        # GitHubUnavailable fliegt bewusst durch: der Router macht daraus 503.
        # Es hier auf „nicht bewiesen" abzubilden hieße, jemandem den Nachweis
        # abzusprechen, weil WIR gerade nicht fragen konnten.
        if not await deps["github"].has_challenge_gist(connection.login, connection.challenge):
            return Result.fail(NotProven())
        connection.verify(now=deps["clock"].now())

    repositories = await deps["github"].repositories(connection.login)
    connection.store(repositories, now=deps["clock"].now())
    await repos["connections"].save(connection)
    return Result.ok(connection)


async def handle_refresh(
    subject_id: UUID, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[GitHubConnection]:
    connection: GitHubConnection | None = await repos["connections"].get(subject_id)
    if connection is None or not connection.is_verified:
        return Result.fail(NoConnection())
    connection.store(await deps["github"].repositories(connection.login), now=deps["clock"].now())
    await repos["connections"].save(connection)
    return Result.ok(connection)


async def handle_get_mine(subject_id: UUID, *, repos: dict[str, Any]) -> GitHubConnection | None:
    """Auch die unbewiesene Verbindung.

    Sonst sähe die Person nach dem ersten Schritt gar nichts und wüsste nicht,
    welche Zeichenfolge sie in den Gist schreiben soll.
    """
    connection: GitHubConnection | None = await repos["connections"].get(subject_id)
    return connection


@dataclass(frozen=True, slots=True)
class VisibleQuery:
    subject_id: UUID
    bearer: str


async def handle_get_visible(
    query: VisibleQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[GitHubConnection]:
    connection: GitHubConnection | None = await repos["connections"].get(query.subject_id)
    # Eine unbewiesene Verbindung ist von außen nicht vorhanden: sie ist eine
    # Behauptung, und Behauptungen zeigt dieser Dienst nicht.
    if connection is None or not connection.is_verified:
        return Result.fail(NoConnection())
    # ConsentUnavailable fliegt durch — der Router macht daraus 503. Hier auf
    # False zu gehen hieße zu behaupten, die Person habe nicht eingewilligt.
    if not await deps["consent"].may_see(query.subject_id, bearer=query.bearer):
        return Result.fail(NoConnection())
    return Result.ok(connection)


async def handle_disconnect(subject_id: UUID, *, repos: dict[str, Any]) -> None:
    """Trennen heißt löschen — der Abzug verschwindet mit."""
    await repos["connections"].delete(subject_id)
