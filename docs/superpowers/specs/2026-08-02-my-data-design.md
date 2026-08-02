# Querschnitt — „Meine Daten": Auskunft und Mitnahme

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0004 (eigene Datenbank je Dienst), [Meine Freigaben](2026-08-02-my-consents-design.md)

## Die Lücke

Eine Person kann auf dieser Plattform sehr genau steuern, wer was sieht. Was sie
nicht kann: **sehen, was überhaupt über sie gespeichert ist** — und es
mitnehmen.

Das ist bei einer Plattform, deren These „du entscheidest" lautet, die
auffälligste verbliebene Lücke. Wer nicht weiß, was da ist, entscheidet über
etwas, das er nicht kennt.

## Zusammengesetzt im Browser

Dieselbe Entscheidung wie bei „Was liegt an", aus demselben Grund: ein Dienst,
der alles einsammelt, müsste über sieben Dienstgrenzen hinweg lesen — genau das,
was ADR-0004 ausschließt. Die Oberfläche fragt jeden Dienst nach dem, wofür er
zuständig ist, und legt die Antworten in **eine Datei**.

Es sind ausnahmslos Endpunkte, die es schon gibt und die ohnehin nur die eigenen
Daten herausgeben. Der Export erfindet keinen neuen Zugriff — er bündelt
vorhandene.

## Der eine neue Endpunkt: die Geschichte der Einwilligungen

```
GET /consent/me/history  →  jedes Ereignis, älteste zuerst
```

Der Ledger führt sie ohnehin (`stream()`), und für die betroffene Person ist sie
das Kernstück einer Auskunft: wer wann was erteilt und widerrufen hat.

**Warum das kein Widerspruch zu `/consent/me` ist.** Dort steht bewusst nur, was
*gilt* — mit der Begründung, eine Historie zeige, wer *einmal* gefragt hat, und
das sei mehr, als eine Übersichtsseite verspricht. Hier ist es genau richtig:
eine Auskunft ist der Ort, an dem die Vergangenheit hingehört, und sie geht an
die Person selbst, nicht an eine Ansicht, die im Vorbeigehen gelesen wird.

**Mit Widerrufsgrund.** Er ist Freitext, den die Person über sich selbst
geschrieben hat — ihr gegenüber gibt es keinen Grund, ihn zurückzuhalten. Nach
außen bleibt er verborgen (`/check` kennt ihn nicht, `/consent/me` auch nicht);
das ist der Unterschied zwischen „gehört ihr" und „geht andere an".

**Nur die eigene.** Kein `subject_id`-Parameter, wie bei `/consent/me`.

## Was in die Datei kommt

| Abschnitt | Quelle |
|---|---|
| Konto | identity-service (`/me`) |
| Benachrichtigungen | identity-service |
| Profil | profile-service |
| Lebenslauf + Anfragen | resume-service |
| Portfolio | portfolio-service |
| Marktstatus + Anfragen | transfer-service |
| Transfer-Vorgänge | transfer-service |
| Bewerbungen | applications-service |
| Einwilligungen: was gilt **und** was war | consent-service |

**JSON, nicht PDF.** Eine Auskunft soll lesbar *und* weiterverwendbar sein; ein
PDF ist das erste und nicht das zweite. Wer es ansehen will, sieht es auf der
Seite — die Datei ist zum Mitnehmen.

## Die wichtigste Eigenschaft: ein unvollständiger Export sagt es

Fällt ein Dienst aus, fehlt sein Abschnitt. Ihn stillschweigend wegzulassen wäre
hier der schlimmste Fehler: die Datei sähe vollständig aus, und jemand würde
daraus schließen, es gebe nichts weiter über ihn.

Deshalb trägt jeder Abschnitt seinen Zustand, und der Export nennt oben, was
gefehlt hat:

```json
{
  "erzeugt_am": "...",
  "unvollständig": ["portfolio"],
  "abschnitte": {
    "profil":    { "status": "ok", "daten": { ... } },
    "portfolio": { "status": "nicht_abrufbar" }
  }
}
```

Und die Seite sagt es vor dem Herunterladen, nicht erst in der Datei.

## Abgrenzung

**Kein Löschen.** Art. 17 ist ein eigener Vorgang mit eigenen Fragen
(Aufbewahrungspflichten, beiderseitige Vorgänge wie Bewerbungen und Transfers,
die nicht allein einer Seite gehören). Ihn neben einen Herunterladen-Knopf zu
setzen wäre eine Einladung, ihn zu verwechseln.
**Keine Zustellung per Mail.** Eine Datei mit allem über eine Person gehört
nicht in ein Postfach, das womöglich nicht nur ihr gehört — dieselbe
Überlegung wie bei den Benachrichtigungen.
**Keine Auskunft über Dritte.** Ein Transfer-Vorgang nennt das Unternehmen, weil
die Person mit ihm gesprochen hat; er nennt keine Personen aus dem Unternehmen.

## Selbstprüfung

*Ist ein Export nicht selbst ein Risiko?* Er entsteht im Browser der
angemeldeten Person und wird nirgends gespeichert. Anders als eine serverseitig
erzeugte Datei gibt es nichts, das liegen bleibt, ablaufen muss oder versehentlich
geteilt wird.

*Warum kein Fortschrittsbalken, kein Auftrag, keine spätere Abholung?* Weil es
neun parallele Abrufe sind und in einer Sekunde fertig. Ein Auftragssystem wäre
Maschinerie für ein Problem, das es nicht gibt — und eine abholbare Datei wäre
genau die, die liegen bleibt.
