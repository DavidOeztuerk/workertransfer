# Glossar

Zentrale Begriffe für WorkerTransfer. Deutsch-geführt (Projektsprache)
mit englischen Fachbegriffen.

- **Candidate / Worker / Arbeitnehmer** — Natürliche Person, die ihr Profil,
  ihre Bewerbungen und ihre Sichtbarkeit selbst steuert. Ganztägig
  konsenspflichtig.
- **Employer / Company / Unternehmen** — Juristische Person, die rekrutiert
  oder Transferangebote macht. Recruiting ist Unterstützungsworkflow, keine
  autonome Entscheidung über Menschen.
- **Current Employer** — Die Firma, in der ein kandidat aktuell beschäftigt ist.
  Über Übernahmen ist in Phase 5 (Transfermarkt) ein direkter Kontakt inkl.
  Spielerfirma vorgesehen.
- **Tenant / Mandant** — Ein **Unternehmen** als handelnde Einheit: Träger von
  Stellenausschreibungen, Arbeitgeberkonten, Recruiting-Teams und aller weiteren
  unternehmensbasierten Funktionen. Eine **natürliche Person hat keinen
  Tenant** — Tenant ist ein optionales Attribut eines Prinzipals, kein
  Pflichtfeld (ADR-0017). Kommt ausschließlich aus authentifizierten Claims,
  nie aus Header oder Request-Body.
- **Subject** — Die natürliche Person, über deren Daten eine Einwilligung
  entscheidet, identifiziert durch `SubjectId`. Die personenbezogene
  Scoping-Achse: Nutzerdaten werden über das Subject getrennt, nicht über den
  Tenant. Beide Achsen bestehen nebeneinander und ersetzen einander nicht.
- **Consent-Ledger** — Das append-only Verzeichnis über erteilte, entzogene und
  gelöschte Einwilligungen. **Enabler**, nicht Feature: jede Sichtbarkeit,
  jeder Versand, jeder Datenimport fragt den Ledger ab. Revocation zieht die
  Capability sofort zurück. Subjekt-, nicht mandantengescopet: eine Einwilligung
  gehört der Person und folgt ihr über Arbeitgeberwechsel hinweg (ADR-0017).
- **Verified Signal** — Ein kompetenznachweisendes Signal aus tatsächlicher Arbeit
  (z. B. GitHub Commits, PRs, Reviews, Security Advisories, OSS-Repos), das nur
  mit Zustimmung geholt und jederzeit widerrufbar ist.
- **Skill-Graph** — Der consent-basierte, multidimensionale Kompetenzgraph, der
  aus Verified Signals aufgebaut wird. Mehrdimensional statt eines Scores.
- **Match-Score** — Ein erklärbaren, nachvollziehbares Match-Kalkül zwischen
  Kandidat und Job. **Explizit kein** verdeckter "Employability-Score".
- **Scout** (Agent) — Die AI-Unterstützung für Unternehmen, um aus
  natürlichsprachlichen Suchanfragen ("Senior Python Backend mit Event
  Sourcing") eine erklärbare Kandidatenliste zu erzeugen. Keine autonome
  Auswahl, kein Auto-Kontakt, kein Ranking ohne Consent.
- **Market Status** — Der Zustand eines Kandidaten im Transfermarkt (Phase 5):
  `Open → Listening → Unavailable → Under Contract → Transfer Listed →
  Negotiating → Transferred`. Beide Wege (beschäftigt / nicht-beschäftigt)
  erfordern Kandidaten-Consent; Ablehnung ist immer möglich.
- **Contract Agent** — AI, die Vertragsentwürfe erzeugt (Arbeitsvertrag, NDA,
   Aufhebungsvertrag, Transfervertrag, Änderungen). **Draft-only**,
   jurisdiktions-spezifische Rechtsprüfung vor Produktivbetrieb, immer mit
   Hinweis.
- **Bridge DTO** — Ein versioniertes, schema-definiertes Datentransferobjekt in
  `worker-contracts`. Niemals ein geteiltes Domain-Modell.
- **Composition-Root** — Die pro-Service-Datei, die Settings, Infrastruktur und
  Middleware explizit in den Kernel einspeist (ADR-0003). Kein fluent Builder.
- **Kernel / worker-platform** — Der laufzeit-reife Bestandteil der Plattform
  (Factory, Middleware, Settings, CQRS-Mediator, Health, Errors). Siehe ADR-0002.
- **Baustein / worker-* Paket** — Eine isolierte, einzeln testbare
  Infrastrukturbibliothek (auth, cache, db, ai, github…), die von der
  Composition-Root eingebunden wird.
- **Connector** — Eine Integration mit einem externen System über eine offizielle
  API/einen Feed (ATS, GitHub, E-Signature, Kalender…). Erfordert eine
  Connector-ADR ( Quelle/Scopes/Sync/Permissions/Deletion); kein Scraping.
- **Outbox/Inbox** — ?
  Die transaktionale Outbox sichert, dass ein Domain-Event genau dann veröffentlicht
  wird, wenn seine auslösende DB-Transaktion committet; die Inbox macht
  eingehende Events idempotent. Schaltet in Phase 9, sobald serviceübergreifende
  Workflows existieren.
