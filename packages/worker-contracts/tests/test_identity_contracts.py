"""Die Auth-DTOs sind ein versionierter Vertrag (ADR-0004 §1)."""

from __future__ import annotations

import pytest
from pydantic import ValidationError
from worker_contracts import (
    CompanyV1,
    CreateCompanyV1,
    MembershipV1,
    RegisterUserV1,
    ResendVerificationV1,
    VerifyEmailV1,
)


def test_register_has_no_tenant_field() -> None:
    # Ein Tenant ist ein Unternehmen und wird nie vom Client geliefert (ADR-0017).
    assert set(RegisterUserV1.model_fields) == {"email", "password", "display_name"}


def test_create_company_takes_only_a_name() -> None:
    # Die Domain wird serverseitig abgeleitet — sie darf gar nicht sendbar sein,
    # sonst könnte sich jemand eine fremde Firma zuschreiben.
    assert set(CreateCompanyV1.model_fields) == {"name"}


def test_a_domain_field_is_silently_ignored_rather_than_accepted() -> None:
    # pydantic verwirft unbekannte Felder; entscheidend ist, dass der Wert
    # nirgends ankommt.
    body = CreateCompanyV1(name="Firma", domain="fremde-firma.de")  # type: ignore[call-arg]

    assert not hasattr(body, "domain")


@pytest.mark.parametrize("bad", ["", "x" * 201])
def test_company_name_length_is_bounded(bad: str) -> None:
    with pytest.raises(ValidationError):
        CreateCompanyV1(name=bad)


def test_verify_requires_a_token() -> None:
    with pytest.raises(ValidationError):
        VerifyEmailV1(token="")


def test_resend_requires_an_email() -> None:
    with pytest.raises(ValidationError):
        ResendVerificationV1(email="")


def test_response_dtos_carry_the_derived_domain() -> None:
    company = CompanyV1(
        id="11111111-1111-1111-1111-111111111111",  # type: ignore[arg-type]
        name="Firma GmbH",
        domain="firma.de",
    )
    membership = MembershipV1(
        id="11111111-1111-1111-1111-111111111111",  # type: ignore[arg-type]
        name="Firma GmbH",
        domain="firma.de",
        role="admin",
    )

    assert company.domain == "firma.de"
    assert membership.role == "admin"


def test_dtos_carry_no_domain_types() -> None:
    """Ein Konsument darf identity_service nie importieren müssen."""
    for model in (
        RegisterUserV1,
        VerifyEmailV1,
        ResendVerificationV1,
        CreateCompanyV1,
        CompanyV1,
        MembershipV1,
    ):
        for field in model.model_fields.values():
            assert "identity_service" not in str(field.annotation)
