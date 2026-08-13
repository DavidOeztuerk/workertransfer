# Erwartete Ansichten je Route

Stand 13.08.2026, nach E3a–E3c und ADR-0030. **27 Routen.** Gelesen aus
`apps/web/src/app.tsx` und den Routendateien — nicht aus dem Gedächtnis.

Wie zu lesen: je Route steht, **wer** sie sehen darf und **was** dabei erscheint,
getrennt nach Zustand. Ein Zustand, der hier steht, ist im Code vorhanden; wo
etwas fehlt, sagt es die Spalte „Lücke".

## Legende der Rollen

| Rolle | heißt hier |
|---|---|
| **anonym** | keine Sitzung |
| **Person** | angemeldet, `tenant_id = null` — „Ich selbst" im Kopfzeilen-Wähler |
| **Unternehmen** | angemeldet **und** oben auf ein Unternehmen gewechselt (`tenant_id` im Token) |

Der Wähler „Handeln als" ist der einzige Weg in die Unternehmensrolle. Ohne
Wechsel ist man Person — auch als Admin. **Die Kopfzeile versteckt nur; der Server
antwortet `403`** (Rollen sind noch nicht durchgesetzt, siehe „Bekannte Lücken").

---

## Öffentlich (kein Konto nötig)

### `/` — Startseite
| Zustand | Ansicht |
|---|---|
| anonym | Marketing-Seite: Hero mit zwei Knöpfen (beide auf `/register`), Abschnitte „Prinzipien" und „Produkt", Kopfzeile transparent über dem Hero |
| angemeldet | **Weiterleitung auf `/overview`** — vor E2.5 diente eine Adresse beidem, was die Übersicht unverlinkbar machte |

### `/login` — Anmelden
Formular (E-Mail, Passwort) in der `AuthLayout`-Hülle mit Claim rechts.
- Falsche Daten → **„Anmeldung fehlgeschlagen"**, kein Absturz, keine Weiterleitung.
- Nicht bestätigtes Konto → `403 email_not_confirmed`, nicht `401`.
- Mit **gemerkter Stelle** → nach Erfolg direkt auf `/jobs/<id>/apply` samt Hinweis „Danach geht es zurück zu: *Stelle*".
- Ohne gemerkte Stelle → `/`.

### `/register` — Konto erstellen
| Zustand | Ansicht |
|---|---|
| Formular | E-Mail, Passwort, Name, **optional „Name des Unternehmens"** |
| nach dem Absenden | „Fast geschafft" — Hinweis auf die Bestätigungsmail |
- **Freemail-Adresse + Unternehmensname → abgelehnt** (ADR-0019), und zwar *vor* der Existenzprüfung, damit die Antwort nichts über Mitgliedschaft verrät.
- Bekannte und unbekannte Adresse antworten **identisch**.

### `/verify` — E-Mail bestätigen
Drei Zustände: „Wird bestätigt…", **„E-Mail bestätigt"**, „Bestätigung fehlgeschlagen".
Vierter, leicht zu übersehender Fall: **Konto bestätigt, Unternehmen abgelehnt** (Domain schon vergeben) — die Seite sagt beides, und die Bestätigung selbst scheitert nie an einem vergebenen Namen.

### `/invitation` — Einladung annehmen
„Einladung wird geprüft…" → angenommen, oder **„Einladung nicht angenommen"** mit Grund.
Das ist auch der Weg für jemanden, dessen Firmendomain schon vergeben ist: er hat dort Kollegen.

### `/jobs` — Offene Stellen
| Zustand | Ansicht |
|---|---|
| lädt | „Wird gesucht…" |
| Fehler | Meldung — **niemals** eine leere Liste |
| leer | „Dazu wurde nichts gefunden." + „Andere Begriffe führen vielleicht weiter." |
| Treffer | Karten: Titel, **wer sucht** (Unternehmensname, sonst nichts), Ort · Arbeitsform · Beschäftigungsart, Beschreibung, **Passung**, Bewerben |

Filter: Suchbegriff, Ort, Arbeitsform, **Beschäftigungsart** (letzterer neu in E3a — der Filter existierte serverseitig längst, nur konnte ihn niemand setzen).

**Passung** (nur für Personen): Liste mit Haken — „Du hast 2 von 3 genannten Fähigkeiten: Python ✓ · Kubernetes ✓ · Go ✗". **Nie eine Zahl, nie ein Prozentwert** (ADR-0022). Wer keine Fähigkeiten eingetragen hat, bekommt **kein „0 von 3"**, sondern den Hinweis aufs Profil.

„Bewerben": anonym ein **Knopf** (merkt die Stelle, führt zur Anmeldung), als Person ein **Link** auf `/jobs/<id>/apply`.

### `/jobs/$id/apply` — Bewerben *(neu in E3a)*
| Zustand | Ansicht |
|---|---|
| lädt | „Stelle wird geladen…" |
| ungültige ID | „Diese Stelle gibt es nicht" — **ohne** Anfrage an den Server |
| Stelle weg | „Diese Stelle gibt es nicht. Sie wurde zurückgezogen, oder es gab sie nie." |
| anonym | Titel der Stelle + „Anmelden und bewerben" (merkt die Absicht) |
| Person | Passung, Anschreiben (optional), **Lebenslauf** ☑ und **Meine Arbeiten** ☐ |
| abgeschickt | „Bewerbung abgeschickt" + wo man sie zurückzieht |

Das **Profil geht immer mit** und steht ausdrücklich nicht zur Wahl. Rückweg „Zurück zu den offenen Stellen" ist Pflicht — ein Deep-Link ohne ihn ist eine Sackgasse.

### `/careers/$slug` — Karriereseite eines Unternehmens
Unternehmensname, Website, „Über uns", Standorte, Benefits, **offene Stellen** mit „Bewerben" je Stelle (direkt auf `/jobs/<id>/apply`).
- Unbekanntes Kürzel → „Diese Seite gibt es nicht" + Link auf `/jobs`.
- **Gescheiterter Abruf ≠ leer**: „Die offenen Stellen sind gerade nicht abrufbar. Das heißt nicht, dass es keine gibt." (vor E3a stand hier „Zurzeit ist nichts ausgeschrieben" — die beruhigendste falsche Antwort).

---

## Person (angemeldet)

### `/overview` — Was liegt an
**Nur Dinge, die auf eine Entscheidung warten.** Was von selbst läuft, steht nicht da.
| Zustand | Ansicht |
|---|---|
| nichts offen | „Gerade wartet nichts auf dich." + Verweis auf `/consents` |
| Teil-Fehler | „Ein Teil konnte nicht geladen werden…" |
| **Für dich** | „*n* Unternehmen möchten sehen, ob du ansprechbar bist" → `/market`; „*n* Anfragen nach deinem Lebenslauf" → `/resume`; „*n* Gespräche warten auf dich" → `/transfers` |
| **Für dein Unternehmen** | „*n* Transfers warten auf euch" → `/company/transfers` |

Gezählt werden **Vorgänge, nie Personen** (ADR-0022/0026).
*Noch nicht gebaut:* der „Stand"-Bereich (wie viele Unternehmen dich sehen) — E3e.

### `/profile` — Mein Profil
| Zustand | Ansicht |
|---|---|
| anonym | „Bitte anmelden, um dein Profil zu bearbeiten." |
| lädt | „Profil wird geladen…" — **kein** leeres Formular, das sich nachträglich füllt |
| Person | Freigabeschalter + Formular (Überschrift, Über mich, Ort, Fähigkeiten, Remote) |
| gespeichert | „Profil gespeichert." |

**Der Schalter „Profil für Unternehmen freigeben" hat vier Lagen** (E3b):
1. kein Profil → gesperrt, „Erst ein Profil speichern"
2. Ledger antwortet noch → gesperrt, „Freigabe wird geprüft…"
3. **Ledger stumm → gesperrt**, „Ob eine Freigabe gilt, ist gerade nicht abrufbar" *(vorher sah das aus wie „nicht freigegeben" und war bedienbar)*
4. bekannt → bedienbar, wirkt **sofort**

**Formulierungshilfe** (ADR-0024): nur auf Knopfdruck, der Hinweis steht am Knopf, der Entwurf landet **nur im Feld**. Über vorhandenem Text heißt der Knopf „Vorschlag holen (ersetzt den Text oben)".
Fähigkeiten werden sichtbar **umbenannt** („postgres" → „PostgreSQL"), nie erfunden, nie abgelehnt.

### `/resume` — Mein Lebenslauf
**Kein öffentlicher Schalter** — und das ist der Kern: ein Profil ist ein Anschlag am Brett, ein Lebenslauf nennt echte Arbeitgeber mit Daten.
| Abschnitt | Ansicht |
|---|---|
| **Anfragen** | lädt: „Anfragen werden geladen…" *(fehlte vor E3c und sah aus wie „niemand hat gefragt")* · leer: „Bislang hat niemand nach deinem Lebenslauf gefragt." · offen: Freigeben / Ablehnen · freigegeben **und aktiv**: Zurückziehen · abgelehnt: „kann nicht erneut fragen" · widerrufen: „Freigabe zurückgezogen" **ohne** Knopf |
| **Stationen** | ein Formular für alle (Arbeitgeber, Position, Von, Bis), Hinzufügen/Entfernen, ein Speichern |

Leeres „Bis" heißt **„bin noch dort"**, nicht Lücke. Die Stationen bleiben absichtlich **ein** Formular (E3b): man bearbeitet sie im Vergleich, und eine Route je Station machte die Lücke zwischen zwei Stationen unsichtbar.

### `/portfolio` — Meine Arbeiten *(Liste seit E3b)*
| Zustand | Ansicht |
|---|---|
| lädt | „Portfolio wird geladen…" |
| leer | „Noch keine Arbeit eingetragen." + „Arbeit hinzufügen" |
| gefüllt | Zeilen: Titel, „Rolle · Jahr · mit Datei", **Bearbeiten**, ggf. „Datei ansehen" |

Eigener Freigabeschalter, **getrennt vom Profil** — man kann ansprechbar sein, ohne seine Arbeiten zu zeigen. Dieselben vier Lagen wie beim Profil.
**Kein Entfernen in der Liste** — Zeilen verwechselt man.

### `/portfolio/new` und `/portfolio/$index` — eine Arbeit *(neu in E3b)*
Titel (Pflicht), Worum es geht, Link (nur http/https), Rolle, Jahr, **Datei** (PNG/JPEG/PDF, ≤ 5 MB).
- Leerer Link → `null`, leeres Jahr → `null` (nie `""`, nie `0`).
- **Der lokale Dateiname erscheint nie** — er ging nie zum Server.
- Hochgeladen wird sofort, gespeichert mit dem Formular.
- **Entfernen steht hier**, nicht in der Liste.
- `/portfolio/7` ohne siebte Arbeit → „Diese Arbeit gibt es nicht" — **kein** leeres Formular, das stillschweigend eine neue anlegt.
- Überschrift = Titel der Arbeit (Gegenmaßnahme zur fehlenden ID).

### `/applications` — Meine Bewerbungen
| Zustand | Ansicht |
|---|---|
| lädt | „Bewerbungen werden geladen…" *(fehlte vor E3a — man sah eine leere Karte)* |
| leer | „Noch keine Bewerbung." + „Offene Stellen ansehen" |
| gefüllt | Zustand (Abgeschickt / Wird gelesen / Abgelehnt / Zusage / Zurückgezogen) + **was freigegeben ist** |

Zurückziehen **nur solange etwas freigegeben ist** — kein deaktivierter Knopf, sondern gar keiner. Danach: „Das Unternehmen sieht deine Daten nicht mehr."

### `/consents` — Meine Freigaben
| Zustand | Ansicht |
|---|---|
| lädt | „Freigaben werden geladen…" |
| Fehler | Meldung — **nie** als leere Liste |
| leer | „Du hast im Moment nichts freigegeben." + „Niemand sieht etwas von dir." |
| gefüllt | Bereich · Empfänger, „Freigegeben am …", Zurückziehen |

Empfänger: „Alle Unternehmen" bei öffentlich, sonst der Firmenname — **ein Unternehmen ohne Profil bekommt keinen erfundenen Namen** („Ein Unternehmen"). Eine unbekannte Capability wird angezeigt, nicht verschluckt. Jeder Widerruf trägt eine Begründung.

### `/my-data` — Meine Daten
| Zustand | Ansicht |
|---|---|
| lädt | „Daten werden gesammelt…" |
| unvollständig | Warnung **vor** dem Herunterladen, mit den fehlenden Teilen |
| fertig | `<dl>`: je Abschnitt „enthalten" oder „fehlt", dazu „Als JSON herunterladen" |

Die Datei entsteht **im Browser** und wird nirgends abgelegt. Enthält den **Freigabe-Verlauf**, den die Übersicht bewusst weglässt. Zweite Karte: „Was hier nicht steht" — Löschen ist ein **Link**, nie ein Nachbarknopf.

### `/settings` — Einstellungen
Vier Schalter, jeder **wirkt sofort** — es gibt keinen Speichern-Knopf.
| Zustand | Ansicht |
|---|---|
| lädt | „Einstellungen werden geladen…" |
| **nicht abrufbar** | **Schalter gesperrt** + „Solange das so ist, ändern die Schalter nichts" *(vorher zeigte die Seite „alles an" und ein Klick nahm drei Abbestellungen zurück, die niemand zurückgenommen hatte — E3c)* |
| bekannt | Schalter, Nachsatz „Höchstens eine Mail pro Stunde" |

Der Vorspann sagt, was in einer Mail steht: **„Es gibt etwas Neues für dich."** Kein Firmenname, kein Vorgang, keine Anzahl.

### `/github` — GitHub verbinden
| Zustand | Ansicht |
|---|---|
| lädt | „Verbindung wird geladen…" — **und kein Formular** *(vorher stand beides zugleich da, E3c)* |
| nicht verbunden | „Konto nennen" (Benutzername) |
| unbestätigt | Nachweis: die genaue Gist-Beschreibung, „Nachweis prüfen", „Anderes Konto" |
| bestätigt | Login, Stand, Repositories mit Sprache und ★, „Aktualisieren", „Verbindung trennen" |
| leer | „Keine öffentlichen Repositories gefunden. Das ist kein Mangel — nur eine Auskunft." |

**Belege, keine Noten** (ADR-0022). Es läuft **kein** Abgleich im Hintergrund. Sichtbar für Unternehmen wird es erst über `/consents`.

### `/market` — Mein Marktstatus
Verfügbarkeit (sucht aktiv / hört zu / gerade nicht), „arbeitet gerade", Notiz, Ansprechbarkeit — plus die Anfragen von Unternehmen.
*Noch nicht umgestellt (E3e): eigenes Markup, `getMyMarketStatus` erfindet bei Fehlschlag vermutlich eine Voreinstellung — derselbe Verdacht wie bei den zwei Funden aus E3b/E3c.*

### `/transfers` — Meine Gespräche
Ein Transfer entsteht nur aus **drei Ja**; der Arbeitgeber wird nie gefragt.
*Noch nicht umgestellt (E3e).*

### `/delete-account` — Konto löschen
| Zustand | Ansicht |
|---|---|
| anonym | „Bitte anmelden, um dein Konto zu löschen." |
| Person | **Was gelöscht wird** (namentlich, acht Punkte) · **Was bleibt und warum** (der Nachweis) · **deine Unternehmen** (nur wenn vorhanden) · **Wie es abläuft** · Knopf |
| erster Klick | „Letzte Frage: …" + **„Ja, endgültig löschen"** / „Abbrechen" |
| angenommen | „Deine Löschung ist angenommen und **läuft**." + warum es dauert |

Tragend (ADR-0027 §6): der Text steht **vor** dem Knopf · **keine Ausnahme** wird versprochen · zwei Schritte **inline, kein Dialog** · **nach keinem Grund** wird gefragt · „läuft", nicht „erledigt" · **kein Fortschrittsbalken** · der Erfolgszustand hat **Vorrang vor der Anmeldeaufforderung** (die Seite räumt die Sitzung weg).
Ausdrücklich genannt: **auch die Bewerbung, über die du eingestellt wurdest, verschwindet** aus der Liste des Unternehmens.
Als Admin: „bist du die letzte Person mit Verwaltungsrechten, wird das Unternehmen stillgelegt" — **hält die Löschung nicht auf**.

---

## Unternehmen (nach „Handeln als")

### `/candidates` — Kandidatinnen und Kandidaten
| Zustand | Ansicht |
|---|---|
| kein Unternehmen | „Profile sehen nur Unternehmen. Wechsle oben auf ein Unternehmen — oder lass dich einladen." |
| lädt | „Profile werden geladen…" — **seit ADR-0030 in 0,015–0,083 s statt 1,7–8,8 s** |
| Fehler | Meldung |
| leer **mit** Filter | „Auf diese Suche passt gerade niemand, der sein Profil freigegeben hat." |
| leer **ohne** Filter | „Im Moment hat niemand sein Profil freigegeben." + „Das ist kein Fehler — es ist die Voreinstellung." |
| Treffer | je Person eine Karte |

Die beiden Leersätze sind **verschieden**, weil eine leere Trefferliste etwas über die *Suche* sagt, nicht über die Plattform.
Je Karte **drei getrennte Türen**: Lebenslauf anfragen · Marktstatus anfragen (dann: angefragt / abgelehnt / „gerade nicht einsehbar" / Status + „Interesse zeigen") · GitHub-Belege.
Getrennt, weil ein Lebenslauf verrät *wo jemand war*, der Marktstatus *dass er weg will*.
**Keine Gesamtzahl** — sie würde verraten, wie viele Profile *nicht* freigegeben sind.

### `/company/jobs` — Unsere Stellen
Eigene Stellen samt Entwurf/Veröffentlicht/Geschlossen, Anlegen und Bearbeiten, KI-Hilfe für die **eigene Anzeige** (`POST /jobs/draft`).
*Noch nicht umgestellt (E3d), Abspaltung `/company/jobs/new` und `/company/jobs/$id/edit` geplant.*

### `/company/profile` — Unser Unternehmen
Anzeigename, Über uns, Website, Standorte, Benefits, Kürzel für `/careers/<slug>`.
*Noch nicht umgestellt (E3d).*

### `/company/team` — Mannschaft
Mitglieder mit Rolle, Einladen per E-Mail.
*Noch nicht umgestellt (E3d), Abspaltung `/company/team/invite` geplant.*

### `/company/transfers` — Transfers
Laufende Gespräche aus Unternehmenssicht, Angebot und Start.
*Noch nicht umgestellt (E3d).*

---

## Bekannte Lücken — ehrlich benannt

| Lücke | Folge |
|---|---|
| **`admin` vs. `member` wird nirgends durchgesetzt** | Die Kopfzeile *versteckt* Unternehmenseinträge; wer die Adresse kennt, kommt hin, und der Server antwortet `403` erst dort, wo `tenant_id` fehlt. **Die Navigation ist keine Zugriffskontrolle.** |
| **`/company/admin` gibt es nicht** | Bewusst zurückgestellt: braucht erst serverseitige Rollen. HR-Verwaltung und DNS-Anbindung für eine Subdomain gehören dort hin. |
| **Kein Client hat ein Zeitlimit** | `fetch` wartet unbegrenzt. Eine Seite, die keine Antwort bekommt, dreht endlos — für eine Person nicht von Langsamkeit zu unterscheiden. Der *Anlass* auf `/candidates` ist mit ADR-0030 weg, der Mangel nicht. |
| **`/market`, `/transfers`, `/overview`, `/company/*` tragen noch eigenes Markup** | E3d und E3e stehen aus; dort sitzt auch der vierte rohe `<select>` (`CompanySwitcher`) und der Verdacht auf einen dritten „Client erfindet eine Voreinstellung"-Fund. |
| **„Anmeldung fehlgeschlagen" ist die einzige Meldung** | Eine englische Server-Meldung würde in einer deutschen Oberfläche stehen; deshalb übersetzt der Client sie pauschal — der Preis ist, dass alle Fehlgründe gleich aussehen. |
| **Kein i18n** | Alle Texte sind hart deutsch, und die Tests prüfen die Literale. |
