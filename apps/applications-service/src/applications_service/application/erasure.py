"""Was „löschen" hier heißt — und der Schalter, der auf AUS steht (ADR-0027 §3).

**Die Voreinstellung löscht vollständig, auch `status = 'hired'`.** Das ist die
tragende Entscheidung dieses Dienstes und eine Entscheidung über die
*Beweislast*: nicht die Löschung muss sich rechtfertigen, sondern das Behalten.

Die Einschätzung, die sie trägt (Auftraggeber, 06.08.2026, ausdrücklich als
Einschätzung und nicht als Rechtsrat): die Plattform ist nicht der Arbeitgeber.
Aufbewahrungspflichten für Arbeitsverträge treffen das Unternehmen mit *seinen
eigenen* Unterlagen, nicht einen Vermittler. Für Bewerbungsdaten gilt eher das
Gegenteil einer Aufbewahrungspflicht: eine kurze Karenz wegen der AGG-Klagefrist,
danach ist zu löschen.

Offen ist genau ein anwaltlich zu bestätigender Satz. Er ändert nicht diesen
Entwurf, sondern nur die Stellung des Schalters — und blockiert deshalb nichts.

**Was das für das Unternehmen heißt, offen gesagt:** löscht ein Mensch sein
Konto, verschwindet auch die Bewerbung, über die er eingestellt wurde, aus der
Liste des Unternehmens. Das ist gewollt.
"""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import ColumnElement, delete, func, select
from sqlalchemy.ext.asyncio import AsyncSession

from applications_service.domain.application import ApplicationStatus
from applications_service.infrastructure.database.models import OUTBOX, ApplicationModel

__all__ = ["RETAIN_HIRED_APPLICATIONS", "erase_subject"]

#: **AUS.** Die Voreinstellung löscht auch eingestellte Bewerbungen (ADR-0027 §3).
#:
#: Eine benannte Konstante und **kein Konfigurationswert**: bei einem
#: Löschversprechen wäre „in Produktion anders als im Test" der schlimmste
#: denkbare Zustand. Ihn umzulegen ist ein sichtbarer Commit, den jemand
#: begründen muss — nicht eine Umgebungsvariable, die niemand liest.
#:
#: Er schaltet **genau eine Zeilenklasse in diesem Dienst**: `status = 'hired'`.
#: Keine Ausdehnung auf `rejected` (eine abgelehnte Bewerbung begründet nichts)
#: und kein „laufender Vorgang" als Gummiwort.
#:
#: Und keine Frist, in keiner Richtung: weder eine geratene Dauer noch ein
#: Nachlauf, der später aufräumt. Wird der Schalter je umgelegt, kommt die Frist
#: *zusammen mit der Antwort*, nicht vorher.
RETAIN_HIRED_APPLICATIONS = False


async def erase_subject(session: AsyncSession, subject_id: UUID) -> int:
    """Löscht die Bewerbungen dieses Menschen und gibt zurück, was stehen blieb.

    In der Voreinstellung immer 0. Nur bei umgelegtem Schalter bleiben die
    `hired`-Zeilen — und dann soll der Ursprung es erfahren, statt es zu
    vermuten: ausgesetzt ist nicht übersprungen (ADR-0027 §3.4).
    """
    mine = ApplicationModel.subject_id == subject_id
    # Absichtlich zur Laufzeit gelesen, nicht beim Import gebunden: sonst wäre
    # der Schalter im Test nicht umlegbar, und die Abgrenzung aus §3.2 könnte
    # niemand prüfen.
    keep_hired = RETAIN_HIRED_APPLICATIONS

    retained = 0
    condition: ColumnElement[bool]
    if keep_hired:
        retained = (
            await session.execute(
                select(func.count())
                .select_from(ApplicationModel)
                .where(mine, ApplicationModel.status == ApplicationStatus.HIRED.value)
            )
        ).scalar_one()

    # Ausgeschrieben statt als Ternär: das ist die Zeile, die entscheidet, ob
    # eine eingestellte Bewerbung fällt. Wer sie überfliegt, soll sehen, was
    # sie tut — nicht Operatorvorrang nachschlagen müssen.
    if keep_hired:
        condition = mine & (ApplicationModel.status != ApplicationStatus.HIRED.value)
    else:
        condition = mine
    await session.execute(delete(ApplicationModel).where(condition))

    # Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.
    await session.execute(delete(OUTBOX).where(OUTBOX.c.user_id == subject_id))
    return int(retained)
