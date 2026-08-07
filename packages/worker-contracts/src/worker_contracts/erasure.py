"""Der Löschbefehl — und wie wenig er trägt (ADR-0027 §4).

Eine Kennung, sonst nichts. Das ist kein Sparzwang, sondern die Sache selbst:
ein Löschbefehl **hat** keinen Inhalt. Genau deshalb passt er in die Outbox,
deren Tabelle nur `user_id` und `kind` führt (ADR-0025 §5) — dort ist der
schmale Vertrag ein Glücksfall statt eines Kompromisses.

Kein `reason`. Von einem Menschen, der sein Konto löschen will, eine Begründung
zu verlangen, ist ein Hebel gegen ihn (ADR-0027 §Kontext 5) — und der Freitext
wäre ausgerechnet das Einzige, das danach wieder gelöscht werden müsste.
"""

from __future__ import annotations

from uuid import UUID

from pydantic import BaseModel

__all__ = ["CompanyWithdrawalV1", "ErasureResultV1", "ErasureV1"]


class ErasureV1(BaseModel):
    """„Lösche alles, was du über diesen Menschen hältst."

    An jeden Empfänger derselbe Rumpf. Was das für den einzelnen Dienst heißt,
    weiß der Dienst — der Ursprung schreibt es ihm nicht vor, sonst müsste
    identity-service wissen, welche Tabellen anderswo stehen (ADR-0004).
    """

    user_id: UUID


class ErasureResultV1(BaseModel):
    """Die Quittung. `retained` ist in der Voreinstellung immer 0.

    ADR-0027 §3: die Voreinstellung löscht vollständig, auch eingestellte
    Bewerbungen und bezahlte Transfers. Nur ein umgelegter
    Aufbewahrungsschalter lässt etwas stehen — und dann soll der Ursprung es
    erfahren, statt es zu vermuten. „Ausgesetzt ist nicht übersprungen."
    """

    retained: int = 0


class CompanyWithdrawalV1(BaseModel):
    """„Zieh die Anzeigen dieses Unternehmens zurück."

    Der Sonderfall des letzten Admins (ADR-0027 §7) — eine Absicht über ein
    **Unternehmen**, nicht über einen Menschen, und deshalb ein eigener Vertrag
    mit `tenant_id` statt `user_id`. Sie zählt ausdrücklich NICHT in den
    Vollständigkeitsnachweis der Löschung: sonst hielte ein stiller jobs-service
    die Löschung eines Menschen offen.
    """

    tenant_id: UUID
