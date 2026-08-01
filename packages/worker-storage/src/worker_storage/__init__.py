"""Storage abstraction: S3, MinIO, Azure Blob, Local, Signed URLs."""

import asyncio
import os
import tempfile
from abc import ABC, abstractmethod
from typing import Any, BinaryIO

import boto3
from azure.storage.blob import BlobServiceClient, ContentSettings
from minio import Minio


class StorageBackend(ABC):
    @abstractmethod
    async def upload(self, key: str, data: BinaryIO, content_type: str) -> str: ...

    @abstractmethod
    async def download(self, key: str) -> BinaryIO: ...

    @abstractmethod
    async def delete(self, key: str) -> None: ...

    @abstractmethod
    async def exists(self, key: str) -> bool: ...

    @abstractmethod
    async def get_presigned_url(self, key: str, expiration: int = 3600) -> str: ...


class S3Storage(StorageBackend):
    def __init__(self, bucket: str, region: str = "us-east-1", **kwargs: Any) -> None:
        self._bucket = bucket
        self._client = boto3.client("s3", region_name=region, **kwargs)

    async def upload(self, key: str, data: BinaryIO, content_type: str) -> str:
        self._client.upload_fileobj(
            data, self._bucket, key, ExtraArgs={"ContentType": content_type}
        )
        return f"s3://{self._bucket}/{key}"

    async def download(self, key: str) -> BinaryIO:
        import io

        buffer = io.BytesIO()
        self._client.download_fileobj(self._bucket, key, buffer)
        buffer.seek(0)
        return buffer

    async def delete(self, key: str) -> None:
        self._client.delete_object(Bucket=self._bucket, Key=key)

    async def exists(self, key: str) -> bool:
        try:
            self._client.head_object(Bucket=self._bucket, Key=key)
            return True
        except Exception:
            return False

    async def get_presigned_url(self, key: str, expiration: int = 3600) -> str:
        return str(
            self._client.generate_presigned_url(
                "get_object",
                Params={"Bucket": self._bucket, "Key": key},
                ExpiresIn=expiration,
            )
        )


class MinIOStorage(StorageBackend):
    def __init__(
        self, bucket: str, endpoint: str, access_key: str, secret_key: str, secure: bool = False
    ):
        self._bucket = bucket
        self._client = Minio(endpoint, access_key=access_key, secret_key=secret_key, secure=secure)
        if not self._client.bucket_exists(bucket):
            self._client.make_bucket(bucket)

    async def upload(self, key: str, data: BinaryIO, content_type: str) -> str:
        self._client.put_object(self._bucket, key, data, length=-1, content_type=content_type)
        return f"minio://{self._bucket}/{key}"

    async def download(self, key: str) -> BinaryIO:
        import io

        response = self._client.get_object(self._bucket, key)
        buffer = io.BytesIO(response.read())
        response.close()
        response.release_conn()
        return buffer

    async def delete(self, key: str) -> None:
        self._client.remove_object(self._bucket, key)

    async def exists(self, key: str) -> bool:
        try:
            self._client.stat_object(self._bucket, key)
            return True
        except Exception:
            return False

    async def get_presigned_url(self, key: str, expiration: int = 3600) -> str:
        from datetime import timedelta

        return self._client.presigned_get_object(
            self._bucket, key, expires=timedelta(seconds=expiration)
        )


class LocalStorage(StorageBackend):
    def __init__(self, base_path: str | None = None):
        # A hardcoded "/tmp/storage" default is a predictable, world-writable
        # path that any local user can pre-create or read — unacceptable for a
        # product that stores CVs and contracts. Callers should pass an explicit
        # path; the fallback resolves the platform temp dir at runtime.
        self._base_path = base_path or os.path.join(tempfile.gettempdir(), "workertransfer-storage")
        os.makedirs(self._base_path, mode=0o700, exist_ok=True)

    def _full_path(self, key: str) -> str:
        return os.path.join(self._base_path, key)

    async def upload(self, key: str, data: BinaryIO, content_type: str) -> str:
        path = self._full_path(key)

        def _write() -> str:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as f:
                f.write(data.read())
            return f"file://{path}"

        return await asyncio.to_thread(_write)

    async def download(self, key: str) -> BinaryIO:
        path = self._full_path(key)
        return await asyncio.to_thread(lambda: open(path, "rb"))

    async def delete(self, key: str) -> None:
        path = self._full_path(key)

        def _delete() -> None:
            if os.path.exists(path):
                os.remove(path)

        await asyncio.to_thread(_delete)

    async def exists(self, key: str) -> bool:
        path = self._full_path(key)
        return await asyncio.to_thread(lambda: os.path.exists(path))

    async def get_presigned_url(self, key: str, expiration: int = 3600) -> str:
        return f"file://{self._full_path(key)}"


class AzureBlobStorage(StorageBackend):
    def __init__(self, container: str, connection_string: str):
        self._container = container
        self._client = BlobServiceClient.from_connection_string(connection_string)
        self._container_client = self._client.get_container_client(container)
        if not self._container_client.exists():
            self._container_client.create_container()

    async def upload(self, key: str, data: BinaryIO, content_type: str) -> str:
        blob_client = self._container_client.get_blob_client(key)
        blob_client.upload_blob(
            data, overwrite=True, content_settings=ContentSettings(content_type=content_type)
        )
        return f"azure://{self._container}/{key}"

    async def download(self, key: str) -> BinaryIO:
        import io

        blob_client = self._container_client.get_blob_client(key)
        buffer = io.BytesIO()
        blob_client.download_blob().readinto(buffer)
        buffer.seek(0)
        return buffer

    async def delete(self, key: str) -> None:
        self._container_client.delete_blob(key)

    async def exists(self, key: str) -> bool:
        return self._container_client.get_blob_client(key).exists()

    async def get_presigned_url(self, key: str, expiration: int = 3600) -> str:
        from datetime import datetime, timedelta

        from azure.storage.blob import BlobSasPermissions, generate_blob_sas

        sas = generate_blob_sas(
            account_name=str(self._client.account_name or ""),
            container_name=self._container,
            blob_name=key,
            permission=BlobSasPermissions(read=True),
            expiry=datetime.utcnow() + timedelta(seconds=expiration),
        )
        return f"https://{self._client.account_name}.blob.core.windows.net/{self._container}/{key}?{sas}"
