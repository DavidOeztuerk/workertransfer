# ADR-0046 — Wer prägen darf, steht im Schlüssel

**Datum:** 21.09.2026 · **Status:** angenommen
**Ersetzt:** nichts · **Berührt:** ADR-0007, ADR-0015 (das geteilte Geheimnis)

## Zusammenhang

Bis heute teilten sich **alle fünfzehn Prozesse ein HS256-Geheimnis**.
identity-service stellte Token aus, die anderen vierzehn prüften sie — aber das
war eine *Verabredung*, keine Eigenschaft der Schlüssel. Ein symmetrisches
Geheimnis kennt den Unterschied zwischen Prüfen und Prägen nicht: wer damit
eine Signatur nachrechnen kann, kann auch eine erzeugen.

Was das praktisch heißt: ein kopiertes Konfigurationsblatt von
`portfolio-service` — dem Dienst mit der kleinsten Angriffsfläche im Baum —
reicht, um ein gültiges Token für **jeden Menschen auf jedem Dienst** zu prägen,
einschließlich eines mit `tenant`-Anspruch für ein fremdes Unternehmen. Die
Firmenrechte hängen zwar an der Mitgliedschaftstabelle und nicht am Token
(ADR-0018), aber `sub` genügt: damit ist man die Person.

Noelias `noelia.jwt.key-separation` meldet das als **Warning in Development und
Fail in Produktion**. Der Befund stand offen, und der Kommentar in
`Dienstgrundlage.cs` nannte den Grund: *„Das ist H3 und braucht eine
Schlüsselverteilung, keine Codeänderung hier."* Das war die halbe Wahrheit — es
brauchte beides.

## Entscheidung

**ECDSA P-256. identity-service hält den privaten Schlüssel, alle fünfzehn den
öffentlichen.**

```
Jwt__PrivateKey   nur identity-service      prägt und prüft
Jwt__PublicKey    alle fünfzehn             prüft, und kann nichts sonst
Jwt__KeyId        alle fünfzehn             benennt den Schlüssel
```

`Schluesselherkunft` in `Dienstgrundlage.cs` entscheidet danach: liegt ein
privater Schlüssel in der Konfiguration, ruft sie `Issue(...)`, sonst
`VerifyOnly(...)`.

### 1. Die Konfiguration entscheidet, nicht eine Codezeile

Naheliegender wäre ein Schalter im Code gewesen — `AlsAussteller()` gibt es
schon, und ADR-0003 legt Verdrahtung in den Verbundpunkt. Dagegen spricht, was
der Betreiber sieht: wer prägen kann, ist eine Frage an den **Betrieb**, und die
Antwort muss dort stehen, wo er sie liest. In `docker-compose.yml` und im Chart
trägt genau ein Block `Jwt__PrivateKey`; ein zweiter fiele beim Lesen auf.

Ein Schalter im Code hätte dieselbe Aussage an eine Stelle gelegt, die beim
Ausrollen niemand aufschlägt — und die Konfiguration hätte trotzdem den
Schlüssel liefern müssen.

`SchluesseltrennungTests` liest `docker-compose.yml` und besteht auf genau einem
Träger. Gegenprobe gemessen: `Jwt__PrivateKey` zusätzlich an profile-service
lässt den Test fallen und **nennt den zweiten Träger beim Namen**.

### 2. Das Paar wird nicht gewürfelt, es wird verlangt

Das Chart hält seine Geheimnisse mit `keepOrMake` — es liest ein bestehendes
Secret zurück und würfelt nur, was fehlt. Für ein Schlüssel**paar** ist das
falsch: zwei unabhängig gewürfelte Hälften passen nicht zueinander, jedes Token
wäre ungültig, und der Fehler sähe aus wie eine kaputte Anmeldung.

`secret.yaml` verlangt beide Hälften deshalb mit `required` und nennt im
Fehlertext den `openssl`-Aufruf. `scripts/k8s-up.sh` erzeugt das Paar — und
übernimmt bei einem bestehenden Release das vorhandene, weil ein neues jede
offene Sitzung abmeldete. `make k8s-lint` übergibt ausdrücklich Wegwerfwerte:
dort wird gerendert, nie angewendet.

### 3. Der Schlüssel trägt einen Namen, damit ein Wechsel keiner ist

`Jwt__KeyId` ist kein Schmuck. Ohne Namen ist ein Schlüsselwechsel ein Schnitt,
bei dem jedes ausgestellte Token auf einen Schlag ungültig wird. Mit Namen
laufen zwei eine Weile nebeneinander (`AlsoVerify(...)`), und niemand wird
ausgeworfen.

Es ist der einzige der drei Werte mit einer Vorgabe (`k1`) — er ist kein
Geheimnis, und `UmgebungTests` führt ihn deshalb nicht in der Liste der Werte,
die in `.env.example` leer stehen müssen.

### 4. In den Tests dasselbe Verfahren, nicht ein zweites

Zwölf Testreihen prägten ihre Token selbst, mit einem festen HS256-Geheimnis.
Sie prägen sie jetzt mit einem festen **Testschlüsselpaar** — fest, weil ein
gewürfeltes die Reise von der Maschine abhängig machte, auf der sie läuft.

Die Trennung gilt dort genauso: identity-services Testwirt bekommt den privaten
Schlüssel, die dreizehn anderen nur den öffentlichen. Ein Test, der einen
symmetrisch signierten Token an einen prüfenden Dienst schickte, prüfte einen
Pfad, den es im Betrieb nicht mehr gibt.

## Der zweite Schlüssel: der Bund des Gerüsts

`noelia.dataprotection.key-ring` war der andere rote Befund, und er hat mit dem
Token nichts zu tun — es ist ASP.NETs eigener Schlüsselbund. Ohne Zutun schreibt
das Gerüst ihn in das Dateisystem des Behälters und **warnt zweimal bei jedem
Start**: zwei Warnungen, auf die niemand reagiert. Was damit geschützt ist — ein
Anmeldecookie, ein Fälschungsschutz-Token, ein Rücksetzlink — prüft nicht mehr,
sobald der Behälter ersetzt wird.

**In diesem Baum liest ihn heute niemand** (kein `IDataProtectionProvider`, kein
Antiforgery, kein `.Protect()`; das Zugriffs-Cookie trägt den JWT im Klartext und
ist `httpOnly`). Trotzdem wird er eingerichtet, und zwar aus zwei Gründen: das
Gerüst registriert ihn ohnehin — man kann ihn nicht abwählen, nur konfigurieren
— und ein Tor, das dauerhaft rot steht, wird nach der zweiten Woche ignoriert und
ist dann schlechter als keins.

**Postgres, kein Zwischenspeicher im Prozess.** Noelias `UseDataProtection` legt
den Bund in den registrierten Cache-Anbieter; ohne Redis wäre das
`Noelia.InMemory`, und der stirbt mit dem Prozess — die Prüfung würde grün, ohne
dass sich etwas ändert. Genau davor warnt Noelias eigener Kommentar an der
Stelle. Die Datenbank hat jeder Dienst ohnehin, sie überlebt den Behälter, und
zwei Instanzen lesen denselben Bund.

**Verschlüsselt mit dem vorhandenen Hauptschlüssel.** `Hauptschluesselhuelle`
benutzt `Geheimnisspeicher` — dieselbe AES-GCM-Mechanik wie für den KI-Zugang
einer Person, derselbe `WORKERTRANSFER_SECRETS_KEY`. Eine zweite Verschlüsselung
daneben wäre eine zweite Stelle, an der jemand den Hauptschlüssel suchen müsste.

Drei Dinge daran sind Entscheidungen und keine Nebensachen:

- **Eine Zeile je Schlüssel, kein Dokument.** Zwei Instanzen, die gleichzeitig
  einen anlegen, schrieben sonst dasselbe Dokument übereinander und eine verlöre
  ihren — was viel später als „Token prüft hier, aber nicht dort" auffällt.
- **Die Tabelle steht in keinem `DbSet`.** Sie gehört keinem Fachmodell, und im
  EF-Modell ließe sie `LoeschempfaengerTests` wie eine Personenzeile aussehen.
  Sie entsteht beim ersten Zugriff mit `CREATE TABLE IF NOT EXISTS`. Der Preis
  ist benannt: die Anweisungen sind PostgreSQL-Syntax in einem Paket, das
  bewusst keine Treiberabhängigkeit hält.
- **Ein unlesbarer Bund wirft.** Wer `WORKERTRANSFER_SECRETS_KEY` wechselt, soll
  das beim Start erfahren und nicht daran, dass plötzlich jedes geschützte Token
  ungültig ist.

`SchluesselbundreiseTests` prüft beides, was der Befund liest — Ablage und
Verschlüsselung —, und liest zusätzlich die Zeile aus der Datenbank: eine
gesetzte Verschlüsselung beweist nicht, dass sie etwas tut.

## Folgen

- `make env` würfelt vier Geheimnisse **und erzeugt ein Schlüsselpaar**.
- Wer eine bestehende `.env` hat, braucht `WORKERTRANSFER_JWT_PRIVATE_KEY`,
  `…_PUBLIC_KEY` und `…_KEY_ID`. `WORKERTRANSFER_JWT_SECRET` ist ersatzlos weg;
  compose bricht ab und **nennt** die fehlende Variable.
- Ein neuer Dienst bekommt den öffentlichen Schlüssel über den gemeinsamen
  Anker und muss nichts tun. Bekäme er den privaten, fiele
  `SchluesseltrennungTests`.
