# Skill: Authentication & Authorization (JWT, OAuth2, OIDC, Casbin)

## Purpose
Implement complete authentication and authorization system with JWT, OAuth2/OIDC, API Keys, RBAC/ABAC via Casbin.

## Dependencies
```toml
dependencies = [
    "pyjwt>=2.8.0,<3.0.0",
    "python-jose>=3.3.0,<4.0.0",
    "authlib>=1.3.0,<2.0.0",
    "passlib[bcrypt]>=1.7.4,<2.0.0",
    "casbin>=1.48.0,<2.0.0",
    "casbin-sqlalchemy-adapter>=2.0.0,<3.0.0",
    "cryptography>=42.0.0,<50.0.0",
]
```

## JWT Token Management

```python
# worker_auth/tokens.py
from datetime import datetime, timedelta, UTC
from jose import jwt, JWTError
from pydantic import BaseModel
from uuid import UUID

class TokenPayload(BaseModel):
    sub: UUID                    # User ID
    tenant_id: UUID              # Tenant ID
    roles: list[str]             # Role names
    permissions: list[str]       # Direct permissions
    exp: int                     # Expiration timestamp
    iat: int                     # Issued at
    type: str                    # "access" | "refresh"
    jti: str                     # JWT ID for revocation

class TokenManager:
    def __init__(self, settings: AuthSettings):
        self._settings = settings
        self._private_key = settings.private_key
        self._public_key = settings.public_key
        self._algorithm = settings.algorithm  # RS256
    
    def create_access_token(self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]) -> str:
        now = datetime.now(UTC)
        payload = TokenPayload(
            sub=user_id,
            tenant_id=tenant_id,
            roles=roles,
            permissions=permissions,
            exp=int((now + timedelta(minutes=self._settings.access_token_expire_minutes)).timestamp()),
            iat=int(now.timestamp()),
            type="access",
            jti=str(uuid4()),
        )
        return jwt.encode(payload.model_dump(), self._private_key, algorithm=self._algorithm)
    
    def create_refresh_token(self, user_id: UUID, tenant_id: UUID) -> str:
        now = datetime.now(UTC)
        payload = TokenPayload(
            sub=user_id,
            tenant_id=tenant_id,
            roles=[],
            permissions=[],
            exp=int((now + timedelta(days=self._settings.refresh_token_expire_days)).timestamp()),
            iat=int(now.timestamp()),
            type="refresh",
            jti=str(uuid4()),
        )
        return jwt.encode(payload.model_dump(), self._private_key, algorithm=self._algorithm)
    
    def decode_token(self, token: str) -> TokenPayload:
        try:
            payload = jwt.decode(token, self._public_key, algorithms=[self._algorithm])
            return TokenPayload(**payload)
        except JWTError as e:
            raise InvalidTokenError(str(e))
    
    def verify_token(self, token: str) -> TokenPayload:
        payload = self.decode_token(token)
        if payload.type != "access":
            raise InvalidTokenError("Not an access token")
        # Check revocation (Redis)
        if self._is_revoked(payload.jti):
            raise InvalidTokenError("Token revoked")
        return payload
```

## OAuth2/OIDC Integration

```python
# worker_auth/oauth.py
from authlib.integrations.starlette_client import OAuth
from authlib.integrations.httpx_client import AsyncOAuth2Client

class OAuthManager:
    def __init__(self, settings: OAuthSettings):
        self._oauth = OAuth()
        self._register_providers(settings)
    
    def _register_providers(self, settings: OAuthSettings):
        # Google
        self._oauth.register(
            name="google",
            client_id=settings.google_client_id,
            client_secret=settings.google_client_secret,
            server_metadata_url="https://accounts.google.com/.well-known/openid-configuration",
            client_kwargs={"scope": "openid email profile"},
        )
        
        # Microsoft
        self._oauth.register(
            name="microsoft",
            client_id=settings.microsoft_client_id,
            client_secret=settings.microsoft_client_secret,
            server_metadata_url="https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
            client_kwargs={"scope": "openid email profile"},
        )
        
        # GitHub (for Developer Intelligence)
        self._oauth.register(
            name="github",
            client_id=settings.github_client_id,
            client_secret=settings.github_client_secret,
            access_token_url="https://github.com/login/oauth/access_token",
            authorize_url="https://github.com/login/oauth/authorize",
            api_base_url="https://api.github.com/",
            client_kwargs={"scope": "read:user user:email repo"},
        )
    
    async def get_authorize_url(self, provider: str, redirect_uri: str, state: str) -> str:
        client = self._oauth.create_client(provider)
        return await client.authorize_redirect(redirect_uri, state=state)
    
    async def exchange_code(self, provider: str, code: str, redirect_uri: str) -> OAuth2Token:
        client = self._oauth.create_client(provider)
        return await client.authorize_access_token(code, redirect_uri=redirect_uri)
    
    async def get_user_info(self, provider: str, token: OAuth2Token) -> dict:
        client = self._oauth.create_client(provider)
        client.token = token
        resp = await client.get("userinfo" if provider != "github" else "user")
        return resp.json()
```

## API Key Authentication

```python
# worker_auth/api_keys.py
import secrets
import hashlib
from datetime import datetime, UTC

class APIKeyManager:
    def __init__(self, repo: APIKeyRepository, hasher: Hasher):
        self._repo = repo
        self._hasher = hasher
    
    def generate_key(self, prefix: str = "wt") -> tuple[str, str]:
        """Returns (plain_key, hashed_key)"""
        raw = secrets.token_urlsafe(32)
        plain = f"{prefix}_{raw}"
        hashed = self._hasher.hash(plain)
        return plain, hashed
    
    async def create_key(self, user_id: UUID, name: str, scopes: list[str], expires_at: datetime | None = None) -> APIKey:
        plain, hashed = self.generate_key()
        key = APIKey(
            id=uuid4(),
            user_id=user_id,
            name=name,
            key_hash=hashed,
            scopes=scopes,
            expires_at=expires_at,
            created_at=datetime.now(UTC),
        )
        await self._repo.add(key)
        return APIKeyWithPlain(key=key, plain_key=plain)
    
    async def verify_key(self, plain_key: str) -> APIKey | None:
        # Lookup by prefix + first 8 chars for efficiency
        prefix = plain_key.split("_")[0]
        candidates = await self._repo.get_by_prefix(prefix)
        for candidate in candidates:
            if self._hasher.verify(plain_key, candidate.key_hash):
                if candidate.expires_at and candidate.expires_at < datetime.now(UTC):
                    return None
                return candidate
        return None
```

## Casbin Authorization (RBAC + ABAC)

```python
# worker_authorization/casbin.py
import casbin
from casbin_sqlalchemy_adapter import Adapter
from sqlalchemy.ext.asyncio import AsyncSession

class AuthorizationService:
    def __init__(self, model_path: str, adapter: Adapter):
        self._enforcer = casbin.AsyncEnforcer(model_path, adapter)
        await self._enforcer.load_policy()
    
    async def check_permission(self, subject: str, resource: str, action: str, context: dict = None) -> bool:
        """subject = user_id or role, resource = domain object, action = read/write/delete"""
        if context:
            return await self._enforcer.enforce(subject, resource, action, context)
        return await self._enforcer.enforce(subject, resource, action)
    
    async def get_permissions(self, subject: str) -> list[tuple[str, str]]:
        return await self._enforcer.get_implicit_permissions_for_user(subject)
    
    async def assign_role(self, user_id: str, role: str, domain: str = "") -> bool:
        return await self._enforcer.add_grouping_policy(user_id, role, domain)
    
    async def remove_role(self, user_id: str, role: str, domain: str = "") -> bool:
        return await self._enforcer.remove_grouping_policy(user_id, role, domain)
    
    async def add_policy(self, role: str, resource: str, action: str, domain: str = "") -> bool:
        return await self._enforcer.add_policy(role, resource, action, domain)
    
    async def add_policy_with_condition(self, role: str, resource: str, action: str, condition: str) -> bool:
        """ABAC: condition like 'request.user.department == resource.department'"""
        return await self._enforcer.add_named_policy("p", role, resource, action, condition)
```

### Casbin Model (RBAC + ABAC)
```ini
# auth_model.conf
[request_definition]
r = sub, obj, act, ctx

[policy_definition]
p = sub, obj, act, ctx

[role_definition]
g = _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub) && r.obj == p.obj && r.act == p.act && (p.ctx == "" || eval(p.ctx))
```

### Policies (Database)
```sql
-- casbin_rule table
-- p_type, v0, v1, v2, v3
-- p, role:admin, user:*, *, ""                    -- Admin can do everything on users
-- p, role:recruiter, candidate:profile, read, ""  -- Recruiters can read profiles
-- p, role:candidate, candidate:profile, write, "r.sub == r.obj.owner_id"  -- Candidates own their profile
-- g, user:123, role:recruiter, ""                  -- User 123 has recruiter role
```

## Permission Decorators

```python
# worker_authorization/decorators.py
from functools import wraps
from fastapi import Depends, HTTPException, status

def require_permission(resource: str, action: str):
    def decorator(func):
        @wraps(func)
        async def wrapper(*args, current_user: CurrentUser = Depends(get_current_user), authz: AuthorizationService = Depends(get_authz), **kwargs):
            has_permission = await authz.check_permission(
                subject=f"user:{current_user.id}",
                resource=resource,
                action=action,
                context={"user": current_user.model_dump()}
            )
            if not has_permission:
                raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Insufficient permissions")
            return await func(*args, current_user=current_user, **kwargs)
        return wrapper
    return decorator

def require_role(*roles: str):
    def decorator(func):
        @wraps(func)
        async def wrapper(*args, current_user: CurrentUser = Depends(get_current_user), **kwargs):
            if not any(role in current_user.roles for role in roles):
                raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Role required")
            return await func(*args, current_user=current_user, **kwargs)
        return wrapper
    return decorator

# Usage
@router.post("/candidates")
@require_permission("candidate:profile", "create")
@require_role("recruiter", "admin")
async def create_candidate(...): ...
```

## Middleware Integration

```python
# worker_platform/presentation/middleware/auth.py
class AuthenticationMiddleware:
    def __init__(self, app: ASGIApp, token_manager: TokenManager, public_paths: set[str]):
        self.app = app
        self._token_manager = token_manager
        self._public_paths = public_paths
    
    async def __call__(self, scope: Scope, receive: Receive, send: Send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        
        path = scope["path"]
        if path in self._public_paths or path.startswith("/health"):
            await self.app(scope, receive, send)
            return
        
        auth_header = Headers(scope=scope).get("Authorization")
        if not auth_header or not auth_header.startswith("Bearer "):
            await self._send_unauthorized(send)
            return
        
        token = auth_header[7:]
        try:
            payload = self._token_manager.verify_token(token)
            scope["user"] = CurrentUser(
                id=payload.sub,
                tenant_id=payload.tenant_id,
                roles=payload.roles,
                permissions=payload.permissions,
            )
        except InvalidTokenError:
            await self._send_unauthorized(send)
            return
        
        await self.app(scope, receive, send)
```

## Session Management (Refresh Tokens)

```python
class SessionManager:
    def __init__(self, redis: Redis, token_manager: TokenManager):
        self._redis = redis
        self._token_manager = token_manager
    
    async def create_session(self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]) -> TokenPair:
        access = self._token_manager.create_access_token(user_id, tenant_id, roles, permissions)
        refresh = self._token_manager.create_refresh_token(user_id, tenant_id)
        
        # Store refresh token hash in Redis with expiry
        refresh_hash = hashlib.sha256(refresh.encode()).hexdigest()
        await self._redis.setex(
            f"refresh:{refresh_hash}",
            timedelta(days=30),
            json.dumps({"user_id": str(user_id), "tenant_id": str(tenant_id)})
        )
        
        return TokenPair(access_token=access, refresh_token=refresh)
    
    async def refresh_session(self, refresh_token: str) -> TokenPair | None:
        payload = self._token_manager.decode_token(refresh_token)
        if payload.type != "refresh":
            return None
        
        refresh_hash = hashlib.sha256(refresh_token.encode()).hexdigest()
        stored = await self._redis.get(f"refresh:{refresh_hash}")
        if not stored:
            return None  # Revoked or expired
        
        # Rotate tokens
        await self._redis.delete(f"refresh:{refresh_hash}")
        return await self.create_session(payload.sub, payload.tenant_id, [], [])
    
    async def revoke_session(self, refresh_token: str) -> bool:
        refresh_hash = hashlib.sha256(refresh_token.encode()).hexdigest()
        return await self._redis.delete(f"refresh:{refresh_hash}") > 0
    
    async def revoke_all_user_sessions(self, user_id: UUID) -> int:
        # Scan and delete all refresh tokens for user
        count = 0
        async for key in self._redis.scan_iter(f"refresh:*"):
            data = await self._redis.get(key)
            if data and json.loads(data).get("user_id") == str(user_id):
                await self._redis.delete(key)
                count += 1
        return count
```