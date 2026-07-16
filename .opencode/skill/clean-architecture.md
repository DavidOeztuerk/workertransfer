# Skill: Clean Architecture Implementation

## Purpose
Implement Clean Architecture layers with strict dependency rules for WorkerTransfer services.

## Layer Structure

```
Presentation → Application → Domain
     |                           ^
     └------- Infrastructure ------┘
```

## Dependency Rules

1. **Domain** (inner): No external dependencies. Pure Python. Contains:
   - Entities (identity + lifecycle)
   - Value Objects (immutable, structural equality)
   - Aggregates (consistency boundaries)
   - Domain Events (side effects within domain)
   - Domain Services (cross-aggregate logic)
   - Specifications (business rules as objects)
   - Repository Interfaces (ports)
   - Domain Exceptions

2. **Application** (middle): Depends on Domain. Contains:
   - Commands (state changes)
   - Queries (reads)
   - Command/Query Handlers
   - DTOs (data transfer objects)
   - Validators (FluentValidation style)
   - Pipeline Behaviors (cross-cutting)
   - Application Services (orchestration)
   - Ports (interfaces for infrastructure)

3. **Infrastructure** (outer): Implements Application ports. Contains:
   - Database (SQLAlchemy, repositories)
   - Messaging (RabbitMQ, Kafka publishers/consumers)
   - Cache (Redis implementations)
   - External APIs (HTTP clients)
   - Authentication providers
   - File storage (S3, MinIO)
   - Email/SMS providers

4. **Presentation** (entry points): Depends on Application. Contains:
   - REST Controllers/Routers (FastAPI)
   - GraphQL Resolvers
   - gRPC Services
   - Message Consumers (RabbitMQ/Kafka)
   - WebSocket Handlers
   - CLI Commands
   - Background Workers/Schedulers

## Implementation Patterns

### Entity Base
```python
# In worker-core
@dataclass(eq=False, slots=True)
class Entity:
    id: UUID = field(default_factory=uuid4)
    
    def __eq__(self, other): ...
    def __hash__(self): ...
```

### Value Object
```python
@dataclass(frozen=True, slots=True)
class Email(ValueObject):
    value: str
    
    def __post_init__(self):
        if not self._is_valid(self.value):
            raise ValueError("Invalid email")
```

### Aggregate Root
```python
class User(AggregateRoot):
    def __init__(self, email: Email, name: str):
        self.email = email
        self.name = name
        self._domain_events: list[DomainEvent] = []
    
    def change_name(self, new_name: str) -> Result[None, DomainError]:
        if not new_name.strip():
            return Result.fail(DomainError("name.empty", "Name cannot be empty"))
        self.name = new_name
        self.add_event(UserNameChanged(self.id, new_name))
        return Result.ok(None)
```

### Domain Event
```python
@dataclass(frozen=True, slots=True)
class UserCreated(DomainEvent):
    user_id: UUID
    email: str
    name: str
```

### Command/Query
```python
# In worker-platform CQRS
class CreateUserCommand(Command[UserId]):
    email: Email
    name: str

class GetUserQuery(Query[UserDTO]):
    user_id: UUID
```

### Handler
```python
class CreateUserHandler:
    def __init__(self, user_repo: UserRepository, event_bus: EventBus):
        self._repo = user_repo
        self._bus = event_bus
    
    async def handle(self, command: CreateUserCommand) -> UserId:
        user = User.create(command.email, command.name)
        await self._repo.add(user)
        await self._bus.publish(user.domain_events)
        return user.id
```

### Repository Interface (Port)
```python
# In Application layer
class UserRepository(Protocol):
    async def add(self, user: User) -> None: ...
    async def get(self, user_id: UUID) -> User | None: ...
    async def save(self, user: User) -> None: ...
```

### Repository Implementation (Adapter)
```python
# In Infrastructure layer
class SqlAlchemyUserRepository(UserRepository):
    def __init__(self, session: AsyncSession):
        self._session = session
    
    async def add(self, user: User) -> None:
        self._session.add(UserModel.from_domain(user))
    
    async def get(self, user_id: UUID) -> User | None:
        model = await self._session.get(UserModel, user_id)
        return model.to_domain() if model else None
```

## Pipeline Behaviors (Cross-cutting)

```python
# Validation Behavior
class ValidationBehavior(PipelineBehavior):
    def __init__(self, validators: dict[type, Validator]):
        self._validators = validators
    
    async def handle(self, request: Request, next_handler: NextHandler):
        validator = self._validators.get(type(request))
        if validator:
            result = await validator.validate(request)
            if not result.is_valid:
                raise ValidationError(result.errors)
        return await next_handler(request)

# Logging Behavior
class LoggingBehavior(PipelineBehavior):
    async def handle(self, request: Request, next_handler: NextHandler):
        logger.info("Handling {request_type}", request_type=type(request).__name__)
        try:
            return await next_handler(request)
        except Exception as e:
            logger.exception("Failed to handle {request_type}", request_type=type(request).__name__)
            raise

# Transaction Behavior
class TransactionBehavior(PipelineBehavior):
    def __init__(self, uow: UnitOfWork):
        self._uow = uow
    
    async def handle(self, request: Request, next_handler: NextHandler):
        async with self._uow:
            result = await next_handler(request)
            await self._uow.commit()
            return result
```

## Service Factory Pattern

```python
# In worker-platform
def create_api_app(
    settings: PlatformSettings,
    *,
    readiness_checks: Iterable[ReadinessCheck] = ()
) -> FastAPI:
    app = FastAPI(...)
    # Middleware order matters (last added = outermost)
    app.add_middleware(SecurityHeadersMiddleware, ...)
    app.add_middleware(TenantContextMiddleware, ...)
    app.add_middleware(CorrelationIdMiddleware)
    app.include_router(health_router)
    return app
```

## Configuration

```python
# Service-specific settings extending PlatformSettings
class IdentityServiceSettings(PlatformSettings):
    service_name: str = "identity-service"
    database_url: PostgresDsn
    redis_url: RedisDsn
    jwt_secret: SecretStr
    jwt_algorithm: str = "RS256"
    token_expiry_minutes: int = 15
```

## Testing Strategy

- **Domain tests**: Pure unit tests, no mocking needed
- **Application tests**: Mock ports (repositories, event bus)
- **Infrastructure tests**: Testcontainers for DB, real Redis/RabbitMQ
- **Presentation tests**: FastAPI TestClient, test full request flow
- **Contract tests**: Pact or similar for API contracts