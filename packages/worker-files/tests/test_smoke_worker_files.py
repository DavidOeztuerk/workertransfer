"""Smoke test for worker-files (Phase 1.5).

The source imports ``import magic`` (``python-magic``), which requires the
system ``libmagic`` C library — not installed in this environment, so
``import worker_files`` raises ``ImportError: failed to find libmagic``. This
is an environment/system-library gap tracked as a follow-up deficit. The smoke
test is skipped with the reason until ``libmagic`` is present; once importable,
the pure targets are ``UploadResult`` (dataclass) and
``FileValidator.ALLOWED_MIME_TYPES``.
"""

import pytest


def test_smoke_worker_files_import_or_skip() -> None:
    try:
        import worker_files
    except ImportError as exc:  # libmagic system lib missing
        pytest.skip(f"worker-files import broken: {exc} (system libmagic missing for python-magic)")
    assert worker_files is not None
