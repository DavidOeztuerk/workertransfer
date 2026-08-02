# Sub-step 3.3 — Resume-Service: der Lebenslauf wird angefragt, nicht veröffentlicht

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler in der Praxis), ADR-0017/0018 (Tenant = Unternehmen, Mitgliedschaft), [product-scope.md](../../product-scope.md), [Profile-Service-Design](2026-08-01-profile-service-design.md)

## Warum das anders aussieht als der Profile-Service

Das Profil ist ein Aushang: Überschrift, Ort, Fähigkeiten. Es freizugeben heißt
„ich bin ansprechbar", und ein Schalter „für Unternehmen sichtbar" ist dafür die
richtige Form.

Ein Lebenslauf ist etwas anderes. Er nennt **echte Arbeitgeber mit Zeiträumen** —
also genau die Information, die der aktuelle Arbeitgeber nicht sehen soll. Wer
seinen Lebenslauf auf einer Jobplattform „öffentlich" schaltet, riskiert
handfeste Nachteile, und dieses Risiko ist der Hauptgrund, warum Menschen sich
auf solchen Plattformen nicht ehrlich zeigen.

Deshalb gibt es hier **keinen Öffentlich-Schalter**. Der Lebenslauf wird einem
**einzelnen Unternehmen** freigegeben, und zwar auf dessen Anfrage hin. Der
Normalzustand ist: niemand hat ihn.

Das ist auch die These der Plattform in ihrer kleinsten Form — nicht „wir haben
deine Daten und du darfst widersprechen", sondern „niemand hat sie, bis du sie
gibst".

## Der Ledger kann das bereits

`Capability` erlaubt drei durch Doppelpunkt getrennte Segmente:

```
^[a-z][a-z_.]+(:\w+)?(:[{]?[\w-]+[}]?)?$
```

`resume.visibility:tenant:550e8400-e29b-41d4-a716-446655440000` ist damit gültig
(nachgeprüft). Das Muster war erkennbar für empfängerbezogene Einwilligungen
gebaut — die geschweiften Klammern im dritten Segment sehen nach einer
Template-Form aus. Es muss also **nichts am Consent-Service geändert werden**.

Eine Einwilligung, die einen Empfänger nennt, ist die genauere Form derselben
Sache: `profile.visibility:public` sagt „alle Unternehmen",
`resume.visibility:tenant:<id>` sagt „dieses eine". Beide gehen durch denselben
Endpunkt, dasselbe Ereignisprotokoll, denselben Widerruf.

## Domäne

**Ein Lebenslauf je Person**, `subject_id` ist der Schlüssel — wie beim Profil.
Mehrere Fassungen je Bewerbung wären ein Merkmal der Bewerbung, nicht des
Lebenslaufs, und würden hier nur eine Auswahl erzwingen, die niemand treffen
will.

```
Resume
  subject_id      UUID  (PK)
  positions       [Position]   ≤ 40
  education       [Education]  ≤ 20
  updated_at

Position                          Education
  employer      ≤ 160, Pflicht      institution ≤ 160, Pflicht
  title         ≤ 160, Pflicht      qualification ≤ 160
  started_on    Monat (YYYY-MM)     started_on  Monat
  ended_on      Monat | None        ended_on    Monat | None
  description   ≤ 2000
```

**Monatsgenau, nicht taggenau.** Kein Lebenslauf der Welt nennt den 14. März;
Tagesangaben suggerieren eine Präzision, die niemand hat, und machen aus einer
Lücke von drei Wochen einen Rechtfertigungsdruck.

**`ended_on = None` heißt „läuft noch"**, nicht „unbekannt". Genau eine Station
darf offen sein — zwei gleichzeitig laufende Anstellungen sind selten genug, um
sie erst zuzulassen, wenn jemand danach fragt; und der Fehler „ich habe das Ende
vergessen einzutragen" ist häufig genug, um ihn abzufangen.

**Reihenfolge kommt aus den Daten**, nicht aus einer `sort_order`-Spalte:
absteigend nach `started_on`, laufende Station zuerst. Eine manuelle Sortierung
wäre ein Feld, das mit jeder Bearbeitung falsch werden kann.

## Der Anfrage-Fluss

```
Unternehmen sieht Profil  ──POST /resumes/{subject}/requests──▶  Anfrage: PENDING
                                                                       │
Person sieht Anfrage  ──POST /resumes/requests/{id}/grant──────────────┤
                        └─ schreibt resume.visibility:tenant:<id> im Ledger
                        └─ Anfrage: GRANTED
                                                                       │
                      ──POST /resumes/requests/{id}/decline────────────┘
                        └─ kein Ledger-Eintrag; Anfrage: DECLINED
```

**Der Anfragestatus ist Anzeige, nicht Autorisierung.** Beim Lesen entscheidet
immer der Ledger, frisch, ohne Cache. Steht in der Anfrage `GRANTED` und im
Ledger nichts, gilt der Ledger — sonst wäre der Anfragestatus eine zweite
Wahrheit, und zwar genau die Falle, die beim Profil ein Sichtbarkeits-Flag
gewesen wäre (ADR-0020 §6).

Daraus folgt: Ein Widerruf braucht **keinen** Eingriff in die Anfrage. Die Person
zieht im Ledger zurück, und der nächste Lesezugriff läuft ins Leere. Die Anfrage
bleibt stehen als das, was sie ist: ein Vorgang, der einmal beantwortet wurde.

### Wer darf überhaupt anfragen

Nur ein Unternehmen, und nur zu einer Person, deren **Profil ihm freigegeben
ist**. Sonst wäre die Anfrage ein Kanal, um die Existenz einer Person zu
erfahren: „Anfrage angelegt" gegen „nicht gefunden" wäre ein perfektes Orakel
über jede geratene UUID.

Geprüft wird das über den Ledger (`profile.visibility:public`), nicht über einen
Aufruf beim Profile-Service. Der Lebenslauf-Dienst braucht die Profildaten nicht
— nur die Antwort auf „darf dieser Aufrufer diese Person überhaupt sehen".

### Eine Ablehnung ist sichtbar

Das Unternehmen sieht `DECLINED`. Die Alternative — die Anfrage bleibt ewig
`PENDING` — wäre für beide Seiten schlechter: das Unternehmen wartet auf eine
Antwort, die nie kommt, und die Person trägt eine offene Aufgabe, die sie nicht
schließen kann.

**Ohne Begründung.** Ein Freitextfeld hier würde entweder leer bleiben oder zu
Rechtfertigung führen. Ein „nein" ist eine vollständige Antwort.

**Und ohne Nachfassen.** Nach einer Ablehnung kann dasselbe Unternehmen keine
neue Anfrage stellen (`409`). Ohne diese Regel wäre die Ablehnung wirkungslos:
wer dreimal fragen darf, hat kein Nein bekommen, sondern eine Verzögerung.
Eine erteilte und später widerrufene Freigabe verhält sich genauso — der Widerruf
ist eine stärkere Aussage als die Ablehnung, nicht eine schwächere.

### Was das Unternehmen NICHT erfährt

- ob es eine Person mit der geratenen UUID gibt (ohne Profilfreigabe: `404`)
- warum abgelehnt wurde
- ob der Lebenslauf überhaupt Inhalt hat (eine Anfrage an eine Person ohne
  Lebenslauf ist zulässig und sieht identisch aus — sonst wäre die Anfrage ein
  Orakel über „hat schon einen CV gepflegt")

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `PUT` | `/resumes/me` | jede angemeldete Person | der gespeicherte Lebenslauf |
| `GET` | `/resumes/me` | dieselbe | Lebenslauf oder `null` |
| `GET` | `/resumes/me/requests` | dieselbe | Anfragen an mich |
| `POST` | `/resumes/requests/{id}/grant` | die angefragte Person | Anfrage + `granted` |
| `POST` | `/resumes/requests/{id}/decline` | dieselbe | Anfrage |
| `POST` | `/resumes/{subject_id}/requests` | Unternehmen | die neue Anfrage |
| `GET` | `/resumes/requests` | Unternehmen | eigene Anfragen |
| `GET` | `/resumes/{subject_id}` | Unternehmen | Lebenslauf, wenn freigegeben |

**Statuscodes** nach ADR-0020, unverändert:

| Situation | Antwort |
|---|---|
| kein Lebenslauf / nicht freigegeben / unlesbare UUID | `404`, bis auf die Korrelations-ID identisch |
| kein aktives Unternehmen | `403` (Aussage über den Aufrufer) |
| Ledger schweigt | `503` |
| Anfrage an eine bereits abgelehnte Person | `409` |
| eigener Lebenslauf noch nicht angelegt | `200` mit `null` |

Eine fremde Anfrage-ID verhält sich wie eine fremde Subject-ID: `404`, egal ob
sie nicht existiert oder jemand anderem gehört.

## Abgrenzung

**Kein Dokument-Upload.** PDF-Lebensläufe brauchen `worker-files`/`worker-storage`,
die für Python 3.14 noch nicht gebaut sind (Sub-step 3.5). Der strukturierte
Lebenslauf steht ohnehin zuerst — aus ihm lässt sich ein PDF erzeugen, umgekehrt
nicht.

**Kein Import aus LinkedIn o. ä.** ADR-0004 verbietet Scraping; ein Import
käme nur über eine offizielle API und wäre ein eigener Schnitt.

**Keine Bewerbung.** Eine Bewerbung ist ein Vorgang mit Stellenausschreibung und
Verlauf; sie kommt in einer späteren Phase und wird den Lebenslauf konsumieren,
nicht ersetzen.

**Keine Benachrichtigungsmails** bei neuer Anfrage. Der Mailweg existiert
(identity-service, Mailpit), aber Benachrichtigungen sind ein Querschnittsthema
mit eigenen Einstellungen und eigenem Consent — sie gehören nicht nebenbei in
diesen Schnitt.

## Umsetzung in drei Scheiben

**Scheibe A — der Lebenslauf selbst.** Neuer Service über `worker new-service`,
Domäne (`Resume`, `Position`, `Education`, `MonthDate`), Migration, Repository,
`PUT`/`GET /resumes/me`. Kein Fremdzugriff, kein Ledger. Testbar und nützlich
für sich: eine Person kann ihren Lebenslauf pflegen.

**Scheibe B — Anfrage und Freigabe.** `ResumeRequest` samt Zustandsübergängen,
Ledger-Gate für `profile.visibility:public` (anfragen) und
`resume.visibility:tenant:<id>` (lesen), die sechs übrigen Endpunkte.
Integrationstest über beide Dienste: anfragen → freigeben → `200` → widerrufen →
`404`.

**Scheibe C — Oberfläche.** `/resume` (bearbeiten + eingegangene Anfragen) und
auf `/candidates` je Karte ein „Lebenslauf anfragen". Testgetrieben, dazu eine
Playwright-Reise über beide Rollen.

## Selbstprüfung

*Ist „ein Lebenslauf je Person" zu eng?* Für den Marktplatz nein: die
Person pflegt ihre Historie, die Zuschnitte macht später die Bewerbung. Käme das
Bedürfnis, wäre es additiv (`/resumes/me/variants`), ohne diesen Vertrag zu
brechen.

*Warum liegen die Anfragen nicht im Consent-Service?* Weil der Ledger
Einwilligungen führt, keine Vorgänge. Eine Anfrage hat einen Zustand, einen
Absender und eine Lebensdauer; eine Einwilligung hat nur „gilt" oder „gilt
nicht". Die beiden zu vermischen würde den Ledger zu einem Postfach machen und
seine wichtigste Eigenschaft verwässern — dass er über jede Frage dieselbe
knappe Auskunft gibt.

*Was, wenn ein Unternehmen gelöscht wird?* Die Capability nennt eine
Tenant-UUID, die dann niemandem mehr gehört. Da bei jedem Lesezugriff ein
gültiges Token mit genau dieser `tenant_id` nötig ist, wird die Einwilligung
damit wirkungslos — sie zeigt ins Leere, statt jemand Falschem zu gehören.
Aufräumen ist möglich, aber nicht sicherheitsrelevant.

*Kann eine Person eine Freigabe auch ohne Anfrage erteilen?* In diesem Schnitt
nicht — sie hätte keinen Weg, ein Unternehmen zu benennen, ohne dass es eine
Unternehmenssuche gäbe. Die kommt später; der Endpunkt bliebe derselbe.
