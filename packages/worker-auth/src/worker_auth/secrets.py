"""Deployment guard for the HS256 signing secret.

identity-service (which issues tokens) and consent-service (which verifies them)
both ship the same committed development default, so a fresh clone runs without
setup. That convenience turns into a liability the moment a service is deployed:
an unset ``WORKER_JWT_SECRET`` would leave a public, git-visible key signing real
sessions. Since ADR-0015 made the secret shared across services, one forgotten
variable compromises every service at once — not just the one that was sloppy.

The guard lives here rather than in ``worker_platform`` because the kernel
deliberately owns no secrets ("secrets remain service-specific",
``PlatformSettings`` docstring), and here rather than once per service because a
hand-copy per service is exactly the drift ADR-0014 documents.

It takes ``environment`` as a plain string on purpose: ``worker-auth`` does not
depend on ``worker-platform`` and this guard is not worth inverting that.
"""

from __future__ import annotations

__all__ = [
    "DEV_JWT_SECRET",
    "MIN_JWT_SECRET_LENGTH",
    "InsecureJwtSecret",
    "assert_deployable_jwt_secret",
]

#: The default committed in both services' settings. Public in git by definition —
#: S105 ("hardcoded password") is precisely the point: this module exists to
#: recognise the value and refuse to start with it where it would matter.
DEV_JWT_SECRET = "dev-only-secret-change-me-in-production-32bytes"  # noqa: S105

#: HS256 keys shorter than the hash output add no security over a 256-bit key.
MIN_JWT_SECRET_LENGTH = 32

# Environments reachable from outside a developer machine. STAGING counts: it
# usually mirrors production and is internet-facing, so a known key is just as
# forgeable there. LOCAL/DEVELOPMENT/TEST keep the convenient default.
_DEPLOYED_ENVIRONMENTS = frozenset({"production", "staging"})


class InsecureJwtSecret(ValueError):
    """Raised while settings are built, so a misconfigured service never starts.

    Fail-fast is the point: a service that boots with a forgeable key looks
    perfectly healthy and would pass every probe.
    """


def assert_deployable_jwt_secret(secret: str, *, environment: str, service_name: str) -> None:
    """Reject a development-grade signing secret in a deployed environment.

    No-op outside PRODUCTION/STAGING so local runs, tests and CI keep working
    with the committed default.
    """
    if environment.lower() not in _DEPLOYED_ENVIRONMENTS:
        return
    if secret == DEV_JWT_SECRET:
        raise InsecureJwtSecret(
            f"{service_name}: WORKER_JWT_SECRET is still the committed development "
            f"default in environment {environment!r}. That value is public in git — "
            f"anyone could forge tokens for every service sharing it (ADR-0015). "
            f"Set a real secret of at least {MIN_JWT_SECRET_LENGTH} characters."
        )
    if len(secret) < MIN_JWT_SECRET_LENGTH:
        raise InsecureJwtSecret(
            f"{service_name}: WORKER_JWT_SECRET is shorter than "
            f"{MIN_JWT_SECRET_LENGTH} characters in environment {environment!r}, "
            f"which is too weak to sign HS256 tokens."
        )
