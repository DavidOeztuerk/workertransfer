"""Datenbank-Schicht von portfolio-service.

Absichtlich leer. Die frühere Fassung brachte hier eine zweite `Base`, eigene
Mixins, `DatabaseSettings` und eine `UnitOfWork` unter — alles doppelt: Base und
Mixins stehen in `base.py` (ADR-0016), die Einstellungen kommen aus
`configuration.py`, und die UnitOfWork liefert `worker_database`. Duplikate
laufen auseinander; genau das dokumentieren ADR-0005 für `worker-cqrs` und
ADR-0014 für `worker-exceptions`.
"""
