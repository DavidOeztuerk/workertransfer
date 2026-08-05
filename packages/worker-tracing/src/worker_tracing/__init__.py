"""OpenTelemetry tracing with context propagation and sampling."""

from collections.abc import Mapping
from typing import Any, cast

from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
from opentelemetry.instrumentation.redis import RedisInstrumentor
from opentelemetry.instrumentation.sqlalchemy import SQLAlchemyInstrumentor
from opentelemetry.sdk.resources import SERVICE_NAME, Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.trace import Tracer
from opentelemetry.trace.span import Span
from opentelemetry.util.types import Attributes

__all__ = ["Tracer", "get_tracer", "setup_tracing", "start_span"]


def setup_tracing(service_name: str, otlp_endpoint: str = "http://localhost:4317") -> Tracer:
    resource = Resource.create({SERVICE_NAME: service_name})
    provider = TracerProvider(resource=resource)

    exporter = OTLPSpanExporter(endpoint=otlp_endpoint, insecure=True)
    provider.add_span_processor(BatchSpanProcessor(exporter))

    trace.set_tracer_provider(provider)

    # Auto-instrument
    FastAPIInstrumentor.instrument()
    SQLAlchemyInstrumentor.instrument()
    RedisInstrumentor().instrument()
    HTTPXClientInstrumentor.instrument()

    return trace.get_tracer(__name__)


def get_tracer(name: str) -> Tracer:
    return trace.get_tracer(name)


def start_span(name: str, **attributes: Any) -> Span:
    tracer = trace.get_tracer(__name__)
    return cast(
        Span,
        tracer.start_as_current_span(name, attributes=_to_attributes(attributes)),
    )


def _to_attributes(attributes: Mapping[str, Any]) -> Attributes:
    return cast(Attributes, dict(attributes))
