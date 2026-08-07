"""Die Verbindung zu einem GitHub-Konto — bewiesen, nicht behauptet.

Was hier NICHT steht, ist der Punkt (ADR-0022): keine Punktzahl, keine
abgeleiteten Eigenschaften, kein „Können" aus Bytes. Ein Repository ist ein
Beleg mit einem Link; wer wissen will, ob der Code gut ist, klickt darauf. Das
ist die einzige ehrliche Bewertung, die dieses System anbieten kann.
"""

from __future__ import annotations

import secrets
from dataclasses import dataclass, field
from datetime import datetime
from uuid import UUID

from worker_core import DomainError

__all__ = [
    "MAX_LOGIN",
    "AlreadyVerified",
    "GitHubConnection",
    "InvalidLogin",
    "NotVerified",
    "Repository",
    "challenge_description",
]

#: GitHubs eigene Grenze für Benutzernamen.
MAX_LOGIN = 39


class InvalidLogin(DomainError):
    def __init__(self, reason: str) -> None:
        super().__init__("invalid_login", f"GitHub login {reason}")


class NotVerified(DomainError):
    def __init__(self) -> None:
        super().__init__("not_verified", "This connection is not verified yet")


class AlreadyVerified(DomainError):
    def __init__(self) -> None:
        super().__init__("already_verified", "This connection is already verified")


def challenge_description(challenge: str) -> str:
    """Die Beschreibung, die im öffentlichen Gist stehen muss.

    In der BESCHREIBUNG, nicht im Inhalt: die Gist-Liste liefert sie mit, ein
    Inhalt bräuchte einen Abruf je Gist. Bei 60 Anfragen pro Stunde ohne Token
    ist das kein Detail.
    """
    return f"workertransfer-verify-{challenge}"


@dataclass(frozen=True, slots=True)
class Repository:
    """Ein Beleg. Alle Felder kommen von GitHub, keins ist gerechnet."""

    name: str
    description: str
    #: Was GitHub als Hauptsprache meldet — weitergegeben, nicht ausgewertet.
    language: str | None
    stars: int
    url: str
    pushed_at: datetime | None


def _validated_login(raw: str) -> str:
    login = raw.strip().lstrip("@")
    if not login:
        raise InvalidLogin("must not be empty")
    if len(login) > MAX_LOGIN:
        raise InvalidLogin(f"must not exceed {MAX_LOGIN} characters")
    # GitHubs Regeln: Buchstaben, Ziffern und einzelne Bindestriche, nicht am
    # Rand. Streng zu prüfen erspart einen Abruf, der ohnehin nichts findet —
    # und verhindert, dass ein Pfadfragment in eine URL wandert.
    if login.startswith("-") or login.endswith("-") or "--" in login:
        raise InvalidLogin("has misplaced hyphens")
    if not all((c.isalnum() and c.isascii()) or c == "-" for c in login):
        raise InvalidLogin("may only contain letters, digits and hyphens")
    return login


@dataclass(eq=False, slots=True)
class GitHubConnection:
    subject_id: UUID
    login: str
    challenge: str
    verified_at: datetime | None = None
    fetched_at: datetime | None = None
    repositories: list[Repository] = field(default_factory=list)

    @classmethod
    def open(cls, *, subject_id: UUID, login: str) -> GitHubConnection:
        return cls(
            subject_id=subject_id,
            login=_validated_login(login),
            # 16 Bytes urlsafe: kein Geheimnis, sondern eine Einmalzeichenfolge.
            # Sie beweist nur, dass jemand mit Zugriff auf das Konto sie dort
            # hingeschrieben hat.
            challenge=secrets.token_urlsafe(16),
        )

    @property
    def is_verified(self) -> bool:
        return self.verified_at is not None

    def relink(self, login: str) -> None:
        """Ein anderes Konto nennen — der Nachweis fällt damit weg.

        Ohne dieses Zurücksetzen könnte jemand ein Konto nachweisen und danach
        den Namen auf ein fremdes ändern: der Nachweis stünde noch, wäre aber
        für ein anderes Konto erbracht worden.
        """
        new_login = _validated_login(login)
        if new_login.casefold() == self.login.casefold():
            self.login = new_login
            return
        self.login = new_login
        self.verified_at = None
        self.fetched_at = None
        self.repositories = []

    def verify(self, *, now: datetime) -> None:
        if self.is_verified:
            raise AlreadyVerified()
        self.verified_at = now

    def store(self, repositories: list[Repository], *, now: datetime) -> None:
        """Den Abzug ablegen. Nur für eine nachgewiesene Verbindung.

        Ohne diese Prüfung könnte jemand einen fremden Benutzernamen eintragen
        und dessen Arbeit als seine zeigen — ohne je Zugriff auf das Konto
        gehabt zu haben.
        """
        if not self.is_verified:
            raise NotVerified()
        # Neueste zuerst. Nicht nach Sternen: die messen Sichtbarkeit, nicht
        # Arbeit — und eine Sortierung ist bereits eine Wertung.
        self.repositories = sorted(
            repositories,
            key=lambda r: (r.pushed_at is not None, r.pushed_at),
            reverse=True,
        )
        self.fetched_at = now
