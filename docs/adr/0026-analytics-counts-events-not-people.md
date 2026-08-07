# ADR-0026 — Kennzahlen zählen Vorgänge, nicht Menschen; `worker-search` gelöscht

- **Status:** angenommen
- **Datum:** 2026-08-06
- **Betrifft:** `apps/applications-service` (`GET /companies/me/application-stats`),
  `packages/worker-search` (gelöscht)
- **Verwandt:** ADR-0022 (keine Zahl, die einen Menschen zusammenfasst),
  ADR-0004 (keine gemeinsame Datenbank), ADR-0025 (dieselbe Aufräumregel)

## Kontext

Die DoD von Phase 9 verlangt zwei Dinge, die hier zusammenkommen: *„Suche
findet"* und *„Analytics aggregiert datenschutzkonform"*.

**Zur Suche.** Sie existiert längst und wird benutzt: `jobs-service` sucht über
Titel, Beschreibung und Ort, `profile-service` über Fähigkeiten und Ort, beides
mit `ilike` gegen Postgres, mit Tests und einer E2E-Reise („die Suche findet
nur, was freigegeben ist"). Der ULTRAPLAN sieht daneben einen `search-service`
auf Basis von `worker-search` vor. Beim Nachsehen: **223 Zeilen, drei schwere
Abhängigkeiten (elasticsearch, meilisearch, qdrant-client), null Konsumenten** —
zum vierten Mal dasselbe Muster nach `worker-files` (ADR-0021), `worker-github`
(ADR-0022) und `worker-messaging` (ADR-0025).

**Zu den Kennzahlen.** Hier liegt die eigentliche Frage, und sie ist heikel:
Auswertungen auf einer Plattform, deren ganze Mechanik „die Person entscheidet"
heißt.

Der naheliegende Reflex wäre **k-Anonymität** — Zahlen erst ab einer
Mindestgröße zeigen. Für diesen Fall ist das aber das falsche Werkzeug: ein
Unternehmen sieht seine Bewerbungen ohnehin **einzeln** in seiner Liste. Eine
Schwelle auf der Summe würde etwas verdecken, das daneben im Klartext steht —
Sicherheitstheater, das Vertrauen vortäuscht, wo keines nötig ist, und das
davon ablenkt, wo die Grenze wirklich verläuft.

## Entscheidung

**1. Die Grenze liegt nicht bei der Aggregation, sondern bei der
Zusammenführung.** Zulässig ist eine Kennzahl, wenn sie **keine Auskunft
erzeugt, die der Fragende nicht ohnehin hat**. Verboten ist sie, sobald sie
Quellen verrechnet, die *einzeln* freigegeben wurden.

Erlaubt, weil es nur zusammenfasst, was das Unternehmen schon sieht:

- „12 Bewerbungen, davon 3 abgelehnt" auf die **eigenen** Stellen.

Verboten, obwohl es aggregiert aussieht:

- „Ihre Bewerber haben sich im Schnitt bei 7 anderen Firmen beworben" — eine
  Aussage über Menschen aus fremden Vorgängen.
- „60 % Ihrer Bewerber suchen aktiv" — der Marktstatus ist die heikelste Angabe
  im System und gehört der Person.
- Irgendetwas je Kopf. Das ist bereits ADR-0022 und bleibt es.

Keine Aggregation macht eine unzulässige Zusammenführung wieder zulässig.

**2. Kennzahlen entstehen dort, wo die Daten liegen** — als Endpunkt in
`applications-service`, nicht in einem eigenen `analytics-service`. Ein
separater Dienst müsste die Vorgänge kopieren oder dienstübergreifend lesen;
beides verstößt gegen ADR-0004 und schafft einen **zweiten Ort**, an dem
personenbezogene Daten liegen und gelöscht werden müssten. Ein Datenschutzrisiko
für eine Zahl, die ein `GROUP BY` beantwortet.

**3. Gezählt wird in der Datenbank, nicht im Dienst.** `count_by_status` macht
ein `GROUP BY`; die Zeilen selbst werden gar nicht erst geladen. Sie ohne Not
durch den Prozess zu tragen, nur um sie zu zählen, wäre die schlechtere Form.

**4. Kein Consent-Aufruf für diese Zahl** — und das ist kein Versehen. Gezählt
wird, was das Unternehmen ohnehin sieht. Den Ledger zu fragen, ob man zählen
darf, was bereits sichtbar ist, wäre Theater und würde ihn mit einer Frage
belasten, die er nicht beantworten soll.

**5. `worker-search` ist gelöscht.** Die Suche, die es gibt, funktioniert, ist
getestet und trägt eine E2E-Reise. Drei Suchmaschinen-Backends ohne Konsumenten
sind keine Suche, sondern eine Absichtserklärung. Sollte die Datenmenge einmal
mehr verlangen als `ilike` — Rangfolge, Tippfehlertoleranz, Facetten —, ist das
eine eigene Entscheidung mit eigenem Anlass; heute gibt es ihn nicht.

## Konsequenzen

- `GET /companies/me/application-stats` liefert `by_status` und `total`, sonst
  nichts. Ein Test hält die Feldmenge der Antwort fest — was es nicht gibt,
  kann nicht herausgehen (dieselbe Strenge wie bei `DraftContext`, ADR-0024).
- Ein Test prüft die Abgrenzung: ein fremdes Unternehmen sieht seine eigene
  (leere) Zahl, nie diese. `403` ohne aktives Unternehmen.
- Sechs Abhängigkeiten weniger im Workspace (elasticsearch, meilisearch,
  qdrant-client und deren Anhang).
- **Bewusst nicht gebaut:** Dashboards, Zeitreihen, Trichter-Auswertungen
  („wie viele springen zwischen Schritt 2 und 3 ab"). Sie sind nicht per se
  verboten, aber jede braucht dieselbe Prüfung wie oben, und keine hat heute
  einen Konsumenten. Sie hier auf Vorrat zu bauen wäre genau der Fehler, den
  ADR-0021/0022/0025 dreimal aufräumen mussten.
- **Kein Verhaltens-Tracking**, nirgends: kein „wer hat wann welche Anzeige
  gesehen". Das wäre eine Datensammlung über Personen, die niemand angefordert
  hat, und der Consent-Ledger kennt dafür keine Einwilligung.
