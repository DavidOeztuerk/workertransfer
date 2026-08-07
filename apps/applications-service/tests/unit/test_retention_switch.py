"""Der Aufbewahrungsschalter steht auf AUS (ADR-0027 §3.1).

Bewusst **hier** und nicht in der Integrationsreihe: die läuft ohne Docker gar
nicht (ADR-0011), und ausgerechnet die Entscheidung, die am leichtesten
verlorengeht, darf nicht zu denen gehören, die sich stillschweigend
wegskippen.

Es ist der billigste Test im ganzen Schnitt.
"""

from __future__ import annotations

from applications_service.application import erasure
from applications_service.configuration import ApplicationsServiceSettings


def test_the_retention_switch_is_off() -> None:
    """Die Voreinstellung löscht vollständig — auch `status = 'hired'`.

    Eine ungeprüfte Vorsichtsannahme, die als Voreinstellung im Code steht,
    verwandelt sich innerhalb weniger Monate in „so ist das eben". Dann liegen
    Daten unbefristet herum, und niemand weiß mehr, dass die Begründung dafür
    nie über eine Vermutung hinauskam. Genau das ist beim Audit-Trail schon
    einmal passiert (ADR-0012).
    """
    assert erasure.RETAIN_HIRED_APPLICATIONS is False


def test_the_switch_is_a_constant_and_not_a_setting() -> None:
    """Bei einem Löschversprechen wäre „in Produktion anders als im Test" der
    schlimmste denkbare Zustand.

    Ihn umzulegen muss ein sichtbarer Commit sein, den jemand begründet — kein
    Wert, den eine Umgebungsvariable je Umgebung still verschiebt.
    """
    assert not [name for name in ApplicationsServiceSettings.model_fields if "retain" in name]
