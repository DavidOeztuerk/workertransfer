"""Die Firmendomain wird abgeleitet, nicht entgegengenommen."""

from __future__ import annotations

import pytest
from identity_service.domain.company import (
    Company,
    EmailDomain,
    InvalidCompanyName,
    PublicEmailDomain,
)
from identity_service.domain.value_objects import Email


def test_domain_is_derived_from_the_address() -> None:
    assert EmailDomain.from_email(Email("Anna@Firma.DE")).value == "firma.de"


def test_domain_is_lowercased_and_stripped() -> None:
    assert EmailDomain("  Firma.DE ").value == "firma.de"


@pytest.mark.parametrize("raw", ["gmail.com", "GMX.de", "web.de", "outlook.com"])
def test_public_providers_are_recognised(raw: str) -> None:
    assert EmailDomain(raw).is_public() is True


@pytest.mark.parametrize("raw", ["firma.de", "siemens.com", "mail.firma.de"])
def test_company_domains_are_not_public(raw: str) -> None:
    assert EmailDomain(raw).is_public() is False


def test_creating_a_company_on_a_public_domain_is_refused() -> None:
    with pytest.raises(PublicEmailDomain):
        Company.create(name="Nicht Google", domain=EmailDomain("gmail.com"))


def test_company_name_must_not_be_blank() -> None:
    with pytest.raises(InvalidCompanyName):
        Company.create(name="   ", domain=EmailDomain("firma.de"))


def test_company_name_is_trimmed() -> None:
    company = Company.create(name="  Firma GmbH  ", domain=EmailDomain("firma.de"))

    assert company.name == "Firma GmbH"
    assert company.domain.value == "firma.de"
