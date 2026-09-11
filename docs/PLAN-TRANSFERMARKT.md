# Plan: der Transfermarkt für **alle** Arbeiter

**Stand:** 09.09.2026 · Nach dem Review in [`REVIEW-09-09.md`](REVIEW-09-09.md).

Dies ist der Plan bis zum Produkt, das der Name verspricht: ein Transfermarkt
wie im Fußball, **aber für Arbeiter** — und zwar für alle, nicht für
Softwareentwickler.

Jede Arbeit steht als **PBI** mit Nutzergeschichte, Aufgaben und
Abnahmekriterien. Die Reihenfolge und die fertigen Sitzungs-Prompts stehen in
[`SESSIONS.md`](SESSIONS.md).

---

## Der Befund, der alles ordnet

**WorkerTransfer ist heute eine Plattform für Softwareentwickler.** Das steht
nirgends und war nie entschieden — es ist die Summe von Einzelentscheidungen:

| Was | Wen es meint |
|---|---|
| `github-service` als einzige Belegquelle | Entwickler |
| Fähigkeiten als Freitext („Python, Kubernetes") | Entwickler |
| Die Häkchenliste über „genannte Fähigkeiten" | Entwickler |
| Profilüberschrift + Text als einziger Aushang | Wissensarbeit |

Ein Metallbauer, eine Elektronikerin, ein Pfleger haben **kein GitHub** — und
sie haben etwas anderes: **Zeugnisse, Zertifikate, Nachweise, Arbeitsproben**.
Davon ist das meiste schon gebaut (`Unterlage`, `portfolio-service`), es wird
nur nicht angeboten.

**Die Entscheidung, die daraus folgt:** ein Beleg ist *berufsabhängig*. GitHub
ist **eine** Quelle unter mehreren, nicht die Quelle. Das braucht ein ADR
(PBI-1), und daran hängt alles Weitere.

---

## PBI-1 — Berufsfeld beim Konto, und Belege danach

> **Als Metallbauer** will ich mich anmelden, ohne dass die Plattform mich nach
> einem GitHub-Konto fragt, **damit** ich meine Zeugnisse und Nachweise zeigen
> kann statt eines Repositoriums, das ich nie haben werde.

**Warum zuerst:** Scout und Berater suchen nach Belegen. Solange „Beleg" nur
GitHub heißt, sucht der Scout an drei Vierteln der Arbeitswelt vorbei.

### Aufgaben

- [x] **ADR-0039: Belege sind berufsabhängig.** Was ein Berufsfeld ist, welche
      Belegarten es zulässt, und warum GitHub dadurch *nicht* abgewertet wird.
      Die drei Herkunftsklassen aus ADR-0033 (genannt / belegt / vorgeschlagen)
      gelten unverändert — es kommen nur Quellen dazu.
- [x] `berufsfeld` auf `users` (nullable, aus einer **geschlossenen** Liste),
      gesetzt bei der Registrierung, änderbar in den Einstellungen
      (`PUT /account/occupational-field`, leer heisst entfernen).
- [x] Die Liste selbst: **kein Freitext.** Ein Feld, aus dem eine Navigation
      folgt, muss endlich sein. Vorschlag als Startpunkt (erweiterbar, jede
      Erweiterung ist eine Entscheidung):
      `handwerk` · `industrie_technik` · `bau` · `gesundheit_pflege` ·
      `logistik_verkehr` · `gastronomie_hotel` · `handel_verkauf` ·
      `buero_verwaltung` · `it_software` · `bildung_soziales` · `sonstiges`
- [x] **Belegarten je Feld**, als Tabelle im ADR und als Wert im Code
      (`web/src/shared/lib/berufsfelder.ts`; jedes Feld erbt die allgemeinen):
      | Feld | Belege, die zählen |
      |---|---|
      | `it_software` | GitHub, Portfolio, Zertifikate |
      | `handwerk`, `industrie_technik`, `bau` | Gesellenbrief, **Meisterbrief**, Schweißerpass, Staplerschein, Arbeitsproben (Fotos) |
      | `gesundheit_pflege` | Berufsurkunde, Fortbildungen, Führungszeugnis (**nie hochgeladen**, nur genannt) |
      | `logistik_verkehr` | Führerscheinklassen, ADR-Schein, Fahrerkarte |
      | alle | Arbeitszeugnisse, Zertifikate, Referenzen |
- [x] **Navigation folgt dem Feld**: `/github` erscheint nur bei `it_software`.
      Die Route bleibt erreichbar (wer sie kennt, darf sie nutzen) — sie wird
      nur nicht angeboten. Verstecken ist keine Zugriffskontrolle.
- [x] Registrierung: ein Auswahlfeld, **kein Pflichtfeld**. Wer nichts wählt,
      bekommt die neutrale Ansicht. Ein Pflichtfeld an der Anmeldung ist eine
      Hürde vor dem ersten Nutzen.
- [x] Der Wortschatz (`WorkerTransfer.Skills`) bekommt die Handwerksbegriffe:
      `MIG/MAG`, `WIG`, `CNC`, `SPS`, `Hubwagen`, `Gerüstbau`, … Die Regel aus
      ADR-0023 gilt unverändert: **benennt um, folgert nie.**

### Abnahme — erfüllt (10.09.2026)

- Ein Konto mit `handwerk` sieht **kein** GitHub in der Navigation, und die
  Profilseite bietet stattdessen Nachweise an. — `SiteHeader.test.tsx` fährt
  die drei Fälle, `e2e/occupational-field-journey.spec.ts` die ganze Naht.
- Ein Konto ohne Berufsfeld sieht die heutige Ansicht — nichts wird schlechter.
  Die Spalte ist nullbar ohne Vorgabe, es gibt **kein** Wanderungsskript, und
  nichts wird geraten: eine GitHub-Verbindung macht niemanden zu `it_software`.
- `Skills`-Test: ein Handwerksbegriff wird kanonisiert, **keiner** wird
  gefolgert — `Aus_MIG_MAG_folgt_kein_Schweissen` und
  `Ein_Schein_ist_keine_Maschine` stehen neben `Aus_React_folgt_kein_JavaScript`.
- Drei Kataloge, drei Sprachen.

**Gegenproben gefahren**, alle vier fielen und keine war ein Übersetzungsfehler:
`zeigtGitHub` auf „immer" → die Handwerk-Reihe fällt; `nurGenannt` vom
Führungszeugnis genommen → die Belegreihe fällt; die 422-Prüfung am Endpunkt
entfernt → `Ein_unbekanntes_Feld_wird_abgesagt` fällt; `SaveAsync` im Befehl
weggelassen → `Nachtragen_und_zuruecknehmen_wirken_beide` fällt.

---

## PBI-2 — Rollen erzwingen

> **Als Inhaberin eines Unternehmens** will ich, dass ein `member` meine
> Stellen nicht löschen kann, **damit** „admin" mehr ist als ein Wort in einer
> Tabelle.

**Die größte echte Lücke im Baum.** Heute: **null** `RequirePermission`. Die
Navigation *versteckt* Firmeneinträge; der Server antwortet 403 nur dort, wo
jemand daran gedacht hat. `Mitgliedschaftsrecht` liest die Rolle je Anfrage —
aber niemand fragt.

### Aufgaben

- [x] Jede Firmen-Route durchgegangen. Die **Linie**, einmal aufgeschrieben,
      damit die nächste Route sie nicht neu erfindet: `admin` ist, wer das
      Unternehmen **bindet oder ändert**, wer dazugehört. `member` ist die
      tägliche Arbeit im Namen der Firma — lesen, entwerfen, schreiben,
      ansprechen, Bewerbungen bearbeiten. **Im Zweifel `member`:** ein zu enges
      Recht macht aus einer Einladung eine Zuschauerkarte, und dann legt jemand
      einen zweiten Admin an, um arbeiten zu können — dann ist „admin" wieder
      ein Wort in einer Tabelle.

      | Route | Recht |
      |---|---|
      | `POST /companies/{id}/invitations` | `company.invite` |
      | `DELETE /companies/{id}/invitations/{id}` | `company.invitations.withdraw` |
      | `DELETE /companies/{id}/members/{id}` | `company.members.remove` |
      | `POST /jobs/{id}/publish` | `jobs.publish` |
      | `POST /jobs/{id}/close` | `jobs.close` |
      | `PUT /companies/me/profile` | `company.profile.write` |
      | `POST /transfers/{id}/offer` | `transfer.offer` |
      | `POST /transfers/{id}/complete` | `transfer.complete` |

      Drei Entscheidungen, die auch anders hätten ausfallen können, und warum
      nicht: `POST /jobs` und `PUT /jobs/{id}` bleiben beim Mitglied (ein
      Entwurf steht niemandem gegenüber — die Linie liegt am Aushang, nicht am
      Text); `POST /transfers/{id}/withdraw` ebenso (wer anfangen darf, muss
      aufhören dürfen, sonst ist die Einladung eine Falle); `GET
      /companies/{id}/members` und `GET .../invitations` ebenso (eine Firma muss
      ihrer eigenen Belegschaft nicht verschweigen, wer dazugehört und wen sie
      sucht).
- [x] Die Richtlinien hängen an den Endpunkten (`Permission:*`, aufgelöst von
      Girders `PermissionPolicyProvider` — derselbe Name, den
      `[RequirePermission]` setzt; geschrieben als `RequireAuthorization`, wie
      die zwei, die es schon gab).
- [x] **Die Rolle kommt aus der Mitgliedschaftstabelle, je Anfrage.** Sie liegt
      in identity-service und nirgends sonst (ADR-0004), also fragen die anderen
      Dienste über `GET /internal/companies/{id}/members/{sub}/role` hinter dem
      gemeinsamen Geheimnis. `ServiceDefaults.Rollen` trägt den Mechanismus, die
      **Liste** steht je Dienst im eigenen `Program.cs` — der Mechanismus darf
      nicht elfmal beantwortet werden, die Liste ist eine Entscheidung.
      **Kein Zwischenspeicher**, aus demselben Grund wie beim Ledger (ADR-0013):
      wer entfernt wird, ist bei der nächsten Anfrage draußen.
- [x] **Ein Schweigen der Rollenauskunft ist 503, nie 403.** Ein
      Autorisierungshandler kann nur „ja" sagen; er hinterlässt deshalb eine
      Notiz am `HttpContext`, und `Ablehnungsgestalt` macht daraus 503. Ein 403
      läse sich als „dir wurde das Recht genommen", und niemand suchte nach
      einem Ausfall.
- [x] `docs/routenkarte.yml` hat die vierte Spalte `mitglied`, und
      `scripts/routenkarte.sh` fährt sie. Das vierte Konto entsteht wie ein
      Mensch: registriert, mit `role: "member"` eingeladen, beigetreten. Das
      Skript **weigert sich zu messen**, wenn es nicht denselben Mandanten und
      die Rolle `member` trägt — ohne diese Prüfung mäße ein misslungener
      Beitritt still eine Person ohne Firma, und alle acht Zeilen sähen trotzdem
      grün aus, weil 403 dort auch für eine Person richtig ist.
- [x] Ein Test je geschützter Route: `member` 403, `admin` 200 — in derselben
      Reihe, denn ein Endpunkt, der IMMER 403 antwortet, ist von einem richtig
      geschützten nicht zu unterscheiden. `RollenTests` in jobs, companies und
      transfer; `UnternehmensreiseTests` in identity.

### Abnahme — erfüllt (11.09.2026)

- `make routenkarte` fährt **vier** Spalten, alle wie aufgeschrieben.
- **Gegenprobe gefahren:** `RequireAuthorization` an `POST /jobs/{id}/publish`
  entfernt → **drei** Reihen fielen
  (`Nur_ein_Administrator_veroeffentlicht`, `Gefragt_wird_nach_dem_Mandanten…`,
  `Eine_schweigende_Rollenauskunft…`). Sie **kompilierte** — ein Build-Fehler
  läse sich in der Ausgabe wie ein bestandener Test. Danach zurückgenommen und
  mit `--no-incremental` neu gebaut.
- Eine zweite Gegenprobe fiel nebenbei an und ist wertvoller als die erste:
  `ZeitlimitTests` suchte wörtlich nach `client.Timeout` und wurde rot an
  `HttpFirmenrollen`, das sein Zeitlimit an `klient` setzt — also an
  **richtigem** Code. Gesucht wird jetzt die Zuweisung `.Timeout =` statt eines
  Variablennamens. Ein Wächter, der bei richtigem Code rot wird, wird beim
  nächsten Mal weggeschaltet statt gelesen.

### Was PBI-2 NICHT getan hat

- **Die Rolle steht weiterhin nicht im Token.** Das wäre der kürzere Weg und
  der schlechtere: eine Entfernung wirkte dann erst beim Ablauf.
- **`GET`-Routen haben keine Richtlinie bekommen.** Was nicht in der Liste
  steht, darf jedes Mitglied — die Prüfung „handelst du für eine Firma?" steht
  schon am Endpunkt, und eine zweite Richtlinie daneben wäre eine zweite Stelle
  für dieselbe Frage.
- **Die Oberfläche versteckt weiterhin, und das ist jetzt nur noch Bequemlich-
  keit.** Niemand hat die Navigation angefasst; ein `member` sieht heute
  Firmeneinträge, die 403 antworten. Das ist die richtige Reihenfolge (erst der
  Server, dann die Anzeige), aber es ist Arbeit, die noch aussteht.

---

## PBI-3 — `scout-service`

> **Als Unternehmen** will ich eine Anforderung stellen und Menschen sehen, die
> sie erfüllen — **mit Häkchen und Belegen, nie mit einer Zahl.**

ADR-0036 ist **angenommen**. Der Code fehlt.

### Aufgaben

- [ ] Neuer Dienst nach dem Muster der elf anderen (`Api`/`Application`/
      `Domain`/`Infrastructure`/`Contracts`, eigene Datenbank, `AddGirder`).
- [ ] `Suche` (Aggregat): Fähigkeiten, Ort, Umkreis, Berufsfeld, Verfügbarkeit.
      **Gespeichert wird die Anfrage, nie das Ergebnis** — ein gespeichertes
      Ergebnis über Menschen veraltet gegen einen Widerruf.
- [ ] Die Treffer kommen aus profile-service (`/candidates` wird abgelöst, die
      harten Teile — Ledger je Zeile über `/check-batch`, **keine Gesamtzahl**,
      Firmenzwang — werden **mitgenommen, nicht neu erfunden**).
- [ ] Belege werden zum Treffer **dazugeholt**, nie zum **Finden** benutzt.
- [ ] Die Nachricht „dein Profil wurde entdeckt" (ADR-0033): eigene Art,
      **nennt kein Unternehmen**, über den Postausgang, **höchstens eine je
      Person und Tag**.
- [ ] Löschempfänger ab der ersten Tabelle.

### Abnahme — die vier Auflagen als Tests

- Keine Sortierung nach Passung (zweimal laden → gleiche Reihenfolge).
- Keine Zahl: `Adr0022Tests`-Muster über Domäne und Verträge.
- Nur Genanntes ist durchsuchbar (ein nur *belegtes* Wort findet niemanden).
- Die Ansprache ist ein **Entwurf**; der Dienst schreibt niemandem.

---

## PBI-4 — `advisor-service`

> **Als Arbeiter mit laufendem Vertrag** will ich einen Berater, der mein
> Mandat kennt, **damit** ich meinen Eintrittstermin nicht dreimal sagen muss
> und mein jetziger Arbeitgeber nichts erfährt.

ADR-0037 ist **angenommen**. Der Code fehlt.

### Aufgaben

- [ ] **Das Mandat ist eine Sicht**, kein zweiter Speicher: Sichtbarkeit lebt
      im Ledger, Verfügbarkeit im Marktstatus (ADR-0020 verbietet die Kopie).
      Eigen sind nur: Eintrittstermin, Gehaltsspanne, Pensum, ausgeschlossene
      Unternehmen.
- [ ] Gespräche in drei Stufen, jede Stufe eine Freigabe der Person.
- [ ] Was in Stufe 1 nicht frei ist, **existiert für die Gegenseite nicht** —
      kein „gesperrt"-Hinweis, der die Existenz verrät.
- [ ] Einigung → Übergabe an `transfer-service` (Dreieckskonsens steht dort
      bereits).

### Abnahme

- Der jetzige Arbeitgeber sieht die eigene Belegschaft **nicht** im Scout.
- Eine Stufenfreigabe wirkt sofort; ein Widerruf ebenso.

---

## PBI-5 — `assessment-service`

> **Als Unternehmen** will ich eine Arbeitsprobe stellen, **damit** ich sehe,
> wie jemand arbeitet — ohne daraus eine Note über einen Menschen zu machen.

**Kein ADR, kein Code.** Der dritte aus `SCOUT-UND-BERATER.md` und der mit dem
größten Missbrauchspotenzial: unbezahlte Arbeit als Aufgabe getarnt.

### Aufgaben

- [ ] **ADR-0040 zuerst.** Drei Regeln, die den Unterschied zwischen einer
      Aufgabe und einer Prüfung mit Note ausmachen:
      1. Die Bewertung gehört dem **Vorgang**, nicht dem Menschen — in keiner
         Suche, in keinem Profil, für kein anderes Unternehmen, ohne Zahl.
      2. Die Person **sieht** die Bewertung. Immer, auch bei Absage.
      3. Der **Umfang in Stunden steht in der Ausschreibung**, und Ablehnen
         wird nirgends vermerkt.
- [ ] Danach der Dienst.

---

## PBI-6 — Aufräumen, was das Review offen ließ

- [ ] **Fund 7:** das tote Bewerbungsformular in `JobApplyPage` (~120 Zeilen
      samt Freigabeschaltern). Entscheidung: löschen. Toter Code mit
      Einwilligungsschaltern ist das, was später falsch wiederbelebt wird.
- [ ] `make k8s-up` **einmal wirklich fahren**. Das Diagramm lintet und
      rendert; nur ein Lauf beweist es.
- [ ] `GET /notifications` → 405 (in der Karte festgehalten, nie behoben).
- [ ] Kommentardichte: `docs/AUFTRAG-ENTLASTUNG.md` wartet auf eine
      Entscheidung.

---

## Die KI-Frage: LangChain, LangGraph, RAG?

**Empfehlung: beim dünnen Port bleiben. Und hier ist der Grund, nicht die
Meinung.**

### Was heute steht

`IAnschreiber` ist ein Port; `HttpAnschreiber` sind **520 Zeilen**, die *zwei*
Protokolle sprechen — Anthropic Messages und `openai_compatible`. Damit sind
abgedeckt: Anthropic, OpenAI, **Ollama**, **MiniMax**, vLLM, LiteLLM, LM Studio,
Groq, Together. Streaming über SSE **und** NDJSON. Der Anbieter hängt am
**Konto** (`KiZugang`), der Schlüssel liegt verschlüsselt und geht nie an den
Browser.

### Warum kein Framework

1. **Es gäbe eine zweite Fehlergestalt.** RFC 9457 gilt hier überall; ein
   Framework bringt seine eigene mit. Genau diese Divergenz wird später in der
   Oberfläche zugekleistert — dieselbe Begründung, aus der Girders
   `ICommand<T>` nicht benutzt wird.
2. **Ein Graph besitzt den Ablauf — und der gehört hier dem Menschen.**
   ADR-0034 ist genau darüber: kein Aufruf ohne Handlung, keine
   Reflexionsschleife, Freigeben und Senden getrennt. Ein Orchestrierer, dessen
   Zweck das selbstständige Weiterlaufen ist, arbeitet gegen die Zusage.
3. **Das .NET-Ökosystem ist nicht der Ort.** LangChain/LangGraph sind
   Python-erst; die .NET-Ports hinken und sind dünner als die 520 Zeilen, die
   sie ersetzen würden. Wir tauschten geprüften Code gegen eine Abhängigkeit
   mit weniger Deckung.
4. **Wir haben schon den Preis bezahlt.** Streaming, zwei Protokolle,
   Zeitlimits, Fehlerarten — das ist die Arbeit, die ein Framework abnimmt, und
   sie ist getan und getestet.

### Wo RAG *ehrlich* hingehört — und wo nicht

**Nicht über Menschen.** ADR-0033: durchsuchbar ist nur, was eine Person
**selbst genannt** hat. Ein Einbettungsindex über Profile und Lebensläufe wäre
genau die Suche „nach Ähnlichkeit zu einem Menschen", die ADR-0022 ausschließt
— und er läge als Kopie personenbezogener Daten neben der Löschung.

**Ehrlich ist RAG über die *eigenen* Unterlagen einer Person:**

> „In deinen hochgeladenen Zeugnissen steht *Schweißfachmann DVS*. Willst du
> das als Fähigkeit ins Profil übernehmen?"

Das ist die **Brücke aus ADR-0033** (belegt → vorgeschlagen → genannt), nur mit
Volltext statt GitHub-Topics. Es spricht über *ihre eigenen* Dokumente, zu ihr,
und macht daraus nichts ohne zwei Klicks. **Das ist PBI-7**, und es ist der
einzige Ort, an dem ein Index personenbezogener Texte hier vertretbar wäre —
mit einer Auflage: **er lebt im selben Dienst wie die Unterlagen und fällt mit
der Löschung.**

### Als Referenzprojekt

Der Wert liegt nicht in einer LangChain-Demo — davon gibt es tausende. Er liegt
in dem, was hier selten ist: eine **KI-Naht mit Zusagen**, die man vorzeigen
kann. Typisierter Kontext als Grenze, Feldliste als Test, kein Gedächtnis,
Anbieter je Konto, Schlüssel verschlüsselt, jeder Schritt eine Handlung eines
Menschen. Das ist der Teil, den kaum jemand baut.

---

## PBI-7 — Vorschläge aus den eigenen Unterlagen (optional, nach PBI-1)

> **Als Elektroniker** will ich, dass die Plattform mir aus meinem hochgeladenen
> Zertifikat vorschlägt, was ich in mein Profil schreiben könnte, **damit** ich
> nicht raten muss, wonach Unternehmen suchen.

- [ ] Texterkennung nur auf Auslösung, nie im Hintergrund (ADR-0004).
- [ ] Der Index lebt in resume-service und fällt mit der Löschung.
- [ ] Zwei Handlungen bis zur Nennung — Klick füllt das Feld, **Speichern**
      macht daraus eine Aussage.
- [ ] Nichts davon ist durchsuchbar, bevor die Person gespeichert hat.
