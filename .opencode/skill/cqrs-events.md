# Skill: CQRS & Event-Driven Architecture

## Purpose
Build a complete CQRS framework with command/query separation, pipeline behaviors, event sourcing support, and event-driven communication.

## Core Components (in worker-platform)

### 1. Mediator (Dispatch)
```python
# application/cqrs.py
class Mediator:
    def __init__(self):
        self._command_handlers: dict[type[Command], Handler] = {}
        self._query_handlers: dict[type[Query], Handler] = {}
        self._behaviors: list[PipelineBehavior] = []
    
    def register_command_handler(self, cmd_type: type[Command], handler: Handler): ...
    def register_query_handler(self, query_type: type[Query], handler: Handler): ...
    def add_behavior(self, behavior: PipelineBehavior): ...
    
    async def send_command[T](self, command: Command[T]) -> T: ...
    async def send_query[T](self, query: Query[T]) -> T: ...
```

### 2. Pipeline Behaviors (Middleware)
```python
# Ordered execution (first added = outermost)
behaviors = [
    LoggingBehavior(),           # 1. Log entry/exit
    CorrelationBehavior(),       # 2. Ensure correlation ID
    ValidationBehavior(),        # 3. Validate request
    AuthorizationBehavior(),     # 4. Check permissions
    TransactionBehavior(),       # 5. DB transaction
    CachingBehavior(),           # 6. Cache queries
    RetryBehavior(),             # 7. Retry transient failures
]
```

### 3. Domain Events
```python
# Domain raises events
class User(AggregateRoot):
    def change_email(self, new_email: Email) -> Result:
        self.email = new_email
        self.add_event(EmailChanged(self.id, new_email))
        return Result.ok(None)

# Application publishes after transaction
class ChangeEmailHandler:
    async def handle(self, cmd: ChangeEmailCommand):
        user = await self._repo.get(cmd.user_id)
        result = user.change_email(cmd.new_email)
        if result.is_success:
            await self._repo.save(user)
            await self._event_bus.publish(user.domain_events)  # After commit
```

### 4. Integration Events (Cross-service)
```python
# contracts/events.py (shared package)
@dataclass(frozen=True)
class UserEmailChanged(IntegrationEvent):
    user_id: UUID
    old_email: str
    new_email: str
    changed_at: datetime

# Published to message broker
class RabbitMQEventBus(EventBus):
    async def publish(self, events: list[IntegrationEvent]) -> None:
        for event in events:
            await self._channel.publish(
                exchange="domain.events",
                routing_key=f"user.{type(event).__name__}",
                message=Message(json.dumps(asdict(event)).encode())
            )
```

## Outbox Pattern (Reliable Publishing)

### Database Outbox Table
```sql
CREATE TABLE outbox (
    id UUID PRIMARY KEY,
    event_type VARCHAR(255) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    processed_at TIMESTAMP WITH TIME ZONE,
    retry_count INT DEFAULT 0
);
```

### Outbox Publisher
```python
class OutboxPublisher:
    async def publish_pending(self, batch_size: int = 100) -> int:
        async with self._uow:
            events = await self._outbox_repo.get_unpublished(batch_size)
            for event in events:
                try:
                    await self._message_bus.publish(event)
                    await self._outbox_repo.mark_published(event.id)
                except Exception:
                    await self._outbox_repo.increment_retry(event.id)
            await self._uow.commit()
```

## Event Consumers

```python
class UserEmailChangedConsumer:
    def __init__(self, mediator: Mediator):
        self._mediator = mediator
    
    async def handle(self, message: Message) -> None:
        event = UserEmailChanged.from_json(message.body)
        # Transform to internal command if needed
        await self._mediator.send_command(NotifyEmailChangedCommand(
            user_id=event.user_id,
            new_email=event.new_email
        ))
```

## Saga Pattern (Distributed Transactions)

```python
class TransferSaga:
    """Orchestrates multi-service transfer process"""
    
    async def start(self, transfer_id: UUID) -> None:
        await self._mediator.send_command(ReserveTransferCommand(transfer_id))
    
    @event_handler(TransferReserved)
    async def on_reserved(self, event: TransferReserved) -> None:
        await self._mediator.send_command(NotifyCurrentEmployerCommand(event.transfer_id))
    
    @event_handler(CurrentEmployerNotified)
    async def on_employer_notified(self, event: CurrentEmployerNotified) -> None:
        await self._mediator.send_command(CreateContractCommand(event.transfer_id))
    
    @event_handler(ContractCreated)
    async def on_contract_created(self, event: ContractCreated) -> None:
        await self._mediator.send_command(CompleteTransferCommand(event.transfer_id))
    
    @event_handler(TransferFailed)
    async def on_failed(self, event: TransferFailed) -> None:
        await self._mediator.send_command(CompensateTransferCommand(event.transfer_id))
```

## Idempotency

```python
class IdempotencyBehavior(PipelineBehavior):
    def __init__(self, cache: Cache, ttl: int = 3600):
        self._cache = cache
        self._ttl = ttl
    
    async def handle(self, request: Request, next_handler: NextHandler):
        if not isinstance(request, Command):
            return await next_handler(request)
        
        key = f"idempotency:{type(request).__name__}:{request.idempotency_key}"
        cached = await self._cache.get(key)
        if cached:
            return cached
        
        result = await next_handler(request)
        await self._cache.set(key, result, ttl=self._ttl)
        return result
```

## Testing CQRS

```python
# Unit test handler
async def test_create_user_handler():
    repo = MockUserRepository()
    bus = MockEventBus()
    handler = CreateUserHandler(repo, bus)
    
    result = await handler.handle(CreateUserCommand(email="test@test.com", name="Test"))
    
    assert result.is_success
    assert len(repo.saved) == 1
    assert len(bus.published) == 1
    assert isinstance(bus.published[0], UserCreated)

# Integration test with real mediator
async def test_mediator_pipeline():
    mediator = Mediator()
    mediator.register_command_handler(CreateUserCommand, CreateUserHandler(repo, bus))
    mediator.add_behavior(ValidationBehavior(validators))
    mediator.add_behavior(LoggingBehavior())
    
    result = await mediator.send_command(CreateUserCommand(...))
    assert result.is_success
```