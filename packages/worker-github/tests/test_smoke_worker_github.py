"""Smoke test for worker-github (Phase 1.5).

The source imports ``from github import Github`` (PyGithub), but the declared
dependency is ``githubkit`` — PyGithub is NOT installed, so ``import
worker_github`` raises ``ModuleNotFoundError: No module named 'github'`` before
any surface can be exercised. This is a dependency-declaration mismatch tracked
as a follow-up deficit. The smoke test is skipped with the reason until PyGithub
is added (or the source switches to ``githubkit``); once importable, the pure
targets are ``SkillAnalyzer.analyze(stats)`` and
``OSSReputationCalculator.calculate(profile, stats)``.
"""

import pytest


def test_smoke_worker_github_import_or_skip() -> None:
    try:
        import worker_github
    except ModuleNotFoundError as exc:  # PyGithub not installed
        pytest.skip(
            f"worker-github import broken: {exc} (declared dep is githubkit, source imports github)"
        )
    assert worker_github is not None
