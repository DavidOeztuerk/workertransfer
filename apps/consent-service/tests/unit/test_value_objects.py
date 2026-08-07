"""Unit tests for consent domain value objects."""

from __future__ import annotations

from uuid import uuid4

import pytest
from consent_service.domain.value_objects import (
    Capability,
    ConsentEventId,
    InvalidCapability,
    InvalidReason,
    Reason,
    SubjectId,
)


class TestSubjectId:
    def test_valid_uuid(self) -> None:
        uid = uuid4()
        sid = SubjectId(uid)
        assert sid.value == uid


class TestCapability:
    def test_simple_namespace(self) -> None:
        c = Capability("profile.visibility:public")
        assert c.value == "profile.visibility:public"

    def test_no_colon(self) -> None:
        Capability("document.attach")

    def test_with_id_placeholder(self) -> None:
        Capability("document.attach:application:{id}")

    def test_rejects_empty(self) -> None:
        with pytest.raises(InvalidCapability):
            Capability("")

    def test_rejects_leading_uppercase(self) -> None:
        with pytest.raises(InvalidCapability):
            Capability("Profile.visibility:public")

    def test_rejects_double_colon(self) -> None:
        with pytest.raises(InvalidCapability):
            Capability("a::b")


class TestReason:
    def test_valid(self) -> None:
        r = Reason("User revoked consent via dashboard")
        assert r.value == "User revoked consent via dashboard"

    def test_optional_short(self) -> None:
        Reason("ok")

    def test_rejects_empty(self) -> None:
        with pytest.raises(InvalidReason):
            Reason("")

    def test_rejects_whitespace_only(self) -> None:
        with pytest.raises(InvalidReason):
            Reason("   ")

    def test_rejects_too_long(self) -> None:
        with pytest.raises(InvalidReason):
            Reason("x" * 501)


class TestConsentEventId:
    def test_valid_uuid(self) -> None:
        uid = uuid4()
        eid = ConsentEventId(uid)
        assert eid.value == uid
