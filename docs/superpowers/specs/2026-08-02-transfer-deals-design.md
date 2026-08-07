# Sub-step 5.2 — Der Transfer-Vorgang: drei Ja, jederzeit ein Nein

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: [Marktstatus-Design](2026-08-02-transfer-service-design.md), ADR-0013 (Consent-Ledger), ADR-0017 (Tenant = Unternehmen), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 5

## Der Fund, der den Entwurf verändert

Der ULTRAPLAN verlangt: *„beschäftigt → Kontakt + Angebot + **Firma muss
mitwirken**"*.

Das lässt sich so nicht bauen — **die Plattform weiß nicht, wo jemand
arbeitet.** `employed` ist ein Boolescher Wert, den die Person selbst setzt
(5.1). Es gibt keinen Datensatz „Anna arbeitet bei X".

Und es soll auch keinen geben. Ein solcher Datensatz wäre die Verbindung
zwischen „Anna arbeitet bei X" und „Anna hört zu" — also genau die Auskunft,
die jemanden den Arbeitsplatz kostet, in einer einzigen Tabelle. Ihn anzulegen,
damit die Plattform den Arbeitgeber anschreiben kann, hieße, das größte Risiko
des Systems zu erzeugen, um eine Höflichkeit zu ermöglichen.

**Entscheidung: die Plattform kontaktiert den aktuellen Arbeitgeber nicht.**

Stattdessen: Sagt die Person, sie sei beschäftigt, trägt der Vorgang, dass eine
**Freigabe nötig** ist, und die **Person selbst bestätigt**, dass sie vorliegt.
Wie sie zustande kommt — Aufhebungsvertrag, Kündigungsfrist, Gespräch — ist
außerhalb dieses Systems, und das ist richtig so: es ist ihr Arbeitsverhältnis.

Das ist eine bewusste Abweichung. Sie macht den Schritt schwächer (die Plattform
kann nicht prüfen, ob die Freigabe wirklich vorliegt) und das System sicherer.
Zwischen einer Zusicherung, die niemand einlösen kann, und einer, die niemanden
gefährdet, ist die zweite die ehrlichere.

## Der Vorgang

```
Transfer
  id              UUID
  subject_id      UUID    die Person
  tenant_id       UUID    das interessierte Unternehmen
  status          INTERESTED | TALKING | OFFERED | ACCEPTED | COMPLETED
                  | DECLINED | WITHDRAWN
  requires_release bool   „ich bin beschäftigt" beim Start des Vorgangs
  release_confirmed bool  von der PERSON bestätigt
  message         ≤ 2000  Anschreiben des Unternehmens
  offer_note      ≤ 2000  das Angebot in Worten
  offer_start_on  Monat | None
  offer_fee_cents int | None
  created_at / updated_at
```

**`requires_release` wird beim Anlegen aus dem Marktstatus kopiert**, nicht bei
jedem Lesezugriff neu geholt. Eine Person, die während eines laufenden Gesprächs
ihren Job kündigt, ändert damit nicht rückwirkend die Bedingungen eines Angebots
— und andersherum kann ein Unternehmen nicht darauf hoffen, dass sich die Regel
noch ändert. Das ist eine Kopie eines Werts, der sich ändern *kann*, aber ihr
Zweck ist gerade, den Stand zum Zeitpunkt des Vorgangs festzuhalten.

**`offer_fee_cents` wird festgehalten, nicht bewegt.** Die Plattform führt kein
Geld. Es ist eine Zahl, auf die sich zwei Unternehmen einigen, und sie steht
hier, damit beide Seiten dieselbe im Blick haben.

## Zustände

| Von | Wer | Nach |
|---|---|---|
| — | Unternehmen zeigt Interesse | `INTERESTED` |
| `INTERESTED` | Person nimmt das Gespräch an | `TALKING` |
| `TALKING` | Unternehmen macht ein Angebot | `OFFERED` |
| `OFFERED` | Person nimmt an | `ACCEPTED` |
| `ACCEPTED` | Person bestätigt die Freigabe (nur wenn nötig) | `COMPLETED` |
| `ACCEPTED` ohne nötige Freigabe | Unternehmen schließt ab | `COMPLETED` |
| jeder nicht-endgültige | **Person** sagt nein | `DECLINED` |
| jeder nicht-endgültige | **Unternehmen** zieht zurück | `WITHDRAWN` |

**Ablehnen geht immer**, aus jedem laufenden Zustand, von beiden Seiten. Das ist
die wichtigste Regel des ganzen Schnitts: ein Verfahren, aus dem man nicht
aussteigen kann, ist kein Verfahren, sondern eine Falle.

**Kein Weg zurück aus `DECLINED`, `WITHDRAWN` oder `COMPLETED`.** Wer erneut
will, beginnt einen neuen Vorgang — und dafür braucht das Unternehmen wieder
eine gültige Freigabe des Marktstatus.

## Wer darf einen Vorgang beginnen

Nur ein Unternehmen, und nur zu einer Person, deren **Marktstatus ihm
freigegeben ist** und die **ansprechbar** ist (`OPEN` oder `LISTENING`).

`UNAVAILABLE` heißt nein — auch mit Freigabe. Die Freigabe erlaubt zu *sehen*,
nicht zu *stören* (5.1). Ein Vorgang gegen ein `UNAVAILABLE` wäre genau die
Belästigung, gegen die der Zustand existiert.

Beides ist ununterscheidbar von „gibt es nicht": `404`. Sonst wäre der Endpunkt
ein Orakel darüber, wer auf der Plattform ist und wer gerade zuhört.

**Genau ein laufender Vorgang je (Person, Unternehmen).** Ein zweiter wäre
Nachfassen an der Ablehnung vorbei. Nach einem endgültigen Ausgang ist ein neuer
möglich — das ist eine neue Entscheidung, keine Wiederholung derselben.

## Endpunkte

| Methode | Pfad | Wer |
|---|---|---|
| `POST` | `/transfers` | Unternehmen (Interesse zeigen) |
| `GET` | `/transfers/me` | Person (eigene Vorgänge) |
| `GET` | `/transfers` | Unternehmen (eigene Vorgänge) |
| `POST` | `/transfers/{id}/accept-talk` | Person |
| `POST` | `/transfers/{id}/offer` | Unternehmen |
| `POST` | `/transfers/{id}/accept-offer` | Person |
| `POST` | `/transfers/{id}/confirm-release` | Person |
| `POST` | `/transfers/{id}/complete` | Unternehmen |
| `POST` | `/transfers/{id}/decline` | Person |
| `POST` | `/transfers/{id}/withdraw` | Unternehmen |

Getrennte Endpunkte statt eines `PATCH status`: jeder Übergang gehört einer
Seite, und ein gemeinsamer Endpunkt müsste bei jedem Aufruf herausfinden, wer
gerade was darf. Getrennt steht es in der URL.

## Abgrenzung

**Keine Verträge, keine Unterschrift** — `worker-templates` und digitale
Signatur sind ein eigener Schnitt mit eigener rechtlicher Abwägung.
**Keine KI-Beratung** (`worker-player-advisor`).
**Kein Geldfluss.**
**Keine Benachrichtigungen** — dasselbe offene Querschnittsthema wie bei den
Lebenslauf-Anfragen.

## Selbstprüfung

*Ist „die Person bestätigt die Freigabe" nicht wertlos, wenn niemand prüft?*
Nicht wertlos, nur ehrlich begrenzt. Es hält fest, dass die Frage gestellt und
beantwortet wurde, und es zwingt beide Seiten, sie zu stellen. Eine Prüfung
könnte die Plattform ohnehin nur vortäuschen — sie kennt weder den Arbeitgeber
noch den Vertrag.

*Warum darf das Unternehmen bei `ACCEPTED` ohne Freigabebedarf abschließen und
nicht die Person?* Weil der Abschluss die Aussage „wir stellen ein" ist, und die
trifft der Arbeitgeber. Die Person hat mit `accept-offer` bereits ja gesagt.

*Warum kein `Transfer Listed`?* Siehe 5.1: ein Unternehmen, das jemanden ohne
dessen Wissen auf eine Liste setzt, ist das Gegenteil dieser Plattform.
