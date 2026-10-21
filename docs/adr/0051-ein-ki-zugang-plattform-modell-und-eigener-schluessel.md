# ADR-0051 — Ein KI-Zugang: das Plattform-Modell und der eigene Schlüssel

**Datum:** 17.10.2026 · **Status:** angenommen
**Ersetzt:** „Standard-Anbieter: Mistral (EU)“ aus `docs/VISION-FRAGEN.md` E2 —
bis zum Start · **Berührt:** ADR-0024 (KI-Naht), ADR-0034 (Anschreiben),
ADR-0047, ADR-0049, `VISION-FRAGEN-2.md` Q1

## Zusammenhang

Das Anschreiben wurde lokal nicht geschrieben: „No writing-help provider is set
up.“ Mistral war seit dem 07.10.2026 der Plattform-Standard — ein Cloud-Dienst,
der einen Schlüssel braucht, und keiner stand in der `.env`. Das lokale Ollama
lief, aber nur ein Konto, das es in seinen Einstellungen selbst eingetragen
hatte, hätte es benutzt.

Dazu war uneinheitlich, wer welchen Zugang nimmt: das Anschreiben und die
Nachrichten-Entwürfe nahmen einen eigenen Zugang der Person, die Profil-Hilfe
nicht. Und als eigener Zugang ließ sich jede beliebige Adresse eintragen.

## Entscheidung

**Eine Regel für jede KI-Stelle der Person: ihr eigener Schlüssel, sonst das
Plattform-Modell.** Anschreiben, Profil-Hilfe, Antwort-Entwürfe und der
KI-Berater fragen identity-service, ob sie einen Schlüssel hinterlegt hat; wenn
ja, schreibt ihr Anbieter, wenn nein, das Plattform-Modell. Ein schweigender
identity-service wird abgelehnt, nicht still durch das Plattform-Modell ersetzt
— sie kann ihren Anbieter gerade gewählt haben, um ihren Text von unserem
fernzuhalten.

**Bis zum Start ist das Plattform-Modell das lokale Ollama des Betreibers.**
Nichts verlässt die Maschine. Compose und der Code tragen es als Vorgabe; der
Schalter bleibt der Schlüssel: leer heißt „die Plattform hat kein Modell“ (so
messen CI und die E2E-Reisen), ein beliebiger Wert schaltet Ollama ein — es
liest keinen.

**Eigene Schlüssel: Anthropic und OpenAI.** Später kommen weitere Anbieter
dazu (Gemini, Ollama Cloud, Hugging Face). **Die Adresse setzt die Plattform,
nie die Person:** eine eingetippte Adresse wäre ein Weg, ihre Texte an jeden
Server zu schicken, und die Egress-Grenze kennt nur, was der Betreiber erklärt
hat — hier genau `api.anthropic.com` und `api.openai.com`. Ohne Schlüssel gibt
identity-service „kein eigener Zugang“ heraus, welcher Anbieter auch gewählt
ist. Der eigene Server (Ollama über das Konto) entfällt als Wahl; eine alte
Zeile mit `openai_compatible` liest sich als „Plattform-Modell“.

**Anthropic denkt vor dem Schreiben.** Aktuelle Claude-Modelle schicken einen
Denkblock vor den Text, und das Denken zählt gegen `max_tokens`. Darum liest
`ModelWire.Text` den ersten **Text**block (nicht den ersten Block), eine
Anfrage an Anthropic trägt mindestens 16 000 `max_tokens` und Aufwand `low`,
und eine Ablehnung (`stop_reason: refusal`) liefert keinen Entwurf. Ohne das
käme mit einem eigenen Anthropic-Schlüssel nie ein Text an.

**Die Firmenseite bleibt beim Plattform-Modell**: Inserat-Hilfe, Ansprache des
Scouts, Antworten der Firma. Ein Schlüssel der Firma kommt nach dem MVP — der
private Schlüssel eines Mitglieds darf nicht für die Firma bezahlen.

## Was vor dem Start entschieden wird

Welches Modell die Plattform im Betrieb anbietet — und wie Menschen es
bekommen. Die Idee vom 17.10.2026: **ein Plattform-Anbieter**, den Person und
Firma per **Abo** oder über ein **gekauftes Kontingent** nutzen. Die harte
Bedingung dabei: **ein Anschreiben darf nie mitten im Schreiben am leeren
Kontingent scheitern** — also wird der Bedarf einer Aufgabe vor dem Start
reserviert und erst danach abgerechnet, und der Preis muss die Kosten tragen,
ohne dass jemand nicht mehr alles machen kann. Das gehört zum Geschäftsmodell
(G1, Q1) und wird dort entschieden.

## Was es kostet

- profile-service fragt identity-service (`Identity__BaseUrl`/`__Secret`) und
  darf zu zwei Cloud-Hosts; der Chart trägt beides nach.
- Lokal schalten die E2E-Reisen und die Routenkarte das Plattform-Modell für
  ihren Lauf ab (`make e2e`, `make route-map`, `scripts/without-model.sh`),
  sonst messen sie etwas anderes als CI: `POST /profiles/me/draft` antwortet
  dort 503 und mit Ollama 200. Ob das Modell wirklich
  antwortet, prüft `make ai-check` auf dem echten Weg (Profil-Hilfe und ein
  Anschreiben).
- Ein 7B-Modell auf dem Laptop braucht für ein Anschreiben rund anderthalb
  Minuten.

## Ergänzung 21.10.2026 — der Schlüssel der Firma (Bauliste 6)

Das Geschäftsmodell (`GESCHAEFTSMODELL-FRAGEN.md` B4) zieht den Schlüssel der
Firma vor den Start: **Anthropic oder OpenAI mit dem Schlüssel der Firma, oder
ihr eigener OpenAI-kompatibler Server** unter der bestätigten Domain.

- **Er gehört der Firma, nicht einem Mitglied.** Er liegt in identity-service
  (`company_ai_settings`, verschlüsselt wie der einer Person), und der Satz
  oben bleibt wahr: der private Schlüssel eines Mitglieds bezahlt nie für die
  Firma. Jede Seite fragt nur nach ihrem eigenen Zugang.
- **Einstellen ist `admin`, lesen auch.** Der Schlüssel entscheidet, wohin die
  Entwürfe des Teams gehen und wer sie bezahlt — das bindet die Firma.
  `GET`/`PUT /companies/{id}/ai` und `PUT /companies/{id}/ai/key`; der
  Schlüssel geht nur hin und kommt nie zurück, auch nicht an Admins.
- **Dieselbe Regel wie bei der Person:** eigener Schlüssel, sonst das
  Plattform-Modell; eine schweigende identity-service wird verweigert, nie
  ersetzt. Gefragt wird über `GET /internal/companies/{id}/ai` je Entwurf, ohne
  Zwischenspeicher — wer den Schlüssel entfernt, trifft schon den nächsten.
- **Ein 404 der Tür ist kein „kein Schlüssel".** Sie antwortet jeder Firma mit
  200 und verweigert nur ein falsches Geheimnis mit 404. Als „keiner" gelesen,
  schickte ein falsch verdrahtetes Geheimnis die Entwürfe jeder Firma still an
  den Anbieter der Plattform.
- **Die Firma wählt nur, welches Modell schreibt.** Sie reist neben dem Kontext
  und gelangt nie in den Prompt — `AdvertDraft` und `ApproachSituation`
  bleiben ohne Feld für sie.
- **Eine ruhende Firma behält keinen Schlüssel.** Löscht ihr letzter Admin sein
  Konto, fällt der Schlüssel mit; ein Mandant wird nie gelöscht, also nähme
  ihn keine Kaskade.
- Im Nachweis zählt das KI-Verzeichnis Firmen mit eigenem Schlüssel getrennt
  von Personen — gezählt, nie benannt.

**Der eigene Server der Firma** (`own`) ist der einzige Anbieter mit getippter
Adresse, und deshalb ist sie gebunden:

- **https, ein Name statt einer IP-Adresse, und der Host liegt auf der
  bestätigten Domain der Firma** (ADR-0019) oder darunter. Die Domain liest
  identity aus der Firma, nie aus der Anfrage. Ein Schlüssel ist freiwillig,
  ein Modell nicht.
- **Die Domain darf nicht nach innen zeigen.** Ein Zugang der Firma geht in
  jedem Dienst über den Client `company-model`, der beim Verbindungsaufbau nur
  öffentliche Adressen zulässt (`ServiceDefaults/PublicAddress`, dieselbe
  Prüfung wie bei den Feeds aus ADR-0052) und keiner Weiterleitung folgt.
  Das Plattform-Modell behält seinen Client, denn das lokale Ollama liegt mit
  Absicht innen.
- **Die Egress-Grenze lernt den Host aus der Antwort von identity**, kurz vor
  dem einen Aufruf, der ihn braucht (`CompanyAiEgress` um Noelias Richtlinie,
  in jobs um `CompanyDomainEgress` herum). Eine Liste in der Konfiguration
  wäre falsch: die Firma legt ihren Server an, nicht der Betreiber.
- Eine Person kann ihn nicht wählen — ihre Einstellungen kennen `own` nicht.

