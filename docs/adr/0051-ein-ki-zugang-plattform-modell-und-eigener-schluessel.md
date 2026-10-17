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
