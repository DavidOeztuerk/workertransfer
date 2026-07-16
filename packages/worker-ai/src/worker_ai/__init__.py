"""AI Runtime: Provider abstraction, Tool calling, Memory, Prompt templates, Streaming."""

from __future__ import annotations

from abc import ABC, abstractmethod
from collections.abc import AsyncGenerator
from dataclasses import dataclass
from typing import Any


@dataclass
class Message:
    role: str  # system, user, assistant, tool
    content: str | None = None
    tool_calls: list[ToolCall] | None = None
    tool_call_id: str | None = None
    name: str | None = None


@dataclass
class ToolCall:
    id: str
    type: str = "function"
    function: FunctionCall | None = None


@dataclass
class FunctionCall:
    name: str
    arguments: str  # JSON string


@dataclass
class Tool:
    type: str = "function"
    function: FunctionSchema | None = None


@dataclass
class FunctionSchema:
    name: str
    description: str
    parameters: dict[str, Any]  # JSON Schema


@dataclass
class CompletionOptions:
    temperature: float = 0.7
    max_tokens: int | None = None
    top_p: float = 1.0
    stop: list[str] | None = None
    tools: list[Tool] | None = None
    tool_choice: str | dict[str, Any] | None = "auto"


@dataclass
class CompletionResponse:
    message: Message
    usage: Usage | None = None
    finish_reason: str = "stop"


@dataclass
class Usage:
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int


class LLMProvider(ABC):
    @abstractmethod
    async def complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> CompletionResponse: ...

    @abstractmethod
    def stream_complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> AsyncGenerator[CompletionResponse]: ...

    @abstractmethod
    async def embed(self, texts: list[str]) -> list[list[float]]: ...


class OpenAIProvider(LLMProvider):
    def __init__(self, api_key: str, model: str = "gpt-4-turbo-preview"):
        import openai

        self._client = openai.AsyncOpenAI(api_key=api_key)
        self._model = model

    async def complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> CompletionResponse:
        response = await self._client.chat.completions.create(  # type: ignore[call-overload]
            model=self._model,
            messages=[m.__dict__ for m in messages],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            top_p=options.top_p,
            stop=options.stop,
            tools=[t.__dict__ for t in options.tools] if options.tools else None,
            tool_choice=options.tool_choice,
        )
        msg = response.choices[0].message
        return CompletionResponse(
            message=Message(
                role=msg.role,
                content=msg.content,
                tool_calls=[
                    ToolCall(
                        id=tc.id,
                        function=FunctionCall(
                            name=tc.function.name, arguments=tc.function.arguments
                        ),
                    )
                    for tc in msg.tool_calls or []
                ],
            ),
            usage=Usage(
                prompt_tokens=response.usage.prompt_tokens,
                completion_tokens=response.usage.completion_tokens,
                total_tokens=response.usage.total_tokens,
            ),
            finish_reason=response.choices[0].finish_reason,
        )

    async def stream_complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> AsyncGenerator[CompletionResponse]:
        stream = await self._client.chat.completions.create(  # type: ignore[call-overload]
            model=self._model,
            messages=[m.__dict__ for m in messages],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            top_p=options.top_p,
            stop=options.stop,
            tools=[t.__dict__ for t in options.tools] if options.tools else None,
            tool_choice=options.tool_choice,
            stream=True,
        )
        async for chunk in stream:
            if chunk.choices[0].delta.content:
                yield CompletionResponse(
                    message=Message(role="assistant", content=chunk.choices[0].delta.content),
                    finish_reason=chunk.choices[0].finish_reason or "stop",
                )

    async def embed(self, texts: list[str]) -> list[list[float]]:
        response = await self._client.embeddings.create(model="text-embedding-3-small", input=texts)
        return [d.embedding for d in response.data]


class AnthropicProvider(LLMProvider):
    def __init__(self, api_key: str, model: str = "claude-3-opus-20240229"):
        import anthropic

        self._client = anthropic.AsyncAnthropic(api_key=api_key)
        self._model = model

    async def complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> CompletionResponse:
        # Convert to Anthropic format
        from anthropic.types import TextBlock

        system = ""
        user_messages: list[dict[str, str | None]] = []
        for m in messages:
            if m.role == "system":
                system = m.content or ""
            else:
                user_messages.append({"role": m.role, "content": m.content})

        response = await self._client.messages.create(
            model=self._model,
            system=system,
            messages=user_messages,  # type: ignore[arg-type]
            max_tokens=options.max_tokens or 4096,
            temperature=options.temperature,
            top_p=options.top_p,
            stop_sequences=options.stop,  # type: ignore[arg-type]
        )
        first_block = response.content[0] if response.content else None
        text = first_block.text if isinstance(first_block, TextBlock) else ""
        return CompletionResponse(
            message=Message(role="assistant", content=text),
            usage=Usage(
                prompt_tokens=response.usage.input_tokens,
                completion_tokens=response.usage.output_tokens,
                total_tokens=response.usage.input_tokens + response.usage.output_tokens,
            ),
            finish_reason=response.stop_reason or "stop",
        )

    async def stream_complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> AsyncGenerator[CompletionResponse]:
        raise NotImplementedError("Anthropic streaming not implemented in Phase 1 scaffold")
        yield CompletionResponse(Message(role="assistant"))  # type: ignore[unreachable]  # pragma: no cover

    async def embed(self, texts: list[str]) -> list[list[float]]:
        raise NotImplementedError("Anthropic embeddings not available")


class OllamaProvider(LLMProvider):
    def __init__(self, base_url: str = "http://localhost:11434", model: str = "llama3"):
        import ollama

        self._client = ollama.AsyncClient(host=base_url)
        self._model = model

    async def complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> CompletionResponse:
        response = await self._client.chat(
            model=self._model,
            messages=[m.__dict__ for m in messages],
            options={
                "temperature": options.temperature,
                "num_predict": options.max_tokens,
            },
        )
        return CompletionResponse(
            message=Message(role="assistant", content=response["message"]["content"]),
            finish_reason="stop",
        )

    async def stream_complete(
        self, messages: list[Message], options: CompletionOptions
    ) -> AsyncGenerator[CompletionResponse]:
        async for chunk in await self._client.chat(
            model=self._model,
            messages=[m.__dict__ for m in messages],
            options={"temperature": options.temperature, "num_predict": options.max_tokens},
            stream=True,
        ):
            if chunk["message"]["content"]:
                yield CompletionResponse(
                    message=Message(role="assistant", content=chunk["message"]["content"]),
                )

    async def embed(self, texts: list[str]) -> list[list[float]]:
        import ollama

        return [ollama.embeddings(model="nomic-embed-text", prompt=t)["embedding"] for t in texts]
