# ADR-0057: Türen mit Aufruferliste — Dienst-Tokens statt eines geteilten Secrets

**Status:** angenommen für `develop` (05.11.2026), Fassung 3. Warden-Runde 1 reject, Delta-Runde approve-with-conditions, Auflagen eingearbeitet. Die Delta-Runde prüfte am Quelltext; zur Laufzeit belegen es erst die Tests dieses ADR.
**Betrifft:** identity-service, notification-service, profile-service, die elf aufrufenden Dienste, `src/shared/WorkerTransfer.ServiceDefaults`, Compose, Helm, `make env`, CI
**Verwandt:** ADR-0045 (Noelia, Plattform nicht live), ADR-0046 (nur identity hält den privaten Schlüssel), ADR-0056 (Ports nur auf Loopback), Noelia ADR-0002 (Dienst-zu-Dienst-Rechte), Noelia ADR-0003 (Dienst-Tokens hinter der Tür), Gate G-0001, Quest Q-0043, Design-Notiz `.monarch/knowledge/Q-0001-design.md` §13

## In einfacher Sprache

Heute öffnet ein einziges Passwort alle internen Türen der Plattform. Wer das Passwort eines Dienstes hat, der nur Mails auslösen soll, kann damit auch Klarnamen und KI-Schlüssel lesen.

Künftig holt sich jeder Dienst bei identity ein kurzlebiges Ausweis-Token, und jede Tür nennt im Code, welche Dienste hinein dürfen. Für alle anderen sieht die Tür aus, als gäbe es sie nicht. Für Nutzer ändert sich nichts, solange alles läuft. Fällt identity aus, antworten die Dienste, wie sie es bei einem schweigenden Dienst schon heute tun.

## Warum es dieses ADR gibt

Gemessen am 05.11.2026 in `docker-compose.yml`: `WORKERTRANSFER_NOTIFY_SECRET` steht an zwanzig Stellen, unter vier Namen (`Identity__Secret` zehnmal, `Notifications__Secret` siebenmal, `Notify__Secret` zweimal, `Profile__Secret` einmal). Derselbe Wert öffnet alle zehn `/internal`-Routen der Plattform. Zwei davon geben entschlüsselte KI-Schlüssel heraus, eine den Klarnamen und die E-Mail-Adresse eines beliebigen Kontos. Ein Dienst, der nur Mails auslösen soll, kann also alles lesen, was diese Türen hergeben. Das ist Befund WT-1.

Die Design-Notiz wollte das am 28.10. mit einem eigenen Secret je Aufrufer lösen und hat die Umsetzung dann zurückgestellt, weil Noelia dieselbe Frage als geteilten Baustein beantworten sollte. Das Ergebnis liegt als Noelia 7.1.1 auf nuget.org. Dieses ADR beschreibt die Übernahme; Inventar, Rechtetabelle und E1 der Notiz gelten weiter.

**Es muss 7.1.1 sein, nicht 7.1.0.** In 7.1.0 endete jeder berechtigte Türaufruf in 401, sobald `UseTokenRevocation()` und `UseNoeliaPrincipal()` hinter der Autorisierung liefen, und genau so steht es in `ServiceFoundation.cs`. Der Fix liegt in Noelia (dort ADR-0003). `UseTokenRevocation()` bleibt, und WorkerTransfer baut keinen Pfad-Bypass. An einer Tür ist `ICurrentPrincipal.Current` jetzt `null`; ein Türhandler darf also keine Person lesen.

## Die Entscheidung

**Ein Dienst weist sich mit einem kurzlebigen Token aus, und die Tür nennt im Code, wer hinein darf.** Das Token sagt nur, wer ruft. Was der Aufrufer darf, steht an der Tür und wird mit ihr reviewt. Ein Konfigurationsfehler kann deshalb kein Recht verbreitern.

identity-service stellt die Tokens aus, weil nur er den privaten Schlüssel hält (ADR-0046). Der Endpoint `/m2m/token` hat bewusst keine Gateway-Route. Es gibt elf Clients, und zwar die zehn Aufrufer der Notiz plus assessment, das nur an notification sendet. Es gibt drei Audiences: `identity`, `notification`, `profile`. Keine davon gleicht `JwtSettings__Audience` (`workertransfer`), sonst verweigert Noelia den Start.

Die Türen tragen diese Listen:

| Tür | Pfad | Aufrufer |
|---|---|---|
| D1 | `POST /internal/notify` | notification |
| D2 | `GET /internal/companies/{t}` | advisor, transfer, jobs |
| D3 | `GET /internal/companies/{t}/members` | applications, messages, transfer |
| D4 | `GET /internal/companies/{t}/members/{s}/role` | companies, advisor, resume, jobs, messages, transfer |
| D5 | `GET /internal/account/{s}/identity` | advisor, transfer |
| D6 | `GET /internal/account/{s}/ai` | profile, applications, messages |
| D7 | `GET /internal/companies/{t}/ai` | jobs, scout, messages |
| D8 | `POST /internal/notifications` | advisor, applications, assessment, messages, resume, scout, transfer |
| D9 | `GET /internal/profiles/search` | scout |
| D10 | `GET /internal/profiles/{s}` | scout |

D8 bis D10 sind gegenüber der Notiz neu. Das ist die Entscheidung des Users zu E4 vom 05.11.2026: Auch notification und profile bekommen Aufruferlisten, nicht ein zweites geteiltes Secret. Damit löst sich die Doppelrolle von notification, die E4 am 28.10. erzwungen hatte, von selbst auf. notification ist an D1 Aufrufer und an D8 Empfänger, und beide Rollen hängen an derselben Dienstidentität. `WORKERTRANSFER_NOTIFY_SECRET`, der Header `X-Notify-Secret` und die Einstellungen `Notify__Secret`, `Identity__Secret`, `Notifications__Secret` und `Profile__Secret` verschwinden vollständig.

**Jede Ablehnung ist dasselbe 404, und zwar ohne Rumpf.** Das weicht bewusst von der Regel ab, dass jede Antwort ein Problemdokument trägt. Die Türen waren bisher Problemdokumente, künftig schreibt Noelias Dekorierer nur den Status. Je Dienst gibt es genau eine `/internal`-Gruppe mit genau einem Fallback; er sorgt dafür, dass eine falsche Methode oder ein unbekannter Pfad genauso antwortet wie eine verschlossene Tür. identity mappt seine Türen heute in zwei Klassen, und zwei Gruppen mit je einem Fallback wären zwei gleiche Routen: Ein unbekannter Pfad endete in `AmbiguousMatchException`, also 500. Beide Klassen hängen deshalb an derselben Gruppe. Die Autorisierung läuft vor der Bindung, deshalb entfällt der Umbau von D1 aus §11 der Notiz: Ein kaputter Rumpf eines Unberechtigten erreicht den Binder nicht mehr. Bleibt nach innen die Korrelation im Header, nach außen verrät die Antwort nichts.

**Ist kein Token zu bekommen, schweigt der Dienst, er stürzt nicht ab.** `ServiceTokenUnavailableException` erbt von `Exception`, die Aufrufer fangen aber `HttpRequestException` und übersetzen das in ihre Schweige-Fehler, an denen die Zusagen hängen: Eine schweigende Rollenabfrage antwortet 503, nie 403. Ungefangen würde daraus ein 500. Statt zwanzig `catch`-Blöcke anzufassen, kommt in `ServiceDefaults` genau eine Erweiterung `AddServiceCall(audience)`, die vor `AddServiceToken(audience)` einen Handler setzt und die Ausnahme in eine `HttpRequestException` übersetzt. Jeder Aufrufer nutzt nur diese Erweiterung. Der Handler fängt nur `ServiceTokenUnavailableException`, nie `OperationCanceledException`: Eine abgelaufene Zeit bleibt eine abgelaufene Zeit, und die Aufrufer behandeln sie schon heute als Schweigen.

**Vergisst ein Dienst das Modul, startet er nicht.** Ohne `UseServiceTokenClient()` fehlt `IServiceTokenSource`, und `CreateClient` würfe beim ersten Aufruf, in `HttpCompanyRoles.cs:81` außerhalb des `try`, also 500 statt 503. Deshalb trägt `AddServiceCall` einen Startwächter ein: Fehlt die Quelle, bricht der Start ab und nennt das Modul.

**Auch die Zeit bleibt eine Schweige-Antwort.** `HttpMailer.cs:94` lässt heute jede `OperationCanceledException` durch, und ein Timeout ist eine. D8 antwortet dann 500, und der Token-Abruf verlängert genau diese Strecke. Der Filter unterscheidet künftig wie alle anderen Aufrufer: Abgebrochen hat nur, wer selbst abgebrochen hat.

**Das Token hängt an einem eigenen benannten Client, nie am Standard-Client.** Die meisten Dienste rufen heute `CreateClient()` mit frei gewählten Namen auf `AddHttpClient()` auf. Künftig registriert jeder Aufruf seinen Namen ausdrücklich. Der Client `company-site` von jobs ruft fremde Hosts und darf nie ein Token tragen; das Token von jobs hängt an `identity-internal`, `identity-roles` und `identity-company-ai`. Ein Token an einem Client, der auch fremde Adressen ruft, wäre der schlimmste Fehler, den dieser Umbau machen kann.

Das sind alle Clients mit Token:

| Dienst | Client | Audience | Türen |
|---|---|---|---|
| advisor | `identity` | identity | D2, D5 |
| advisor | `identity-roles` | identity | D4 |
| advisor | `HttpNotifier` | notification | D8 |
| applications | `identity-internal` | identity | D3, D6 |
| applications | `HttpNotifier` | notification | D8 |
| assessment | `HttpNotifier` | notification | D8 |
| companies | `identity-roles` | identity | D4 |
| jobs | `identity-internal` | identity | D2 |
| jobs | `identity-roles` | identity | D4 |
| jobs | `identity-company-ai` | identity | D7 |
| messages | `identity-internal` | identity | D3, D6 |
| messages | `identity-roles` | identity | D4 |
| messages | `identity-company-ai` | identity | D7 |
| messages | `HttpNotifier` | notification | D8 |
| notification | `identity` | identity | D1 |
| profile | `identity-internal` | identity | D6 |
| resume | `identity-roles` | identity | D4 |
| resume | `HttpNotifier` | notification | D8 |
| scout | `identity-company-ai` | identity | D7 |
| scout | `profile` | profile | D9, D10 |
| scout | `HttpNotifier` | notification | D8 |
| transfer | `identity-internal` | identity | D2, D3, D5 |
| transfer | `identity-roles` | identity | D4 |
| transfer | `HttpNotifier` | notification | D8 |

Jeder andere Client trägt keines, namentlich `company-site`, `company-model`, `ats`, `cover-letter`, `reply-draft`, der Client `HttpDrafter` von profile, jobs und scout, `github`, `github-oauth`, `consent`, `lookup`, `jobs`, `transfer`, `applications`, `own-facts`, `external-login`, `HttpErasureDelivery` und der Client `HttpConsentGate` von portfolio.

**Der Token-Endpoint bekommt eine eigene Bremse.** `.Without(RateLimiting)` begründet sich damit, dass ein Dienst hinter dem Gateway nur das Gateway als Herkunft sieht. Für `/m2m/token` gilt das nicht, denn die Dienste rufen ihn direkt und jeder hat eine eigene Adresse. identity ruft deshalb selbst `AddRateLimiter` und `UseRateLimiter` auf, nur für diesen Endpoint, je Herkunft 600 Anfragen in der Minute. `UseRateLimiter()` steht direkt hinter `UseWorkerTransferDefaults`, damit auch eine Ablehnung ihre Korrelation trägt, und `RejectionStatusCode` ist 429, denn die Vorgabe des Frameworks wäre 503. Das ist eine Flutbremse, keine Ratebremse, denn 256 Bit errät niemand. Vor ihr läuft eine Flut trotzdem durch `InputSanitization`, die den Rumpf bis 10 MB liest; das ist derselbe Weg wie an jedem anderen Endpoint und bleibt so. Normalbetrieb sind elf Dienste mal höchstens drei Audiences alle fünf Minuten. Ohne Bremse meldete der Check `noelia.m2m.caller-policy` dauerhaft eine Warnung.

**Der Umstieg ist ein Hartschnitt in einem Pull Request.** Noelias Weg „erst alle auf 7.1, dann der Aussteller“ schützt eine gemischte Flotte. Hier gibt es keine, denn alle Dienste kommen aus einem Image, und die Plattform ist nicht live (ADR-0045).

**Secret und Hash entstehen immer gemeinsam.** Zwei unabhängig gewürfelte Hälften passen nicht zusammen, wie beim Schlüsselpaar. `make env` erzeugt je Client ein Secret und seinen Hash nach dem Rezept aus Noelia ADR-0002 §4. In Helm geht das nicht über `keepOrMake`, weil sprig `sha256sum` nur als Hex liefert und Noelia Base64url verlangt. Die Werte sind daher `required` und kommen aus `scripts/k8s-up.sh`, genau wie das Schlüsselpaar. Identity hält nur die Hashes, jeder Dienst nur sein eigenes Secret. `NOELIA_M2M_TOKEN_ENDPOINT` steht je Dienst und nicht im Anker `x-environment`, sonst dürften auch consent, github und portfolio über die Egress-Grenze zu identity. Dasselbe gilt in Helm für die gemeinsame `-env`-ConfigMap, die jeder Pod per `envFrom` lädt: Der Wert steht auch dort nicht.

Eine vorhandene `.env` hat die neuen Werte nicht, und `make env` lässt sie unberührt, weil sie Drittanbieter-Secrets trägt. Dafür kommt `make env-m2m`, das nur fehlende Einträge ergänzt und nichts überschreibt. Es ergänzt immer ganze Paare. Findet es eine einzelne Hälfte, meldet es den Namen laut und ändert an diesem Paar nichts, denn eine neu gewürfelte zweite Hälfte passte nicht zur ersten.

`scripts/k8s-up.sh` entscheidet je Schlüssel, nicht nach dem Dasein des Secret-Objekts. Eine Release, die vor diesem Umbau entstand, hat das Objekt, aber keine M2M-Werte; übernommen ergäbe das leere Werte, und `required` bräche ab. Vorhandene Paare werden übernommen, fehlende neu gewürfelt. `HELM_PROBE` im Makefile braucht alle 22 Wegwerfwerte, sonst prüft `make k8s-lint` den Chart nicht zu Ende.

Der Wächter in `EnvironmentFileTests` erkennt ein Secret am Namen, und sein Muster endet auf `SECRET`. Die Hashes enden auf `_SECRET_HASH` und fielen durch die Prüfung „in der Vorlage leer“. Das Muster wird erweitert.

## Was das kostet

**D8 hängt jetzt an identity.** Heute schreibt notification bei schweigendem identity den Eintrag ins Postfach, und nur die Mail geht sofort verloren (`HttpMailer.cs:94`). Künftig braucht schon der Sender ein Token. Ein Ausfall kürzer als die Restlaufzeit des Tokens fällt nicht auf. Dauert er länger, zählt die Outbox Fehlversuche, und zwar frühestens nach 25 Minuten alle zehn: Die Abstände verdoppeln sich von 5 Sekunden bis 5 Minuten (`DeliveryLimits.Standard`, `OutboxLoop`). Läuft die Schleife schon im großen Abstand, sind es rund 45 Minuten.

Das hat zwei Seiten. **Neu verloren geht der Eintrag im Postfach:** Nach dem zehnten Versuch bleibt die Zeile liegen und abfragbar, aber nichts kommt mehr an. **Neu gewonnen ist die Mail:** Bei einem Ausfall unter dieser Grenze wird sie verzögert zugestellt und geht nicht mehr verloren. Tokenfehler nicht zu zählen hieße, sie an sieben Sendern von anderen Fehlern zu unterscheiden. Das entscheidet dieses ADR dagegen.

## Was verworfen wurde

Das eigene Secret je Aufrufer aus §5 der Notiz wäre mit weniger Umbau ausgekommen. Es bliebe aber ein unbefristetes Bearer-Geheimnis, das bei jeder Anfrage über die Leitung geht, und WorkerTransfer pflegte eine eigene Prüfklasse neben Noelia. Ein abgefangenes Token dagegen gilt fünf Minuten und nur bei einem Empfänger.

Das eigene eingehende Secret für notification (E4 vom 28.10.) hätte sieben Sender weiter ein Geheimnis teilen lassen. Wer einen von ihnen übernimmt, könnte sich als jeder andere ausgeben.

Ein `IServiceTokenSource`-Stub im Produktionscode für Testwirte ist verworfen: Die Pflichtwerte werden in den Tests gesetzt, damit der Start in Tests derselbe ist wie im Betrieb. Gesetzt werden sie je Testprojekt an einer Stelle, nicht in jeder der 48 Testdateien.

## Was dieses ADR NICHT entscheidet

Es entscheidet nicht über `X-Erasure-Secret`: Die Löschkaskade und der Rückzug von Inseraten liegen nicht unter `/internal` und teilen weiter ein Secret über fünfzehn Dienste. Das ist dieselbe Krankheit und wird ein eigenes Issue. `Evidence__Secret` ist Q-0004, die Datenbankrollen und der Master-Key sind Q-0002.

Es bindet Rechte an die Tür, nicht an den Datensatz. Ein berechtigter Aufrufer kann weiter jede ID abfragen; das schließt erst Q-0044. Dass fünf Dienste KI-Schlüssel sehen, schließt erst Q-0045. Wer einen Dienst vollständig übernimmt, hat dessen Türen. Die Liste begrenzt gestohlene Credentials, nicht die Übernahme.

F1 bleibt offen, wird aber kleiner: Ein falsches Secret fällt jetzt beim Token-Abruf auf und nicht mehr als stilles 404. Ein Aufrufer, der auf der Liste fehlt, bekommt weiter ein 404, das advisor und die KI-Pfade als „nichts da“ lesen. Deshalb prüft ein Test die Liste gegen den Quelltext der Aufrufer.

## Die Zusagen als Tests

`InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey` ist der Boss: Mit dem alten Header und mit einem Token von notification antworten D5, D6 und D7 mit 404, und im Rumpf stehen weder Name noch Adresse noch Schlüssel. Eine Matrix prüft alle zehn Türen gegen elf Clients und einen unbekannten Aufrufer. Ein Wächter hält Route und Tabelle in beiden Richtungen zusammen. Ein Test in CrossCutting leitet aus dem Quelltext ab, welcher Dienst welche Tür ruft, und vergleicht das mit der Tabelle; so fällt ein vergessener Aufrufer im Build auf und nicht als stilles 404 im Betrieb. **Je Dienst läuft ein Test über die echte Client-Fabrik des echten `Program`.** Die Stub-Fabriken der vorhandenen Tests umgehen die Handlerkette und könnten ein vergessenes `AddServiceCall` nie sehen; ein solcher Client bekäme von der Tür ein 404, das advisor als „nichts da“ liest (`HttpLookups.cs:141`). Der Test sendet über jeden Client der Tabelle oben und prüft am primären Handler das Token und seine Audience, und über jeden anderen Client des Dienstes, dass kein Token dabei ist. Ein Test belegt, dass eine verweigerte Token-Ausgabe an D4 zu 503 führt und nicht zu 500. Ein Test belegt, dass ein Dienst mit `AddServiceCall`, aber ohne `UseServiceTokenClient()`, nicht startet. Ein Test belegt, dass ein Timeout an D8 nicht als 500 endet. Der Konsistenztest gegen compose prüft, dass jeder Dienst nur sein eigenes Secret trägt und nur identity die Hashes. `make k8s-lint` prüft dasselbe am gerenderten Chart, und dafür bekommt es einen neuen Schritt: Heute schreibt es das Rendering nach `/dev/null` und prüft nur, dass es gelingt. Ein Test belegt, dass die Bremse wirkt: Über dem Limit antwortet `/m2m/token` mit 429, andere Routen bleiben unberührt; die Selbstprüfung liest nur Metadaten und sähe ein vergessenes `UseRateLimiter()` nicht. Ein Test vergleicht verschlossene Tür, falsche Methode und unbekannten Pfad byte-gleich, je Dienst.
