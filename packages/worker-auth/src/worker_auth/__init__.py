"""Authentication: JWT, OAuth2, OIDC, API Keys, Refresh tokens, Session management."""

from datetime import UTC, datetime, timedelta
from uuid import UUID, uuid4

from jose import jwt
from passlib.context import CryptContext
from pydantic import BaseModel

pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")


class TokenPayload(BaseModel):
    sub: UUID
    tenant_id: UUID
    roles: list[str] = []
    permissions: list[str] = []
    exp: int
    iat: int
    type: str  # "access" | "refresh"
    jti: str


class TokenManager:
    def __init__(
        self,
        private_key: str,
        public_key: str,
        algorithm: str = "RS256",
        access_token_expire_minutes: int = 15,
    ) -> None:
        self.private_key = private_key
        self.public_key = public_key
        self.algorithm = algorithm
        self.access_token_expire_minutes = access_token_expire_minutes

    def create_access_token(
        self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]
    ) -> str:
        now = datetime.now(UTC)
        payload = TokenPayload(
            sub=user_id,
            tenant_id=tenant_id,
            roles=roles,
            permissions=permissions,
            exp=int((now + timedelta(minutes=self.access_token_expire_minutes)).timestamp()),
            iat=int(now.timestamp()),
            type="access",
            jti=str(uuid4()),
        )
        return str(jwt.encode(payload.model_dump(), self.private_key, algorithm=self.algorithm))

    def verify_token(self, token: str) -> TokenPayload:
        payload = jwt.decode(token, self.public_key, algorithms=[self.algorithm])
        return TokenPayload(**payload)


def hash_password(password: str) -> str:
    return str(pwd_context.hash(password))


def verify_password(plain: str, hashed: str) -> bool:
    return bool(pwd_context.verify(plain, hashed))
