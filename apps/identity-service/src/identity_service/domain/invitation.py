"""Die Einladung in ein Unternehmen.

Ein Unternehmen entsteht mit genau einer Person — der, die es angelegt hat
(ADR-0019). Ohne einen Weg hinein bleibt es dabei; die Einladung ist dieser Weg.

Sie ist an eine **Adresse** gebunden, nicht an ein Konto. Das ist Absicht: die
eingeladene Person muss noch keines haben, und sie darf beim Einladen nicht
verraten bekommen, ob sie eines hat. Beim Annehmen muss die angemeldete Person
dann genau diese Adresse führen — der Server vergleicht, der Client behauptet
nichts.

Die Firmendomain spielt hier bewusst keine Rolle. Sie beweist, wem die Domain
gehört (ADR-0019), und ist der Grund, warum ein Unternehmen entstehen darf. Wen
dieses Unternehmen danach hereinlässt, ist seine Entscheidung — ein externer
Personalberater mit fremder Adresse ist ein völlig normaler Fall.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import StrEnum
from uuid import UUID, uuid4

from worker_core import DomainError

from identity_service.domain.membership import MembershipRole
from identity_service.domain.value_objects import Email

__all__ = [
    "INVITATION_LIFETIME",
    "Invitation",
    "InvitationExpired",
    "InvitationInvalid",
    "InvitationStatus",
    "NotYourInvitation",
    "OnlyAdminsMayInvite",
]

#: Sieben Tage. Kurz genug, dass eine vergessene Einladung nicht ein Jahr später
#: noch Zugang zu Bewerberdaten öffnet; lang genug für einen Urlaub.
INVITATION_LIFETIME = timedelta(days=7)


class InvitationStatus(StrEnum):
    PENDING = "pending"
    ACCEPTED = "accepted"
    WITHDRAWN = "withdrawn"


class OnlyAdminsMayInvite(DomainError):
    def __init__(self) -> None:
        super().__init__("only_admins_may_invite", "Only an administrator may invite")


class InvitationInvalid(DomainError):
    def __init__(self) -> None:
        # Ohne Detail: unbekannt, zurückgezogen und bereits angenommen sind von
        # außen nicht unterscheidbar, sonst wird der Endpunkt ein Orakel über
        # fremde Einladungen.
        super().__init__("invitation_invalid", "This invitation is not valid")


class InvitationExpired(DomainError):
    def __init__(self) -> None:
        # Abgelaufen darf man sagen: es ist eine Aussage über die Einladung, die
        # der Empfänger ohnehin in Händen hält, und sie ist behebbar (neu
        # einladen lassen). Ein pauschales „ungültig" würde hier nur ratlos
        # machen.
        super().__init__("invitation_expired", "This invitation has expired")


class NotYourInvitation(DomainError):
    def __init__(self) -> None:
        super().__init__(
            "not_your_invitation",
            "This invitation was issued for a different email address",
        )


@dataclass(eq=False, slots=True)
class Invitation:
    id: UUID
    tenant_id: UUID
    email: Email
    role: MembershipRole
    #: `None`, wenn die einladende Person ihr Konto gelöscht hat (ADR-0027 §2).
    #: Die Einladung gehört dem Unternehmen und bleibt gültig.
    invited_by: UUID | None
    status: InvitationStatus
    created_at: datetime
    expires_at: datetime
    accepted_at: datetime | None

    @classmethod
    def issue(
        cls,
        *,
        tenant_id: UUID,
        email: Email,
        role: MembershipRole,
        inviter_role: MembershipRole,
        invited_by: UUID,
        now: datetime,
    ) -> Invitation:
        if inviter_role is not MembershipRole.ADMIN:
            raise OnlyAdminsMayInvite()
        return cls(
            id=uuid4(),
            tenant_id=tenant_id,
            email=email,
            role=role,
            invited_by=invited_by,
            status=InvitationStatus.PENDING,
            created_at=now,
            expires_at=now + INVITATION_LIFETIME,
            accepted_at=None,
        )

    def accept(self, *, by_email: Email, now: datetime) -> None:
        if self.status is not InvitationStatus.PENDING:
            raise InvitationInvalid()
        if now >= self.expires_at:
            raise InvitationExpired()
        # Der Server vergleicht die Adresse der angemeldeten Person mit der
        # eingeladenen. Ein Token allein würde reichen, um irgendjemanden
        # hereinzulassen — und Tokens werden weitergeleitet.
        if by_email != self.email:
            raise NotYourInvitation()
        self.status = InvitationStatus.ACCEPTED
        self.accepted_at = now

    def withdraw(self, *, by_role: MembershipRole) -> None:
        if by_role is not MembershipRole.ADMIN:
            raise OnlyAdminsMayInvite()
        if self.status is not InvitationStatus.PENDING:
            # Eine angenommene Einladung zurückzuziehen würde die Mitgliedschaft
            # nicht beenden — das wäre ein anderer Vorgang, und ihn hier zu
            # suggerieren wäre gefährlich.
            raise InvitationInvalid()
        self.status = InvitationStatus.WITHDRAWN
