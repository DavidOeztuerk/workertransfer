# Product and trust constraints

## Consent and user control

- A worker controls whether a profile is discoverable, which evidence is visible, and
  which company may contact them.
- A GitHub connection uses OAuth and imports only the data and scopes a user has
  explicitly approved. Imported evidence is reviewable, revocable, and deletable.
- An employment-transfer offer never bypasses the worker: both direct contact and any
  employer-to-employer process require explicit worker participation and consent.
- Applicant documents are attached only after the worker has reviewed the specific
  application and selected the recipients.

## Integrations and data acquisition

- There is no scraping of sites without an official API, documented feed, or explicit
  written permission.
- Career-site connectors are implemented provider by provider and record the source,
  synchronization time, permissions, and deletion behavior.
- No credentials, tokens, CVs, contracts, or raw source-code content are committed to
  this repository or placed in logs.

## AI and decisions

- AI drafts applications, summaries, portfolios, and contract templates; a human must
  review and approve every external send or legal document.
- Matching evidence is shown as explainable, user-controlled signals rather than a
  hidden or single "employability" score.
- Recruiter tools support discovery and workflow. They do not autonomously reject,
  rank, contact, or make employment decisions about people.
- Contract templates and AI analysis require a jurisdiction-specific legal review
  before production use.

## Security and privacy by design

- Each service owns its data and emits audit events for security-sensitive actions.
- Tenant identity will come from authenticated claims, never from a browser-controlled
  request header — nor from a request body, which is equally client-controlled. The
  development-only header support in the foundation is for local test fixtures only.
- A tenant is a **company**; a natural person has none (ADR-0017). Tenant scopes
  company data, never personal data — personal data is scoped by the person. The
  consent-ledger is subject-scoped for exactly this reason.
- Data retention, deletion, export, consent history, access controls, encryption,
  threat modelling, and a DPIA are first-class delivery requirements, not a final
  hardening phase.
