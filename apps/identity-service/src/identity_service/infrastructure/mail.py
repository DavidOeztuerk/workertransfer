# apps/identity-service/src/identity_service/infrastructure/mail.py
"""SMTP-Adapter für den Mailer-Port.

Bewusst `smtplib` aus der Standardbibliothek statt `worker-email`: dessen
SMTPBackend ruft unbedingt `server.login()` auf (Mailpit hat keine
Authentifizierung), schluckt jede Exception zu einem nackten `False` und zieht
boto3, sendgrid und aiohttp in ein Image, das eine Textmail verschicken soll.
"""

from __future__ import annotations

import asyncio
import smtplib
from email.message import EmailMessage

__all__ = ["NullMailer", "SmtpMailer"]


class NullMailer:
    """Sammelt statt zu senden — für Tests und für Läufe ohne Mailcatcher."""

    def __init__(self) -> None:
        self.sent: list[tuple[str, str, str]] = []

    async def send(self, *, to: str, subject: str, body: str) -> None:
        self.sent.append((to, subject, body))


class SmtpMailer:
    def __init__(
        self,
        *,
        host: str,
        port: int,
        mail_from: str,
        username: str | None = None,
        password: str | None = None,
        use_tls: bool = False,
    ) -> None:
        self._host = host
        self._port = port
        self._mail_from = mail_from
        self._username = username
        self._password = password
        self._use_tls = use_tls

    def build_message(self, *, to: str, subject: str, body: str) -> EmailMessage:
        message = EmailMessage()
        message["From"] = self._mail_from
        message["To"] = to
        message["Subject"] = subject
        message.set_content(body)
        return message

    async def send(self, *, to: str, subject: str, body: str) -> None:
        message = self.build_message(to=to, subject=subject, body=body)
        # smtplib ist blockierend; im Thread ausführen, damit der Event-Loop
        # nicht für die Dauer der SMTP-Konversation steht.
        await asyncio.to_thread(self._send_blocking, message)

    def _send_blocking(self, message: EmailMessage) -> None:
        with smtplib.SMTP(self._host, self._port, timeout=10) as server:
            if self._use_tls:
                server.starttls()
            # Nur anmelden, wenn Zugangsdaten gesetzt sind: Mailpit und die
            # meisten Entwicklungs-Catcher kennen AUTH nicht.
            if self._username and self._password:
                server.login(self._username, self._password)
            server.send_message(message)
