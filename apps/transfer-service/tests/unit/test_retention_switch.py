"""Der Aufbewahrungsschalter steht auf AUS (ADR-0027 §3.1).

Hier und nicht in der Integrationsreihe: die läuft ohne Docker gar nicht
(ADR-0011), und ausgerechnet die Entscheidung, die am leichtesten verlorengeht,
darf nicht zu denen gehören, die sich stillschweigend wegskippen.
"""

from __future__ import annotations

from transfer_service.application import erasure
from transfer_service.configuration import TransferServiceSettings


def test_the_retention_switch_is_off() -> None:
    """Die Voreinstellung löscht auch **bezahlte** Transfers.

    Ein Betrag, auf den sich zwei Unternehmen geeinigt haben, macht die Zeile
    nicht zur Unterlage eines Vermittlers — die Plattform führt kein Geld, sie
    hält eine Zahl fest, die beide Seiten im Blick haben sollen.
    """
    assert erasure.RETAIN_PAID_TRANSFERS is False


def test_the_switch_is_a_constant_and_not_a_setting() -> None:
    """„In Produktion anders als im Test" wäre bei einem Löschversprechen der
    schlimmste denkbare Zustand."""
    assert not [name for name in TransferServiceSettings.model_fields if "retain" in name]
