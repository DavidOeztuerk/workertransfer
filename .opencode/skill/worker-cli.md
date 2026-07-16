# Skill: Worker CLI & Platform Builder

## Purpose
Build a CLI tool (`worker`) for generating services, packages, and boilerplate code, plus a `PlatformBuilder` API for composing services from shared packages.

## CLI Structure

```python
# packages/worker-cli/src/worker_cli/main.py
import typer
from rich.console import Console
from rich.prompt import Prompt, Confirm

app = typer.Typer(help="WorkerTransfer Platform CLI")
console = Console()

@app.command()
def new_service(
    name: str = typer.Argument(..., help="Service name (kebab-case)"),
    template: str = typer.Option("full", help="Template: full, minimal, event-driven"),
    path: str = typer.Option("apps", help="Target directory"),
):
    """Create a new microservice with Clean Architecture"""
    generator = ServiceGenerator(Path(path))
    generator.generate(name, template)
    console.print(f"[green]✓ Created service {name} at {path}/{name}[/green]")

@app.command()
def new_package(
    name: str = typer.Argument(..., help="Package name (worker-kebab-case)"),
    type: str = typer.Option("shared", help="Type: shared, domain, infrastructure"),
):
    """Create a new shared package"""
    generator = PackageGenerator(Path("packages"))
    generator.generate(name, type)
    console.print(f"[green]✓ Created package {name}[/green]")

@app.command()
def command(
    name: str = typer.Argument(..., help="Command name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    handler: bool = typer.Option(True, help="Generate handler"),
):
    """Generate CQRS command + handler"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_command(name, handler)
    console.print(f"[green]✓ Generated command {name}[/green]")

@app.command()
def query(
    name: str = typer.Argument(..., help="Query name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    handler: bool = typer.Option(True, help="Generate handler"),
):
    """Generate CQRS query + handler"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_query(name, handler)
    console.print(f"[green]✓ Generated query {name}[/green]")

@app.command()
def entity(
    name: str = typer.Argument(..., help="Entity name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    fields: str = typer.Option("", help="Fields as name:type (comma-separated)"),
):
    """Generate domain entity"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_entity(name, fields)
    console.print(f"[green]✓ Generated entity {name}[/green]")

@app.command()
def aggregate(
    name: str = typer.Argument(..., help="Aggregate name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
):
    """Generate aggregate root"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_aggregate(name)
    console.print(f"[green]✓ Generated aggregate {name}[/green]")

@app.command()
def valueobject(
    name: str = typer.Argument(..., help="Value object name (PascalCase)"),
    service: str = typer.Option(..., help="Target service or package"),
    fields: str = typer.Option(..., help="Fields as name:type (comma-separated)"),
):
    """Generate value object"""
    target = Path(f"apps/{service}") if (Path(f"apps/{service}").exists()) else Path(f"packages/{service}")
    gen = CodeGenerator(target)
    gen.generate_value_object(name, fields)
    console.print(f"[green]✓ Generated value object {name}[/green]")

@app.command()
def event(
    name: str = typer.Argument(..., help="Event name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    type: str = typer.Option("domain", help="Type: domain, integration, application"),
):
    """Generate domain/integration event"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_event(name, type)
    console.print(f"[green]✓ Generated event {name}[/green]")

@app.command()
def consumer(
    name: str = typer.Argument(..., help="Consumer name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    event: str = typer.Option(..., help="Event to consume"),
):
    """Generate message consumer"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_consumer(name, event)
    console.print(f"[green]✓ Generated consumer {name}[/green]")

@app.command()
def publisher(
    name: str = typer.Argument(..., help="Publisher name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    event: str = typer.Option(..., help="Event to publish"),
):
    """Generate message publisher"""
    gen = CodeGenerator(Path(f"apps/{service}"))
    gen.generate_publisher(name, event)
    console.print(f"[green]✓ Generated publisher {name}[/green]")

@app.command()
def migrate(
    message: str = typer.Argument(..., help="Migration message"),
    service: str = typer.Option(..., help="Target service"),
):
    """Create Alembic migration"""
    subprocess.run(["uv", "run", "alembic", "revision", "--autogenerate", "-m", message], cwd=f"apps/{service}")
    console.print(f"[green]✓ Created migration[/green]")

@app.command()
def upgrade(
    service: str = typer.Option(..., help="Target service"),
    revision: str = typer.Option("head", help="Target revision"),
):
    """Run migrations"""
    subprocess.run(["uv", "run", "alembic", "upgrade", revision], cwd=f"apps/{service}")
    console.print(f"[green]✓ Ran migrations[/green]")

if __name__ == "__main__":
    app()
```

## Service Generator

```python
# packages/worker-cli/src/worker_cli/generators/service.py
from pathlib import Path
from string import Template
import shutil

class ServiceGenerator:
    TEMPLATES_DIR = Path(__file__).parent.parent / "templates" / "service"
    
    def __init__(self, target_dir: Path):
        self.target_dir = target_dir
    
    def generate(self, name: str, template: str = "full") -> None:
        service_dir = self.target_dir / name
        service_dir.mkdir(parents=True, exist_ok=False)
        
        # Module name conversion
        module_name = name.replace("-", "_")
        
        context = {
            "service_name": name,
            "module_name": module_name,
            "service_class": "".join(w.capitalize() for w in name.split("-")),
            "service_title": name.replace("-", " ").title(),
        }
        
        # Generate from templates
        self._render_template("pyproject.toml.tmpl", service_dir / "pyproject.toml", context)
        self._render_template("dockerfile.tmpl", service_dir / "Dockerfile", context)
        self._render_template("docker-compose.yml.tmpl", service_dir / "docker-compose.yml", context)
        self._render_template(".env.example.tmpl", service_dir / ".env.example", context)
        self._render_template("README.md.tmpl", service_dir / "README.md", context)
        
        # Source structure
        src_dir = service_dir / "src" / module_name
        self._create_structure(src_dir, module_name, template, context)
        
        # Tests
        tests_dir = service_dir / "tests"
        self._create_tests(tests_dir, module_name, context)
        
        # Alembic
        self._create_alembic(service_dir, context)
    
    def _create_structure(self, src_dir: Path, module_name: str, template: str, context: dict) -> None:
        # Create directories
        for layer in ["domain", "application", "infrastructure", "presentation"]:
            (src_dir / layer).mkdir(parents=True, exist_ok=True)
        
        # Core files
        self._render_template("src/__init__.py.tmpl", src_dir / "__init__.py", context)
        self._render_template("src/main.py.tmpl", src_dir / "main.py", context)
        self._render_template("src/configuration.py.tmpl", src_dir / "configuration.py", context)
        
        if template in ["full", "event-driven"]:
            self._create_cqrs_structure(src_dir, context)
            self._create_domain_structure(src_dir, context)
            self._create_infrastructure_structure(src_dir, context)
            self._create_presentation_structure(src_dir, context)
    
    def _create_cqrs_structure(self, src_dir: Path, context: dict) -> None:
        app_dir = src_dir / "application"
        for subdir in ["commands", "queries", "handlers", "dtos", "validators", "behaviors", "ports"]:
            (app_dir / subdir).mkdir(parents=True, exist_ok=True)
        
        self._render_template("src/application/__init__.py.tmpl", app_dir / "__init__.py", context)
        self._render_template("src/application/mediator.py.tmpl", app_dir / "mediator.py", context)
        self._render_template("src/application/behaviors.py.tmpl", app_dir / "behaviors.py", context)
    
    def _create_domain_structure(self, src_dir: Path, context: dict) -> None:
        domain_dir = src_dir / "domain"
        for subdir in ["entities", "value_objects", "aggregates", "events", "specifications", "exceptions", "services", "repositories"]:
            (domain_dir / subdir).mkdir(parents=True, exist_ok=True)
        
        self._render_template("src/domain/__init__.py.tmpl", domain_dir / "__init__.py", context)
        self._render_template("src/domain/base.py.tmpl", domain_dir / "base.py", context)
    
    def _create_infrastructure_structure(self, src_dir: Path, context: dict) -> None:
        infra_dir = src_dir / "infrastructure"
        for subdir in ["database", "messaging", "cache", "repositories", "configurations", "external", "events"]:
            (infra_dir / subdir).mkdir(parents=True, exist_ok=True)
        
        self._render_template("src/infrastructure/__init__.py.tmpl", infra_dir / "__init__.py", context)
        self._render_template("src/infrastructure/database/__init__.py.tmpl", infra_dir / "database" / "__init__.py", context)
        self._render_template("src/infrastructure/database/models.py.tmpl", infra_dir / "database" / "models.py", context)
        self._render_template("src/infrastructure/database/repositories.py.tmpl", infra_dir / "database" / "repositories.py", context)
        self._render_template("src/infrastructure/database/uow.py.tmpl", infra_dir / "database" / "uow.py", context)
    
    def _create_presentation_structure(self, src_dir: Path, context: dict) -> None:
        pres_dir = src_dir / "presentation"
        for subdir in ["http", "middleware", "schemas", "dependencies", "health"]:
            (pres_dir / subdir).mkdir(parents=True, exist_ok=True)
        
        self._render_template("src/presentation/__init__.py.tmpl", pres_dir / "__init__.py", context)
        self._render_template("src/presentation/http/__init__.py.tmpl", pres_dir / "http" / "__init__.py", context)
        self._render_template("src/presentation/http/router.py.tmpl", pres_dir / "http" / "router.py", context)
        self._render_template("src/presentation/health.py.tmpl", pres_dir / "health.py", context)
    
    def _render_template(self, template_name: str, target: Path, context: dict) -> None:
        template_path = self.TEMPLATES_DIR / template_name
        if template_path.exists():
            content = Template(template_path.read_text()).safe_substitute(context)
            target.write_text(content)
```

## Platform Builder API

```python
# worker_platform/builder.py
from typing import Self
from worker_platform.configuration import PlatformSettings
from worker_platform.presentation.app import create_api_app
from fastapi import FastAPI

class PlatformBuilder:
    """Fluid API for composing services from shared packages"""
    
    def __init__(self, settings: PlatformSettings | None = None):
        self._settings = settings or PlatformSettings()
        self._features: dict[str, bool] = {}
        self._custom_middleware: list = []
        self._custom_routers: list = []
        self._readiness_checks: list = []
        self._startup_tasks: list = []
        self._shutdown_tasks: list = []
    
    def add_configuration(self) -> Self:
        self._features["configuration"] = True
        return self
    
    def add_logging(self, json_format: bool = True, level: str = "INFO") -> Self:
        self._features["logging"] = True
        self._logging_config = {"json": json_format, "level": level}
        return self
    
    def add_database(self, url: str | None = None, **kwargs) -> Self:
        self._features["database"] = True
        self._database_config = {"url": url, **kwargs}
        return self
    
    def add_cache(self, redis_url: str | None = None, **kwargs) -> Self:
        self._features["cache"] = True
        self._cache_config = {"redis_url": redis_url, **kwargs}
        return self
    
    def add_authentication(self, **kwargs) -> Self:
        self._features["auth"] = True
        self._auth_config = kwargs
        return self
    
    def add_authorization(self, model_path: str | None = None, **kwargs) -> Self:
        self._features["authz"] = True
        self._authz_config = {"model_path": model_path, **kwargs}
        return self
    
    def add_multitenancy(self, resolver: str = "header", **kwargs) -> Self:
        self._features["multitenancy"] = True
        self._tenant_config = {"resolver": resolver, **kwargs}
        return self
    
    def add_correlation(self) -> Self:
        self._features["correlation"] = True
        return self
    
    def add_healthchecks(self, checks: list = None) -> Self:
        self._features["health"] = True
        self._readiness_checks = checks or []
        return self
    
    def add_metrics(self, **kwargs) -> Self:
        self._features["metrics"] = True
        self._metrics_config = kwargs
        return self
    
    def add_tracing(self, **kwargs) -> Self:
        self._features["tracing"] = True
        self._tracing_config = kwargs
        return self
    
    def add_rate_limiting(self, **kwargs) -> Self:
        self._features["ratelimit"] = True
        self._ratelimit_config = kwargs
        return self
    
    def add_resilience(self, **kwargs) -> Self:
        self._features["resilience"] = True
        self._resilience_config = kwargs
        return self
    
    def add_cqrs(self, **kwargs) -> Self:
        self._features["cqrs"] = True
        self._cqrs_config = kwargs
        return self
    
    def add_events(self, **kwargs) -> Self:
        self._features["events"] = True
        self._events_config = kwargs
        return self
    
    def add_messaging(self, broker: str = "rabbitmq", **kwargs) -> Self:
        self._features["messaging"] = True
        self._messaging_config = {"broker": broker, **kwargs}
        return self
    
    def add_storage(self, provider: str = "s3", **kwargs) -> Self:
        self._features["storage"] = True
        self._storage_config = {"provider": provider, **kwargs}
        return self
    
    def add_security(self, **kwargs) -> Self:
        self._features["security"] = True
        self._security_config = kwargs
        return self
    
    def add_middleware(self, middleware_class, **kwargs) -> Self:
        self._custom_middleware.append((middleware_class, kwargs))
        return self
    
    def add_router(self, router, prefix: str = "", tags: list[str] = None) -> Self:
        self._custom_routers.append((router, prefix, tags or []))
        return self
    
    def add_startup_task(self, task) -> Self:
        self._startup_tasks.append(task)
        return self
    
    def add_shutdown_task(self, task) -> Self:
        self._shutdown_tasks.append(task)
        return self
    
    def build(self) -> FastAPI:
        """Build the complete FastAPI application"""
        # Create base app with platform features
        app = create_api_app(
            self._settings,
            readiness_checks=self._readiness_checks,
        )
        
        # Add custom middleware (in reverse order for correct stacking)
        for middleware_class, kwargs in reversed(self._custom_middleware):
            app.add_middleware(middleware_class, **kwargs)
        
        # Include custom routers
        for router, prefix, tags in self._custom_routers:
            app.include_router(router, prefix=prefix, tags=tags)
        
        # Register startup/shutdown events
        @app.on_event("startup")
        async def startup():
            for task in self._startup_tasks:
                await task() if callable(task) else task
        
        @app.on_event("shutdown")
        async def shutdown():
            for task in self._shutdown_tasks:
                await task() if callable(task) else task
        
        return app
```

## Usage in Service

```python
# apps/identity-service/src/identity_service/main.py
from worker_platform.builder import PlatformBuilder
from worker_platform.configuration import PlatformSettings
from identity_service.configuration import IdentityServiceSettings
from identity_service.presentation.http import router as api_router

def create_app(settings: IdentityServiceSettings | None = None) -> FastAPI:
    settings = settings or IdentityServiceSettings()
    
    return (
        PlatformBuilder(PlatformSettings(
            service_name="identity-service",
            service_version="0.1.0",
            environment=settings.environment,
            host=settings.host,
            port=settings.port,
        ))
        .add_configuration()
        .add_logging(json_format=True)
        .add_database(url=settings.database_url)
        .add_cache(redis_url=settings.redis_url)
        .add_authentication(
            jwt_secret=settings.jwt_secret.get_secret_value(),
            jwt_algorithm=settings.jwt_algorithm,
        )
        .add_authorization(model_path="auth_model.conf")
        .add_multitenancy(resolver="claim")
        .add_correlation()
        .add_healthchecks([DatabaseCheck(), RedisCheck(), RabbitMQCheck()])
        .add_metrics()
        .add_tracing()
        .add_rate_limiting(requests=100, window=60)
        .add_resilience()
        .add_cqrs()
        .add_events()
        .add_messaging(broker="rabbitmq", url=settings.rabbitmq_url)
        .add_router(api_router, prefix="/api/v1", tags=["identity"])
        .build()
    )
```