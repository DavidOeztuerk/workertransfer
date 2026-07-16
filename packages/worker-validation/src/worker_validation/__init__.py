"""FluentValidation-style validation for requests, domain, and business rules."""

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from pydantic import BaseModel
from pydantic import ValidationError as PydanticValidationError


@dataclass
class ValidationError:
    field: str
    message: str
    code: str = "validation_error"


@dataclass
class ValidationResult:
    is_valid: bool
    errors: list[ValidationError]

    @classmethod
    def success(cls) -> ValidationResult:
        return cls(is_valid=True, errors=[])

    @classmethod
    def failure(cls, errors: list[ValidationError]) -> ValidationResult:
        return cls(is_valid=False, errors=errors)


class Validator:
    def __init__(self, model: type[BaseModel]) -> None:
        self._model = model

    def validate(self, data: dict[str, Any]) -> ValidationResult:
        try:
            self._model(**data)
            return ValidationResult.success()
        except PydanticValidationError as e:
            errors = [
                ValidationError(
                    field=".".join(str(loc) for loc in err["loc"]),
                    message=err["msg"],
                    code=err["type"],
                )
                for err in e.errors()
            ]
            return ValidationResult.failure(errors)

    def validate_object(self, obj: Any) -> ValidationResult:
        return self.validate(obj.model_dump() if hasattr(obj, "model_dump") else obj)


class FluentValidator:
    def __init__(self) -> None:
        self._rules: list[Callable[[dict[str, Any]], ValidationResult]] = []

    def rule_for(self, field: str) -> FluentRuleBuilder:
        return FluentRuleBuilder(self, field)

    def validate(self, data: dict[str, Any]) -> ValidationResult:
        errors: list[ValidationError] = []
        for rule in self._rules:
            result = rule(data)
            if not result.is_valid:
                errors.extend(result.errors)
        return ValidationResult(is_valid=len(errors) == 0, errors=errors)


class FluentRuleBuilder:
    def __init__(self, validator: FluentValidator, field: str) -> None:
        self._validator = validator
        self._field = field

    def not_empty(self, message: str = "Field is required") -> FluentRuleBuilder:
        def rule(data: dict[str, Any]) -> ValidationResult:
            value = data.get(self._field)
            if not value or (isinstance(value, str) and not value.strip()):
                return ValidationResult.failure(
                    [ValidationError(self._field, message, "not_empty")]
                )
            return ValidationResult.success()

        self._validator._rules.append(rule)
        return self

    def email(self, message: str = "Invalid email format") -> FluentRuleBuilder:
        import re

        pattern = r"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"

        def rule(data: dict[str, Any]) -> ValidationResult:
            value = data.get(self._field)
            if value and not re.match(pattern, str(value)):
                return ValidationResult.failure([ValidationError(self._field, message, "email")])
            return ValidationResult.success()

        self._validator._rules.append(rule)
        return self

    def length(
        self, min_len: int = 0, max_len: int | None = None, message: str | None = None
    ) -> FluentRuleBuilder:
        def rule(data: dict[str, Any]) -> ValidationResult:
            value = data.get(self._field)
            if value is not None:
                length = len(value) if hasattr(value, "__len__") else 0
                if length < min_len or (max_len is not None and length > max_len):
                    msg = message or f"Length must be between {min_len} and {max_len or 'infinity'}"
                    return ValidationResult.failure([ValidationError(self._field, msg, "length")])
            return ValidationResult.success()

        self._validator._rules.append(rule)
        return self

    def must(self, predicate: Callable[[Any], bool], message: str) -> FluentRuleBuilder:
        def rule(data: dict[str, Any]) -> ValidationResult:
            value = data.get(self._field)
            if value is not None and not predicate(value):
                return ValidationResult.failure([ValidationError(self._field, message, "must")])
            return ValidationResult.success()

        self._validator._rules.append(rule)
        return self
