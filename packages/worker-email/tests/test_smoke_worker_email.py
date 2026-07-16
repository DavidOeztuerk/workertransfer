"""Smoke tests for worker-email (Phase 1.5).

Exercises the ``EmailMessage`` dataclass and the ``SMTPBackend`` /
``EmailService`` constructors (store fields only; no socket, no SES session).
``send()`` on any backend is NOT called — it hits SMTP/SES/SendGrid.
"""

from worker_email import EmailMessage, EmailService, SMTPBackend


def test_smoke_email_message_and_service() -> None:
    message = EmailMessage(to=["a@b.c"], subject="s", body="b")

    assert message.to == ["a@b.c"]
    assert message.body == "b"

    backend = SMTPBackend(host="smtp.example.com", port=587, username="u", password="p")

    assert backend._host == "smtp.example.com"
    assert backend._port == 587

    service = EmailService(backend=None, default_from="x@y.z")

    assert service._default_from == "x@y.z"
