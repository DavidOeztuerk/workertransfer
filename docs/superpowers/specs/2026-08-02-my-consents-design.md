# Querschnitt — „Meine Freigaben": die Seite, die einer Consent-Plattform fehlt

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler), [Marktzugang (5.3)](2026-08-02-market-access-and-ui-design.md), [Benachrichtigungen](2026-08-02-notifications-design.md)

## Die Lücke im Zentrum

Der Consent-Ledger kann `grant`, `revoke`, `delete` und `check`. Was er **nicht**
kann: *„zeig mir, was gerade gilt."*

Damit fehlt einer Plattform, die sich über Einwilligung definiert, genau die
Seite, auf der Einwilligung sichtbar wird. Heute sind die Freigaben einer Person
über die Oberfläche verstreut: der Profilschalter auf `/profile`, der
Portfolioschalter auf `/portfolio`, die Lebenslauf-Anfragen auf `/resume`, die
Marktstatus-Anfragen auf `/markt`. Wer wissen will, wer gerade was von ihm sehen
darf, muss vier Seiten besuchen und die Antwort selbst zusammensetzen — und die
Freigaben, die über eine Bewerbung entstanden sind (4.2), stehen auf **keiner**
davon.

Das ist nicht nur unbequem. Eine Einwilligung, die man nicht überblicken kann,
ist keine informierte Einwilligung, und ein Widerruf, den man nicht findet, ist
keiner.

## Was der Ledger dafür braucht

Einen Endpunkt, der die derzeit wirksamen Freigaben einer Person aufzählt.

```
GET /consent/me  →  [{ capability, granted_at }]
```

**Nur die eigenen.** `actor_id == subject_id`, dieselbe Regel wie bei `grant`
und `revoke`. Ein Unternehmen darf diese Liste **nie** sehen — sie enthielte,
welche *anderen* Unternehmen Zugriff haben, und das ist eine Aussage über die
Person, die niemand außer ihr treffen darf.

Deshalb hat der Endpunkt auch keinen `subject_id`-Parameter: was man nicht
angeben kann, kann man nicht fälschen — dieselbe Regel, die ADR-0018 für
`tenant_id` und ADR-0019 für die Firmendomain durchgesetzt hat.

**Nur wirksame.** Widerrufene und gelöschte Fähigkeiten stehen nicht drin. Die
Seite beantwortet „was gilt", nicht „was war". Eine Historie wäre ein eigener
Gegenstand mit eigener Abwägung: sie zeigt, wer *einmal* gefragt hat, und das ist
mehr, als diese Seite verspricht.

**Kein Widerrufsgrund.** Er kommt hier ohnehin nicht vor (widerrufene Einträge
fehlen), und die Regel aus 3.1 bleibt: der Grund ist Freitext, den ein Mensch
über sich selbst geschrieben hat.

## Die Projektion

`project_state` reduziert einen Ereignisstrom je Fähigkeit. Für die Liste
braucht es dieselbe Reduktion **je Fähigkeit über alle Fähigkeiten hinweg** —
also `DISTINCT ON (capability)` mit derselben Ordnung `(recorded_at, event_id)`,
danach die wirksamen herausfiltern.

Das ist bewusst die schon vorhandene Regel, nur breiter angewandt, und **nicht**
eine zweite Wahrheit daneben. Ein Test prüft, dass beide Wege für dieselbe
Fähigkeit dasselbe sagen — sonst driften sie, und der Tag, an dem die Liste
etwas anderes behauptet als `check`, ist der Tag, an dem niemand mehr weiß, was
gilt.

## Die Seite

`/freigaben`, für die angemeldete Person.

Fähigkeiten sind opake Zeichenketten (`resume.visibility:tenant:<uuid>`). Die
Oberfläche zerlegt sie in **Bereich** (Profil, Lebenslauf, Portfolio,
Marktstatus) und **Empfänger** (alle Unternehmen / ein bestimmtes) und holt den
Firmennamen beim companies-service — der ist öffentlich (4.3). Ohne
Unternehmensprofil steht dort „Ein Unternehmen": ein Name, den es nicht gibt,
wird nicht erfunden, und die UUID gehört nicht auf eine Seite für Menschen.

**Eine unbekannte Form wird angezeigt, nicht verschluckt.** Wer eine Fähigkeit
nicht zerlegen kann, zeigt die Zeichenkette und bietet trotzdem „Zurückziehen"
an. Eine Freigabe zu verbergen, weil die Oberfläche ihr Format nicht kennt, wäre
der schlimmste denkbare Fehler auf genau dieser Seite.

**Zurückziehen geht direkt über `POST /consent/revoke`** — nicht über den
Fachdienst, dem die Fähigkeit „gehört". Das ist kein Umweg an einer Buchführung
vorbei: der Widerruf im Fachdienst *ist* dieser Aufruf, und `GRANTED` heißt dort
ohnehin „wurde einmal erteilt", nicht „gilt gerade" (3.3). Die eine Wahrheit
über das, was gilt, steht im Ledger — und diese Seite spricht mit ihm.

## Abgrenzung

**Keine Historie**, siehe oben.
**Kein Löschen (`delete`) von dieser Seite.** Löschung ist eine stärkere
Aussage als Widerruf und gehört an einen Ort, an dem sie erklärt wird — nicht
neben vierzehn Knöpfe, die etwas anderes tun.
**Keine Liste für Unternehmen.**

## Selbstprüfung

*Ist der Endpunkt nicht ein Leck, wenn ein Token gestohlen ist?* Wer das Token
hat, kann ohnehin alles tun, was die Person darf — einschließlich `check` auf
jede geratene Fähigkeit. Die Liste macht es bequemer, nicht möglich. Der
Gegenwert ist, dass die Person selbst zum ersten Mal sehen kann, was sie erteilt
hat.

*Warum kein Filter nach Bereich?* Weil die Liste kurz ist und ein Filter die
gefährlichste Eigenschaft dieser Seite beschädigt: dass sie **alles** zeigt. Wer
filtert, sieht weniger und glaubt, es sei alles.

*Warum steht der Firmenname nicht im Ledger?* Weil der Ledger Fähigkeiten
verwaltet, keine Unternehmen — und weil ein kopierter Name veraltet, sobald sich
das Unternehmen umbenennt. Die Auflösung gehört in die Oberfläche, wo sie bei
jedem Aufruf frisch ist.
