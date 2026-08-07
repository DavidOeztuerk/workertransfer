"""Smoke tests for worker-validation (Phase 1.5).

Exercises the ``FluentValidator`` with a ``not_empty`` rule and the
``ValidationResult.success`` classmethod. ``validate`` runs pure-python
predicate closures — no IO.
"""

from worker_validation import FluentValidator, ValidationResult


def test_smoke_fluent_validator() -> None:
    validator = FluentValidator()
    validator.rule_for("email").not_empty()

    result = validator.validate({"email": ""})

    assert result.is_valid is False
    assert result.errors[0].code == "not_empty"


def test_smoke_validation_result_success() -> None:
    result = ValidationResult.success()

    assert result.is_valid is True
    assert result.errors == []
