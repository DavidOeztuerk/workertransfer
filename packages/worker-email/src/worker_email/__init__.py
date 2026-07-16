"""Email: SMTP, SES, SendGrid, Templates, Queue, Tracking."""

import smtplib
from abc import ABC, abstractmethod
from dataclasses import dataclass
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText
from typing import Any

import boto3
from sendgrid import SendGridAPIClient
from sendgrid.helpers.mail import Mail


@dataclass
class EmailMessage:
    to: list[str]
    subject: str
    body: str
    from_email: str | None = None
    from_name: str | None = None
    reply_to: str | None = None
    cc: list[str] | None = None
    bcc: list[str] | None = None
    attachments: list[dict[str, Any]] | None = None  # {filename, content, content_type}
    headers: dict[str, str] | None = None


class EmailBackend(ABC):
    @abstractmethod
    async def send(self, message: EmailMessage) -> bool: ...


class SMTPBackend(EmailBackend):
    def __init__(
        self, host: str, port: int, username: str, password: str, use_tls: bool = True
    ) -> None:
        self._host = host
        self._port = port
        self._username = username
        self._password = password
        self._use_tls = use_tls

    async def send(self, message: EmailMessage) -> bool:
        try:
            msg = MIMEMultipart("alternative")
            msg["Subject"] = message.subject
            msg["From"] = (
                f"{message.from_name} <{message.from_email}>"
                if message.from_name
                else (message.from_email or "")
            )
            msg["To"] = ", ".join(message.to)
            if message.cc:
                msg["Cc"] = ", ".join(message.cc)
            if message.reply_to:
                msg["Reply-To"] = message.reply_to

            msg.attach(MIMEText(message.body, "html" if "<html" in message.body else "plain"))

            with smtplib.SMTP(self._host, self._port) as server:
                if self._use_tls:
                    server.starttls()
                server.login(self._username, self._password)
                recipients = message.to + (message.cc or []) + (message.bcc or [])
                server.sendmail(message.from_email or "", recipients, msg.as_string())
            return True
        except Exception:
            return False


class SESBackend(EmailBackend):
    def __init__(self, region: str = "us-east-1", **kwargs: Any) -> None:
        self._client = boto3.client("ses", region_name=region, **kwargs)

    async def send(self, message: EmailMessage) -> bool:
        try:
            self._client.send_email(
                Source=message.from_email,
                Destination={
                    "ToAddresses": message.to,
                    "CcAddresses": message.cc or [],
                    "BccAddresses": message.bcc or [],
                },
                Message={
                    "Subject": {"Data": message.subject},
                    "Body": {"Html": {"Data": message.body}}
                    if "<html" in message.body
                    else {"Text": {"Data": message.body}},
                },
                ReplyToAddresses=[message.reply_to] if message.reply_to else [],
            )
            return True
        except Exception:
            return False


class SendGridBackend(EmailBackend):
    def __init__(self, api_key: str) -> None:
        self._client = SendGridAPIClient(api_key)

    async def send(self, message: EmailMessage) -> bool:
        try:
            mail = Mail(
                from_email=(message.from_email, message.from_name),
                to_emails=message.to,
                subject=message.subject,
                html_content=message.body if "<html" in message.body else None,
                plain_text_content=message.body if "<html" not in message.body else None,
            )
            if message.cc:
                for cc in message.cc:
                    mail.add_cc(cc)
            if message.bcc:
                for bcc in message.bcc:
                    mail.add_bcc(bcc)
            if message.reply_to:
                mail.reply_to = message.reply_to

            response = self._client.send(mail)
            return bool(response.status_code == 202)
        except Exception:
            return False


class EmailService:
    def __init__(
        self,
        backend: EmailBackend,
        default_from: str,
        default_from_name: str | None = None,
    ) -> None:
        self._backend = backend
        self._default_from = default_from
        self._default_from_name = default_from_name

    async def send(self, message: EmailMessage) -> bool:
        if not message.from_email:
            message.from_email = self._default_from
        if not message.from_name:
            message.from_name = self._default_from_name
        return await self._backend.send(message)

    async def send_template(
        self,
        template_name: str,
        context: dict[str, Any],
        to: list[str],
        subject: str,
        **kwargs: Any,
    ) -> bool:
        from worker_templates import TemplateEngine

        engine = TemplateEngine("templates/email")
        body = engine.render(template_name, context)
        return await self.send(EmailMessage(to=to, subject=subject, body=body, **kwargs))
