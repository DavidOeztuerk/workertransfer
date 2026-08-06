# ADR-0027 — Löschung: was fällt, was bleibt, und wie sich beweisen lässt, dass es überall ankam

- **Status:** angenommen. **Die Voreinstellung löscht vollständig** — auch
  eingestellte Bewerbungen und bezahlte Transfers. Die Aufbewahrungsausnahme
  existiert nur als *ein* abgegrenzter Schalter, der auf **aus** steht (§3).
  Offen ist genau ein anwaltlich zu bestätigender Satz; er entscheidet über die
  Stellung dieses Schalters, nicht über den Entwurf, und blockiert deshalb
  nichts.
- **Datum:** 2026-08-06
- **Betrifft:** alle zehn Dienste, `packages/worker-outbox`, `packages/worker-storage`
- **Verwandt:** ADR-0004 (keine gemeinsame Datenbank), ADR-0012 (Audit-Allowlist,
  keine Kaskade), ADR-0013 (Ledger als einzige Anlaufstelle), ADR-0017 (Tenant
  ist ein Unternehmen), ADR-0020 (Sichtbarkeit lebt nur im Ledger), ADR-0025
  (Outbox, „mindestens einmal"), ADR-0026 (kein zweiter Ort für personenbezogene
  Daten)

---

## Kontext

ROADMAP 10.5 hält fest: `POST /consent/delete` nimmt ein Löschverlangen
entgegen, schreibt ein Ereignis, projiziert `deleted=True` — und **kein Dienst
reagiert darauf**. Ein Konto lässt sich überhaupt nicht löschen.

Beim Nachlesen im Code ist der Befund noch etwas anders, und der Unterschied
entscheidet, welcher Endpunkt gebaut wird:

**1. `/consent/delete` ist kapabilitätsbezogen, nicht kontobezogen.**
`DeleteConsentCommand` trägt ein `capability`-Feld
(`application/commands.py:103–107`), der Router nimmt denselben Rumpf wie
`/revoke` entgegen (`ConsentRevokeV1`, `presentation/http/router.py:123`). Der
Aufruf sagt also „diese eine Erlaubnis, endgültig" — nicht „mein Konto".

**2. Jede Capability in diesem System ist eine *Sichtbarkeit*.** Nachgezählt
über alle Dienste: `profile.visibility:public`, `profile.visibility:tenant:<id>`,
`resume.visibility:tenant:<id>`, `portfolio.visibility:public`,
`portfolio.visibility:tenant:<id>`, `market.visibility:tenant:<id>`,
`github.visibility:public`. **Keine einzige benennt einen Datenbestand**, den
man unabhängig vom Konto löschen könnte. „Lösche `profile.visibility:public`"
kann deshalb nicht „lösche das Profil" heißen: Sichtbarkeit lebt nur im Ledger
(ADR-0020), das Profil gehört der Person und liegt unter einer anderen
Grundlage. Wer das gleichsetzt, löscht bei einem Widerruf der Sichtbarkeit den
Lebenslauf — unwiderruflich, und niemand hat es verlangt.

**3. `/consent/delete` hat keinen Konsumenten.** Gesucht über `apps/` und
`packages/`: die einzigen Aufrufe stehen in zwei Testdateien. Der Web-Client
kennt den Pfad nicht. Das Versprechen wird heute also nirgends *gegeben* —
es steht offen im API und wartet darauf, geglaubt zu werden.

**4. Nur die Person selbst.** `_record` wirft `ConsentSubjectMismatch`, sobald
`actor_id != subject_id` (`application/commands.py:132`). Es gibt kein
Delegations- oder Adminmodell, und das ist Absicht (ADR-0013).

**5. Ein Grund ist Pflicht.** Für `REVOKE` und `DELETE` ist `reason` nicht
optional (`commands.py:152`). Für einen Widerruf ist das vertretbar. Von einem
Menschen, der sein Konto löschen will, eine Begründung zu verlangen, ist ein
Hebel gegen ihn — und der Freitext ist ausgerechnet das Einzige im Ledger, das
später wieder gelöscht werden muss.

---

## Entscheidung

### §1 Ein Vorgang, nicht zwei — `/consent/delete` wird zurückgezogen

**Die Löschung ist ein Kontovorgang und existiert genau einmal:**
`POST /account/erasure` an `identity-service`, nur für sich selbst, **ohne
Begründungsfeld**.

**`POST /consent/delete` entfällt.** Es hat keinen Konsumenten (§Kontext 3), es
kann nicht bedeuten, wonach es klingt (§Kontext 2), und ein Endpunkt, der ein
Löschversprechen entgegennimmt, ohne etwas zu löschen, ist genau der Zustand,
den 10.5 als Täuschung beschreibt. Das ist dieselbe Aufräumregel wie bei
`worker-files` (ADR-0021), `worker-github` (ADR-0022), `worker-messaging`
(ADR-0025) und `worker-search` (ADR-0026): kein Konsument, keine Berechtigung
zu existieren. Der Unterschied ist nur, dass hier nicht Ballast wegfällt,
sondern eine unwahre Zusage.

**`ConsentAction.DELETE` bleibt — und bekommt seine Bedeutung zurück.** Das
Ereignis wird künftig **ausschließlich von der Kontolöschung** erzeugt, je
Capability, die die Person je hielt. Damit heißt `deleted=True` im Ledger
endlich das, wonach es aussieht, und es gibt genau einen Weg, es zu erzeugen.
Die `CHECK`-Bedingung (`action IN ('GRANT','REVOKE','DELETE')`) und die
bestehenden Zeilen bleiben unangetastet.

Der Knopf „diese Freigabe zurücknehmen" in der Oberfläche ist und bleibt
`REVOKE`.

### §2 Was fällt, was bleibt, was anonymisiert wird — Dienst für Dienst

Gelesen aus `apps/*/src/*/infrastructure/database/models.py`. Grundsatz:
**Der Inhalt fällt. Der Beleg, dass er gefallen ist, bleibt. Was einem
Unternehmen gehört, gehört weiter dem Unternehmen — aber ohne den Namen der
Person daran.**

#### identity-service — der Ursprung, und als Letzter dran

| Tabelle | Was passiert |
|---|---|
| `users` | **fällt** — mit ihr `email`, `password_hash`, `display_name`, `status`, `roles`. Das ist die eigentliche Löschung: danach bildet nichts im System mehr eine `subject_id` auf einen Menschen ab. |
| `sessions` | **fällt** (FK `ondelete="CASCADE"`). Zusätzlich sofort bei Antragstellung widerrufen, nicht erst am Ende. |
| `email_verification_tokens` | **fällt** (FK CASCADE). |
| `user_tenant_memberships` | **fällt** (FK CASCADE). Die Person handelt für kein Unternehmen mehr. |
| `notification_preferences` | **fällt — und zwar ausdrücklich.** Diese Tabelle hat **keinen Fremdschlüssel** auf `users` (so dokumentiert im Modell). Ein `DELETE FROM users` lässt sie stehen. Das ist die Zeile, die man vergisst. |
| `company_invitations` — Einladungen, die die Person *verschickt* hat | **bleibt, `invited_by` wird NULL.** Heute steht dort `ondelete="CASCADE"` (`models.py:168`): löscht ein Recruiter sein privates Konto, verschwinden die offenen Einladungen seines Arbeitgebers. Das ist falsch — die Einladung gehört dem Unternehmen. Migration auf nullable + `ON DELETE SET NULL`. |
| `company_invitations` — Einladungen *an* die Adresse der Person | **fallen.** Sie tragen deren E-Mail-Adresse im Klartext (`email`, CITEXT). |
| `tenants` | **bleibt** — ein Unternehmen ist keine natürliche Person (ADR-0017). War die Person der **letzte Admin**, wird das Unternehmen stillgelegt (§7). |
| `audit_events` | **bleibt, `metadata` wird geleert.** Keine Kaskade mit `users` — das ist eine ausdrückliche Entscheidung (ADR-0012). Aber die Allowlist erlaubt `ip` und `user_agent` (`domain/audit.py:46`); heute schreibt sie zwar niemand (nachgesehen: kein Aufrufer setzt sie), doch die Löschung entscheidet die *Form*, nicht den Tagesstand. Was bleibt: `action`, `occurred_at`, `correlation_id`, `actor_id`/`target_id` als Pseudonym. |

#### consent-service — der Beleg

| Tabelle | Was passiert |
|---|---|
| `consent_events` | **bleibt.** Siehe §5. `reason` wird auf `NULL` gesetzt, `metadata` geleert. Es kommt je Capability eine `DELETE`-Zeile hinzu. |
| `audit_events` | **bleibt, `metadata` geleert** — wie bei identity, gleiche Begründung. |

#### profile-service

`profiles` (`id` **ist** die `subject_id`): **die Zeile fällt vollständig.**
Headline, Bio, Ort, Fähigkeiten. Es gibt hier nichts, was einem anderen gehört.

#### resume-service

| Tabelle | Was passiert |
|---|---|
| `resumes` | **fällt vollständig** (`id` ist die `subject_id`). Stationen und Ausbildung liegen als JSONB in derselben Zeile. |
| `resume_requests` mit `subject_id` = Person | **fällt.** Die Zeile ist die Aussage „Unternehmen X hat nach diesem Menschen gefragt" — eine Aussage über ihn. |
| `resume_requests` mit `requested_by` = Person | **bleibt, `requested_by` wird NULL.** Ein Recruiter löscht sein privates Konto; die Anfrage gehört dem Unternehmen und handelt von einem *Dritten*. Spalte ist heute `nullable=False` → Migration. |
| `outbox` mit `user_id` = Person | **fällt.** Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt. |

#### portfolio-service — der einzige Dienst mit Dateien

| Was | Was passiert |
|---|---|
| `portfolios` | **fällt vollständig** (`id` ist die `subject_id`, Einträge als JSONB). |
| Anhänge im Speicher unter `<subject_id>/…` | **fallen**, über `list_names(str(subject_id))` und `delete` je Name — genau der Zweck, für den `list_names` dokumentiert ist. **Auch das Verzeichnis selbst**: ein leeres Verzeichnis, das nach einer `subject_id` heißt, ist die Spur, die man übersieht. |

**Reihenfolge umgekehrt zum Hochladen.** Der Upload committet zuerst und räumt
danach auf, weil ein fehlgeschlagener Commit sonst Dateien löscht, auf die
gültige Einträge zeigen (`router.py:112–118`). Beim Löschen gilt das Gegenteil:
**erst die Dateien, dann die Zeile.** Bricht es dazwischen ab, zeigen Einträge
ins Leere und der nächste Zustellversuch räumt sie weg — andersherum bliebe der
Inhalt liegen, den niemand mehr referenziert und deshalb auch niemand mehr
findet.

#### jobs-service — **nichts fällt**

`jobs` trägt `tenant_id`, Titel, Beschreibung, Ort, Fähigkeiten, Status — und
**keine Spalte, die auf eine natürliche Person zeigt**. Kein `created_by`, keine
`subject_id`. Eine Stellenanzeige ist der Text eines Unternehmens. Der Dienst
ist kein Empfänger der Kaskade (§4).

#### applications-service — hier liegt der Konflikt

| Fall | Was passiert |
|---|---|
| `status ∈ {submitted, reviewing, rejected, withdrawn}` | **Zeile fällt vollständig**, inklusive `message` — dem Anschreiben, das die Person selbst verfasst hat. |
| `status = hired` | **Fällt ebenfalls** — das ist die Voreinstellung (§3). Nur wenn der Aufbewahrungsschalter umgelegt wird, bleibt die Zeile stehen; er steht auf *aus*. |
| `outbox` mit `user_id` = Person | **fällt.** |

#### companies-service — **nichts fällt**

`company_profiles` (`id` **ist** die `tenant_id`): Kürzel, Anzeigename,
Beschreibung, Website, Standorte, Leistungen. Keine Spalte zeigt auf eine
natürliche Person. Kein Empfänger der Kaskade. *Grenze, die die Mechanik nicht
löst:* nennt sich ein Einzelunternehmen nach seinem Inhaber, steht dessen Name
in `display_name` — als Firmenname, nicht als Personendatum. Das ist eine
Abwägung im Einzelfall, keine Zeile, die ein Löschlauf erkennen könnte.

#### transfer-service — hier liegt der zweite Konflikt

| Tabelle / Fall | Was passiert |
|---|---|
| `market_status` | **fällt vollständig** (`id` ist die `subject_id`, samt `note`). |
| `market_requests` mit `subject_id` = Person | **fällt.** |
| `market_requests` mit `requested_by` = Person | **bleibt, `requested_by` wird NULL** (wie bei `resume_requests`; Migration). |
| `transfers` mit `status ∉ {accepted, completed}` | **Zeile fällt vollständig**, samt `message` und `offer_note`. |
| `transfers` mit `status ∈ {accepted, completed}` **und** `offer_fee_cents IS NULL` | **fällt** — ohne Vergütung ist kein Handelsvorgang entstanden, an dem etwas hängen könnte. |
| `transfers` mit `status ∈ {accepted, completed}` **und** `offer_fee_cents IS NOT NULL` | **Fällt ebenfalls** — Voreinstellung (§3). Die einzige Zeilenklasse neben `hired`, die der Aufbewahrungsschalter überhaupt betrifft. |
| `outbox` mit `user_id` = Person | **fällt.** |

#### github-service

`github_connections` (`id` **ist** die `subject_id`): **fällt vollständig** —
`login` (der GitHub-Name, öffentlich, hier aber mit der Person verknüpft),
`challenge`, `repositories` als JSONB.

---

### §3 Aufbewahrungspflicht — Voreinstellung ist vollständige Löschung

**Die Voreinstellung löscht alles. Auch `hired`. Auch bezahlte Transfers.**
Die Ausnahme ist ein einzelner, klar abgegrenzter Schalter, und er steht auf
**aus**.

Das ist die tragende Entscheidung dieses Abschnitts, und sie ist eine Entscheidung
über die *Beweislast*: Nicht die Löschung muss sich rechtfertigen, sondern das
Behalten. Eine ungeprüfte Vorsichtsannahme, die als Voreinstellung im Code steht,
verwandelt sich innerhalb weniger Monate in „so ist das eben" — dann liegen Daten
unbefristet herum, und niemand weiß mehr, dass die Begründung dafür nie über eine
Vermutung hinauskam. Genau das ist beim Audit-Trail schon einmal passiert:
ADR-0012 hielt 2026-07 fest, `audit_events` würden „wegen Retention" nicht mit
`users` kaskadiert — mit einem Verweis auf einen späteren Schritt, der nie kam.

**Die Einschätzung, die die Voreinstellung trägt** (vom Auftraggeber, 06.08.2026,
ausdrücklich als Einschätzung und nicht als Rechtsrat):

> Die Plattform ist nicht der Arbeitgeber. Aufbewahrungspflichten für
> Arbeitsverträge und Lohnunterlagen treffen das Unternehmen mit *seinen eigenen*
> Unterlagen, nicht einen Vermittler. Wird jemand über die Plattform eingestellt,
> liegt der Vertrag beim Arbeitgeber. Für Bewerbungsdaten gilt eher das
> Gegenteil einer Aufbewahrungspflicht: eine kurze Karenz wegen der
> AGG-Klagefrist, danach ist zu löschen — eine **Lösch**pflicht mit Karenz, keine
> Bindung über Jahre.

Trifft das zu, entfällt die Ausnahme vollständig — für `hired` genauso wie für
die bezahlten Transfers. Deshalb wird sie nicht gebaut, sondern nur *vorbereitet*.

**Der Schalter — was er genau ist:**

1. **Aus, und ein Test hält ihn dort.** Je betroffenem Dienst eine benannte
   Konstante, Voreinstellung „aus", mit dem Verweis auf diese ADR daneben. Ein
   Test pinnt den Wert. Ihn umzulegen ist ein sichtbarer Commit, den jemand
   begründen muss — kein Konfigurationswert, der sich zwischen Umgebungen
   unterscheiden kann. Bei einem Löschversprechen wäre „in Produktion anders als
   im Test" der schlimmste denkbare Zustand.
2. **Er schaltet genau zwei Zeilenklassen, nicht mehr.** Ausgeschrieben, weil
   das Abgrenzen der ganze Punkt ist:
   ```
   applications.status = 'hired'                  -- ApplicationStatus.HIRED, _FINAL
   transfers.status IN ('accepted','completed')   -- TransferStatus, _FINAL
     AND transfers.offer_fee_cents IS NOT NULL
   ```
   Keine Ausdehnung auf `rejected` (eine abgelehnte Bewerbung begründet nichts),
   keine auf `interested`/`talking`/`offered` (ein Gespräch ist kein Vertrag),
   und kein „laufender Vorgang" als Gummiwort. Es sind Endzustände, die die
   Domäne ohnehin führt — keine Heuristik und keine Zahl über einen Menschen.
3. **Keine Frist im Code, in keiner Richtung.** Weder eine geratene Dauer noch
   ein Nachlauf, der später aufräumt. Ein Nachlauf braucht eine Frist, die Frist
   braucht eine Antwort, und bis dahin gibt es sie nicht. Wird der Schalter je
   umgelegt, kommt die Frist *zusammen mit der Antwort*, nicht vorher.
4. **Ist er an, ist ausgesetzt nicht übersprungen.** Eine Zeile, die bleibt, wird
   markiert, ist abfragbar, und die Person erfährt in der Abschlussnachricht,
   *dass* etwas geblieben ist und *warum* — nicht als Fußnote in einer
   Datenschutzerklärung.

**Was die Voreinstellung für das Unternehmen bedeutet, offen gesagt:** Löscht ein
Mensch sein Konto, verschwindet auch die Bewerbung, über die er eingestellt
wurde, aus der Liste des Unternehmens. Das ist gewollt und folgt genau aus der
Einschätzung oben: die Unterlage über das Arbeitsverhältnis ist der Vertrag beim
Arbeitgeber, nicht eine Zeile bei einem Vermittler. Wer diese Zeile als
Unterlage braucht, führt sie am falschen Ort.

**Was noch offen ist, ist genau ein Satz** (siehe Offene Fragen). Er ändert nicht
den Entwurf, sondern nur die Stellung eines Schalters — und deshalb blockiert er
nichts.

---

### §4 Die Kaskade über acht Empfänger — und der Nachweis, dass sie ankam

**Ursprung ist `identity-service`.** Nicht `consent-service`, obwohl ADR-0013
ihm die GDPR-Vorgänge zuweist: der Ursprung muss das Konto beenden können, und
`users` liegt hier. Der Präzedenzfall ist der Datenexport — auch der wurde nicht
in einem Sammeldienst gebaut, sondern dort, wo er hingehört, weil ein Dienst,
der über sieben Grenzen liest, genau das ist, was ADR-0004 ausschließt. Der
Ledger bleibt die Anlaufstelle für die *Auskunft*, was gelöscht wurde; er ist
hier ein Empfänger wie die anderen.

**Empfänger sind acht Dienste:** consent, profile, resume, portfolio,
applications, transfer, github — und identity selbst, zuletzt. **Nicht**
jobs-service und companies-service: sie halten nichts Personenbezogenes (§2).
Ein Löschbefehl an einen Dienst ohne zu löschende Daten wäre ein Endpunkt, der
„erledigt" sagt, ohne je etwas zu tun. (jobs-service bekommt im Sonderfall des
letzten Admins eine **andere** Absicht — sie zählt ausdrücklich nicht in diesen
Nachweis hinein, §7.)

Damit das nicht durch Wegsehen still wird, **fällt ein Test rot, sobald
irgendein Dienst eine Tabelle mit `subject_id` oder `user_id` bekommt, die
nicht in der Empfängerliste steht.** Dieselbe Bauart wie
`tests/test_workspace_dependencies.py` und `tests/test_skill_limits_align.py`:
die Regel bewacht sich selbst, statt in einer Datei zu stehen, die niemand liest.

**Werkzeug ist die Outbox aus ADR-0025 — ohne eine einzige neue Spalte.** Die
Tabelle trägt `user_id` und `kind`, und mehr braucht ein Löschbefehl nicht. Das
ist kein glücklicher Zufall: ein Löschbefehl **hat** keinen Inhalt. Genau die
Regel, die eine `payload`-Spalte verbietet (ADR-0025 §5), macht die Outbox hier
zum passenden Werkzeug statt zum Kompromiss.

**Eine Zeile je Empfänger**, `kind = "erasure:profile"`, `"erasure:consent"`, …
(passt in `String(64)`). Alle acht entstehen in **derselben Transaktion** wie
die Zustandsänderung des Kontos.

**Der Nachweis ist die Menge der Zeilen.** Eine Löschung ist genau dann fertig,
wenn für diese `user_id` **keine Outbox-Zeile mehr ohne `delivered_at`** ist. Das
ist eine SQL-Abfrage, keine Vermutung, und sie ist je Empfänger einzeln
beantwortbar — man sieht nicht nur *dass* etwas offen ist, sondern *welcher
Dienst*.

**Vier Bedingungen, unter denen dieser Nachweis trägt:**

1. **Der Zusteller muss scheitern können.** Heute kann er das nicht. Der
   produktive `HttpNotifier` fängt **jede** Ausnahme und prüft die Antwort
   nicht (`infrastructure/notify.py:47–64`, wortgleich in transfer-,
   applications- und resume-service). `OutboxDispatcher._deliver` setzt
   `delivered_at`, sobald `notify` ohne Ausnahme zurückkehrt
   (`worker_outbox/__init__.py:208–229`). Ein `ConnectError` oder ein `500`
   wird damit als **zugestellt** verbucht. Bewiesen ist der Wiederholungspfad
   nur gegen `RecordingNotifier`, und der **wirft**
   (`test_market_requests.py:46–49`) — ein Attrappe, die sich anders verhält als
   der Code, den sie vertritt. Für die Löschung ist das tödlich: `delivered_at`
   wäre exakt die Lüge, die diese ADR verhindern soll. Es braucht einen
   **eigenen Zustell-Adapter**, der bei Transportfehler *und* bei Nicht-2xx
   wirft. Der Notifier bleibt, wie er ist — sein Schlucken ist für eine Mail
   richtig (ADR-0025) und nur hier fatal. *Nebenbefund, eigener Schnitt: damit
   ist auch die heutige Zusage aus ADR-0025 schwächer als sie klingt.*
2. **Der Empfänger muss doppelte Zustellung vertragen.** „Mindestens einmal"
   heißt hier: zweimal löschen. Ein `DELETE` auf eine Zeile, die schon weg ist,
   ist von Natur aus idempotent — und der Endpunkt antwortet dann **2xx, nicht
   404**. Ein 404 sähe für den Zusteller wie ein Fehlschlag aus und er würde
   ewig wiederholen, was längst erledigt ist.
3. **Aufgeben ist hier nicht erlaubt.** `MAX_ATTEMPTS = 10`, und `pending()`
   filtert `attempts < max_attempts` (`__init__.py:186`) — die Zeile bleibt
   zwar stehen, wird aber **nie wieder versucht**. Für eine Benachrichtigung ist
   „liegenlassen statt löschen" richtig; für eine Löschung wäre es das stille
   Scheitern, gegen das die ganze Konstruktion antritt. Die Löschzustellung läuft
   deshalb **ohne Versuchsobergrenze**, mit wachsendem Abstand zwischen den
   Versuchen.
4. **Der Endpunkt braucht ein eigenes Geheimnis.** Muster wie bei
   `/notifications`: geteiltes Geheimnis im Header, `404` statt `401` bei
   falschem Wert, damit der Endpunkt sich nicht selbst bestätigt
   (`notification_router.py:55–60`). Aber ein **anderer** Schlüssel als der
   Mail-Auslöser: „darf eine Mail anstoßen" und „darf alles über einen Menschen
   löschen" dürfen nicht dasselbe Papier sein.

**Wenn ein Dienst dauerhaft nicht antwortet**, passiert genau das: die Zeile
bleibt offen, der Zusteller versucht weiter, und **die Löschung gilt nicht als
fertig**. Es gibt keine Frist, nach der sie sich selbst für erledigt erklärt.
Die Person bekommt keine Abschlussnachricht, die nicht stimmt; der offene Rest
ist abfragbar und gehört auf die Betriebsübersicht. Das ist die einzige ehrliche
Antwort: eine Löschung, die einen Dienst nicht erreicht hat, **ist** nicht
fertig, und keine Zeitüberschreitung macht sie fertig.

---

### §5 Was im Ledger stehen bleibt

**Es bleibt stehen:** `event_id`, `subject_id`, `capability`, `action`,
`recorded_at`, `actor_id` — die vollständige Kette aus Erteilungen,
Widerrufen und, am Ende, je einer `DELETE`-Zeile. Das ist der Beleg: *diese
Kennung hat diese Erlaubnisse erteilt, an diesen Tagen zurückgezogen, und am
Ende ihre Löschung verlangt.* Ihn mitzulöschen hieße, die Löschung
unbeweisbar zu machen — und die Behauptung „wir haben gelöscht" gegen nichts
mehr prüfbar.

**Es fällt heraus:** `reason` (→ `NULL`) und `metadata` (→ `{}`). Der Grund ist
Freitext, den ein Mensch über sich selbst geschrieben hat — das einzige
wirklich personenbezogene Feld im Ledger, und der Beleg braucht es nicht: *dass*
widerrufen wurde, steht in `action`. Dieselbe Behandlung trifft
`audit_events.metadata` in beiden Diensten.

**Vereinbarkeit mit Art. 17 — das Argument, und wo es endet.**

Das tragende Argument ist nicht „wir dürfen aufbewahren", sondern **was
übrigbleibt, ist keine Auskunft über einen Menschen mehr**: nach der Löschung
gibt es im ganzen System keine Abbildung `subject_id → Mensch`. Adresse, Name
und Passwort-Hash lagen ausschließlich in `identity_service.users`, und die
Zeile fällt (§2 — nachgesehen: keine andere Tabelle in keinem anderen Dienst
trägt eine E-Mail- oder Namensspalte). Zurück bleiben UUIDs, Capability-Namen
und Zeitstempel.

**In der Voreinstellung trägt dieses Argument vollständig** — und das ist der
zweite, weniger offensichtliche Gewinn der Entscheidung aus §3: solange keine
`applications`- oder `transfers`-Zeile stehenbleibt, existiert nirgends mehr ein
Weg von der `subject_id` zu einem Menschen.

**Zwei Einschränkungen, die dazugehören, weil sie das Argument begrenzen:**

- **Ein umgelegter Aufbewahrungsschalter hielte den Schlüssel am Leben.** Bliebe
  wegen §3 eine `applications`- oder `transfers`-Zeile stehen, kennte das
  Unternehmen die Person weiterhin und könnte sie über `subject_id` mit dem
  Ledger zusammenbringen. Das Übrige wäre dann pseudonym, nicht anonym. Die
  beiden Fragen hängen also zusammen, und zwar in die gute Richtung: bestätigt
  sich die Einschätzung aus §3, bleibt der Schalter aus und dieses Argument
  ungeschmälert.
- **Ob das rechtlich „anonym" heißt, ist keine Frage, die Code beantwortet.**
  Diese ADR entscheidet, *was* stehenbleibt und *warum es fachlich das Minimum
  ist*. Die Einordnung gehört zur offenen Frage und wird zusammen mit ihr
  beantwortet — nicht hier erfunden.

---

### §6 Wie die Person erfährt, dass es fertig ist

**Sofort und sichtbar:** Bestätigung auf der Seite, alle Sitzungen widerrufen,
Konto auf `DISABLED` (der Zustand existiert bereits). Ab diesem Moment passiert
nichts mehr unter diesem Namen, auch wenn die Kaskade noch läuft.

**Kein Fortschrittsbalken, keine Statusseite.** Wer sich noch anmelden könnte,
um zuzusehen, hätte ein Konto, das noch funktioniert — und genau das soll nicht
mehr stimmen. Die Auskunft kommt in **einer** Nachricht am Ende.

**Die Abschlussnachricht ist die letzte Handlung vor der eigenen Löschung.**
Reihenfolge, und sie ist erzwungen, nicht empfohlen:

1. Alle sieben fremden Empfänger haben quittiert (`delivered_at` gesetzt, §4).
2. Die Abschlussnachricht ist zugestellt — **selbst eine Outbox-Zeile**, also
   ebenfalls wiederholbar und ebenfalls quittiert.
3. **Erst danach** fallen `users` und die daran hängenden Zeilen.

Der Grund ist unromantisch: die Nachricht braucht die Adresse, und die Adresse
liegt in der Zeile, die gelöscht werden soll. Sie in die Outbox mitzunehmen wäre
die naheliegende Abkürzung und ist ausdrücklich verboten — eine Outbox ist ein
dauerhafter Speicher und landet in jedem Backup (ADR-0025 §5); eine
E-Mail-Adresse dort abzulegen wäre ausgerechnet beim Löschen das Gegenteil
dessen, was verlangt wurde.

**Inhalt der Nachricht:** dass es fertig ist. In der Voreinstellung ist das der
ganze Text — es bleibt nichts, über das zu berichten wäre. Nur bei umgelegtem
Schalter (§3) kommt hinzu, was geblieben ist und warum. **Nicht** darin: eine
Aufstellung dessen, was die Person hatte. Eine
Abschiedsmail, die auflistet, was gerade gelöscht wurde, ist eine Kopie der
Daten in einem Postfach, das womöglich nicht nur ihr gehört — dieselbe
Überlegung, aus der der Datenexport nicht per Mail zugestellt wird.

Zwischen Schritt 2 und 3 liegt ein Takt des Zustellers (Voreinstellung 5 s), in
dem „fertig" gesagt ist und die Kontozeile noch existiert. Das wird hier
benannt statt weggerechnet: das Konto ist in diesem Fenster gesperrt und
sitzungslos, und die Alternative wäre, die Adresse anderswo zu kopieren.

---

### §7 Der letzte Admin — und die Adresse danach

**Löscht die einzige Person mit `role='admin'` ihr Konto, wird das Unternehmen
stillgelegt und seine Anzeigen zurückgezogen.** Nicht: die Löschung blockieren,
bis jemand anderes Admin ist. Ein persönliches Recht darf nicht an einer
Organisationsfrage hängen — sonst kann jemand sein Konto nicht löschen, nur weil
niemand sonst die Rolle trägt. Und eine unbeaufsichtigte Stellenanzeige ist
schlechter als keine: Bewerbungen liefen an niemanden.

Erkennbar bleibt der Fall vollständig **innerhalb von identity-service**: nach
dem Entfernen der Mitgliedschaften zählt es die verbliebenen `admin`-Zeilen in
`user_tenant_memberships` für diesen Tenant. Null heißt stilllegen. `tenants`
braucht dafür eine Statusspalte (V5) — heute trägt es nur `id`, `name`, `domain`,
`created_at`.

**Und das Zurückziehen der Anzeigen hängt ausdrücklich NICHT im Nachweis der
Löschung.** Es ist eine eigene Absicht mit eigener Outbox-Zeile an jobs-service,
die **nicht** in die Vollständigkeitsprüfung aus §4 eingerechnet wird. Sonst
könnte ein stiller jobs-service die Löschung eines Menschen offenhalten — genau
die Kopplung, die der erste Absatz ausschließt. Die Löschung ist fertig, wenn die
personenbezogenen Daten weg sind; was mit den Anzeigen eines Unternehmens
geschieht, ist eine Frage des Unternehmens.

Damit ist jobs-service Empfänger *dieser einen* Absicht, aber weiterhin **kein
Empfänger der Löschkaskade** (§4) — er hält nach wie vor nichts
Personenbezogenes.

**Die Adresse ist nach der Löschung wieder frei.** Wer sich erneut anmelden will,
kann das. Die Alternative wäre ein dauerhafter Rest ausgerechnet der Angabe, die
gelöscht werden sollte — siehe „Kein Grabstein".

---

## Was ausdrücklich NICHT gebaut wird

- **Kein Papierkorb, keine Reue-Frist.** Dreißig Tage „falls Sie es sich anders
  überlegen" heißen: die Daten liegen weiter da, und „gelöscht" heißt
  „vielleicht gelöscht". Gegen den Fehlklick hilft die bewusste Bestätigung vor
  der Tat, nicht das Aufbewahren danach.
- **Kein `erasure-service`.** Er müsste dienstübergreifend lesen (gegen
  ADR-0004) und wäre ein zweiter Ort mit personenbezogenen Daten — dieselbe
  Begründung, mit der ADR-0026 den `analytics-service` verworfen hat.
- **Kein Adminlöschen.** Niemand außer der Person löscht ihr Konto. Der Ledger
  weist die Delegation heute schon zurück (`ConsentSubjectMismatch`); ein
  Löschknopf für Fremde wäre die mächtigste Delegation im System und braucht
  eine eigene Entscheidung, keine Nebenwirkung dieser hier.
- **Kein Grabstein.** Kein E-Mail-Hash, der weiterlebt, um Neuanmeldungen zu
  erkennen — das wäre ein dauerhaftes Personendatum, angelegt in dem Moment, in
  dem jemand um Löschung bittet.
- **Keine Zahl über die Person.** Keine „gelöschten Konten je Woche" mit Bezug
  auf einzelne Menschen, keine Löschquote je Unternehmen, kein „hat X-mal etwas
  widerrufen". Was gezählt werden darf, richtet sich nach ADR-0026: eine Zahl,
  die keine Auskunft erzeugt, die der Fragende nicht ohnehin hat.
- **Keine Pseudonymisierung als Ersatz für Löschung.** Die Zeile wird *gelöscht*,
  nicht „anonymisiert". Ein Anschreiben ohne Namen bleibt der Text, den dieser
  Mensch geschrieben hat.
- **Kein Zwang zum Export vorher.** Die Seite verweist auf `/meine-daten`; wer
  ohne Herunterladen löschen will, darf das.
- **Keine Aufbewahrungslogik über den einen Schalter hinaus** (§3). Kein
  Nachlauf, keine Frist, keine zweite Bedingung, kein Konfigurationswert je
  Umgebung. Was nicht gebaut ist, kann auch nicht versehentlich angehen.

---

## Die eine offene Frage — und sie blockiert nichts

**Anwaltlich zu bestätigen ist genau ein Satz:**

> Trifft WorkerTransfer als Vermittlungsplattform eine **eigene** handels- oder
> steuerrechtliche Aufbewahrungspflicht für Bewerbungs- und Transferdaten — oder
> trifft sie ausschließlich die beteiligten Unternehmen für ihre eigenen
> Unterlagen?

**Lautet die Antwort „ausschließlich die Unternehmen", ist nichts zu tun:** der
Schalter aus §3 bleibt aus, wird beim nächsten Aufräumen entfernt, und diese ADR
verliert ihren letzten Vorbehalt. Lautet sie anders, wird der Schalter umgelegt
— und dann, und erst dann, kommen die Anschlussfragen mit ihren Antworten
zusammen: wen die Pflicht trifft, ob die Plattform Verantwortliche oder
Auftragsverarbeiterin ist, ob der Freitext (`applications.message`,
`transfers.message`, `offer_note`) mitumfasst ist oder nur der Vorgang, welche
Dauer gilt, und ob hinter `offer_fee_cents` eine eigene Pflicht mit eigener
Frist steht.

Diese Fragen stehen hier bewusst **als Anschluss und nicht als Voraussetzung**.
Sie vorab zu beantworten hieße, für einen Fall zu planen, der nach der
Einschätzung in §3 wahrscheinlich nicht eintritt — und jede vorsorgliche Antwort
darauf würde als Verhalten im Code landen.

**Was dagegen entschieden ist und nicht mehr offensteht** (06.08.2026): der letzte
Admin legt das Unternehmen still (§7), und die Adresse ist nach der Löschung
wieder frei (§7). Beides sind Produktentscheidungen, beide sind getroffen.

---

## Voraussetzungen vor dem ersten Test

- **V1** Ein Zustell-Adapter für die Löschung, der bei Transportfehler und bei
  Nicht-2xx **wirft** (§4.1). Ohne ihn ist `delivered_at` bedeutungslos und der
  gesamte Nachweis aus §4 wertlos.
- **V2** `identity-service` braucht eine Outbox-Tabelle — es hat heute keine
  (`build_outbox_table` steht nur in transfer-, applications- und
  resume-service).
- **V3** Migrationen für die Nullbarkeit: `company_invitations.invited_by`
  (zusätzlich `CASCADE` → `SET NULL`), `resume_requests.requested_by`,
  `market_requests.requested_by`.
- **V4** Der Wächtertest über die Empfängerliste (§4).
- **V5** `tenants` braucht eine Statusspalte für die Stilllegung (§7) — heute
  trägt die Tabelle nur `id`, `name`, `domain`, `created_at`.

---

## Konsequenzen

- Die Plattform verspricht ab dann nur noch, was sie tut: entweder das Konto
  ist gelöscht, oder die Person hat keine Abschlussnachricht bekommen.
- Ein Dienst, der dauerhaft nicht antwortet, **verhindert** den Abschluss einer
  Löschung. Das ist gewollt und der Preis dafür, dass „fertig" etwas bedeutet.
  Es macht die Löschung zum ersten Vorgang im System, der einen Betriebsfehler
  nicht wegschluckt.
- `deleted=True` im Ledger bekommt genau einen Erzeuger und wird damit erstmals
  aussagekräftig.
- **Es bleibt nichts stehen.** Wer sein Konto löscht, verliert auch die
  eingestellte Bewerbung und den bezahlten Transfer. Für Unternehmen heißt das:
  die Unterlage über ein Arbeitsverhältnis ist der Vertrag beim Arbeitgeber,
  nicht eine Zeile bei einem Vermittler.
- Der Aufbewahrungsfall ist damit **kein Zustand, sondern ein Commit**. Solange
  ihn niemand begründet umlegt, kann er nicht versehentlich gelten — und
  niemand kann später eine Vermutung für eine getroffene Entscheidung halten.
- Der Befund aus §4.1 betrifft **auch die heutigen Benachrichtigungen**: der
  Zusammenhang „Zeile zugestellt ⇒ Mail angekommen" hält im produktiven Code
  nicht. Eigener Schnitt, nicht Teil dieser Löschung, aber vor ihr fällig.
- **Der `Storage`-Port bleibt unverändert.** `list_names` + `delete` reichen,
  und das Waisen-Aufräumen benutzt beide bereits genau so. Das zurückbleibende
  leere Verzeichnis wird **innerhalb von `LocalStorage`** entfernt, nicht über
  eine neue Port-Methode: „Verzeichnis" ist ein Begriff des Dateisystems, den
  ein Objektspeicher gar nicht kennt — dort verschwindet ein Präfix von selbst,
  sobald das letzte Objekt weg ist. Ein `rmdir` am Port wäre das lokale Backend,
  das in die Naht durchschlägt, die es verbergen soll (ADR-0021).

---

## Verifikation (was der Bauschritt beweisen muss)

- **Zuerst rot.** Jeder Test läuft einmal gegen den ungefixten Code. In dieser
  Codebasis sind mehrfach Tests entstanden, die nicht fehlschlagen konnten.
- **Je Dienst ein Integrationstest, der die Datenbank abfragt** — nicht, dass
  ein Endpunkt 200 sagt, sondern dass die Zeile weg ist.
- **Die Voreinstellung löscht auch `hired` und die bezahlten Transfers** — ein
  Test je Zeilenklasse, der genau das beweist. Das ist die Umkehrung des
  ursprünglichen Entwurfs und die Stelle, an der ein stillschweigendes
  Zurückrutschen in die Vorsichtsannahme auffallen muss.
- **Der Schalter steht auf aus**, festgenagelt je betroffenem Dienst. Dieser
  Test ist der billigste im ganzen Schnitt und bewacht die Entscheidung, die am
  leichtesten verlorengeht.
- **Der umgelegte Schalter hält genau zwei Zeilenklassen und keine dritte.**
  Er wird im Test umgelegt, nie in der Voreinstellung: sonst prüft niemand, ob
  die Abgrenzung hält, und der Schalter wäre unbenutzter Code mit einer
  Behauptung daran.
- **Zweimal zustellen ändert nichts** (Idempotenz, §4.2).
- **Ein dauerhaft toter Empfänger** führt nie zu `delivered_at` und nie zu einer
  Abschlussnachricht (§4.3) — und dieser Test muss gegen einen Zusteller laufen,
  der sich wie der produktive verhält, nicht wie eine Attrappe, die wirft.
- **Der Ledger überlebt:** die Zeilen stehen noch, `reason IS NULL`,
  `metadata = '{}'`, und je Capability existiert eine `DELETE`-Zeile.
- **Anhänge und Verzeichnis sind weg**, geprüft über den Speicher, nicht über
  die Portfolio-Antwort.
- **Der Wächtertest** wird rot, wenn eine neue Tabelle mit `subject_id` oder
  `user_id` entsteht, die kein Empfänger ist.
