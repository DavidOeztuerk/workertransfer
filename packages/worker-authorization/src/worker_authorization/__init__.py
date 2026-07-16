"""Authorization: RBAC, ABAC, Casbin, Permission decorators, Policy evaluation."""

from typing import Any, cast

import casbin
from casbin_sqlalchemy_adapter import Adapter


class AuthorizationService:
    def __init__(self, model_path: str, adapter: Adapter) -> None:
        self.enforcer = casbin.AsyncEnforcer(model_path, adapter)

    async def load_policy(self) -> None:
        await self.enforcer.load_policy()

    async def check_permission(
        self, subject: str, resource: str, action: str, context: dict[str, Any] | None = None
    ) -> bool:
        if context:
            return bool(await self.enforcer.enforce(subject, resource, action, context))
        return bool(await self.enforcer.enforce(subject, resource, action))

    async def get_permissions(self, subject: str) -> list[tuple[str, str]]:
        permissions = await self.enforcer.get_implicit_permissions_for_user(subject)
        return [(cast("str", p[0]), cast("str", p[1])) for p in cast("list[Any]", permissions)]

    async def assign_role(self, user_id: str, role: str, domain: str = "") -> bool:
        return bool(await self.enforcer.add_grouping_policy(user_id, role, domain))

    async def add_policy(self, role: str, resource: str, action: str, domain: str = "") -> bool:
        return bool(await self.enforcer.add_policy(role, resource, action, domain))


__all__ = ["AuthorizationService"]
