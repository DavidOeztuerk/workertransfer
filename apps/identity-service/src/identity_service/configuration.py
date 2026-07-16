"""Identity-service-specific configuration."""

from worker_platform.configuration import PlatformSettings


class IdentityServiceSettings(PlatformSettings):
    service_name: str = "identity-service"
    port: int = 8001
