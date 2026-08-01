"""Worker CLI: Code generation for services, packages, and Clean Architecture components."""

import re
import subprocess
from pathlib import Path
from string import Template

import typer
from rich.console import Console

app = typer.Typer(help="WorkerTransfer Platform CLI")
console = Console()

TEMPLATE_DIR = Path(__file__).parent / "templates"


def _alembic_dir_for(service: str) -> Path:
    """Return the per-service root that must contain an alembic.ini (ADR-0010)."""
    return Path(f"apps/{service}")


@app.command()
def new_service(
    name: str = typer.Argument(..., help="Service name (kebab-case)"),
    template: str = typer.Option("full", help="Template: full, minimal, event-driven"),
    path: str = typer.Option("apps", help="Target directory"),
) -> None:
    """Create a new microservice with Clean Architecture"""
    service_dir = Path(path) / name
    if service_dir.exists():
        console.print(f"[red]Directory {service_dir} already exists[/red]")
        raise typer.Exit(1)

    module_name = name.replace("-", "_")
    service_class = "".join(w.capitalize() for w in name.split("-"))

    context = {
        "service_name": name,
        "module_name": module_name,
        "service_class": service_class,
        "service_title": name.replace("-", " ").title(),
        # Aliases: older templates spelled these in PascalCase. Keeping both
        # spellings means a stale template renders instead of silently emitting
        # a literal "${ServiceClass}" into generated source.
        "ServiceClass": service_class,
        "ModuleName": module_name,
        "ServiceName": name,
    }

    console.print(f"[green]Creating service {name} at {service_dir}[/green]")

    # Create directory structure
    dirs = [
        "src",
        f"src/{module_name}",
        f"src/{module_name}/domain",
        f"src/{module_name}/domain/entities",
        f"src/{module_name}/domain/value_objects",
        f"src/{module_name}/domain/aggregates",
        f"src/{module_name}/domain/events",
        f"src/{module_name}/domain/specifications",
        f"src/{module_name}/domain/exceptions",
        f"src/{module_name}/domain/services",
        f"src/{module_name}/domain/repositories",
        f"src/{module_name}/application",
        f"src/{module_name}/application/commands",
        f"src/{module_name}/application/queries",
        f"src/{module_name}/application/handlers",
        f"src/{module_name}/application/dtos",
        f"src/{module_name}/application/validators",
        f"src/{module_name}/application/behaviors",
        f"src/{module_name}/application/ports",
        f"src/{module_name}/infrastructure",
        f"src/{module_name}/infrastructure/database",
        f"src/{module_name}/infrastructure/messaging",
        f"src/{module_name}/infrastructure/cache",
        f"src/{module_name}/infrastructure/repositories",
        f"src/{module_name}/infrastructure/configurations",
        f"src/{module_name}/infrastructure/external",
        f"src/{module_name}/infrastructure/events",
        f"src/{module_name}/presentation",
        f"src/{module_name}/presentation/http",
        f"src/{module_name}/presentation/middleware",
        f"src/{module_name}/presentation/schemas",
        f"src/{module_name}/presentation/dependencies",
        f"src/{module_name}/presentation/health",
        "migrations",
        "migrations/versions",
        "tests",
        "tests/unit",
        "tests/integration",
        "tests/contract",
    ]

    for d in dirs:
        (service_dir / d).mkdir(parents=True, exist_ok=True)

    # Generate files from templates.
    # Templates live under templates/service/src/... (without the module name);
    # they are rendered into src/{module_name}/... so the package import path is correct.
    template_files = [
        ("pyproject.toml.tmpl", "pyproject.toml"),
        ("Dockerfile.tmpl", "Dockerfile"),
        ("docker-compose.yml.tmpl", "docker-compose.yml"),
        (".env.example.tmpl", ".env.example"),
        ("README.md.tmpl", "README.md"),
        # Package root __init__.py has no template; created as an empty stub below.
        ("__module_init__", f"src/{module_name}/__init__.py"),
        ("src/main.py.tmpl", f"src/{module_name}/main.py"),
        ("src/configuration.py.tmpl", f"src/{module_name}/configuration.py"),
        ("src/domain/__init__.py.tmpl", f"src/{module_name}/domain/__init__.py"),
        ("src/domain/base.py.tmpl", f"src/{module_name}/domain/base.py"),
        ("src/domain/entities/example.py.tmpl", f"src/{module_name}/domain/entities/example.py"),
        (
            "src/application/__init__.py.tmpl",
            f"src/{module_name}/application/__init__.py",
        ),
        (
            "src/application/mediator.py.tmpl",
            f"src/{module_name}/application/mediator.py",
        ),
        (
            "src/application/behaviors.py.tmpl",
            f"src/{module_name}/application/behaviors.py",
        ),
        (
            "src/infrastructure/__init__.py.tmpl",
            f"src/{module_name}/infrastructure/__init__.py",
        ),
        (
            "src/infrastructure/database/__init__.py.tmpl",
            f"src/{module_name}/infrastructure/database/__init__.py",
        ),
        (
            "src/infrastructure/database/models.py.tmpl",
            f"src/{module_name}/infrastructure/database/models.py",
        ),
        (
            "src/infrastructure/database/repositories.py.tmpl",
            f"src/{module_name}/infrastructure/database/repositories.py",
        ),
        (
            "src/infrastructure/database/uow.py.tmpl",
            f"src/{module_name}/infrastructure/database/uow.py",
        ),
        (
            "src/presentation/compose_api.py.tmpl",
            f"src/{module_name}/presentation/compose_api.py",
        ),
        (
            "src/presentation/http/router.py.tmpl",
            f"src/{module_name}/presentation/http/router.py",
        ),
        # Per-service async Alembic (ADR-0010): the "next steps" told the user to
        # run `alembic revision`, but nothing generated an alembic.ini for it.
        ("alembic.ini.tmpl", "alembic.ini"),
        ("migrations/env.py.tmpl", "migrations/env.py"),
        ("migrations/script.py.mako.tmpl", "migrations/script.py.mako"),
        ("tests/test_app.py.tmpl", "tests/test_app.py"),
        # Testcontainers guard (ADR-0011): integration tests self-skip without Docker.
        ("tests/integration/__init__.py.tmpl", "tests/integration/__init__.py"),
        ("tests/integration/_docker.py.tmpl", "tests/integration/_docker.py"),
        ("tests/integration/conftest.py.tmpl", "tests/integration/conftest.py"),
    ]

    for template_name, target_name in template_files:
        target_path = service_dir / target_name
        if template_name == "__module_init__":
            target_path.write_text(f'"""{module_name} package."""\n')
            console.print(f"  [cyan]Created {target_name}[/cyan]")
            continue
        template_path = TEMPLATE_DIR / "service" / template_name
        if template_path.exists():
            _render_template(template_path, target_path, context)
            console.print(f"  [cyan]Created {target_name}[/cyan]")

    console.print(f"[green]✓ Service {name} created successfully![/green]")
    console.print("  Next steps:")
    console.print(f"  1. cd {service_dir}")
    console.print("  2. uv sync")
    console.print("  3. uv run alembic revision --autogenerate -m 'initial'")
    console.print("  4. uv run alembic upgrade head")
    console.print(f"  5. uv run {module_name}")


@app.command()
def new_package(
    name: str = typer.Argument(..., help="Package name (worker-kebab-case)"),
    type: str = typer.Option("shared", help="Type: shared, domain, infrastructure"),
) -> None:
    """Create a new shared package"""
    package_dir = Path("packages") / name
    if package_dir.exists():
        console.print(f"[red]Directory {package_dir} already exists[/red]")
        raise typer.Exit(1)

    module_name = name.replace("-", "_")
    context = {
        "package_name": name,
        "module_name": module_name,
    }

    console.print(f"[green]Creating package {name} at {package_dir}[/green]")

    dirs = [
        f"src/{module_name}",
        "tests",
    ]
    for d in dirs:
        (package_dir / d).mkdir(parents=True, exist_ok=True)

    template_files = [
        ("pyproject.toml.tmpl", "pyproject.toml"),
        ("__init__.py.tmpl", f"src/{module_name}/__init__.py"),
        ("README.md.tmpl", "README.md"),
    ]

    for template_name, target_name in template_files:
        template_path = TEMPLATE_DIR / "package" / template_name
        target_path = package_dir / target_name
        if template_path.exists():
            _render_template(template_path, target_path, context)
            console.print(f"  [cyan]Created {target_name}[/cyan]")

    console.print(f"[green]✓ Package {name} created successfully![/green]")


@app.command()
def command(
    name: str = typer.Argument(..., help="Command name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    handler: bool = typer.Option(True, help="Generate handler"),
) -> None:
    """Generate CQRS command + handler"""
    _generate_cqrs("command", name, service, handler)


@app.command()
def query(
    name: str = typer.Argument(..., help="Query name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    handler: bool = typer.Option(True, help="Generate handler"),
) -> None:
    """Generate CQRS query + handler"""
    _generate_cqrs("query", name, service, handler)


@app.command()
def entity(
    name: str = typer.Argument(..., help="Entity name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    fields: str = typer.Option("", help="Fields as name:type (comma-separated)"),
) -> None:
    """Generate domain entity"""
    _generate_domain("entity", name, service, fields)


@app.command()
def aggregate(
    name: str = typer.Argument(..., help="Aggregate name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
) -> None:
    """Generate aggregate root"""
    _generate_domain("aggregate", name, service, "")


@app.command()
def valueobject(
    name: str = typer.Argument(..., help="Value object name (PascalCase)"),
    service: str = typer.Option(..., help="Target service or package"),
    fields: str = typer.Option(..., help="Fields as name:type (comma-separated)"),
) -> None:
    """Generate value object"""
    target = (
        Path(f"apps/{service}") if Path(f"apps/{service}").exists() else Path(f"packages/{service}")
    )
    _generate_domain("valueobject", name, str(target), fields)


@app.command()
def event(
    name: str = typer.Argument(..., help="Event name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    type: str = typer.Option("domain", help="Type: domain, integration, application"),
) -> None:
    """Generate domain/integration event"""
    _generate_domain("event", name, service, type)


@app.command()
def consumer(
    name: str = typer.Argument(..., help="Consumer name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    event: str = typer.Option(..., help="Event to consume"),
) -> None:
    """Generate message consumer"""
    _generate_infrastructure("consumer", name, service, event)


@app.command()
def publisher(
    name: str = typer.Argument(..., help="Publisher name (PascalCase)"),
    service: str = typer.Option(..., help="Target service"),
    event: str = typer.Option(..., help="Event to publish"),
) -> None:
    """Generate message publisher"""
    _generate_infrastructure("publisher", name, service, event)


@app.command()
def migrate(
    message: str = typer.Argument(..., help="Migration message"),
    service: str = typer.Option(..., help="Target service"),
) -> None:
    """Create Alembic migration"""
    service_dir = _alembic_dir_for(service)
    if not (service_dir / "alembic.ini").is_file():
        console.print(
            f"[red]No alembic.ini at {service_dir}/alembic.ini[/red] "
            "(per-service Alembic, see ADR-0010): run `worker new-service`"
        )
        raise typer.Exit(1)
    # S603/S607: fixed argv, no shell=True; `uv` is resolved from PATH on
    # purpose so the project's own venv provides alembic (ADR-0010).
    result = subprocess.run(  # noqa: S603
        ["uv", "run", "alembic", "revision", "--autogenerate", "-m", message],  # noqa: S607
        cwd=str(service_dir),
    )
    if result.returncode == 0:
        console.print("[green]Migration created successfully![/green]")
    else:
        console.print("[red]Migration creation failed[/red]")


@app.command()
def upgrade(
    service: str = typer.Option(..., help="Target service"),
    revision: str = typer.Option("head", help="Target revision"),
) -> None:
    """Run database migrations"""
    service_dir = _alembic_dir_for(service)
    if not (service_dir / "alembic.ini").is_file():
        console.print(
            f"[red]No alembic.ini at {service_dir}/alembic.ini[/red] "
            "(per-service Alembic, see ADR-0010)."
        )
        raise typer.Exit(1)
    # S603/S607: fixed argv, no shell=True; `uv` is resolved from PATH on
    # purpose so the project's own venv provides alembic (ADR-0010).
    result = subprocess.run(  # noqa: S603
        ["uv", "run", "alembic", "upgrade", revision],  # noqa: S607
        cwd=str(service_dir),
    )
    if result.returncode == 0:
        console.print("[green]Migrations applied successfully![/green]")
    else:
        console.print("[red]Migration failed[/red]")


def _generate_cqrs(type: str, name: str, service: str, handler: bool) -> None:
    service_path = Path(f"apps/{service}")
    if not service_path.exists():
        console.print(f"[red]Service {service} not found[/red]")
        raise typer.Exit(1)

    module_name = service.replace("-", "_")
    context = {
        "name": name,
        "service": service,
        "module_name": module_name,
    }

    if type == "command":
        target_dir = service_path / f"src/{module_name}/application/commands"
        template = TEMPLATE_DIR / "cqrs" / "command.py.tmpl"
        handler_template = TEMPLATE_DIR / "cqrs" / "command_handler.py.tmpl"
    else:
        target_dir = service_path / f"src/{module_name}/application/queries"
        template = TEMPLATE_DIR / "cqrs" / "query.py.tmpl"
        handler_template = TEMPLATE_DIR / "cqrs" / "query_handler.py.tmpl"

    target_dir.mkdir(parents=True, exist_ok=True)

    command_file = target_dir / f"{name.lower()}.py"
    _render_template(template, command_file, context)
    console.print(f"[cyan]Created {command_file}[/cyan]")

    if handler:
        handler_dir = service_path / f"src/{module_name}/application/handlers"
        handler_dir.mkdir(parents=True, exist_ok=True)
        handler_file = handler_dir / f"{name.lower()}_handler.py"
        _render_template(handler_template, handler_file, context)
        console.print(f"[cyan]Created {handler_file}[/cyan]")


def _generate_domain(type: str, name: str, target: str, extra: str) -> None:
    target_path = Path(target)
    if not target_path.exists():
        console.print(f"[red]Target {target} not found[/red]")
        raise typer.Exit(1)

    module_name = target_path.name.replace("-", "_")
    context = {
        "name": name,
        "module_name": module_name,
        "extra": extra,
    }

    if type == "entity":
        target_dir = target_path / f"src/{module_name}/domain/entities"
        template = TEMPLATE_DIR / "domain" / "entity.py.tmpl"
    elif type == "aggregate":
        target_dir = target_path / f"src/{module_name}/domain/aggregates"
        template = TEMPLATE_DIR / "domain" / "aggregate.py.tmpl"
    elif type == "valueobject":
        target_dir = target_path / f"src/{module_name}/domain/value_objects"
        template = TEMPLATE_DIR / "domain" / "valueobject.py.tmpl"
    elif type == "event":
        target_dir = target_path / f"src/{module_name}/domain/events"
        template = TEMPLATE_DIR / "domain" / f"{extra}_event.py.tmpl"

    target_dir.mkdir(parents=True, exist_ok=True)
    target_file = target_dir / f"{name.lower()}.py"
    _render_template(template, target_file, context)
    console.print(f"[cyan]Created {target_file}[/cyan]")


def _generate_infrastructure(type: str, name: str, service: str, event: str) -> None:
    service_path = Path(f"apps/{service}")
    module_name = service.replace("-", "_")
    context = {
        "name": name,
        "event": event,
        "service": service,
        "module_name": module_name,
    }

    if type == "consumer":
        target_dir = service_path / f"src/{module_name}/infrastructure/messaging"
        template = TEMPLATE_DIR / "infrastructure" / "consumer.py.tmpl"
    else:
        target_dir = service_path / f"src/{module_name}/infrastructure/messaging"
        template = TEMPLATE_DIR / "infrastructure" / "publisher.py.tmpl"

    target_dir.mkdir(parents=True, exist_ok=True)
    target_file = target_dir / f"{name.lower()}.py"
    _render_template(template, target_file, context)
    console.print(f"[cyan]Created {target_file}[/cyan]")


# Only Python output is checked for leftover placeholders. Compose files and
# Dockerfiles use ${VAR} as *runtime* interpolation (docker compose substitutes
# it from the environment), and alembic's script.py.mako keeps its Mako
# placeholders until a revision is actually generated. In a .py file, however, a
# surviving ${...} is a syntax error.
_UNRESOLVED_PLACEHOLDER = re.compile(r"\$\{[A-Za-z_][A-Za-z0-9_]*\}")


def _render_template(template_path: Path, target_path: Path, context: dict[str, str]) -> None:
    content = template_path.read_text()
    rendered = Template(content).safe_substitute(context)

    # safe_substitute leaves unknown placeholders verbatim, which used to emit
    # literal "${ServiceClass}" into generated Python — the scaffold could not
    # even be parsed. Refuse to write such a file instead of shipping it.
    if target_path.suffix == ".py":
        leftovers = sorted(set(_UNRESOLVED_PLACEHOLDER.findall(rendered)))
        if leftovers:
            raise RuntimeError(
                f"{template_path.name}: unresolved placeholders {leftovers}. "
                "Add them to the render context or fix the template."
            )

    target_path.parent.mkdir(parents=True, exist_ok=True)
    target_path.write_text(rendered)


if __name__ == "__main__":
    app()
