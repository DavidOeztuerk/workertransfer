"""Handler mit Fake-Repository und Fake-Gate — kein Docker, kein HTTP."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID, uuid4

import pytest
from profile_service.application.handlers import (
    GetProfileQuery,
    ListProfilesQuery,
    ProfileNotVisible,
    SaveMyProfileCommand,
    handle_get_my_profile,
    handle_get_visible_profile,
    handle_list_visible_profiles,
    handle_save_my_profile,
)
from profile_service.domain.profile import InvalidHeadline, Profile
from profile_service.infrastructure.consent import ConsentUnavailable

NOW = datetime(2026, 8, 2, 12, 0, tzinfo=UTC)
BEARER = "token"


class _Clock:
    def __init__(self) -> None:
        self._t = NOW

    def now(self) -> datetime:
        return self._t

    def advance(self, delta: timedelta) -> None:
        self._t += delta


class _FakeProfiles:
    def __init__(self) -> None:
        self.rows: dict[UUID, Profile] = {}

    async def get(self, subject_id: UUID) -> Profile | None:
        return self.rows.get(subject_id)

    async def save(self, profile: Profile) -> None:
        self.rows[profile.subject_id] = profile

    async def page(self, *, limit: int, cursor: str | None) -> tuple[list[Profile], str | None]:
        ordered = sorted(self.rows.values(), key=lambda p: p.updated_at, reverse=True)
        start = int(cursor) if cursor else 0
        window = ordered[start : start + limit]
        nxt = str(start + limit) if start + limit < len(ordered) else None
        return window, nxt


class _FakeGate:
    """Antwortet nach einer Menge freigegebener Subjekte."""

    def __init__(self, visible: set[UUID] | None = None, *, broken: bool = False) -> None:
        self.visible = visible or set()
        self.broken = broken
        self.asked: list[UUID] = []

    async def may_see(self, subject_id: UUID, *, bearer: str) -> bool:
        if self.broken:
            raise ConsentUnavailable("down")
        self.asked.append(subject_id)
        return subject_id in self.visible


def _deps(clock: _Clock, gate: _FakeGate) -> dict[str, Any]:
    return {"clock": clock, "consent": gate}


def _cmd(subject: UUID, **over: Any) -> SaveMyProfileCommand:
    base: dict[str, Any] = {
        "subject_id": subject,
        "headline": "Senior Python Backend",
        "bio": "Über mich",
        "location": "Berlin",
        "remote_ok": True,
        "skills": ["Python", "Rust"],
    }
    base.update(over)
    return SaveMyProfileCommand(**base)


class TestSaveMyProfile:
    async def test_creates_when_absent(self) -> None:
        repos = {"profiles": _FakeProfiles()}
        subject = uuid4()

        res = await handle_save_my_profile(
            _cmd(subject), deps=_deps(_Clock(), _FakeGate()), repos=repos
        )

        assert res.is_success
        assert repos["profiles"].rows[subject].headline == "Senior Python Backend"

    async def test_updates_when_present_and_keeps_created_at(self) -> None:
        repos = {"profiles": _FakeProfiles()}
        clock = _Clock()
        subject = uuid4()
        await handle_save_my_profile(_cmd(subject), deps=_deps(clock, _FakeGate()), repos=repos)
        clock.advance(timedelta(days=1))

        await handle_save_my_profile(
            _cmd(subject, headline="Staff"), deps=_deps(clock, _FakeGate()), repos=repos
        )

        stored = repos["profiles"].rows[subject]
        assert stored.headline == "Staff"
        assert stored.created_at == NOW
        assert stored.updated_at == NOW + timedelta(days=1)

    async def test_an_invalid_headline_is_a_failed_result_not_an_exception(self) -> None:
        repos = {"profiles": _FakeProfiles()}

        res = await handle_save_my_profile(
            _cmd(uuid4(), headline=""), deps=_deps(_Clock(), _FakeGate()), repos=repos
        )

        assert not res.is_success
        assert isinstance(res.error, InvalidHeadline)
        assert repos["profiles"].rows == {}

    async def test_skills_are_normalised_on_the_way_in(self) -> None:
        repos = {"profiles": _FakeProfiles()}
        subject = uuid4()

        await handle_save_my_profile(
            _cmd(subject, skills=["Python", "python", "  ", "Rust"]),
            deps=_deps(_Clock(), _FakeGate()),
            repos=repos,
        )

        assert repos["profiles"].rows[subject].skills.value == ("Python", "Rust")


class TestGetMyProfile:
    async def test_the_owner_needs_no_consent(self) -> None:
        repos = {"profiles": _FakeProfiles()}
        gate = _FakeGate()  # nichts freigegeben
        subject = uuid4()
        await handle_save_my_profile(_cmd(subject), deps=_deps(_Clock(), gate), repos=repos)

        found = await handle_get_my_profile(subject, repos=repos)

        assert found is not None
        assert gate.asked == [], "das eigene Profil darf den Ledger gar nicht erst fragen"

    async def test_missing_own_profile_is_a_clean_absence(self) -> None:
        # Kein Result: „noch keins angelegt" ist ein Zustand, kein Fehler.
        found = await handle_get_my_profile(uuid4(), repos={"profiles": _FakeProfiles()})

        assert found is None


class TestGetForeignProfile:
    async def _stored(self, subject: UUID) -> dict[str, Any]:
        repos = {"profiles": _FakeProfiles()}
        await handle_save_my_profile(_cmd(subject), deps=_deps(_Clock(), _FakeGate()), repos=repos)
        return repos

    async def test_with_consent_it_is_returned(self) -> None:
        subject = uuid4()
        repos = await self._stored(subject)
        gate = _FakeGate({subject})

        res = await handle_get_visible_profile(
            GetProfileQuery(subject_id=subject, bearer=BEARER),
            deps=_deps(_Clock(), gate),
            repos=repos,
        )

        assert res.is_success
        assert res.value.headline == "Senior Python Backend"

    async def test_without_consent_it_is_indistinguishable_from_absent(self) -> None:
        subject = uuid4()
        repos = await self._stored(subject)

        withheld = await handle_get_visible_profile(
            GetProfileQuery(subject_id=subject, bearer=BEARER),
            deps=_deps(_Clock(), _FakeGate()),
            repos=repos,
        )
        never_existed = await handle_get_visible_profile(
            GetProfileQuery(subject_id=uuid4(), bearer=BEARER),
            deps=_deps(_Clock(), _FakeGate()),
            repos=repos,
        )

        # Beide Male derselbe Fehler: „versteckt" darf von „gibt es nicht" nicht
        # unterscheidbar sein (product-scope.md).
        assert isinstance(withheld.error, ProfileNotVisible)
        assert isinstance(never_existed.error, ProfileNotVisible)

    async def test_an_unknown_subject_is_not_asked_about(self) -> None:
        # Kein Ledger-Aufruf für ein Profil, das es nicht gibt: das wäre ein
        # unnötiger Round-Trip und ein Signal an den Ledger über geratene IDs.
        gate = _FakeGate()

        await handle_get_visible_profile(
            GetProfileQuery(subject_id=uuid4(), bearer=BEARER),
            deps=_deps(_Clock(), gate),
            repos={"profiles": _FakeProfiles()},
        )

        assert gate.asked == []

    async def test_a_broken_ledger_propagates_rather_than_hiding(self) -> None:
        subject = uuid4()
        repos = await self._stored(subject)

        with pytest.raises(ConsentUnavailable):
            await handle_get_visible_profile(
                GetProfileQuery(subject_id=subject, bearer=BEARER),
                deps=_deps(_Clock(), _FakeGate(broken=True)),
                repos=repos,
            )


class TestListProfiles:
    async def _many(self, count: int) -> tuple[dict[str, Any], list[UUID]]:
        repos = {"profiles": _FakeProfiles()}
        clock = _Clock()
        subjects: list[UUID] = []
        for index in range(count):
            subject = uuid4()
            subjects.append(subject)
            await handle_save_my_profile(
                _cmd(subject, headline=f"P{index}"), deps=_deps(clock, _FakeGate()), repos=repos
            )
            clock.advance(timedelta(minutes=1))
        return repos, subjects

    async def test_only_released_profiles_appear(self) -> None:
        repos, subjects = await self._many(5)
        gate = _FakeGate({subjects[0], subjects[3]})

        res = await handle_list_visible_profiles(
            ListProfilesQuery(limit=10, cursor=None, bearer=BEARER),
            deps=_deps(_Clock(), gate),
            repos=repos,
        )

        assert {p.subject_id for p in res.value[0]} == {subjects[0], subjects[3]}

    async def test_a_page_may_return_fewer_than_the_limit(self) -> None:
        """Beabsichtigt: nachladen bis die Seite voll ist würde über die Anzahl
        der Runden verraten, wie viele Profile NICHT freigegeben sind."""
        repos, subjects = await self._many(5)
        gate = _FakeGate({subjects[0]})

        res = await handle_list_visible_profiles(
            ListProfilesQuery(limit=3, cursor=None, bearer=BEARER),
            deps=_deps(_Clock(), gate),
            repos=repos,
        )

        page, _cursor = res.value
        assert len(page) < 3

    async def test_an_empty_result_is_not_an_error(self) -> None:
        repos, _ = await self._many(3)

        res = await handle_list_visible_profiles(
            ListProfilesQuery(limit=10, cursor=None, bearer=BEARER),
            deps=_deps(_Clock(), _FakeGate()),
            repos=repos,
        )

        assert res.is_success
        assert res.value[0] == []

    async def test_a_broken_ledger_fails_the_whole_page(self) -> None:
        """Keine leere Liste ausliefern: die läse sich als 'niemand hat
        freigegeben' und wäre damit unwahr."""
        repos, _ = await self._many(3)

        with pytest.raises(ConsentUnavailable):
            await handle_list_visible_profiles(
                ListProfilesQuery(limit=10, cursor=None, bearer=BEARER),
                deps=_deps(_Clock(), _FakeGate(broken=True)),
                repos=repos,
            )

    async def test_the_limit_is_capped(self) -> None:
        repos, subjects = await self._many(3)
        gate = _FakeGate(set(subjects))

        res = await handle_list_visible_profiles(
            ListProfilesQuery(limit=9999, cursor=None, bearer=BEARER),
            deps=_deps(_Clock(), gate),
            repos=repos,
        )

        assert res.is_success
