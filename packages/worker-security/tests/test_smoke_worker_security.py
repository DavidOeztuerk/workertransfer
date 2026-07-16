"""Smoke test for worker-security (Phase 1.5).

The only export is ``SecurityHeadersMiddleware``, a Starlette middleware that
needs a real ASGI app to construct (its ``__init__(self, app, ...)`` calls
``super().__init__(app)``). There is no standalone pure constructor. Module
import is side-effect-free (``starlette`` loads cleanly, declared
``cryptography``/``passlib`` are unused by the source). The smoke stays at
module-import level — exercising the middleware properly needs a Starlette
``TestClient`` (an integration test, not a smoke).
"""

import worker_security


def test_smoke_module_imports() -> None:
    assert worker_security is not None
    assert worker_security.SecurityHeadersMiddleware.__name__ == "SecurityHeadersMiddleware"
