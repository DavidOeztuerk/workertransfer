"""Die Regeln der Benachrichtigung — ohne Datenbank, ohne HTTP."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from identity_service.domain.notification import (
    THROTTLE,
    NotificationKind,
    NotificationPreference,
    notification_body,
    notification_subject,
)

NOW = datetime(2026, 8, 2, 12, 0, tzinfo=UTC)


def _pref() -> NotificationPreference:
    return NotificationPreference.default(uuid4())


def test_every_kind_is_on_by_default() -> None:
    """Die einzige Voreinstellung in diesem System, die nicht zurückhaltend ist.

    Wer nicht erfährt, dass gefragt wurde, hat keine Wahl, sondern nur den
    Anschein einer.
    """
    pref = _pref()
    assert all(pref.wants(kind) for kind in NotificationKind)


def test_a_switched_off_kind_is_not_sent() -> None:
    pref = _pref()
    pref.set(NotificationKind.MARKET_REQUEST, False)

    assert not pref.may_send(NotificationKind.MARKET_REQUEST, now=NOW)
    # Die anderen Arten bleiben unberührt: vier Schalter, nicht einer.
    assert pref.may_send(NotificationKind.RESUME_REQUEST, now=NOW)


def test_the_throttle_swallows_a_second_notification_within_the_hour() -> None:
    """Nicht Höflichkeit, sondern derselbe Grund wie der leere Inhalt.

    Wer beobachtet, WANN WorkerTransfer schreibt, sieht, wann etwas passiert.
    Drei Mails an einem Tag heißt: da läuft etwas.
    """
    pref = _pref()
    pref.mark_sent(NOW)

    assert not pref.may_send(NotificationKind.TRANSFER_UPDATE, now=NOW + timedelta(minutes=59))
    assert pref.may_send(NotificationKind.TRANSFER_UPDATE, now=NOW + THROTTLE)


def test_the_throttle_spans_kinds() -> None:
    # Sonst wären vier Arten vier Kanäle, und die Frequenz verriete wieder etwas.
    pref = _pref()
    pref.mark_sent(NOW)

    assert not pref.may_send(NotificationKind.RESUME_REQUEST, now=NOW + timedelta(minutes=5))


@pytest.mark.parametrize("kind", list(NotificationKind))
def test_the_message_never_says_what_it_is_about(kind: NotificationKind) -> None:
    """Der tragende Test dieses Schnitts.

    Eine Mail landet womöglich im Postfach beim aktuellen Arbeitgeber. Ein
    Betreff mit „Marktstatus" wäre genau die Auskunft, gegen die diese Plattform
    gebaut ist — freiwillig verschickt, in Klartext.
    """
    text = f"{notification_subject(kind)}\n{notification_body(kind, web_url='http://x')}"
    # Der Markenname wird herausgenommen, bevor geprüft wird. „WorkerTransfer"
    # enthält „transfer", und das ist kein Leck dieses Schnitts: der Name steht
    # schon auf der Bestätigungsmail bei der Registrierung. Dass die Marke
    # verrät, wofür die Plattform da ist, ist eine Entscheidung, die vor diesem
    # Schnitt getroffen wurde — hier geht es darum, dass die Mail nicht sagt,
    # WELCHER Vorgang ansteht.
    lowered = text.lower().replace("workertransfer", "")

    for leak in (
        "marktstatus",
        "market",
        "lebenslauf",
        "resume",
        "bewerbung",
        "application",
        "transfer",
        "angebot",
        "anfrage",
        kind.value,
    ):
        assert leak not in lowered, f"{leak!r} steht in der Nachricht"


def test_the_message_is_identical_for_every_kind() -> None:
    """Sonst verriete schon die Länge oder der Betreff, worum es geht."""
    subjects = {notification_subject(kind) for kind in NotificationKind}
    bodies = {notification_body(kind, web_url="http://x") for kind in NotificationKind}

    assert len(subjects) == 1
    assert len(bodies) == 1


def test_the_message_carries_a_way_back() -> None:
    body = notification_body(NotificationKind.RESUME_REQUEST, web_url="https://app.example")
    assert "https://app.example" in body
