# Phase 6, Sub-step 6.1 — GitHub als Beleg, nicht als Note

Date: 2026-08-03
Status: Entwurf (selbst geprüft)
Related: **ADR-0022** (`worker-github` gelöscht), ADR-0004 (kein Scraping, consent-first), ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 6

## Wo dieser Schnitt anfängt

ADR-0022 hat das alte `worker-github` gelöscht, weil es Menschen zu einer Zahl
zwischen 0 und 100 verrechnete. Die ADR sagt auch, was wiederkommen darf:
**Belege mit Herkunft, Einwilligung zuerst, Sichtbarkeit über den Ledger** — und
was nicht: ein Gesamtscore, abgeleitete Eigenschaften ohne Grundlage,
stillschweigende Vollständigkeit.

Dieser Schnitt baut genau das Erlaubte.

## Was gezeigt wird

Öffentliche Repositories, die der Person gehören. Je Eintrag: Name,
Beschreibung, Hauptsprache, Sterne, letzte Änderung, **Link**.

Mehr nicht. Keine Punktzahl, keine Reihung nach „Qualität", keine abgeleiteten
Eigenschaften. Wer wissen will, ob der Code gut ist, klickt auf den Link — das
ist die einzige ehrliche Bewertung, die dieses System anbieten kann.

**Die Sprachen kommen von GitHub, nicht aus einer Rechnung.** GitHub sagt, was
die Hauptsprache eines Repositories ist; die Plattform gibt das weiter. Sie
leitet daraus **kein Können** ab — der alte Code maß Können als Anteil an
geschriebenen Bytes, und eine eingecheckte Abhängigkeit schlug damit jede
sorgfältige Bibliothek.

## Der Nachweis: ein öffentlicher Gist

Ohne OAuth-App (die braucht eine Registrierung und ein Geheimnis, das es hier
noch nicht gibt) und trotzdem beweisbar:

1. Die Person nennt ihren GitHub-Benutzernamen.
2. Die Plattform gibt ein einmaliges Kürzel aus.
3. Die Person legt einen **öffentlichen Gist** an, dessen Beschreibung genau
   `workertransfer-verify-<kürzel>` lautet.
4. Die Plattform ruft `GET /users/<login>/gists` ab und sucht die Beschreibung.

Findet sie sie, ist erwiesen, dass die Person über das Konto verfügt. Danach darf
der Gist weg.

Dieselbe Form wie der Domain-Nachweis bei Unternehmen (ADR-0019): **erst
beweisen, dann behaupten.** Ein Feld „mein GitHub-Name" ohne Nachweis wäre eine
Einladung, sich mit fremder Arbeit zu schmücken — und das Opfer erführe es nie.

Eine Beschreibung statt eines Dateiinhalts, weil die Gist-Liste sie mitliefert:
**ein Abruf statt einem je Gist.** Das ist bei 60 Anfragen pro Stunde (ohne
Token) kein Detail.

## Die tragende Entscheidung: einmal lesen, nicht zusehen

Die Plattform holt die Repositories **wenn die Person es auslöst** — beim
Verbinden und wenn sie auf „Aktualisieren" drückt. Danach steht ein Abzug in der
eigenen Datenbank, mit Zeitstempel, und die Anzeige sagt „Stand: …".

**Kein Hintergrundabgleich, kein Nachtlauf, kein Webhook.**

ADR-0004 verbietet Scraping. Der Buchstabe wäre mit einem periodischen Abruf
eingehalten — man liest ja nur, was öffentlich ist und wozu eingewilligt wurde.
Der Sinn wäre es nicht: **eine Plattform, die einem Menschen dauerhaft
hinterhersieht, tut etwas anderes als eine, die einmal auf seine Bitte hinsieht.**
Der Unterschied steht in keiner Antwort und in jedem Protokoll.

Nebenbei löst es das Ratenlimit: die Zahl der Abrufe hängt an Handlungen von
Menschen, nicht an einer Uhr.

## Sichtbarkeit

```
github.visibility:public
```

Wie beim Portfolio, und aus demselben Grund: die Repositories sind ohnehin
öffentlich.

**Das Heikle ist nicht der Code, sondern die Verbindung.** Wer auf
WorkerTransfer unter einem anderen Namen auftritt als auf GitHub, wird durch
diese Verknüpfung identifizierbar. Deshalb liegt sie im Ledger wie alles andere
und wirkt ein Widerruf sofort (ADR-0013).

**Der Abzug bleibt beim Widerruf liegen, wird aber nicht mehr herausgegeben** —
dieselbe Regel wie beim Profil: gespeichert ist nicht gezeigt. Wer ihn wirklich
los sein will, trennt die Verbindung; dann wird gelöscht.

## Domäne

```
GitHubConnection
  subject_id   UUID (PK)     eine Verbindung je Person
  login        ≤ 39          GitHubs Grenze
  verified_at  datetime | None
  challenge    ≤ 64          das einmalige Kürzel
  fetched_at   datetime | None
  repositories [Repository]  der Abzug

Repository
  name, description, language | None, stars, url, pushed_at
```

**Eine Verbindung je Person.** Wer ein zweites Konto verbinden will, ersetzt das
erste. Zwei gleichzeitig wären eine Liste, und eine Liste wirft die Frage auf,
welches „das echte" ist.

**`challenge` bleibt nach dem Nachweis stehen**, damit ein zweiter Versuch
denselben Gist benutzen kann. Sie ist kein Geheimnis: sie beweist nur, dass
jemand, der über das Konto verfügt, sie dort hingeschrieben hat.

## Endpunkte (`github-service`)

| Methode | Pfad | Wer |
|---|---|---|
| `POST` | `/github/me` | Person — Login nennen, Kürzel bekommen |
| `POST` | `/github/me/verify` | Person — Nachweis prüfen und Abzug holen |
| `POST` | `/github/me/refresh` | Person — Abzug erneuern |
| `GET` | `/github/me` | Person — eigener Stand, auch unbestätigt |
| `DELETE` | `/github/me` | Person — Verbindung trennen, Abzug löschen |
| `GET` | `/github/{subject_id}` | Unternehmen — nur mit Freigabe |

`404` für „gibt es nicht ODER nicht freigegeben", `403` ohne aktives
Unternehmen, `503` wenn der Ledger schweigt — wie überall.

**`GET /github/me` zeigt auch die unbestätigte Verbindung**, sonst sähe die
Person nach Schritt 1 gar nichts und wüsste nicht, welches Kürzel sie eintragen
soll.

## Abgrenzung

**Kein Skill-Graph, kein Matching.** Beides steht in Phase 6 und braucht eine
eigene Abwägung; aus Belegen eine Rangfolge zu machen ist genau der Schritt, den
ADR-0022 verboten hat.
**Keine privaten Repositories.** Sie zu lesen bräuchte OAuth mit weitreichenden
Rechten, und der Gegenwert wäre eine Anzeige, die niemand nachprüfen kann.
**Keine Beiträge zu fremden Repositories.** Die Events-API ist lückenhaft und
90 Tage kurz; eine unvollständige Liste, die vollständig aussieht, ist schlimmer
als keine.
**Keine Sterne-Sortierung als Voreinstellung.** Sortiert wird nach letzter
Änderung — Sterne messen Sichtbarkeit, nicht Arbeit.

## Selbstprüfung

*Ist ein Gist nicht umständlich?* Ja. Die Alternative ohne Nachweis ist
schlimmer, und OAuth kommt, wenn es eine registrierte App gibt — dann tritt es
an die Stelle des Gists, und die Verbindungen bleiben gültig.

*Warum ein eigener Dienst?* Weil er als einziger nach außen spricht und ein
Ratenlimit sowie einen Abzug verwaltet. Im profile-service läge ein
Fremdsystem-Client neben Daten, die die Person selbst eingegeben hat — zwei
Dinge mit ganz verschiedenen Ausfallarten.

*Was, wenn GitHub nicht antwortet?* Beim Verbinden: ein ehrlicher Fehler, kein
halber Zustand. Beim Lesen: der letzte Abzug mit seinem Zeitstempel — er war
einmal wahr, und das steht dabei.
