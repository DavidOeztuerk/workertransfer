"""Smoke tests for worker-mcp (Phase 1.5).

Exercises ``MCPClient`` parameter construction (pure metadata via
``StdioServerParameters``) and ``MCPRegistry`` (empty client map). ``connect()``
is NOT called — it would spawn a real stdio subprocess and open an MCP session.
"""

from worker_mcp import MCPClient, MCPRegistry


def test_smoke_mcp_client_and_registry() -> None:
    client = MCPClient("echo", args=["hi"])
    registry = MCPRegistry()

    assert client._session is None
    assert client._params.command == "echo"
    assert registry._clients == {}
