"""Ablage für Dateien — der Port und ein Backend, das benutzt wird.

Die frühere Fassung deklarierte drei Cloud-SDKs gleichzeitig (boto3, minio,
azure-storage-blob) und dazu `worker-files` mit pillow und python-magic. Nichts
davon hatte einen Konsumenten, und die C-Erweiterungen ließen sich für Python
3.14 nicht bauen — deshalb war das Paket aus dem Workspace ausgeschlossen und
seit Phase 1 tot. Ein Paket, das alles kann, was jemand einmal brauchen könnte,
kann am Ende nichts, weil niemand es benutzen kann.

Was hier steht, ist das Gegenteil: ein schmaler Port und **ein** Backend, das
im Betrieb wirklich läuft. Ein S3-Backend kommt, wenn eine Umgebung es braucht;
der Port ist die Naht dafür, und ein zweites Backend zu ergänzen kostet dann
eine Datei — nicht eine Abhängigkeit, die heute niemand benutzt.
"""

from worker_storage.content import (
    ALLOWED_TYPES,
    ContentTooLarge,
    UnsupportedContentType,
    sniff_content_type,
)
from worker_storage.local import LocalStorage
from worker_storage.ports import Storage, StoredObject

__all__ = [
    "ALLOWED_TYPES",
    "ContentTooLarge",
    "LocalStorage",
    "Storage",
    "StoredObject",
    "UnsupportedContentType",
    "sniff_content_type",
]
