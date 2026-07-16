"""Smoke test for worker-storage (Phase 1.5).

``worker-storage`` backends have NO pure constructor — every concrete
``__init__`` touches the filesystem (`LocalStorage.makedirs`) or the network
(`S3Storage`/`AzureBlobStorage`/`MinIOStorage` client setup). Only the
``StorageBackend`` ABC is pure. The smoke therefore stays at module-import
level, which is verified side-effect-free (no global client, no socket).
"""

import worker_storage


def test_smoke_module_imports() -> None:
    assert worker_storage is not None
    assert worker_storage.StorageBackend.__name__ == "StorageBackend"
