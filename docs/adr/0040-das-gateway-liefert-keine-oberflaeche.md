# ADR-0040: Das Gateway liefert keine Oberfläche

**Status:** angenommen (11.09.2026)
**Betrifft:** gateway, `web/`, docker-compose, `deploy/helm/`, E2E
**Verwandt:** ADR-0028 (ein Image, eine Landkarte, eine Replik), ADR-0031 (die Plattform spricht die Sprache der Person)

## Warum es dieses ADR gibt

Seit dem 02.09.2026 lieferte das Gateway zwei verschiedene Dinge unter derselben
Adresse aus. `GET /jobs` mit `Sec-Fetch-Dest: document` beantwortete die
`Navigation`-Zwischenschicht mit der Seite, indem sie den Pfad auf `/__ui/jobs`
umschrieb und eine Ocelot-Route ihn an den `web`-Container weiterreichte;
dieselbe Adresse ohne den Kopf beantwortete jobs-service mit JSON. Gemessen am
laufenden Stapel, beides mit einem `curl` Abstand von zwei Sekunden.

Der Anlass war echt: hinter **einem** Ursprung kollidieren die Pfade der
Oberfläche mit denen der Dienste, und ein geteilter Direktlink auf `…/jobs`
lieferte rohes JSON. Die Lösung hat das behoben und dabei drei Dinge eingeführt,
die teurer sind als das Problem:

- **Eine Adresse bedeutet zwei Sachen.** Welche, entscheidet ein Kopf, den kein
  Mensch sieht. Das ist bei jedem neuen Endpunkt eine Regel, an die sich jemand
  erinnern muss.
- **Der Entwicklungsserver wurde zum Proxy-Ziel.** Damit ging HMR durch das
  Gateway verloren: Vites Client leitet seine WebSocket-Adresse aus der
  Seitenadresse ab, landete auf `ws://localhost:8090/`, fand dort keine Route
  (ein Handshake trägt `Sec-Fetch-Dest: websocket`, nicht `document`) und bekam
  ein leeres `200`. Weil die Bestätigungsmail auf das Gateway zeigte, landete
  dort jeder, der sein Konto bestätigte.
- **Es wich vom Muster des Hauses ab.** Skillswap führt dasselbe Gespann und
  trennt es sauber: 263 Routen, keine davon auf das Frontend, keine
  Auffangregel, keine Kopfbedingung. Die Oberfläche liegt auf `:3000`, das
  Gateway auf `:8080`.

## Die Entscheidung

**Das Gateway ist eine API-Tür.** Es liefert keine Oberfläche aus, unter keinem
Kopf und für keinen Pfad. Die Oberfläche liegt in der Entwicklung auf `:5173`
und ruft von dort das Gateway auf `:8090`; `CORS_ORIGINS` nennt beide,
`WORKERTRANSFER_WEB_URL` zeigt auf die Oberfläche, und die Bestätigungsmail
damit auch.

Entfernt wurden: die Route `/__ui/{alles}`, `Navigation.cs`, `app.UseNavigation()`
und die vier Testreihen, die die Umschreibung festhielten. `allowedHosts: ["web"]`
in `vite.config.ts` fällt mit weg — es stand nur da, weil Ocelot den Host des
Ziels mitschickte.

### Die verworfene Möglichkeit: es lassen

Die Kollision, um derentwillen es gebaut wurde, ist real. Sie verschwindet
nicht — sie wird nur an den Ort verschoben, an den sie gehört: **wer die
Oberfläche und die API hinter einen Ursprung stellen will, trennt sie über ein
Präfix** (`/api/...`), nicht über einen Anfragekopf. Das ist eine Änderung an
den Adressen und damit sichtbar, statt an einer Zwischenschicht und damit
unsichtbar. Solange die Entwicklung zwei Ursprünge fährt, braucht es sie nicht.

## Der Preis, und er wird bezahlt

**`make k8s-up` liefert keine Oberfläche mehr.** Das Helm-Chart veröffentlicht
allein das Gateway (`publicUrl: http://localhost:8090`, `gateway.nodePort`); der
`web`-Pod ist `ClusterIP` und von außen nicht erreichbar. Bisher war die
`/__ui/`-Route der einzige Weg dorthin. Wer die Staging-Umgebung zurückwill,
gibt `web` einen eigenen Eingang und teilt `publicUrl` in einen Web- und einen
API-Ursprung — Chart, Werte, Kind-Portweiterleitung und die
Laufzeitkonfiguration.

Das ist bewusst offen gelassen worden und nicht übersehen: `make k8s-up` war auf
keiner Maschine je gefahren, und eine ungeprüfte Umgebung ist kein Grund, eine
Regel zu behalten, die die tägliche Arbeit kostet.

## Was dieses ADR NICHT entscheidet

- **Wie Produktion die Oberfläche ausliefert.** Dort gibt es keinen
  Entwicklungsserver, sondern gebaute Dateien; ob die hinter demselben Ursprung
  liegen (dann mit `/api`-Präfix) oder hinter einem eigenen, entscheidet, wer
  die erste echte Umgebung aufsetzt.
- **Die Route-Reihenfolge in `ocelot.json`.** Die Prioritäten und ihre Prüfung
  durch `ReihenfolgeTests` bleiben unverändert; es fällt genau eine Route weg.
