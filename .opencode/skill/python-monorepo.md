# Skill: Python Monorepo Development (uv workspace)

## Purpose
Expert-level guidance for developing in a Python monorepo managed by `uv` with multiple workspace packages and services.

## Key Commands

```bash
# Sync all packages with all dependency groups
uv sync --all-packages --all-groups

# Run formatter check
uv run ruff format --check .

# Run linter
uv run ruff check .

# Run type checker (excludes tests)
uv run mypy packages apps

# Run tests
uv run pytest

# Run tests for specific package
uv run pytest packages/worker-platform/tests/

# Run tests with coverage
uv run pytest --cov=worker_platform --cov-report=term-missing

# Add dependency to a package
uv add --package worker-platform fastapi

# Add dev dependency
uv add --package worker-platform --dev pytest

# Build package
uv build packages/worker-platform

# Run a service
uv run worker-identity

# Create new package structure
mkdir -p packages/new-package/src/new_package/{domain,application,infrastructure,presentation}
```

## Package Structure Convention

Each package in `packages/` follows:
```
packages/<package-name>/
├── pyproject.toml          # Package config with [build-system] uv_build
├── src/
│   └── <package_name>/     # Python module (underscores)
│       ├── __init__.py     # Exports public API
│       ├── domain/         # Domain primitives (if applicable)
│       ├── application/    # Application layer (if applicable)
│       ├── infrastructure/ # Infrastructure implementations
│       └── presentation/   # HTTP/messaging entry points
├── tests/                  # Package tests (excluded from mypy)
└── README.md
```

## Service Structure Convention

Each service in `apps/` follows Clean Architecture:
```
apps/<service-name>/
├── pyproject.toml
├── src/
│   └── <service_name>/
│       ├── __init__.py
│       ├── main.py                 # Entrypoint with run()
│       ├── configuration.py        # Service-specific settings
│       ├── domain/                 # Entities, VOs, Events, Errors
│       ├── application/            # Commands, Queries, Handlers, DTOs
│       ├── infrastructure/         # DB, Messaging, Cache, External APIs
│       └── presentation/           # REST, GraphQL, Consumers, Workers
└── tests/
    ├── unit/                       # Domain/Application tests
    ├── integration/                # Infrastructure tests
    └── contract/                   # API contract tests
```

## Dependency Rules

1. **worker-core**: Zero dependencies. Pure domain primitives.
2. **worker-platform**: Depends on worker-core + FastAPI, Pydantic, etc.
3. **Services**: Depend on worker-core, worker-platform, and other packages via workspace deps.
4. **Packages**: Never depend on services. Only domain-neutral code in packages.
5. **Shared code**: Only technical, domain-neutral code in `packages/`. Business models stay in owning service.

## Testing Conventions

- `asyncio_mode = "auto"` in pyproject.toml
- Use `pytest-asyncio` for async tests
- Test files in `tests/` directories (excluded from mypy)
- `S101` (assert) allowed in tests via ruff per-file-ignores
- Use `faker` and `factory-boy` for test data
- Run single test: `uv run pytest apps/identity-service/tests/test_app.py::test_liveness_is_available_with_correlation_and_security_headers -v`

## Type Checking

```bash
# Full type check
uv run mypy packages apps

# Check specific package
uv run mypy packages/worker-platform

# Check with verbose output
uv run mypy --verbose packages/worker-platform
```

## Code Generation

Use the Worker CLI (when available):
```bash
uv run worker new-service profile
uv run worker new-package worker-newpackage
uv run worker command CreateUser
uv run worker query GetUser
uv run worker entity User
uv run worker aggregate User
uv run worker valueobject Email
uv run worker event UserCreated
```

## Adding a New Package

1. Create directory structure
2. Add `pyproject.toml` with `[build-system]` using `uv_build`
3. Add to `[tool.uv.workspace].members` in root `pyproject.toml`
4. Add to `[tool.uv.sources]` if needed
5. Run `uv sync --all-packages --all-groups`
6. Add exports in `__init__.py`
7. Add tests

## Adding a New Service

1. Create `apps/new-service/` with pyproject.toml
2. Add `[project.scripts]` entry point
3. Implement Clean Architecture layers
4. Add to root `pyproject.toml` workspace members
5. Create service settings extending `PlatformSettings`
6. Implement `create_app()` and `run()` in `main.py`
7. Add health checks
8. Write tests
9. Add Dockerfile
10. Add to docker-compose.yml