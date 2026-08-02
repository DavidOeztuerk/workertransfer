"""Shared API contracts: DTOs, Events, Messages, Versioning."""

from worker_contracts.consent import (
    ConsentCheckResultV1,
    ConsentCheckV1,
    ConsentGrantV1,
    ConsentRevokeV1,
    ConsentStateV1,
)
from worker_contracts.identity import (
    AcceptInvitationV1,
    CompanyMemberV1,
    CompanyV1,
    CreateCompanyV1,
    InvitationV1,
    InviteMemberV1,
    MembershipV1,
    RegisterUserV1,
    ResendVerificationV1,
    VerifyEmailV1,
)
from worker_contracts.messages import Command, Event, Message, Query
from worker_contracts.portfolio import PortfolioItemV1, PortfolioV1, SavePortfolioV1
from worker_contracts.profile import ProfilePageV1, ProfileV1, SaveProfileV1
from worker_contracts.resume import (
    EducationV1,
    PositionV1,
    ResumeRequestV1,
    ResumeV1,
    SaveResumeV1,
)

__all__ = [
    "AcceptInvitationV1",
    "Command",
    "CompanyMemberV1",
    "CompanyV1",
    "ConsentCheckResultV1",
    "ConsentCheckV1",
    "ConsentGrantV1",
    "ConsentRevokeV1",
    "ConsentStateV1",
    "CreateCompanyV1",
    "EducationV1",
    "Event",
    "InvitationV1",
    "InviteMemberV1",
    "MembershipV1",
    "Message",
    "PortfolioItemV1",
    "PortfolioV1",
    "PositionV1",
    "ProfilePageV1",
    "ProfileV1",
    "Query",
    "RegisterUserV1",
    "ResendVerificationV1",
    "ResumeRequestV1",
    "ResumeV1",
    "SavePortfolioV1",
    "SaveProfileV1",
    "SaveResumeV1",
    "VerifyEmailV1",
]
