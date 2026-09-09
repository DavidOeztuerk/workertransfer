# Wo WorkerTransfer steht, und was fehlt

- **Datum:** 03.09.2026
- **Gemessen an:** dem laufenden `docker compose`-Stapel, nicht am Quelltext
- **Zweck:** eine eigenständige Bestandsaufnahme — was trägt, was behauptet
  wird, und was zwischen dem Heute und der Vision liegt

Diese Datei ist eine **Analyse**, keine Anleitung. Sie nennt Zahlen, wo sie
gemessen wurden, und sagt es dazu, wo sie es nicht sind.

---

## 1. Was heute wirklich trägt

Elf Dienste, ein Gateway, eine Oberfläche. **757 Backend-Tests, 83
Frontend-Tests, 21 Playwright-Reisen, 333 Antworten der Routenkarte** — alle
grün, alle an diesem Tag gefahren.

Das ist nicht das Bemerkenswerte. Bemerkenswert ist, **welche Zusagen
maschinell gehalten werden**:

| Zusage | Wer hält sie | Fiele sie auf? |
|---|---|---|
| Einwilligung wirkt sofort, nie zwischengespeichert | ADR-0013 + Reisen | ja |
| Kein Punktwert über einen Menschen | `Adr0022Tests` scannt Vokabular | ja |
| Sichtbarkeit nur im Ledger, 404 ≡ 404 | `Profil`-Tests | ja |
| Löschung geht vollständig durch, acht Empfänger | `LoeschempfaengerTests` liest das EF-MODELL | ja |
| Die Karte kennt jeden Endpunkt in drei Handlungsformen | `RoutenkarteTests` + Skript | ja |
| Der Draht spricht dieselbe Sprache wie der Empfänger | `BenachrichtigungsdrahtTests` | ja |
| Kein deutsches Literal in der Oberfläche | Katalog-Wächter | ja |
| Kein Katalogschlüssel roh im Text | `schluessel.test.ts` | ja |

**Die Löschwächter sind der stärkste Teil des Systems.** `LoeschempfaengerTests`
liest nicht den Quelltext, sondern das EF-Modell, und geht rot, sobald
irgendein Dienst eine Tabelle mit Personenbezug bekommt und nicht auf der
Empfängerliste steht. Das ist der Unterschied zwischen einer Regel und einer
Absicht.

---

## 2. Was gemessen wurde — und was dabei herauskam

### 2.1 Die Korrelationskennung reist. Sie ist nur unsichtbar.

**Probe:** ein `X-Correlation-ID: PROBE2-…` durch das Gateway an
consent-service, dann in den Protokollen gesucht.

| Wo | Ergebnis |
|---|---|
| Antwortkopf `X-Correlation-ID` | ✅ derselbe Wert |
| `correlationId` im Problemdokument | ✅ derselbe Wert |
| Logdatei **im Container** (`/app/consent/logs/…`) | ✅ `CorrelationId: PROBE2-…` |
| **Konsole** (`docker compose logs`) | ❌ **kein Treffer** |

**Der Befund ist nicht „sie fehlt", sondern „sie steht am falschen Ort."**
Girders Serilog-Aufbau hat `Enrich.FromLogContext()`, und
`CorrelationIdMiddleware` legt die Kennung per `BeginScope` an jedes Ereignis.
Die **Dateisenke** schreibt sie ausdrücklich aus. Die **Konsolensenke** für
`Development` nicht:

```
outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}"
```

Warum das zählt: **in einem Container IST die Konsole das Protokoll.**
`docker compose logs` und `kubectl logs` lesen stdout. Die Datei im Container
erreicht im Betrieb niemand, und sie fällt mit dem Container. Der Faden, an dem
eine Beschwerde durch alle Dienste zurückverfolgbar sein soll — so steht es in
`shared/api/fehler.ts` —, ist damit vorhanden und unbenutzbar.

Ein zweiter, kleinerer Fund derselben Probe: Girder schreibt in der Produktion
nach `/app/logs/…`, unser Einstiegspunkt wechselt aber vorher in
`/app/$SERVICE_DIR`. Die Dateien landen also unter `/app/consent/logs/` — nicht
falsch, aber nicht dort, wo Girder es meint.

### 2.2 Das Token trägt neun Ansprüche, nicht acht

`CLAUDE.md` sagte: `sub`, `email`, `jti`, `iat`, `exp`, `iss`, `aud`,
`session_id`, dazu `tenant` bei einer Firma — **„nothing else"**.

Auf dem Draht steht zusätzlich:

```
http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier
```

eine Verdopplung von `sub` in der langen WS-Schreibweise, rund 84 Byte in jeder
Anfrage.

**Sie ist in Girder begründet und bleibt.** `MapInboundClaims = false` schaltet
die Ableitung des Gerüsts ab, und siebzehn Leser holen den Aufrufer über diesen
Namen — zwei davon in Fremdpaketen, die Girders Assembly nicht sehen. Sie
fallenzulassen macht aus jedem dieser Leser ein stilles `null`.

**Der Fehler lag also in der Zusage.** Und schwerer wiegt: `TokenformTests`
verbot zwei *Namen* (`tenant_id`, `type`) und hätte einen neunten nie bemerkt.
Ein Verbot einzelner Mitglieder ist keine Aussage über die Menge. Der Test hält
seit heute den **vollständigen Satz**, in beiden Handlungsformen.

### 2.3 Nichts ruft nach draussen

**Probe:** das gebaute Bündel und der Quelltext der Oberfläche nach fremden
Adressen durchsucht.

- Keine Skripte von Fremdservern, keine Schriften von Fremdservern
  (`@fontsource-variable/inter` liegt lokal), keine Werbe- oder Messdienste.
- Die einzigen fremden Adressen im Bündel sind **Fehlermeldungen von
  Bibliotheken** (`https://mui.com`, `https://react.dev`) — Text, kein Abruf.
- Keine OTLP- oder Jaeger-Adresse in `docker-compose.yml`: die Beobachtbarkeit
  ist verdrahtet, aber es gibt keinen Empfänger. Nichts verlässt die Maschine.

Das ist für eine Plattform, die über Einwilligungen entscheidet, kein Detail —
es ist die Voraussetzung dafür, dass die Datenschutzseite stimmt.

---

## 3. Girder: was wir damit erreichen wollen, und wo es steht

### 3.1 Der Zweck

Girder ist **kein Framework, sondern eine Zusagensammlung**: dass ein Token in
elf Diensten gleich geprüft wird, dass ein Fehlschlag überall gleich aussieht,
dass die Reihenfolge der Kette dieselbe ist. Der Gewinn ist nicht Codeersparnis
— er ist, dass es **elf Gelegenheiten weniger gibt, dieselbe Frage verschieden
zu beantworten**.

Der Auftrag Ihres letzten Durchgangs war genau der: *„wenn der Entwickler sich
immer noch um Ratenbegrenzung und Korrelation kümmern muss, obwohl er Girder
installiert hat, dann bringt das gar nichts."* Das ist eingelöst — `Bremse.cs`
und `Korrelation.cs` sind gelöscht, beides kommt jetzt aus `Girder.Http`, und
konfiguriert wird es in `ocelot.json` und der Umgebung.

### 3.2 Was steht

Von 26 Modulen ruft `UseDefaults()` 19. Wir nehmen sie und schliessen **sechs**
aus, jedes mit einer Begründung im Code (`GirderBuilder` lehnt eine leere ab):

| Ausgeschlossen | Warum |
|---|---|
| `RateLimiting` | Topologie: ein Dienst hinter dem Gateway sieht als Herkunft nur das Gateway |
| `PermissionEnforcement` | Blanket-401 auf jeden Pfad; wir haben eine öffentliche Oberfläche |
| `Communication` | Verlangt einen Broker, routet Dienst-zu-Dienst durch das Gateway, cacht GET-Antworten fünf Minuten |
| `HttpResponseCaching` | ADR-0013: eine Einwilligung muss sofort wirken |
| `Encryption` | Verlangt `IMasterKeyProvider` — die Entscheidung stand aus. **Seit heute steht sie** (siehe unten) |
| `ResourceAuthorization` | Ungemessen |

### 3.3 Was Girder für maximale digitale Souveränität noch fehlt

Nach der Messung von heute, sortiert nach Gewicht:

**1. Die Korrelationskennung gehört auf die Konsole.** Ein Wort in der
Entwicklungsvorlage (`{CorrelationId}`), und der Faden ist da, wo ihn jemand
findet. Das ist die billigste und wirksamste Änderung auf dieser Liste.

**2. `ErrorMessageService` ist auf Deutsch verdrahtet.** Rund zwanzig
Meldungen, fest im Code, die einzige Datei in ganz Girder mit deutschem Text.
Eine Bibliothek, die die Sprache ihres ersten Anwenders festschreibt, ist für
jeden zweiten kaputt. Uns erreicht sie heute nicht (gemessen: jede Antwort auf
dem Draht ist englisch), aber sie steht der Verbreitung im Weg.

**3. Es gibt keine Schlüsselverwaltung, nur einen Schlüssel.** Der
`Geheimnisspeicher`, den wir heute gebaut haben, ist ehrlich beschriftet: AES-GCM,
Hauptschlüssel aus der Umgebung, **keine Rotation, keine Versionierung**. Wer
ihn wechselt, macht jedes hinterlegte Geheimnis unlesbar. Für den ersten Schritt
richtig; für „digitale Souveränität" fehlt die Version im Geheimtext, damit zwei
Schlüssel nebeneinander gelten können. Das ist die Stelle, an der Girders
`AddSecretManagement` einen Platz hätte — und Infisical füllt dann dieselbe
Variable, nicht eine neue Mechanik.

**4. Kein Dienst kann heute ohne Fremdanbieter denken.** Bis heute war die
Entwurfshilfe fest auf Anthropic verdrahtet. Seit heute wählt die Person, und
**„eigener Server" steht vor den Fremdanbietern** — ein lokales Ollama ist die
einzige Wahl, bei der der Text die Maschine nicht verlässt. Was noch fehlt: die
Umsetzung im `IEntwerfer`, die diese Wahl auch liest (der Speicher steht, der
Adapter noch nicht).

**5. Beobachtbarkeit ohne Empfänger.** `AddObservability` läuft, es gibt keinen
Collector. Das ist kein Mangel an Souveränität — es ist die *souveränste*
Einstellung. Aber es heisst auch: es gibt keine Spuren, wenn man sie braucht.
Ein lokaler Collector im Compose-Stapel (Jaeger, Tempo) wäre der nächste
Schritt, und er würde nichts nach draussen geben.

---

## 4. Die Vision, und was zwischen ihr und heute liegt

`docs/SCOUT-UND-BERATER.md` (411 Zeilen) beschreibt drei Dienste, die es nicht
gibt: **Scout** (Anforderung hinein, Menschen mit Häkchen und Belegen heraus),
**Berater** (das Mandat einer Person und ihre gestuften Gespräche mit einem
Unternehmen), **Assessment** (eine benannte, begrenzte Aufgabe, deren Bewertung
dem Vorgang gehört und nicht der Person).

Ihr Bild aus diesem Durchgang — *„der Berater eines Unternehmens kann mit einem
Freelancer reden, oder mit einem Angestellten, der geheim bleiben will"* — ist
genau der Berater-Dienst. Was dafür fehlt, in der Reihenfolge, in der es
gebraucht wird:

**1. Der Chat-Faden selbst.** Ein Gespräch hat eine Historie, und die muss bei
jedem Zug mitgehen. Das ist eine neue Tabelle und ein neuer Dienst. **Kein
technisches Problem, aber ein ADR-Problem:** `CLAUDE.md` verbietet ausdrücklich,
dass ein Agent diese Dienste ohne eigenen ADR baut, und der Grund ist gut — die
Frage „was darf ein Berater über eine Person sagen" ist keine, die man beim
Programmieren beantwortet.

**2. Die Regel „beide brauchen einen Schlüssel" braucht eine Antwort für den
Normalfall.** Sie haben sie selbst genannt: *„wenn der eine keinen hat, kann er
auch nicht mit ihm quatschen."* Das ist eine harte Zutrittsschranke — und
zugleich der Grund, warum Ollama als **Vorgabe des Betreibers** die bessere
Antwort ist: dann hat jeder einen, und wer mehr will, bringt seinen eigenen mit.

**3. Die Rollen sind nicht durchgesetzt.** `admin` gegen `member` steht nirgends
im Weg; das Menü versteckt, der Server antwortet 403 nur da, wo eine Abfrage
zufällig eine Firma verlangt. Für „Berater" und „Scout" als *Rollen* eines
Unternehmens ist das die Voraussetzung.

**4. Die Geheimhaltung des Angestellten ist heute schon gebaut** — und das ist
das Bemerkenswerte an diesem System. Ein Marktstatus wird Unternehmen für
Unternehmen freigegeben, es gibt kein „für alle", die Mail nennt nie den Anlass,
und der jetzige Arbeitgeber wird nie gefragt. Der Berater-Dienst muss diese
Zusage **erben**, nicht neu erfinden.

---

## 5. Was offen ist, ohne Beschönigung

**Aus dem laufenden Auftrag**

- **Kommentare unter 10 %** — gebaut, in drei Schärfen gemessen, zurückgenommen.
  Der schärfste Filter kommt auf 20 % und löscht dabei die Sätze, die erklären,
  *warum 503 und nicht 404*. Braucht eine Entscheidung, keine Regex
  (`docs/AUFTRAG-ENTLASTUNG.md`).
- **Der `IEntwerfer` liest die Kontowahl noch nicht.** Speicher, Endpunkt und
  Oberfläche stehen; der Adapter fragt weiterhin den Anbieter aus der Umgebung.
- **Der Berater-Chat ist nicht gebaut.** Absichtlich: er braucht ADR-0032.

**Älteres, das steht**

- **`make k8s-up` wurde auf dieser Maschine nie gefahren.** Das Chart lintet und
  rendert; nur ein Lauf beweist es.
- **`GET /notifications` antwortet 405** — in der Karte festgehalten, nicht
  behoben.
- **Die Kandidatenliste blättert nicht.** Sie bleibt beim Zeiger, und das ist
  richtig: eine Gesamtzahl sagte über die Differenz, wie viele Menschen sich
  verborgen haben (ADR-0026). Sie braucht trotzdem einen Weg zur zweiten Seite.
- **Sechs Commits liegen lokal**, nicht gepusht.

**Was ich NICHT als Lücke zähle**

Kein Broker, kein Zwischenspeicher für Einwilligungen, keine Bewertung von
Menschen, keine Gesamtzahl auf der Kandidatenliste, kein Reflect-Loop bei der
KI. Das sind **Entscheidungen mit ADR**, und sie sind der Grund, warum diese
Plattform anders ist als die, mit denen sie verglichen wird.

---

## 6. Die drei Sätze, wenn Sie nur drei lesen

1. **Das System hält seine Zusagen maschinell** — die Löschwächter, die
   Routenkarte und die Katalogwächter fallen, bevor ein Mensch etwas merkt. Das
   ist der eigentliche Wert dessen, was hier steht.

2. **Zwei Behauptungen stimmten nicht, und beide waren dokumentiert** — die
   Korrelationskennung ist auf der Konsole unsichtbar, und das Token trägt einen
   Anspruch mehr als versprochen. Beide fand erst die Messung am laufenden
   Stapel, keine davon ein Test. Das ist die Lehre, nicht der Fund.

3. **Die Souveränität ist eine Konfigurationsfrage geworden, keine
   Architekturfrage.** Nichts ruft nach draussen, ausser jemand trägt einen
   Anbieter ein — und die erste Wahl in der Liste ist die eigene Maschine.
