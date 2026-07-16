"""MCP Integration: MCP servers/clients, Tool registry, Resource registry."""

from typing import Any, cast

from mcp import ClientSession, StdioServerParameters
from mcp.types import CallToolResult, Resource, Tool

__all__ = ["MCPClient", "MCPRegistry"]


class MCPClient:
    def __init__(
        self,
        command: str,
        args: list[str] | None = None,
        env: dict[str, Any] | None = None,
    ) -> None:
        self._params = StdioServerParameters(command=command, args=args or [], env=env or {})
        self._session: ClientSession | None = None

    async def connect(self) -> None:
        from mcp.client.stdio import stdio_client

        self._stdio, self._write = await stdio_client(self._params).__aenter__()
        self._session = ClientSession(self._stdio, self._write)
        await self._session.initialize()

    async def disconnect(self) -> None:
        if self._session:
            await self._session.__aexit__(None, None, None)

    def _session_required(self) -> ClientSession:
        if self._session is None:
            raise RuntimeError("MCP client not connected; call connect() first")
        return self._session

    async def list_tools(self) -> list[Tool]:
        return (await self._session_required().list_tools()).tools

    async def list_resources(self) -> list[Resource]:
        return (await self._session_required().list_resources()).resources

    async def call_tool(self, name: str, arguments: dict[str, Any]) -> CallToolResult:
        return await self._session_required().call_tool(name, arguments)

    async def read_resource(self, uri: str) -> str:
        result = await self._session_required().read_resource(cast("Any", uri))
        contents = result.contents
        if not contents:
            return ""
        first = contents[0]
        return cast("str", getattr(first, "text", ""))


class MCPRegistry:
    def __init__(self) -> None:
        self._clients: dict[str, MCPClient] = {}

    def register(self, name: str, client: MCPClient) -> None:
        self._clients[name] = client

    async def connect_all(self) -> None:
        for client in self._clients.values():
            await client.connect()

    async def disconnect_all(self) -> None:
        for client in self._clients.values():
            await client.disconnect()

    async def call_tool(self, server: str, tool: str, arguments: dict[str, Any]) -> CallToolResult:
        client = self._clients.get(server)
        if not client:
            raise ValueError(f"MCP server '{server}' not found")
        return await client.call_tool(tool, arguments)

    async def read_resource(self, server: str, uri: str) -> str:
        client = self._clients.get(server)
        if not client:
            raise ValueError(f"MCP server '{server}' not found")
        return await client.read_resource(uri)
