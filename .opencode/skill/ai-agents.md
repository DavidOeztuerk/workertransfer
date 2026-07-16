# Skill: AI Agent Runtime & MCP Integration

## Purpose
Build a complete AI agent runtime with provider abstraction, tool calling, memory, prompt templates, planner/executor/evaluator/reflection loop, and MCP integration.

## Dependencies
```toml
# worker-ai/pyproject.toml
dependencies = [
    "openai>=1.0.0,<2.0.0",
    "anthropic>=0.30.0,<1.0.0",
    "google-generativeai>=0.5.0,<1.0.0",
    "ollama>=0.2.0,<1.0.0",
    "mcp>=1.0.0,<2.0.0",
    "pydantic>=2.8.0,<3.0.0",
    "pydantic-ai>=0.1.0,<1.0.0",
    "chromadb>=0.5.0,<1.0.0",
    "sentence-transformers>=3.0.0,<4.0.0",
    "langchain-core>=0.2.0,<1.0.0",
]
```

## Provider Abstraction

```python
# worker_ai/providers/base.py
from abc import ABC, abstractmethod
from typing import AsyncIterator
from pydantic import BaseModel

class Message(BaseModel):
    role: str  # "system" | "user" | "assistant" | "tool"
    content: str | None = None
    tool_calls: list["ToolCall"] | None = None
    tool_call_id: str | None = None
    name: str | None = None  # for tool messages

class ToolCall(BaseModel):
    id: str
    type: str = "function"
    function: "FunctionCall"

class FunctionCall(BaseModel):
    name: str
    arguments: str  # JSON string

class Tool(BaseModel):
    type: str = "function"
    function: "FunctionSchema"

class FunctionSchema(BaseModel):
    name: str
    description: str
    parameters: dict  # JSON Schema

class CompletionOptions(BaseModel):
    temperature: float = 0.7
    max_tokens: int | None = None
    top_p: float = 1.0
    stop: list[str] | None = None
    tools: list[Tool] | None = None
    tool_choice: str | dict | None = "auto"

class CompletionResponse(BaseModel):
    message: Message
    usage: "Usage" | None = None
    finish_reason: str

class Usage(BaseModel):
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int

class LLMProvider(ABC):
    @abstractmethod
    async def complete(
        self,
        messages: list[Message],
        options: CompletionOptions,
    ) -> CompletionResponse: ...
    
    @abstractmethod
    async def stream_complete(
        self,
        messages: list[Message],
        options: CompletionOptions,
    ) -> AsyncIterator[CompletionResponse]: ...
    
    @abstractmethod
    async def embed(self, texts: list[str]) -> list[list[float]]: ...

class OpenAIProvider(LLMProvider):
    def __init__(self, api_key: str, model: str = "gpt-4-turbo-preview"):
        self._client = openai.AsyncOpenAI(api_key=api_key)
        self._model = model
    
    async def complete(self, messages: list[Message], options: CompletionOptions) -> CompletionResponse:
        response = await self._client.chat.completions.create(
            model=self._model,
            messages=[m.model_dump(exclude_none=True) for m in messages],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            top_p=options.top_p,
            stop=options.stop,
            tools=[t.model_dump() for t in options.tools] if options.tools else None,
            tool_choice=options.tool_choice,
        )
        return CompletionResponse(
            message=Message(
                role=response.choices[0].message.role,
                content=response.choices[0].message.content,
                tool_calls=[
                    ToolCall(
                        id=tc.id,
                        function=FunctionCall(name=tc.function.name, arguments=tc.function.arguments)
                    ) for tc in response.choices[0].message.tool_calls or []
                ],
            ),
            usage=Usage(
                prompt_tokens=response.usage.prompt_tokens,
                completion_tokens=response.usage.completion_tokens,
                total_tokens=response.usage.total_tokens,
            ),
            finish_reason=response.choices[0].finish_reason,
        )

class AnthropicProvider(LLMProvider):
    # Similar implementation for Anthropic
    pass

class OllamaProvider(LLMProvider):
    # Local model support
    pass
```

## Tool System

```python
# worker_ai/tools/registry.py
from abc import ABC, abstractmethod
from typing import Any
from pydantic import BaseModel, Field
import inspect

class Tool(ABC):
    @property
    @abstractmethod
    def name(self) -> str: ...
    
    @property
    @abstractmethod
    def description(self) -> str: ...
    
    @property
    @abstractmethod
    def parameters_schema(self) -> dict: ...
    
    @abstractmethod
    async def execute(self, **kwargs) -> Any: ...

class FunctionTool(Tool):
    def __init__(self, func: callable, name: str | None = None, description: str | None = None):
        self._func = func
        self._name = name or func.__name__
        self._description = description or func.__doc__ or ""
        self._schema = self._generate_schema(func)
    
    @property
    def name(self) -> str:
        return self._name
    
    @property
    def description(self) -> str:
        return self._description
    
    @property
    def parameters_schema(self) -> dict:
        return self._schema
    
    async def execute(self, **kwargs) -> Any:
        if inspect.iscoroutinefunction(self._func):
            return await self._func(**kwargs)
        return self._func(**kwargs)
    
    def _generate_schema(self, func: callable) -> dict:
        sig = inspect.signature(func)
        properties = {}
        required = []
        for name, param in sig.parameters.items():
            if param.annotation != inspect.Parameter.empty:
                # Convert Python type to JSON Schema
                properties[name] = self._type_to_schema(param.annotation)
            else:
                properties[name] = {"type": "string"}
            if param.default == inspect.Parameter.empty:
                required.append(name)
        return {"type": "object", "properties": properties, "required": required}

class ToolRegistry:
    def __init__(self):
        self._tools: dict[str, Tool] = {}
    
    def register(self, tool: Tool) -> None:
        self._tools[tool.name] = tool
    
    def register_function(self, func: callable, name: str | None = None, description: str | None = None) -> None:
        self.register(FunctionTool(func, name, description))
    
    def get(self, name: str) -> Tool | None:
        return self._tools.get(name)
    
    def get_all(self) -> list[Tool]:
        return list(self._tools.values())
    
    def get_schemas(self) -> list[dict]:
        return [{"type": "function", "function": t.parameters_schema} for t in self._tools.values()]

# Example tools
async def search_candidates(query: str, skills: list[str], location: str | None = None, limit: int = 10) -> dict:
    """Search for candidates matching criteria"""
    # Implementation
    pass

async def analyze_github_profile(username: str) -> dict:
    """Analyze GitHub profile for skills and experience"""
    pass

async def generate_contract(template: str, variables: dict) -> str:
    """Generate contract from template"""
    pass
```

## Memory System

```python
# worker_ai/memory/base.py
from abc import ABC, abstractmethod
from typing import Any
from pydantic import BaseModel
from uuid import UUID

class MemoryEntry(BaseModel):
    id: UUID
    type: str  # "conversation" | "fact" | "skill" | "preference" | "episode"
    content: str
    metadata: dict[str, Any] = {}
    embedding: list[float] | None = None
    created_at: datetime
    expires_at: datetime | None = None
    importance: float = 1.0

class MemoryStore(ABC):
    @abstractmethod
    async def add(self, entry: MemoryEntry) -> None: ...
    
    @abstractmethod
    async def get(self, id: UUID) -> MemoryEntry | None: ...
    
    @abstractmethod
    async def search(self, query: str, limit: int = 10, filters: dict | None = None) -> list[MemoryEntry]: ...
    
    @abstractmethod
    async def search_by_embedding(self, embedding: list[float], limit: int = 10) -> list[MemoryEntry]: ...

class VectorMemoryStore(MemoryStore):
    def __init__(self, chroma_client, collection_name: str, embedder: Embedder):
        self._collection = chroma_client.get_or_create_collection(collection_name)
        self._embedder = embedder
    
    async def add(self, entry: MemoryEntry) -> None:
        if entry.embedding is None:
            entry.embedding = await self._embedder.embed(entry.content)
        
        self._collection.add(
            ids=[str(entry.id)],
            embeddings=[entry.embedding],
            documents=[entry.content],
            metadatas=[{
                "type": entry.type,
                **entry.metadata,
                "created_at": entry.created_at.isoformat(),
                "importance": entry.importance,
            }]
        )
    
    async def search(self, query: str, limit: int = 10, filters: dict | None = None) -> list[MemoryEntry]:
        query_embedding = await self._embedder.embed(query)
        return await self.search_by_embedding(query_embedding, limit, filters)
    
    async def search_by_embedding(self, embedding: list[float], limit: int = 10, filters: dict | None = None) -> list[MemoryEntry]:
        where = filters or {}
        results = self._collection.query(
            query_embeddings=[embedding],
            n_results=limit,
            where=where if where else None,
        )
        return [
            MemoryEntry(
                id=UUID(id_),
                type=meta["type"],
                content=doc,
                metadata={k: v for k, v in meta.items() if k not in ["type", "created_at", "importance"]},
                created_at=datetime.fromisoformat(meta["created_at"]),
                importance=meta.get("importance", 1.0),
            )
            for id_, doc, meta in zip(results["ids"][0], results["documents"][0], results["metadatas"][0])
        ]

class ShortTermMemory:
    """In-memory conversation buffer"""
    def __init__(self, max_messages: int = 20):
        self._messages: list[Message] = []
        self._max_messages = max_messages
    
    def add(self, message: Message) -> None:
        self._messages.append(message)
        if len(self._messages) > self._max_messages:
            self._messages = self._messages[-self._max_messages:]
    
    def get_messages(self) -> list[Message]:
        return self._messages.copy()
    
    def clear(self) -> None:
        self._messages.clear()

class AgentMemory:
    """Unified memory interface for agents"""
    def __init__(
        self,
        short_term: ShortTermMemory,
        long_term: MemoryStore,
        working_memory: dict[str, Any] | None = None,
    ):
        self.short_term = short_term
        self.long_term = long_term
        self.working_memory = working_memory or {}
    
    async def remember(self, content: str, type: str = "fact", **metadata) -> None:
        entry = MemoryEntry(
            id=uuid4(),
            type=type,
            content=content,
            metadata=metadata,
            created_at=datetime.now(UTC),
            importance=metadata.get("importance", 1.0),
        )
        await self.long_term.add(entry)
    
    async def recall(self, query: str, limit: int = 5) -> list[MemoryEntry]:
        return await self.long_term.search(query, limit)
```