# Github Service Service

Github Service microservice for WorkerTransfer platform.

## Architecture

Clean Architecture layers:
- **Domain** - Entities, Value Objects, Aggregates, Domain Events, Specifications
- **Application** - Commands, Queries, Handlers, DTOs, Validators, Pipeline Behaviors
- **Infrastructure** - Database, Messaging, Cache, Repositories, External APIs
- **Presentation** - HTTP API, Middleware, Health Checks

## Quick Start

```bash
# Install dependencies
uv sync --all-packages --all-groups

# Run service
uv run github_service

# Run tests
uv run pytest

# Type check
uv run mypy packages apps

# Lint
uv run ruff check .
uv run ruff format --check .
```

## Development

### Running locally

```bash
# Start dependencies
docker compose up -d postgres redis rabbitmq

# Run migrations
uv run alembic upgrade head

# Start service
uv run github_service
```

### Creating new components

```bash
# Generate command
uv run worker command CreateGithubService --service github-service --handler

# Generate query
uv run worker query GetGithubService --service github-service --handler

# Generate entity
uv run worker entity GithubService --service github-service --fields "name:str,email:Email"

# Generate aggregate
uv run worker aggregate GithubService --service github-service

# Generate value object
uv run worker valueobject Email --service github-service --fields "value:str"

# Generate event
uv run worker event GithubServiceCreated --service github-service --type domain
```

## Configuration

Environment variables (see `.env.example`):

- `WORKER_SERVICE_NAME` - Service name
- `WORKER_DATABASE_URL` - PostgreSQL connection string
- `WORKER_REDIS_URL` - Redis connection string
- `WORKER_RABBITMQ_URL` - RabbitMQ connection string
- `WORKER_JWT_SECRET` - JWT signing secret
- `WORKER_JWT_ALGORITHM` - JWT algorithm (RS256)

## API

- `GET /health/live` - Liveness probe
- `GET /health/ready` - Readiness probe
- `GET /docs` - OpenAPI docs (dev only)

## Testing

```bash
# Unit tests
uv run pytest tests/unit -v

# Integration tests
uv run pytest tests/integration -v

# With coverage
uv run pytest --cov=github_service --cov-report=term-missing
```