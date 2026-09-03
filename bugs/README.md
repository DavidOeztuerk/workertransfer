# Fehler, die nicht uns gehören

Hier liegt ein Ticket je Fehler, der **in Girder** steckt und beim Bauen von
WorkerTransfer aufgefallen ist.

Der Ordner existiert, weil die Versuchung groß ist, um einen Bibliotheksfehler
herumzuprogrammieren. Das kostet zweimal: einmal beim Umbauen, und ein zweites
Mal, wenn Girder den Fehler behebt und der Umweg stehen bleibt, ohne dass
jemand weiß, wofür er da war.

## Regel

Wenn ein Fehler aus Girder kommt: **anhalten**, hier ein Ticket schreiben, dem
Benutzer melden. Nicht umgehen, bevor er entschieden hat.

## Wie man das auseinanderhält

Ein Stapelabzug, der durch Girder läuft, sagt gar nichts — Girders Pipeline
liegt um jeden Handler herum, also steht sie in *jedem* Abzug. Der Test ist ein
anderer:

> **Lässt sich der Fehler ohne WorkerTransfer-Code auslösen?**

Ein kleiner Testfall, der nur Girder benutzt. Reproduziert er den Fehler, ist
es Girders. Reproduziert er ihn nicht, ist es unserer — auch dann, wenn der
Abzug voller `Girder.*`-Zeilen ist.

Zweiter Test, wenn der erste unklar bleibt: **widerspricht das Verhalten dem,
was Girders README oder die XML-Doku zusagt?** Eine Zusage, die nicht gilt, ist
ein Fehler. Etwas, das nirgends zugesagt ist, ist erst mal eine offene Frage —
auch die gehört hierher, dann als `Art: Lücke`.

## Wenn Girder den Fehler behebt

Dann geht das Ticket **weg** — es wird nicht auf „erledigt" gehakt und liegen
gelassen. Ein Ordner, in dem behobene Tickets stehen bleiben, beantwortet die
Frage „was ist hier offen?" nach kurzer Zeit falsch, und dann liest sie niemand
mehr.

Drei Dinge vorher, in dieser Reihenfolge:

1. **Nachmessen**, an der neuen Fassung und ohne WorkerTransfer-Code. Ein
   Änderungsprotokoll ist keine Messung.
2. **Den Umweg entfernen**, falls einer beschlossen wurde — das ist der ganze
   Grund, warum das Ticket geschrieben wurde.
3. **Die tragende Begründung retten.** Was hier stand und weiterhin eine
   Entscheidung trägt, gehört an den Ort dieser Entscheidung: in den Kommentar
   an der Registrierung, in die `Without`-Begründung, in `CLAUDE.md`. Erst dann
   löschen. Eine Begründung, die nicht mehr stimmt, ist schlimmer als keine —
   und eine, die mit ihrer Datei verschwindet, war nie eine.

Was **nicht** hierher gehört, war nie ein Girder-Fehler: unsere eigenen offenen
Punkte sind Arbeit, kein Ticket. Sie werden behoben, nicht abgelegt.

## Ablage

Eine Datei je Fehler, benannt nach dem, was kaputt ist:

```
bugs/refresh-token-store-vergisst-subject.md
bugs/tenant-filter-greift-nicht-bei-projektion.md
```

`VORLAGE.md` ist die Vorlage.
