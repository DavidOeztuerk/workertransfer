"""Command dispatcher surface for identity-service (ADR-0003).

Phase 2 keeps the transaction surface router-driven: the HTTP router (Task 18)
opens a per-request UoW via ``compose.request_scope`` and calls the command
handler explicitly with ``deps`` + ``repos`` bound to that one session:

    async with request_scope(session_factory) as (uow, repos):
        result = await handle_register(cmd, deps=deps, repos=repos)
    # uow commits on success (atomicity: audit + domain state together — ADR-0012)

This module therefore carries no runtime dispatcher: a placeholder ``run``
would only obscure the explicit per-request UoW that lives in the router.
The module exists so that future pipeline behaviors (validation, logging,
metrics) can attach to a single dispatch seam without re-wiring every route.
"""

from __future__ import annotations

__all__: list[str] = []
