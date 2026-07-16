"""Smoke tests for worker-auth (Phase 1.5).

Exercises the ``TokenManager`` constructor (pure field storage) and the
``TokenPayload`` pydantic model. ``hash_password``/``verify_password`` are NOT
called — they trigger passlib's bcrypt backend, which is broken in the current
venv (passlib/bcrypt version skew), and run real crypto. ``create_access_token``
needs a real RSA key and is also avoided.
"""

import time
from uuid import uuid4

from worker_auth import TokenManager, TokenPayload


def test_smoke_token_manager_and_payload() -> None:
    manager = TokenManager(
        private_key="priv",
        public_key="pub",
        algorithm="HS256",
        access_token_expire_minutes=5,
    )
    now = int(time.time())
    payload = TokenPayload(
        sub=uuid4(),
        tenant_id=uuid4(),
        exp=now + 60,
        iat=now,
        type="access",
        jti="j",
    )

    assert manager.algorithm == "HS256"
    assert manager.private_key == "priv"
    assert payload.type == "access"
    assert payload.jti == "j"
