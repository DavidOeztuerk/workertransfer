# Sub-step 4.2 — Applications-Service: wo Person und Unternehmen sich treffen

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler), ADR-0017 (Tenant = Unternehmen), [Jobs-Design](2026-08-02-jobs-service-design.md), [Resume-Design](2026-08-02-resume-service-design.md)

## Der Punkt, an dem beide Achsen aufeinandertreffen

Bis hier lief alles auf einer von zwei Achsen. Profil, Lebenslauf und Portfolio
gehören einer **Person** und werden über den Consent-Ledger freigegeben. Eine
Stellenausschreibung gehört einem **Unternehmen**, und der Ledger kommt dort
nicht vor, weil niemand betroffen ist, der einwilligen könnte.

Eine Bewerbung verbindet beide. Damit kehrt der Ledger zurück — und zwar an der
Stelle, an der er am meisten zählt: eine Person gibt einem bestimmten
Unternehmen Zugriff auf ihre Daten, für einen bestimmten Zweck, und will ihn
zurücknehmen können.

## Die Kernfrage: kopieren oder verweisen?

**Kopieren** wäre einfach: beim Absenden wird der Lebenslauf in die Bewerbung
geschrieben, und das Unternehmen sieht für immer, was damals dort stand. Es
löst sogar ein echtes Problem — ein Lebenslauf, der sich mitten im Verfahren
unter dem Leser ändert, ist für beide Seiten unangenehm.

Aber es bricht die These der Plattform. Ein Widerruf muss wirken (ADR-0013), und
eine Kopie lässt sich nicht widerrufen. Wer einmal kopiert hat, hat für immer.

**Entscheidung: verweisen, und den Verweis über den Ledger absichern.**

Beim Absenden erteilt die Bewerbung im Namen der Person **empfängerbezogene
Einwilligungen** für genau die Artefakte, die sie mitschickt:

```
profile.visibility:tenant:<tenant_id>
resume.visibility:tenant:<tenant_id>
portfolio.visibility:tenant:<tenant_id>
```

Beim Zurückziehen werden sie widerrufen. Das Unternehmen sieht die Bewerbung
weiterhin als Vorgang — dass jemand sich beworben und zurückgezogen hat, ist
Teil seiner eigenen Geschichte — aber die Daten der Person sind weg.

Das Änderungsproblem bleibt und wird bewusst in Kauf genommen: wer seinen
Lebenslauf während eines Verfahrens ändert, ändert ihn für alle laufenden
Verfahren. Das ist ehrlicher als eine Kopie, die veraltet, ohne dass es jemand
merkt.

## Was das für die anderen Dienste heißt

`resume-service` kann das bereits — es kennt nur die empfängerbezogene Form.

`profile-service` und `portfolio-service` prüfen bisher nur `:public`. Sie
müssen zusätzlich `…:tenant:<id>` akzeptieren. Das ist genau die additive
Verfeinerung, die ADR-0020 vorgesehen hat („feinere Abstufungen kämen additiv
dazu, ohne diese zu brechen"), und es ist eine Erweiterung, keine Lockerung:
`:public` bleibt, was es war.

Wichtig für beide: **geprüft wird gegen den Tenant des Aufrufers**, nie gegen
einen aus dem Request. Der Tenant steht im Token, und der kam aus einer
geprüften Mitgliedschaft (ADR-0018).

## Domäne

```
Application
  id            UUID
  job_id        UUID       die Stelle
  tenant_id     UUID       das Unternehmen — dupliziert, mit Absicht (siehe unten)
  subject_id    UUID       die Person
  message       ≤ 4000     Anschreiben, optional
  shared        {profile, resume, portfolio}   was mitgeschickt wird
  status        SUBMITTED | REVIEWING | REJECTED | WITHDRAWN | HIRED
  created_at / updated_at
```

**`tenant_id` steht in der Bewerbung, obwohl die Stelle ihn kennt.** Ein
Fremdschlüssel geht hier nicht — die Stelle lebt in einer anderen Datenbank
(ADR-0004). Ihn bei jedem Lesezugriff beim Jobs-Service zu erfragen wäre ein
Round-Trip für eine Angabe, die sich nie ändert: eine Stelle wechselt nicht das
Unternehmen. Er wird beim Anlegen einmal übernommen und danach nicht mehr
angefasst.

**`profile` ist in `shared` immer enthalten.** Eine Bewerbung ohne jede Angabe
zur Person ist keine Bewerbung, und die Wahl „ich bewerbe mich, aber ihr dürft
nichts von mir sehen" ist keine, die jemand ernsthaft treffen will.

**Genau eine Bewerbung je (Person, Stelle).** Zweimal auf dieselbe Stelle zu
bewerben ist kein Ausdruck von Interesse, sondern ein Versehen. Erneutes
Absenden nach einem Rückzug ist erlaubt — das ist eine neue Entscheidung — und
setzt denselben Vorgang zurück auf `SUBMITTED`.

**Zustände und wer sie ändert:**

| Übergang | Wer |
|---|---|
| → `SUBMITTED` | die Person (bewerben, oder erneut nach Rückzug) |
| `SUBMITTED` → `REVIEWING` → `REJECTED`/`HIRED` | das Unternehmen |
| beliebig → `WITHDRAWN` | die Person |

Eine abgelehnte Bewerbung kann die Person **nicht** erneut absenden: das wäre
Nachfassen gegen einen Willen, der schon geäußert wurde — dieselbe Regel wie
beim Lebenslauf („einmal fragen"). `HIRED` ist ebenfalls endgültig.

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `POST` | `/applications` | Person | die Bewerbung |
| `GET` | `/applications/me` | dieselbe | eigene Bewerbungen |
| `POST` | `/applications/{id}/withdraw` | dieselbe | die zurückgezogene |
| `GET` | `/jobs/{job_id}/applications` | Unternehmen | Bewerbungen auf eine Stelle |
| `POST` | `/applications/{id}/status` | Unternehmen | die geänderte |

Statuscodes wie gehabt: `404` für „gibt es nicht ODER nicht deins"
(ununterscheidbar), `403` für „kein aktives Unternehmen" (Aussage über den
Aufrufer), `409` für einen Übergang, den es nicht gibt, `503` wenn der Ledger
schweigt.

**Die Bewerbung selbst enthält keine Profildaten.** Sie nennt eine
`subject_id`; das Unternehmen holt Profil, Lebenslauf und Portfolio bei den
zuständigen Diensten, und dort greift der Ledger. Ein zweiter Weg an dieselben
Daten hätte einen zweiten Filter, und der weicht irgendwann vom ersten ab —
dieselbe Überlegung wie bei den Anhängen im Portfolio.

## Reihenfolge beim Absenden

Erst der Ledger, dann der Vorgang, dann der Commit — wie beim Lebenslauf
(3.3). Schlägt der Ledger fehl, wird nichts committet. Gelingt er und scheitert
der Commit, hat das Unternehmen Zugriff ohne sichtbare Bewerbung; deshalb
**widerruft auch das Zurückziehen bedingungslos**, und ein erneuter Versuch
führt in einen sauberen Zustand.

## Abgrenzung

**Keine KI-Entwürfe** — Phase 4 nennt sie, aber „AI-assisted Bewerbungen als
Entwurf, human review vor Versand" ist ein eigener Schnitt mit eigener
Begründung und eigener Einwilligung.
**Kein Matching, keine Empfehlungen.**
**Keine Benachrichtigungen** — dasselbe Querschnittsthema wie bei den
Lebenslauf-Anfragen, weiterhin offen.

## Umsetzung in zwei Scheiben

**Scheibe A** — `profile-service` und `portfolio-service` akzeptieren die
empfängerbezogene Capability; neuer `applications-service` mit Domäne,
Persistenz, allen fünf Endpunkten; Integrationstest über drei Dienste:
bewerben → das Unternehmen sieht Profil und Lebenslauf → zurückziehen → beides
weg, der Vorgang bleibt.

**Scheibe B** — Oberfläche: „Bewerben" auf `/jobs`, `/applications` für die
Person, Bewerbungsliste je Stelle für das Unternehmen.

## Selbstprüfung

*Warum bleibt die Bewerbung nach dem Rückzug sichtbar?* Weil sie ein Vorgang im
Unternehmen ist, kein Datum über die Person. Dass jemand sich beworben und
zurückgezogen hat, gehört zur Geschichte des Verfahrens; die Person dahinter ist
danach nicht mehr einsehbar. Wer auch den Vorgang loswerden will, spricht über
Löschung — das ist `DELETE` im Ledger und ein eigener Weg.

*Warum darf ein abgelehnter Bewerber nicht erneut?* Weil ein „nein" sonst nur
eine Verzögerung wäre. Dieselbe Regel wie beim Lebenslauf, und sie schützt
dieselbe Seite.

*Ist die duplizierte `tenant_id` nicht genau die zweite Wahrheit, vor der ich
sonst warne?* Nein: sie ist eine **Kopie eines unveränderlichen Werts**. Eine
Stelle wechselt nicht das Unternehmen. Gefährlich wird eine Kopie erst, wenn das
Original sich ändern kann — dann laufen sie auseinander.
