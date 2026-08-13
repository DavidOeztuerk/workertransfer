"""Die Sammelprüfung — und dass sie dasselbe sagt wie die einzelne.

Zwei Wege an dieselbe Auskunft, die sich uneinig werden können, sind schlimmer
als kein zweiter Weg. Deshalb steht hier nicht nur „der Stapel funktioniert",
sondern der Vergleich Paar für Paar.
"""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any
from uuid import UUID, uuid4

import pytest
from consent_service.application.commands import (
    CheckConsentBatchQuery,
    CheckConsentQuery,
    handle_check,
    handle_check_many,
)
from consent_service.domain.consent_event import ConsentEvent
from consent_service.domain.value_objects import (
    Capability,
    ConsentAction,
    ConsentEventId,
    Reason,
    SubjectId,
)
from worker_core import DomainError

PUBLIC = "profile.visibility:public"
OTHER = "resume.visibility:tenant:11111111-1111-1111-1111-111111111111"


def _event(subject: UUID, capability: str, action: ConsentAction, minute: int) -> ConsentEvent:
    return ConsentEvent(
        event_id=ConsentEventId(uuid4()),
        subject_id=SubjectId(subject),
        capability=Capability(capability),
        action=action,
        recorded_at=datetime(2026, 8, 13, 12, minute, tzinfo=UTC),
        reason=Reason("weil") if action is not ConsentAction.GRANT else None,
    )


class _Repo:
    """Ein Speicher, der beide Wege aus DENSELBEN Ereignissen beantwortet.

    Wichtig für den Vergleichstest: würde der Fake die Sammelprüfung aus einer
    zweiten Quelle beantworten, verglichen die Tests zwei Fakes und nicht zwei
    Wege.
    """

    def __init__(self, events: list[ConsentEvent]) -> None:
        self.events = events
        self.batch_calls = 0
        self.single_calls = 0

    def _newest(self, subject: UUID, capability: str) -> ConsentEvent | None:
        passend = [
            e
            for e in self.events
            if e.subject_id.value == subject and e.capability.value == capability
        ]
        return max(passend, key=lambda e: e.recorded_at) if passend else None

    async def latest_effective(self, subject_id: Any, capability: Any) -> ConsentEvent | None:
        self.single_calls += 1
        return self._newest(subject_id.value, capability.value)

    async def latest_effective_many(
        self, pairs: list[tuple[Any, Any]]
    ) -> dict[tuple[UUID, str], ConsentEvent]:
        self.batch_calls += 1
        gefunden: dict[tuple[UUID, str], ConsentEvent] = {}
        for subject, capability in pairs:
            event = self._newest(subject.value, capability.value)
            if event is not None:
                gefunden[(subject.value, capability.value)] = event
        return gefunden


async def test_eine_anfrage_statt_n() -> None:
    """Der ganze Zweck: eine Runde, nicht `n`."""
    subjects = [uuid4() for _ in range(20)]
    repo = _Repo([_event(s, PUBLIC, ConsentAction.GRANT, 0) for s in subjects])

    result = await handle_check_many(
        CheckConsentBatchQuery(
            pairs=tuple(CheckConsentQuery(subject_id=s, capability=PUBLIC) for s in subjects)
        ),
        deps={},
        repos={"consent": repo},
    )

    assert result.is_success
    assert len(result.value) == 20
    assert repo.batch_calls == 1
    assert repo.single_calls == 0


async def test_antworten_in_der_reihenfolge_der_fragen() -> None:
    """Die Reihenfolge ist Vertrag: der Aufrufer ordnet sie seinen Zeilen zu."""
    ja, nein, nie = uuid4(), uuid4(), uuid4()
    repo = _Repo(
        [
            _event(ja, PUBLIC, ConsentAction.GRANT, 0),
            _event(nein, PUBLIC, ConsentAction.GRANT, 0),
            _event(nein, PUBLIC, ConsentAction.REVOKE, 5),
        ]
    )

    result = await handle_check_many(
        CheckConsentBatchQuery(
            pairs=(
                CheckConsentQuery(subject_id=nie, capability=PUBLIC),
                CheckConsentQuery(subject_id=ja, capability=PUBLIC),
                CheckConsentQuery(subject_id=nein, capability=PUBLIC),
            )
        ),
        deps={},
        repos={"consent": repo},
    )

    assert result.is_success
    assert [state.granted for state in result.value] == [False, True, False]


async def test_sagt_paar_fuer_paar_dasselbe_wie_die_einzelne_pruefung() -> None:
    """Der eigentliche Wächter: beide Wege, dieselben Ereignisse, dasselbe Urteil.

    Abgedeckt sind alle vier Lagen, die `project_state` unterscheidet: erteilt,
    widerrufen, gelöscht und nie berührt.
    """
    erteilt, widerrufen, geloescht, unberuehrt = uuid4(), uuid4(), uuid4(), uuid4()
    repo = _Repo(
        [
            _event(erteilt, PUBLIC, ConsentAction.GRANT, 0),
            _event(widerrufen, PUBLIC, ConsentAction.GRANT, 0),
            _event(widerrufen, PUBLIC, ConsentAction.REVOKE, 5),
            _event(geloescht, PUBLIC, ConsentAction.GRANT, 0),
            _event(geloescht, PUBLIC, ConsentAction.DELETE, 5),
            # Ein zweites Paar derselben Person, damit die Zuordnung über beide
            # Schlüsselteile geht und nicht nur über die Kennung.
            _event(erteilt, OTHER, ConsentAction.GRANT, 0),
        ]
    )
    paare = [
        (erteilt, PUBLIC),
        (widerrufen, PUBLIC),
        (geloescht, PUBLIC),
        (unberuehrt, PUBLIC),
        (erteilt, OTHER),
        (widerrufen, OTHER),
    ]

    gesammelt = await handle_check_many(
        CheckConsentBatchQuery(
            pairs=tuple(CheckConsentQuery(subject_id=s, capability=c) for s, c in paare)
        ),
        deps={},
        repos={"consent": repo},
    )
    assert gesammelt.is_success

    for (subject, capability), sammel in zip(paare, gesammelt.value, strict=True):
        einzeln = await handle_check(
            CheckConsentQuery(subject_id=subject, capability=capability),
            deps={},
            repos={"consent": repo},
        )
        assert einzeln.is_success
        assert (sammel.granted, sammel.deleted) == (
            einzeln.value.granted,
            einzeln.value.deleted,
        ), f"uneinig über {capability} von {subject}"


async def test_ein_paar_ohne_ereignis_ist_nicht_erteilt_und_kein_fehler() -> None:
    """Abwesenheit ist ein Zustand — wie in der einzelnen Prüfung."""
    repo = _Repo([])

    result = await handle_check_many(
        CheckConsentBatchQuery(pairs=(CheckConsentQuery(subject_id=uuid4(), capability=PUBLIC),)),
        deps={},
        repos={"consent": repo},
    )

    assert result.is_success
    assert result.value[0].granted is False


async def test_ein_ungueltiges_paar_laesst_die_ganze_anfrage_scheitern() -> None:
    """Und wird NICHT als „nicht erteilt" ausgegeben.

    Eine unlesbare Kennung ist ein Fehler beim Aufrufer. Sie als fehlende
    Einwilligung zu melden würde ihn verstecken — bis jemand eine ganze Seite
    lang niemanden mehr sieht und die Ursache im Ledger sucht.
    """
    repo = _Repo([_event(uuid4(), PUBLIC, ConsentAction.GRANT, 0)])

    result = await handle_check_many(
        CheckConsentBatchQuery(
            pairs=(
                CheckConsentQuery(subject_id=uuid4(), capability=PUBLIC),
                CheckConsentQuery(subject_id=uuid4(), capability=""),
            )
        ),
        deps={},
        repos={"consent": repo},
    )

    assert not result.is_success
    assert isinstance(result.error, DomainError)


@pytest.mark.parametrize("name", ["latest_effective_many"])
def test_der_port_kennt_die_sammelabfrage(name: str) -> None:
    """Damit ein Fake, der sie nicht hat, beim Typprüfen auffällt und nicht erst
    im Betrieb."""
    from consent_service.domain.ports import ConsentEventRepository

    assert hasattr(ConsentEventRepository, name)
