"""Smoke test for worker-authorization (Phase 1.5).

The only export is ``AuthorizationService``, whose ``__init__(model_path,
adapter)`` builds a ``casbin.AsyncEnforcer`` — needs a model file on disk and a
DB-backed adapter (network). There is no pure constructor or helper. Module
import is side-effect-free (``casbin`` + ``casbin_sqlalchemy_adapter`` load with
no connection). The smoke stays at module-import level.
"""

import worker_authorization


def test_smoke_module_imports() -> None:
    assert worker_authorization is not None
    assert worker_authorization.AuthorizationService.__name__ == "AuthorizationService"
