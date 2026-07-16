"""Base DTO for API contracts."""

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, ConfigDict


class BaseDTO(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")


class TimestampedDTO(BaseDTO):
    created_at: datetime
    updated_at: datetime


class IdentifiedDTO(BaseDTO):
    id: UUID
