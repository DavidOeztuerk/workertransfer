"""List-valued settings accept a comma-separated env value, not only JSON.

pydantic-settings json.loads any list field's raw env value inside the *source*,
before validators run. `WORKER_CORS_ALLOW_ORIGINS=http://a,http://b` therefore
raised SettingsError at import time and killed both services on startup —
scripts/run-dev.sh had exported exactly that. `Annotated[..., NoDecode]` plus a
`mode="before"` validator makes the documented behaviour real.

The subclass case has its own test on purpose: overriding a field drops the base
class's annotation, so identity-service reintroduced the crash even after the
kernel was fixed.
"""

from __future__ import annotations

import pytest
from worker_platform.configuration import PlatformSettings


def test_comma_separated_origins_are_split(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("WORKER_CORS_ALLOW_ORIGINS", "http://a:1,http://b:2")

    assert PlatformSettings().cors_allow_origins == ["http://a:1", "http://b:2"]


def test_json_array_still_works(monkeypatch: pytest.MonkeyPatch) -> None:
    # Anything generated rather than hand-written keeps emitting JSON.
    monkeypatch.setenv("WORKER_CORS_ALLOW_ORIGINS", '["http://a:1", "http://b:2"]')

    assert PlatformSettings().cors_allow_origins == ["http://a:1", "http://b:2"]


def test_whitespace_and_trailing_commas_are_tolerated(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("WORKER_CORS_ALLOW_ORIGINS", " http://a:1 , http://b:2 , ")

    assert PlatformSettings().cors_allow_origins == ["http://a:1", "http://b:2"]


def test_empty_value_is_an_empty_allowlist(monkeypatch: pytest.MonkeyPatch) -> None:
    # Empty must mean "no CORS", not a crash and not [""].
    monkeypatch.setenv("WORKER_CORS_ALLOW_ORIGINS", "")

    assert PlatformSettings().cors_allow_origins == []


def test_unset_keeps_the_default(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("WORKER_CORS_ALLOW_ORIGINS", raising=False)

    assert PlatformSettings().cors_allow_origins == []


def test_the_other_cors_lists_parse_the_same_way(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("WORKER_CORS_ALLOW_METHODS", "GET,POST")
    monkeypatch.setenv("WORKER_CORS_ALLOW_HEADERS", "content-type")
    monkeypatch.setenv("WORKER_CORS_EXPOSE_HEADERS", "x-correlation-id")
    settings = PlatformSettings()

    assert settings.cors_allow_methods == ["GET", "POST"]
    assert settings.cors_allow_headers == ["content-type"]
    assert settings.cors_expose_headers == ["x-correlation-id"]


def test_a_subclass_that_overrides_the_field_still_parses_csv(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The regression that survived the first fix: identity-service overrides
    # cors_allow_origins to add a dev default, which drops Annotated[..., NoDecode].
    from identity_service.configuration import IdentityServiceSettings

    monkeypatch.setenv("WORKER_JWT_SECRET", "test-secret-with-at-least-thirty-two-bytes")
    monkeypatch.setenv("WORKER_CORS_ALLOW_ORIGINS", "http://localhost:5173,http://127.0.0.1:5173")

    assert IdentityServiceSettings().cors_allow_origins == [
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ]
