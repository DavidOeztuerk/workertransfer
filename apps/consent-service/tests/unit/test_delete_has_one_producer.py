"""`POST /consent/delete` ist zurückgezogen (ADR-0027 §1).

Der Endpunkt war **kapabilitätsbezogen**, nicht kontobezogen: er trug ein
`capability`-Feld und nahm denselben Rumpf entgegen wie `/revoke`. Der Aufruf
sagte also „diese eine Erlaubnis, endgültig" — nicht „mein Konto".

Und jede Capability in diesem System ist eine **Sichtbarkeit**. Keine einzige
benennt einen Datenbestand, den man unabhängig vom Konto löschen könnte.
„Lösche `profile.visibility:public`" kann deshalb nicht „lösche das Profil"
heißen — wer das gleichsetzt, löscht bei einem Widerruf der Sichtbarkeit den
Lebenslauf, unwiderruflich, und niemand hat es verlangt.

Dazu kam: er hatte **keinen Konsumenten**. Ein Endpunkt, der ein
Löschversprechen entgegennimmt, ohne etwas zu löschen, ist genau der Zustand,
den ROADMAP 10.5 als Täuschung beschreibt.

`ConsentAction.DELETE` bleibt — und bekommt seine Bedeutung zurück: erzeugt wird
es künftig ausschließlich von der Kontolöschung, je Capability, die die Person
je hielt. Damit heißt `deleted=True` endlich das, wonach es aussieht.
"""

from __future__ import annotations

from consent_service.configuration import ConsentServiceSettings
from consent_service.presentation.compose_api import build_app


def _paths() -> set[str]:
    """Aus dem OpenAPI-Schema, nicht aus `app.routes`.

    FastAPI hängt eingebundene Router als `_IncludedRouter` ein; die tragen
    keinen `path`. Das Schema ist ohnehin das, woraus Konsumenten ihre Clients
    erzeugen — was dort fehlt, gibt es für sie nicht.
    """
    return set(build_app(ConsentServiceSettings()).openapi()["paths"])


def test_the_capability_scoped_delete_endpoint_is_gone() -> None:
    paths = _paths()

    assert "/consent/delete" not in paths
    # Widerrufen kann man weiter — das ist der Knopf „diese Freigabe
    # zurücknehmen" in der Oberfläche, und er bleibt REVOKE.
    assert "/consent/revoke" in paths


def test_the_account_erasure_is_the_only_way_in() -> None:
    assert "/internal/erasure" in _paths()


def test_no_handler_produces_a_delete_event_besides_the_erasure() -> None:
    """Genau ein Erzeuger — sonst hieße `deleted=True` wieder zweierlei."""
    from consent_service.application import commands

    assert not hasattr(commands, "handle_delete")
    assert not hasattr(commands, "DeleteConsentCommand")
