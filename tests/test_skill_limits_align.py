"""Die eine Zahl, die zwei Dienste gemeinsam haben müssen.

`profile-service` und `jobs-service` führen jeweils ihre eigene `Skills` — so
will es die Sharing-Regel, denn Fähigkeiten sind ein Modell des Dienstes, dem
sie gehören, und ein geteiltes Domänenmodell gibt es hier nicht.

Geteilt ist trotzdem etwas: **die Listen werden im Browser gegeneinander
gehalten** (Sub-step 6.2). Dürfte eine Ausschreibung eine Anforderung nennen,
die länger ist, als eine Person sie überhaupt eintragen kann, dann wäre sie
garantiert nie ein Treffer — eine Zeile in der Liste, die für niemanden je ein
Haken werden kann. Das sieht man in keiner der beiden Dateien; deshalb steht es
hier.

Kleiner darf die Stelle sein, größer nicht.
"""

from __future__ import annotations

from jobs_service.domain.job import MAX_SKILL_LENGTH as JOB_SKILL_LENGTH
from profile_service.domain.profile import MAX_SKILL_LENGTH as PROFILE_SKILL_LENGTH


def test_a_job_may_not_demand_a_skill_no_profile_could_hold() -> None:
    assert JOB_SKILL_LENGTH <= PROFILE_SKILL_LENGTH
