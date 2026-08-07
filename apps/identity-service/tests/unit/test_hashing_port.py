import pytest
from identity_service.domain.value_objects import PasswordHash
from identity_service.infrastructure.auth.hasher import BcryptPasswordAdapter
from worker_auth import PasswordTooLong


def test_adapter_hash_and_verify() -> None:
    ad = BcryptPasswordAdapter(rounds=4)  # low rounds only in tests
    hashed = ad.hash("hunter2")
    assert isinstance(hashed, PasswordHash)
    assert hashed.value.startswith("$2")
    assert ad.verify("hunter2", hashed) is True
    assert ad.verify("wrong", hashed) is False


def test_adapter_rejects_overlong_via_domain_port() -> None:
    ad = BcryptPasswordAdapter(rounds=4)
    with pytest.raises(PasswordTooLong):
        ad.hash("a" * 73)
