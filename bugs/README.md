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

## Ablage

Eine Datei je Fehler, benannt nach dem, was kaputt ist:

```
bugs/refresh-token-store-vergisst-subject.md
bugs/tenant-filter-greift-nicht-bei-projektion.md
```

`VORLAGE.md` ist die Vorlage.
