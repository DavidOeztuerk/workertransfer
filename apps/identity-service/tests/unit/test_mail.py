# apps/identity-service/tests/unit/test_mail.py
"""Der Mailer-Adapter baut eine korrekte Nachricht und meldet Fehler ehrlich."""

from __future__ import annotations

import pytest
from identity_service.infrastructure.mail import NullMailer, SmtpMailer


async def test_null_mailer_records_instead_of_sending() -> None:
    mailer = NullMailer()

    await mailer.send(to="a@b.com", subject="Hallo", body="Text")

    assert mailer.sent == [("a@b.com", "Hallo", "Text")]


def test_smtp_mailer_builds_a_plaintext_message() -> None:
    mailer = SmtpMailer(host="h", port=1025, mail_from="noreply@x.de")

    message = mailer.build_message(to="a@b.com", subject="Betreff", body="Zeile")

    assert message["To"] == "a@b.com"
    assert message["From"] == "noreply@x.de"
    assert message["Subject"] == "Betreff"
    assert message.get_content_type() == "text/plain"
    assert message.get_content().strip() == "Zeile"


async def test_smtp_mailer_raises_when_the_server_is_unreachable() -> None:
    # Ehrlich scheitern statt False zurückgeben: der Aufrufer entscheidet,
    # ob ein Fehlschlag die Registrierung kippen darf (er darf es nicht, §5).
    mailer = SmtpMailer(host="127.0.0.1", port=1, mail_from="noreply@x.de")

    with pytest.raises(OSError):
        await mailer.send(to="a@b.com", subject="s", body="b")
