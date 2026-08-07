# Sub-step 5.3 — Der Transfermarkt wird sichtbar: die Tür und die Oberfläche

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: [Marktstatus (5.1)](2026-08-02-transfer-service-design.md), [Transfer-Vorgang (5.2)](2026-08-02-transfer-deals-design.md), ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler), [Resume-Design](2026-08-02-resume-service-design.md)

## Zwei Lücken, und die zweite ist die schlimmere

Der Transfermarkt ist das Unterscheidungsmerkmal dieser Plattform, und er hat
nach 5.1 und 5.2 **keine einzige Seite in der Oberfläche**. Niemand kann seinen
Marktstatus setzen, niemand sieht einen Vorgang. Das ist die sichtbare Lücke.

Die unsichtbare ist ernster: **es gibt keinen Weg, auf dem die Freigabe
entsteht.** Ein Unternehmen darf einen Vorgang nur beginnen, wenn ihm der
Marktstatus freigegeben ist (5.2). Erteilt wird `market.visibility:tenant:<id>`
— aber kein Endpunkt und keine Seite erteilt ihn je. 5.1 hat das offen gelassen
(„über denselben Weg wie beim Lebenslauf"), 5.2 hat es vorausgesetzt. Der
gesamte Transfermarkt ist damit heute unerreichbar: technisch fertig, praktisch
tot.

Dieser Schnitt baut die Tür und die Räume dahinter.

## Die Tür: das Unternehmen fragt, die Person antwortet

Genau wie beim Lebenslauf. Ein Unternehmen stellt eine **Marktstatus-Anfrage**,
die Person erteilt oder lehnt ab, und sie kann später zurückziehen.

Drei Möglichkeiten standen zur Wahl:

**A — an der Lebenslauf-Freigabe mithängen.** Wer den Lebenslauf freigibt, gäbe
den Marktstatus mit frei. Verworfen, und zwar deutlich: 5.1 nennt den
Marktstatus die gefährlichste Angabe im System, gefährlicher als der
Lebenslauf. *Ein Lebenslauf verrät, wo jemand war; der Marktstatus verrät, dass
er weg will.* Ein Schalter, der beim Umlegen etwas Zweites mitfreigibt, ist
genau der Schalter, dessen Folgen niemand überblickt.

**B — die Person gibt von sich aus frei.** Sie sucht sich Unternehmen und
erteilt. Verworfen, weil ein Transfermarkt daran hängt, dass Unternehmen
zugehen: die Seite, die etwas will, muss anfangen dürfen. Und die Person müsste
Unternehmen suchen können, die sie nicht kennt — ein Auswahlproblem, das dieser
Schnitt nicht lösen muss.

**C — eine eigene Anfrage, wie beim Lebenslauf.** Gewählt.

Es ist bewusst dieselbe Form in einem zweiten Dienst und keine geteilte
Mechanik: die Dienste haben getrennte Datenbanken (ADR-0004), und ein
gemeinsamer Anfragenapparat wäre ein Kopplungspunkt zwischen zwei Diensten für
zwei Vorgänge, die nur ähnlich aussehen.

### Warum die Transfer-Anfrage nicht selbst die Tür sein kann

Naheliegend wäre: `POST /transfers` gegen jemanden ohne Freigabe erzeugt eben
eine Anfrage statt `404`. Das spart einen Vorgang und ist trotzdem falsch.

`unavailable` heißt nein — auch mit Freigabe (5.1). Würde die Kontaktaufnahme
selbst die Anfrage sein, bekäme eine Person, die gerade **nicht** angesprochen
werden will, genau die Ansprache, gegen die dieser Zustand existiert.

Die Marktstatus-Anfrage ist die **leichtere** Frage, ein Bit weniger
aufdringlich: nicht *„lass uns reden"*, sondern *„darf ich sehen, ob du gerade
zuhörst?"*. Sie zu beantworten kostet nichts — wer `UNAVAILABLE` ist und
freigibt, zeigt genau das, und das Unternehmen weiß Bescheid, ohne dass jemand
gestört wurde.

Zwei Stufen, jede einzeln mit Nein beantwortbar.

## Domäne

```
MarketRequest
  id            UUID
  subject_id    UUID    die Person
  tenant_id     UUID    das fragende Unternehmen
  requested_by  UUID    wer im Unternehmen gefragt hat
  status        PENDING | GRANTED | DECLINED
  created_at
  answered_at   | None
```

**Ein Vorgang, keine Berechtigung** — dieselbe Trennung wie beim Lebenslauf.
`GRANTED` heißt „wurde einmal erteilt", nicht „gilt gerade". Ob der Zugriff
jetzt besteht, beantwortet ausschließlich der Ledger, frisch bei jedem Zugriff
(ADR-0013). Deshalb gibt es weder `is_active` noch `revoked_at`: nach einem
Widerruf bleibt die Anfrage `GRANTED`, und der Zugriff läuft trotzdem ins Leere.

**Einmal fragen.** Eine zweite Anfrage desselben Unternehmens wird abgelehnt,
auch nach einem Widerruf. Wer dreimal fragen darf, hat kein Nein bekommen,
sondern eine Verzögerung.

**Voraussetzung ist die Profilfreigabe**, nicht die Existenz eines Marktstatus.
Beides zu prüfen wäre ein Orakel: „hat schon einen Marktstatus gepflegt" ist
eine Information über die Person, die niemand erfragen können soll — und sie
wäre in diesem Fall besonders verräterisch.

**Reihenfolge beim Antworten:** erst der Ledger, dann der Vorgang, dann der
Commit. Und **auch die Ablehnung widerruft** — unverändert die Regel aus 3.3:
gelingt der Ledger-Aufruf und scheitert danach der Commit, wäre sonst eine
Berechtigung ohne sichtbaren Vorgang entstanden.

## Endpunkte (transfer-service)

| Methode | Pfad | Wer |
|---|---|---|
| `POST` | `/market/{subject_id}/requests` | Unternehmen |
| `GET` | `/market/me/requests` | Person |
| `GET` | `/market/requests` | Unternehmen |
| `POST` | `/market/requests/{id}/grant` | Person |
| `POST` | `/market/requests/{id}/decline` | Person |
| `POST` | `/market/requests/{id}/revoke` | Person |

Statuscodes wie gehabt: `404` für „gibt es nicht ODER nicht deins", `403` ohne
aktives Unternehmen, `409` für eine zweite Anfrage, `503` wenn der Ledger
schweigt.

## Die Oberfläche

| Seite | Wer | Was |
|---|---|---|
| `/markt` | Person | Status setzen (`OPEN`/`LISTENING`/`UNAVAILABLE`, beschäftigt, Notiz); offene Anfragen beantworten; erteilte Freigaben zurückziehen |
| `/transfers` | Person | eigene Vorgänge: Gespräch annehmen, Angebot annehmen, Freigabe bestätigen, ablehnen |
| `/unternehmen/transfers` | Unternehmen | eigene Vorgänge: Angebot machen, abschließen, zurückziehen |
| `/candidates` | Unternehmen | zusätzlich „Marktstatus anfragen" und, sobald freigegeben, der Status mit „Interesse zeigen" |

**Der Marktstatus steht auf einer eigenen Seite, nicht im Profil.** Dieselbe
Begründung wie beim eigenen Dienst (5.1): die harmloseste Angabe (ein Aushang)
und die gefährlichste (die Wechselabsicht) gehören nicht in dasselbe Formular,
sonst verwechselt sie irgendwann jemand.

**Die Voreinstellung im Formular ist der geladene Status**, und der ist ohne
Angabe `UNAVAILABLE` — die Seite denkt sich nichts aus. `GET /market/me`
antwortet nie `null`, genau damit die Oberfläche das nicht muss.

**Für jeden Übergang ein eigener Knopf**, kein Auswahlfeld: dieselbe Begründung
wie bei den zehn Endpunkten. Wer sieht, was er tun kann, muss nicht raten, was
erlaubt ist.

## Abgrenzung

**Keine Benachrichtigungen** — weiterhin offen, jetzt spürbarer als je zuvor:
eine Marktstatus-Anfrage erreicht nur, wer sich anmeldet. Es bleibt ein
Querschnittsthema und kein Anhängsel dieses Schnitts.
**Keine Verträge, keine Unterschrift, keine KI-Beratung.**
**Kein Geldfluss.**

## Umsetzung in zwei Scheiben

**Scheibe A** — `MarketRequest` in `transfer-service`: Domäne, Migration,
Repository, sechs Endpunkte, Integrationstests über zwei Dienste.

**Scheibe B** — Oberfläche: drei neue Seiten, Erweiterung von `/candidates`,
Unit-Tests je Seite und eine Playwright-Reise über den vollständigen Weg —
Status setzen, Anfrage, Freigabe, Interesse, Gespräch, Angebot, Annahme,
Freigabe bestätigen, Abschluss.

## Selbstprüfung

*Ist eine zweite Anfrage-Mechanik nicht Verdopplung?* Sie ist dieselbe **Form**
in einem anderen Dienst mit einer anderen Datenbank und einer anderen
Capability. Die Alternative wäre ein geteilter Anfragenapparat über eine
Dienstgrenze hinweg — teurer und gegen ADR-0004. Verdopplung wäre es, wenn
beide dieselben Daten hielten; sie halten verschiedene.

*Warum darf die Person nicht von sich aus freigeben?* Sie darf — der Ledger
nimmt jede Erteilung entgegen, und die Seite `/markt` zeigt ihr, was gilt. Was
dieser Schnitt nicht baut, ist eine Unternehmenssuche, aus der heraus sie
freigibt. Das ist ein Auswahlproblem und kein Einwilligungsproblem.

*Warum sieht das Unternehmen abgelehnte Anfragen weiterhin in seiner Liste?*
Weil sonst „abgelehnt" und „nie gefragt" gleich aussähen — und dann fragt
jemand erneut, im guten Glauben. Die Liste ist die Erinnerung daran, dass die
Frage schon gestellt wurde.
