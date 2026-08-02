"""Die Regeln der Einladung."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from identity_service.domain.invitation import (
    INVITATION_LIFETIME,
    Invitation,
    InvitationExpired,
    InvitationInvalid,
    InvitationStatus,
    NotYourInvitation,
    OnlyAdminsMayInvite,
)
from identity_service.domain.membership import MembershipRole
from identity_service.domain.value_objects import Email

NOW = datetime(2026, 8, 2, tzinfo=UTC)


def invite(
    email: str = "neu@firma.example",
    role: MembershipRole = MembershipRole.MEMBER,
    inviter_role: MembershipRole = MembershipRole.ADMIN,
) -> Invitation:
    return Invitation.issue(
        tenant_id=uuid4(),
        email=Email(email),
        role=role,
        inviter_role=inviter_role,
        invited_by=uuid4(),
        now=NOW,
    )


class TestIssuing:
    def test_an_admin_may_invite(self) -> None:
        assert invite().status is InvitationStatus.PENDING

    def test_a_member_may_not(self) -> None:
        with pytest.raises(OnlyAdminsMayInvite):
            invite(inviter_role=MembershipRole.MEMBER)

    def test_it_expires(self) -> None:
        # Eine vergessene Einladung darf nicht ein Jahr später noch Zugang zu
        # Bewerberdaten öffnen.
        assert invite().expires_at == NOW + INVITATION_LIFETIME

    def test_an_admin_may_invite_another_admin(self) -> None:
        assert invite(role=MembershipRole.ADMIN).role is MembershipRole.ADMIN

    def test_a_foreign_domain_is_fine(self) -> None:
        # Die Firmendomain beweist, wem sie gehört (ADR-0019). Wen das
        # Unternehmen danach hereinlässt, ist seine Entscheidung — ein externer
        # Personalberater ist ein normaler Fall.
        assert invite("berater@gmail.com").email == Email("berater@gmail.com")


class TestAccepting:
    def test_the_invited_address_may_accept(self) -> None:
        inv = invite("anna@firma.example")

        inv.accept(by_email=Email("anna@firma.example"), now=NOW + timedelta(hours=1))

        assert inv.status is InvitationStatus.ACCEPTED
        assert inv.accepted_at is not None

    def test_case_does_not_make_it_a_different_person(self) -> None:
        inv = invite("anna@firma.example")

        inv.accept(by_email=Email("ANNA@Firma.Example"), now=NOW + timedelta(hours=1))

        assert inv.status is InvitationStatus.ACCEPTED

    def test_somebody_else_may_not_accept_even_with_the_token(self) -> None:
        # Tokens werden weitergeleitet. Wer den Link hat, ist nicht, wer
        # eingeladen wurde.
        inv = invite("anna@firma.example")

        with pytest.raises(NotYourInvitation):
            inv.accept(by_email=Email("fremd@woanders.example"), now=NOW)

        assert inv.status is InvitationStatus.PENDING

    def test_an_expired_invitation_says_so(self) -> None:
        inv = invite()

        with pytest.raises(InvitationExpired):
            inv.accept(by_email=inv.email, now=NOW + INVITATION_LIFETIME)

    def test_it_can_only_be_accepted_once(self) -> None:
        inv = invite()
        inv.accept(by_email=inv.email, now=NOW)

        with pytest.raises(InvitationInvalid):
            inv.accept(by_email=inv.email, now=NOW)

    def test_a_withdrawn_invitation_cannot_be_accepted(self) -> None:
        inv = invite()
        inv.withdraw(by_role=MembershipRole.ADMIN)

        with pytest.raises(InvitationInvalid):
            inv.accept(by_email=inv.email, now=NOW)


class TestWithdrawing:
    def test_only_an_admin_withdraws(self) -> None:
        inv = invite()

        with pytest.raises(OnlyAdminsMayInvite):
            inv.withdraw(by_role=MembershipRole.MEMBER)

    def test_an_accepted_invitation_is_not_withdrawable(self) -> None:
        """Das wäre ein Rauswurf, und der ist ein anderer Vorgang.

        Ihn hier zu suggerieren wäre gefährlich: ein Administrator würde
        glauben, jemanden entfernt zu haben, der weiterhin drin ist.
        """
        inv = invite()
        inv.accept(by_email=inv.email, now=NOW)

        with pytest.raises(InvitationInvalid):
            inv.withdraw(by_role=MembershipRole.ADMIN)
