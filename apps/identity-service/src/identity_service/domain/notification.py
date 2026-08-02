"""Benachrichtigungen — und vor allem: was sie NICHT sagen.

Eine Mail landet in einem Postfach, und dieses Postfach kann das Postfach beim
aktuellen Arbeitgeber sein: auf dessen Servern, in dessen Backups, im Blick
seiner Administratoren. Eine Zeile wie „Acme GmbH möchte deinen Marktstatus
sehen" wäre genau die Auskunft, gegen die diese Plattform gebaut ist —
freiwillig verschickt und in Klartext.

Deshalb sagt eine Benachrichtigung nicht, worum es geht. Der Inhalt lebt hinter
der Anmeldung, wo geprüft wird, wer liest.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import StrEnum
from uuid import UUID

__all__ = [
    "THROTTLE",
    "NotificationKind",
    "NotificationPreference",
    "notification_body",
    "notification_subject",
]

#: Höchstens eine Benachrichtigung je Person und Stunde, über alle Arten hinweg.
#:
#: Auch eine inhaltsfreie Mail verrät ihren Zeitpunkt: wer beobachtet, wann
#: WorkerTransfer schreibt, sieht, wann etwas passiert. Die Drossel nimmt der
#: Frequenz die Aussagekraft — und macht den Endpunkt nebenbei als
#: Spam-Werkzeug wertlos.
#:
#: Eine Stunde ist eine Wahl, keine Ableitung: kürzer drosselt nichts, weil
#: zwischen zwei Sitzungen ohnehin mehr Zeit liegt; länger verschluckt eine
#: echte Nachricht.
THROTTLE = timedelta(hours=1)


class NotificationKind(StrEnum):
    RESUME_REQUEST = "resume_request"
    MARKET_REQUEST = "market_request"
    APPLICATION_UPDATE = "application_update"
    TRANSFER_UPDATE = "transfer_update"


def notification_subject(kind: NotificationKind) -> str:
    """Für jede Art derselbe Betreff — die Art ist genau das Geheimnis.

    Das `kind` steht in der Signatur, obwohl es nicht benutzt wird: der Aufrufer
    soll nicht in Versuchung geraten, sich selbst einen Betreff zu bauen, und
    ein Test prüft, dass alle vier Arten dieselbe Zeile ergeben.
    """
    _ = kind
    return "Neuigkeiten auf WorkerTransfer"


def notification_body(kind: NotificationKind, *, web_url: str) -> str:
    _ = kind
    return (
        "Es gibt etwas Neues für dich.\n\n"
        f"Melde dich an, um nachzusehen: {web_url}\n\n"
        "Was es ist, steht bewusst nicht in dieser Mail — sie könnte in einem\n"
        "Postfach liegen, das nicht nur dir gehört.\n"
    )


@dataclass(eq=False, slots=True)
class NotificationPreference:
    """Vier Schalter und eine Drossel, alles an der Person.

    `last_sent_at` steht hier und nicht in einem eigenen Tisch: es gibt genau
    eine Drossel je Person, sie ist kein eigener Gegenstand, und ein zweiter
    Tisch wäre ein Join für einen Zeitstempel.
    """

    user_id: UUID
    kinds: dict[NotificationKind, bool]
    last_sent_at: datetime | None

    @classmethod
    def default(cls, user_id: UUID) -> NotificationPreference:
        # Alle an. Eine Benachrichtigung über den eigenen Vorgang ist keine
        # Werbung, sondern die Bedingung dafür, dass „die Person entscheidet"
        # überhaupt eintreten kann.
        return cls(
            user_id=user_id,
            kinds={kind: True for kind in NotificationKind},
            last_sent_at=None,
        )

    def wants(self, kind: NotificationKind) -> bool:
        return self.kinds.get(kind, True)

    def set(self, kind: NotificationKind, wanted: bool) -> None:
        self.kinds[kind] = wanted

    def may_send(self, kind: NotificationKind, *, now: datetime) -> bool:
        if not self.wants(kind):
            return False
        if self.last_sent_at is None:
            return True
        return now - self.last_sent_at >= THROTTLE

    def mark_sent(self, now: datetime) -> None:
        self.last_sent_at = now
