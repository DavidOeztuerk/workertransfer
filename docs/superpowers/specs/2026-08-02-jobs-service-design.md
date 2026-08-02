# Sub-step 4.1 — Jobs-Service: die erste Sache, die einem Unternehmen gehört

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0017 (Tenant ist ein Unternehmensbegriff), ADR-0018 (Mitgliedschaft), ADR-0020 (Consent als Enabler), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 4

## Was hier anders ist als in Phase 3

Profil, Lebenslauf und Portfolio gehören einer **Person**. Ihre Schlüssel sind
`subject_id`, und ihre Sichtbarkeit beantwortet der Consent-Ledger.

Eine Stellenausschreibung gehört einem **Unternehmen**. Sie ist damit die erste
Sache im System, für die der Tenant die Achse ist und nicht ein Nebenattribut —
genau der Fall, für den ADR-0017 den Tenant vorgesehen hat.

Daraus folgt sofort: **der Consent-Ledger kommt hier nicht vor.** Eine
Ausschreibung ist eine Aussage des Unternehmens über sich selbst, keine
Information über eine Person. Sie zu lesen braucht keine Einwilligung, denn es
gibt niemanden, der einwilligen könnte. Der Ledger kehrt zurück, sobald sich
jemand bewirbt — dann geht es wieder um die Daten eines Menschen.

Das ist keine Lockerung, sondern die Anwendung derselben Regel: gefragt wird,
wo eine Person betroffen ist.

## Domäne

```
Job
  id            UUID
  tenant_id     UUID       das Unternehmen — die Achse
  title         ≤ 160, Pflicht
  description   ≤ 20000, Pflicht
  location      ≤ 160        leer heißt „nicht angegeben", nicht „überall"
  remote        NONE | HYBRID | FULL
  employment    FULL_TIME | PART_TIME | CONTRACT | INTERNSHIP
  status        DRAFT | PUBLISHED | CLOSED
  published_at  datetime | None
  created_at / updated_at
```

**Drei Zustände, zwei Übergänge, keine Rückwege.** `DRAFT → PUBLISHED →
CLOSED`. Eine geschlossene Ausschreibung wird nicht wieder veröffentlicht: wer
erneut sucht, sucht etwas anderes, auch wenn der Titel gleich lautet. Ein
Rückweg würde eine Bewerbungshistorie an eine Stelle hängen, die es so nicht
mehr gibt.

**`remote` ist ein Aufzählungstyp, kein Boolescher Wert.** „Remote möglich" ist
die Frage, die alle stellen, und „ja/nein" beantwortet sie falsch: hybrid ist
der häufigste Fall und keine Zwischenstufe von wahr.

**`location` darf leer sein und heißt dann „nicht angegeben".** Nicht
„überall" — das wäre eine Behauptung, die das Unternehmen nicht gemacht hat.

**Bearbeiten ist erlaubt, auch nach dem Veröffentlichen.** Eine Ausschreibung
mit einem Tippfehler zurückzuziehen und neu zu stellen würde Bewerbungen
zerreißen. Was sich ändert, ändert sich sichtbar (`updated_at`).

## Wer darf was

| Handlung | Wer |
|---|---|
| anlegen, bearbeiten, veröffentlichen, schließen | jedes **Mitglied** des Unternehmens |
| Entwürfe und geschlossene sehen | dasselbe |
| veröffentlichte lesen und durchsuchen | **jeder**, auch ohne Anmeldung |

**Jedes Mitglied, nicht nur Administratoren.** Ausschreiben ist die Arbeit, für
die jemand ins Unternehmen geholt wurde; sie an die Administratorenrolle zu
binden würde die Rolle „Mitglied" bedeutungslos machen. Administratoren
verwalten die Mannschaft (Scheibe C), nicht die Inhalte.

**Öffentlich lesbar, ohne Anmeldung.** Eine Stellenausschreibung, die man nur
angemeldet sieht, ist keine Ausschreibung. Das ist der erste Endpunkt im System
ohne Authentifizierung — und er ist es bewusst.

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `POST` | `/jobs` | Mitglied | die neue Ausschreibung (`DRAFT`) |
| `PUT` | `/jobs/{id}` | Mitglied desselben Unternehmens | die geänderte |
| `POST` | `/jobs/{id}/publish` | dasselbe | die veröffentlichte |
| `POST` | `/jobs/{id}/close` | dasselbe | die geschlossene |
| `GET` | `/companies/me/jobs` | Mitglied | alle eigenen, jeden Status |
| `GET` | `/jobs` | jeder | veröffentlichte, mit Filtern und Cursor |
| `GET` | `/jobs/{id}` | jeder | eine veröffentlichte |

**Statuscodes**

| Situation | Antwort |
|---|---|
| fremdes Unternehmen, oder Ausschreibung existiert nicht | `404`, ununterscheidbar |
| kein aktives Unternehmen bei einer schreibenden Handlung | `403` (Aussage über den Aufrufer) |
| `GET /jobs/{id}` auf einen Entwurf oder eine geschlossene | `404` — für die Öffentlichkeit gibt es sie nicht |
| Übergang, den es nicht gibt (z. B. `CLOSED → PUBLISHED`) | `409` |

Eine fremde Job-ID verhält sich wie eine, die es nicht gibt: sonst wäre der
Endpunkt ein Orakel darüber, welche Unternehmen wie viele Stellen ausschreiben —
eine Information, die im Wettbewerb etwas wert ist.

## Suche

Volltext über Titel und Beschreibung (`ILIKE` auf beiden), dazu Filter für
`location`, `remote` und `employment`. Cursor-Pagination über
`(published_at, id)` absteigend — dieselbe Bauart wie die Kandidatenliste, weil
sie dasselbe Problem löst.

**Kein Relevanz-Ranking in diesem Schnitt.** Ein Ranking ist eine Aussage
darüber, was wichtiger ist, und die will begründet sein. Chronologisch ist
ehrlich und nachvollziehbar; ein Ranking kommt, wenn es eine Grundlage hat.

## Abgrenzung

**Keine Bewerbungen** — das ist 4.2, und dort kehrt der Consent-Ledger zurück.
**Kein Matching** — Phase 4 nennt es „explainable, kein hidden
Employability-Score", und das ist ein eigener Schnitt mit eigener Begründung.
**Keine Karriere-Seiten** und **keine Connectoren** (ohne offizielle API nicht
gebaut, ADR-0004).

## Umsetzung in zwei Scheiben

**Scheibe A** — Dienst über `worker new-service`, Domäne, Migration,
Repository, alle sieben Endpunkte, Integrationstests inklusive der Frage, ob
ein fremdes Unternehmen etwas sieht.

**Scheibe B** — Oberfläche: `/jobs` (öffentliche Suche), `/company/jobs`
(verwalten). Playwright-Reise: anlegen → veröffentlichen → als Fremder finden →
schließen → verschwindet.

## Selbstprüfung

*Warum kein `ARCHIVED` neben `CLOSED`?* Weil niemand sagen könnte, worin der
Unterschied besteht. Zwei Zustände, die dasselbe bedeuten, werden verschieden
benutzt und driften.

*Warum darf ein Mitglied veröffentlichen, ohne dass jemand gegenliest?* Weil
eine Freigabestufe eine Organisationsentscheidung ist, die je Unternehmen
anders ausfällt — sie hier festzuschreiben hieße, allen dieselbe aufzuzwingen.
Wer sie braucht, bekommt sie später als Einstellung, und `DRAFT` ist schon der
Platz dafür.

*Ist „öffentlich ohne Anmeldung" ein Risiko?* Es ist die Voraussetzung dafür,
dass eine Ausschreibung ihren Zweck erfüllt. Was dort steht, hat das
Unternehmen selbst geschrieben und selbst veröffentlicht. Personenbezogene
Daten kommen erst mit der Bewerbung ins Spiel — und dort greift der Ledger.
