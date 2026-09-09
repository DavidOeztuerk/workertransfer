# ADR-0035: Die Bewerbungsmappe — zwei Wege zum Lebenslauf, und eine Ablage

**Status:** angenommen (05.09.2026)
**Betrifft:** resume-service, applications-service, consent-service, `src/shared/`, `web/`
**Verwandt:** ADR-0020 (Sichtbarkeit lebt im Ledger), ADR-0013 (Einwilligung wirkt sofort), ADR-0021 (`worker-files` gelöscht, `worker-storage` zurückgeschnitten), ADR-0027 (Löschung), ADR-0034 (das Anschreiben)

## Der Fund, der dieses ADR ausgelöst hat

Auf der Freigabeseite stand:

> Der Lebenslauf hat keinen Schalter. […] ein Lebenslauf nennt echte
> Arbeitgeber mit Zeiträumen — genau das, was dein jetziger nicht sehen soll.

Der Satz ist richtig und stand am falschen Ort. Wer sich **selbst bewirbt**,
schickt seinen Lebenslauf mit; das ist der Sinn einer Bewerbung. Ein Nutzer
las den Satz, während er genau das tun wollte, und verstand die Plattform nicht
mehr — zu Recht.

Die Ursache liegt tiefer als im Text: **es gab nur einen Begriff.** Der Ledger
kennt `resume.requested`, `resume.granted`, `resume.declined` — alle drei
beschreiben den Weg, auf dem ein *Unternehmen fragt*. Für „ich schicke ihn
selbst" gab es kein Wort, also musste die Seite den einen Weg für beide
erklären.

## Entscheidung 1: zwei Wege, zwei Wörter

| | **Transfermarkt** | **Bewerbung** |
|---|---|---|
| Wer beginnt? | Das Unternehmen fragt | **Die Person schickt** |
| Fähigkeit | `resume.requested/granted/declined` | **`unterlagen.granted`** |
| Umfang | je Unternehmen, auf Anfrage | **je Unternehmen, durch die Bewerbung** |
| Warum ohne Schalter „für alle"? | Ein Lebenslauf auf einem Aushang wäre für den jetzigen Arbeitgeber lesbar | — der Frage stellt sich hier nicht: es geht an *ein* Unternehmen |
| Endet wann? | Beim Widerruf | **Beim Widerruf oder beim Zurückziehen der Bewerbung** |

**Die Handlung ist die Einwilligung**, und sie wird trotzdem im Ledger
vermerkt. Nicht, weil ohne den Eintrag etwas fehlte, sondern weil er der
*Beleg* ist und der *Hebel*: die Auskunftsseite zeigt ihn, die Widerrufsseite
kann ihn zurücknehmen, die Löschung räumt ihn ab. Eine Erlaubnis, die nur als
Zeile in einer Bewerbungstabelle existierte, wäre an keiner dieser drei
Stellen sichtbar.

**Zurückziehen widerruft.** Sonst bliebe die Mappe lesbar, nachdem die
Bewerbung fort ist — und das wäre genau die stille Differenz zwischen dem, was
eine Oberfläche zeigt, und dem, was ein Server erlaubt.

## Entscheidung 2: die Ablage kommt zurück

ADR-0021 beschrieb einen `Storage`-Port mit einem lokalen Backend und einer
Signaturprüfung. Er beschrieb ihn **in Python**, und die .NET-Migration hat ihn
nicht mitgenommen — `src/shared/` hält heute sechs Dinge, und keines speichert
Dateien. Zertifikate brauchen ihn.

Er kommt so zurück, wie ADR-0021 ihn begründet hat, und keinen Schritt weiter:

- **`IAblage`** — `LegeAbAsync` / `HoleAsync` / `LoescheAsync`. `HoleAsync`
  gibt `null` statt zu werfen: „gibt es nicht" ist beim Abrufen ein normaler
  Ausgang. `LoescheAsync` schweigt über einen unbekannten Schlüssel, damit
  Aufräumpfade keinen Unterschied behandeln müssen, der sie nicht interessiert.
- **`LokaleAblage`** — Dateisystem, **schreiben unter Zwischennamen und dann
  umbenennen**. Ein Absturz mittendrin hinterlässt sonst eine halbe Datei unter
  dem richtigen Namen, und die sieht für jeden Leser gültig aus.
- **`Typerkennung`** — der Typ kommt aus den **ersten Bytes**, nie aus dem, was
  der Aufrufer behauptet. Ein `Content-Type` und eine Dateiendung sind beide
  frei wählbar, eine Signatur nicht. Erlaubt sind **PNG, JPEG, PDF** — genau
  die drei aus ADR-0021.
- **Kein S3, noch nicht.** Es zu bauen, bevor eine Umgebung es braucht, wäre
  der Fehler, der zu ADR-0021 geführt hat. `IAblage` ist die Naht.

**Ein siebtes Paket in `src/shared/` ist eine Ausnahme und wird hier
begründet:** Ablage ist fachfrei, transportunabhängig und ohne Geschäftslogik —
dieselbe Prüfung, die `Outbox` und `Skills` bestanden haben. Sie in
resume-service zu legen hieße, sie beim zweiten Nutzer zu kopieren.

## Entscheidung 3: die Mappe wird gesetzt, nicht erzeugt

Es gibt **kein PDF und kein Word**. Weder auf dem Server noch beim Versand.

Der Grund ist der Weg: eine Bewerbung bleibt **plattformintern**. Sie geht
nicht als E-Mail an einen fremden Posteingang, sondern in die Bewerberliste des
Unternehmens, das die Stelle ausgeschrieben hat. Dort öffnet ein Mensch sie im
Browser — und für einen Browser ist ein PDF ein Umweg über zwei Formate, der
nichts hinzufügt.

Die Mappe erscheint dem Unternehmen **genau so, wie die Person sie gewählt
hat**:

- **Anschreiben** — als Brief gesetzt: Seitenspiegel, Ränder, Absatzbild. Es
  soll aussehen wie das, was es ist, ohne eine Datei zu sein.
- **Lebenslauf** — aus den Daten der Person in **ihrer gewählten Vorlage**
  (`schlicht`, `klassisch`, `modern`). Dieselbe Darstellung, die sie selbst
  sieht; nichts wird für den Empfänger anders gerendert.
- **Zertifikate** — Bilder als Bild, PDF eingebettet. Ein Reiter je Stück, und
  die Reiterleiste **scrollt**, weil niemand weiß, wie viele es sind.

**Die Vorlage ist ein Wert, kein Layout im Server.** resume-service speichert
den Namen; das Aussehen liegt in `web/src/styles/` und gilt für beide Seiten.
Ein Server, der Layout kennt, ist ein Server, der es zweimal kennen muss.

Wer ein PDF braucht, druckt die Seite — der Browser kann das, und das Ergebnis
ist das, was auf dem Schirm stand.

## Was die Löschung angeht

- `Unterlage` ist eine **`Personenzeile`**: der Schlüssel ist die Person.
  `LoeschempfaengerTests` sieht sie dadurch, ohne dass jemand eine Liste pflegt.
- **Die Löschung räumt auch die Ablage.** Zeilen zu löschen und Dateien liegen
  zu lassen wäre eine gebrochene Zusage (ADR-0027) — und zwar eine, die niemand
  bemerkt, weil die Oberfläche danach leer aussieht.
- Der Ledgereintrag `unterlagen.granted` wird wie jeder andere zur
  DELETE-Zeile; die Kette bleibt als Nachweis, die Daten gehen.

## Grenzen, die dazugehören

- **Höchstens zehn Unterlagen und je fünf MB.** Nicht aus Sparsamkeit: ohne
  Grenze baut ein Aufrufer mit einer Schleife eine beliebig teure Ablage, und
  eine Bewerbung mit dreißig Anhängen liest ohnehin niemand.
- **Der Inhalt geht nur an zwei Adressaten:** an die Person selbst und an ein
  Unternehmen mit gültigem Ledgereintrag für *diese* Bewerbung. Es gibt keine
  öffentliche Adresse für eine Unterlage.
- **Kein Vorschaubild, keine Umwandlung, keine Textauslese im Hintergrund.**
  ADR-0021 hat die Bildbearbeitung aus gutem Grund gelöscht; was ein Mensch
  hochgeladen hat, wird gezeigt und sonst nichts damit getan.
