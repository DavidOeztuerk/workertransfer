# ADR-0057: Türen mit Aufruferliste — Dienst-Tokens statt eines geteilten Secrets

**Status:** vorgeschlagen (05.11.2026), Warden-Runde steht aus
**Betrifft:** identity-service, notification-service, profile-service, die elf aufrufenden Dienste, `src/shared/WorkerTransfer.ServiceDefaults`, Compose, Helm, `make env`, CI
**Verwandt:** ADR-0045 (Noelia, Plattform nicht live), ADR-0046 (nur identity hält den privaten Schlüssel), ADR-0056 (Ports nur auf Loopback), Noelia ADR-0002 (Dienst-zu-Dienst-Rechte), Gate G-0001, Quest Q-0043, Design-Notiz `.monarch/knowledge/Q-0001-design.md` §13

## Warum es dieses ADR gibt

Gemessen am 05.11.2026 in `docker-compose.yml`: `WORKERTRANSFER_NOTIFY_SECRET` steht an zwanzig Stellen, unter vier Namen (`Identity__Secret` zehnmal, `Notifications__Secret` siebenmal, `Notify__Secret` zweimal, `Profile__Secret` einmal). Derselbe Wert öffnet alle zehn `/internal`-Routen der Plattform. Zwei davon geben entschlüsselte KI-Schlüssel heraus, eine den Klarnamen und die E-Mail-Adresse eines beliebigen Kontos. Ein Dienst, der nur Mails auslösen soll, kann also alles lesen, was diese Türen hergeben. Das ist Befund WT-1.

Die Design-Notiz wollte das am 28.10. mit einem eigenen Secret je Aufrufer lösen und hat die Umsetzung dann zurückgestellt, weil Noelia dieselbe Frage als geteilten Baustein beantworten sollte. Noelia 7.1.0 liegt seit dem 29.10. auf nuget.org. Dieses ADR beschreibt die Übernahme; Inventar, Rechtetabelle und E1 der Notiz gelten weiter.

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

**Jede Ablehnung ist dasselbe 404, und zwar ohne Rumpf.** Das weicht bewusst von der Regel ab, dass jede Antwort ein Problemdokument trägt. Die Türen waren bisher Problemdokumente, künftig schreibt Noelias Dekorierer nur den Status. Ein Fallback je Gruppe sorgt dafür, dass eine falsche Methode oder ein unbekannter Pfad genauso antwortet wie eine verschlossene Tür. Die Autorisierung läuft vor der Bindung, deshalb entfällt der Umbau von D1 aus §11 der Notiz: Ein kaputter Rumpf eines Unberechtigten erreicht den Binder nicht mehr. Bleibt nach innen die Korrelation im Header, nach außen verrät die Antwort nichts.

**Ist kein Token zu bekommen, schweigt der Dienst, er stürzt nicht ab.** `ServiceTokenUnavailableException` erbt von `Exception`, die Aufrufer fangen aber `HttpRequestException` und übersetzen das in ihre Schweige-Fehler, an denen die Zusagen hängen: Eine schweigende Rollenabfrage antwortet 503, nie 403. Ungefangen würde daraus ein 500. Statt zwanzig `catch`-Blöcke anzufassen, kommt in `ServiceDefaults` genau eine Erweiterung `AddServiceCall(audience)`, die vor `AddServiceToken(audience)` einen Handler setzt und die Ausnahme in eine `HttpRequestException` übersetzt. Jeder Aufrufer nutzt nur diese Erweiterung.

**Das Token hängt an einem eigenen benannten Client, nie am Standard-Client.** Die meisten Dienste rufen heute `CreateClient()` mit frei gewählten Namen auf `AddHttpClient()` auf. Künftig registriert jeder Aufruf seinen Namen ausdrücklich. Der Client `company-site` von jobs ruft fremde Hosts und darf nie ein Token tragen; das Token von jobs hängt an `identity-internal`. Ein Token an einem Client, der auch fremde Adressen ruft, wäre der schlimmste Fehler, den dieser Umbau machen kann.

**Der Token-Endpoint bekommt eine eigene Bremse.** `.Without(RateLimiting)` begründet sich damit, dass ein Dienst hinter dem Gateway nur das Gateway als Herkunft sieht. Für `/m2m/token` gilt das nicht, denn die Dienste rufen ihn direkt und jeder hat eine eigene Adresse. identity ruft deshalb selbst `AddRateLimiter` und `UseRateLimiter` auf, nur für diesen Endpoint, je Herkunft 600 Anfragen in der Minute. Das ist eine Flutbremse, keine Ratebremse, denn 256 Bit errät niemand. Normalbetrieb sind elf Dienste mal höchstens drei Audiences alle fünf Minuten. Ohne Bremse meldete der Check `noelia.m2m.caller-policy` dauerhaft eine Warnung.

**Der Umstieg ist ein Hartschnitt in einem Pull Request.** Noelias Weg „erst alle auf 7.1, dann der Aussteller“ schützt eine gemischte Flotte. Hier gibt es keine, denn alle Dienste kommen aus einem Image, und die Plattform ist nicht live (ADR-0045).

**Secret und Hash entstehen immer gemeinsam.** Zwei unabhängig gewürfelte Hälften passen nicht zusammen, wie beim Schlüsselpaar. `make env` erzeugt je Client ein Secret und seinen Hash nach dem Rezept aus Noelia ADR-0002 §4. In Helm geht das nicht über `keepOrMake`, weil sprig `sha256sum` nur als Hex liefert und Noelia Base64url verlangt. Die Werte sind daher `required` und kommen aus `scripts/k8s-up.sh`, genau wie das Schlüsselpaar. Identity hält nur die Hashes, jeder Dienst nur sein eigenes Secret. `NOELIA_M2M_TOKEN_ENDPOINT` steht je Dienst und nicht im Anker `x-environment`, sonst dürften auch consent, github und portfolio über die Egress-Grenze zu identity.

Eine vorhandene `.env` hat die neuen Werte nicht, und `make env` lässt sie unberührt, weil sie Drittanbieter-Secrets trägt. Dafür kommt `make env-m2m`, das nur fehlende Einträge ergänzt und nichts überschreibt.

## Was verworfen wurde

Das eigene Secret je Aufrufer aus §5 der Notiz wäre mit weniger Umbau ausgekommen. Es bliebe aber ein unbefristetes Bearer-Geheimnis, das bei jeder Anfrage über die Leitung geht, und WorkerTransfer pflegte eine eigene Prüfklasse neben Noelia. Ein abgefangenes Token dagegen gilt fünf Minuten und nur bei einem Empfänger.

Das eigene eingehende Secret für notification (E4 vom 28.10.) hätte sieben Sender weiter ein Geheimnis teilen lassen. Wer einen von ihnen übernimmt, könnte sich als jeder andere ausgeben.

Ein `IServiceTokenSource`-Stub im Produktionscode für Testwirte ist verworfen: Die Pflichtwerte werden in den Tests gesetzt, damit der Start in Tests derselbe ist wie im Betrieb.

## Was dieses ADR NICHT entscheidet

Es entscheidet nicht über `X-Erasure-Secret`: Die Löschkaskade und der Rückzug von Inseraten liegen nicht unter `/internal` und teilen weiter ein Secret über fünfzehn Dienste. Das ist dieselbe Krankheit und wird ein eigenes Issue. `Evidence__Secret` ist Q-0004, die Datenbankrollen und der Master-Key sind Q-0002.

Es bindet Rechte an die Tür, nicht an den Datensatz. Ein berechtigter Aufrufer kann weiter jede ID abfragen; das schließt erst Q-0044. Dass fünf Dienste KI-Schlüssel sehen, schließt erst Q-0045. Wer einen Dienst vollständig übernimmt, hat dessen Türen. Die Liste begrenzt gestohlene Credentials, nicht die Übernahme.

F1 bleibt offen, wird aber kleiner: Ein falsches Secret fällt jetzt beim Token-Abruf auf und nicht mehr als stilles 404. Ein Aufrufer, der auf der Liste fehlt, bekommt weiter ein 404, das advisor und die KI-Pfade als „nichts da“ lesen. Deshalb prüft ein Test die Liste gegen den Quelltext der Aufrufer.

## Die Zusagen als Tests

`InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey` ist der Boss: Mit dem alten Header und mit einem Token von notification antworten D5, D6 und D7 mit 404, und im Rumpf stehen weder Name noch Adresse noch Schlüssel. Eine Matrix prüft alle zehn Türen gegen elf Clients und einen unbekannten Aufrufer. Ein Wächter hält Route und Tabelle in beiden Richtungen zusammen. Ein Test in CrossCutting leitet aus dem Quelltext ab, welcher Dienst welche Tür ruft, und vergleicht das mit der Tabelle; so fällt ein vergessener Aufrufer im Build auf und nicht als stilles 404 im Betrieb. Ein Test belegt, dass `company-site` kein Token trägt. Ein Test belegt, dass eine verweigerte Token-Ausgabe an D4 zu 503 führt und nicht zu 500. Der Konsistenztest gegen compose prüft, dass jeder Dienst nur sein eigenes Secret trägt und nur identity die Hashes. `make k8s-lint` prüft dasselbe am gerenderten Chart.
