"""Smoke tests for worker-agents (Phase 1.5).

Exercises the agent-role enum and the orchestrator constructor, which only
allocates an empty registry (``self._agents = {}``). No LLM/network.
"""

from worker_agents import AgentOrchestrator, AgentRole


def test_smoke_agent_role_and_orchestrator() -> None:
    orchestrator = AgentOrchestrator()

    assert AgentRole.PLANNER.value == "planner"
    assert orchestrator._agents == {}
