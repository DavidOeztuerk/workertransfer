# Befund & Soll — E3c: Konto, Freigaben, Einstellungen, GitHub

`/delete-account`, `/consents`, `/settings`, `/github`. Vier Seiten, auf denen
eine Person **entscheidet, was mit ihr geschieht** — die Löschung, die
Freigaben, die Nachrichten, die Belege.

Gemessen am 13.08.2026, vor der Umstellung.

> **Abweichung von der Spec.** Dort war E3b `portfolio/profile/resume/github` und
> E3c `account-deletion/consents/my-data/settings`. Getauscht: `my-data` ist in
> E3b mitgelaufen (es gehört zu „meine Daten"), `github` kommt hier dazu. Gleiche
> Fläche, ein Tausch.

## Ist-Stand

| Route | Zeilen | Tests | Was sie trägt |
|---|---|---|---|
| `/delete-account` | 230 | 24 | ADR-0027 §6 — die meisten Zusagen pro Zeile im ganzen Projekt |
| `/consents` | 183 | 9 | was gerade gilt, und das Zurückziehen |
| `/settings` | 131 | 6 | vier Schalter, jeder wirkt sofort |
| `/github` | 186 | 10 | Belege, keine Noten (ADR-0022) |

## Drei Funde, die keine Umstellung sind

### 1. `/settings` erfindet „alles an" — und schreibt es beim nächsten Klick

Im Client:

```ts
export async function getNotificationPreferences(): Promise<NotificationPreferences> {
  try {
    const res = await send("/me/notification-preferences");
    if (!res.ok) return { ...ALL_ON };
    return (await res.json()) as NotificationPreferences;
  } catch {
    return { ...ALL_ON };
  }
}
```

Der Test dazu heißt *„falls back to all-on, never to all-off"* und begründet es:
„Ein Netzfehler ist keine Abbestellung. Vier ausgeschaltete Schalter würden beim
nächsten Speichern geschrieben — und die Person hätte sich abgemeldet, ohne es zu
wollen."

Die Hälfte ist richtig. Die andere Hälfte ist **derselbe Satz mit umgekehrtem
Vorzeichen**: vier *eingeschaltete* Schalter werden beim nächsten Speichern
ebenso geschrieben. Wer drei Arten abbestellt hatte, sieht nach einem
Serverausfall alle vier an — und ein Klick auf den vierten schickt
`{...ALL_ON, [key]: next}`, also **drei Abbestellungen zurückgenommen, die
niemand zurückgenommen hat.**

Verschärfend: der Fehlschlag ist von außen **nicht sichtbar**. Die Abfrage
gelingt (sie liefert ja ein Ergebnis), also ist `isPending` falsch, es gibt
keinen Fehlerzustand, und die Schalter sind **bedienbar**.

Wichtig — und geprüft, damit hier keine Absicht kaputtrepariert wird: das
`ALL_ON` **für ein neues Konto** kommt vom **Server**. `GET
/me/notification-preferences` antwortet immer `200` mit den Voreinstellungen
(`preference.wants(...)`), auch wenn nie etwas gespeichert wurde. Der
Client-Rückfall gilt also ausschließlich für Fehlschläge, und die Zusage „shows
every kind as on before anyone touched them" hängt nicht an ihm.

Das ist Muster für Muster derselbe Fund wie `isGranted` in E3b: **ein Client, der
einen Fehlschlag in eine plausibel aussehende Voreinstellung verwandelt.** Dort
ging es um eine Anzeige, hier um einen Schreibvorgang.

### 2. `.form__actions` hat keine Regel

`account-deletion.tsx` legt die zwei Knöpfe der letzten Bestätigung in
`<div className="form__actions">`. Diese Klasse ist in `apps/web/src/styles.css`
**nicht definiert** — die beiden Knöpfe der unwiderruflichsten Handlung des
Systems liegen also ohne jedes Layout da. Vergeben wurde sie irgendwann, eine
Regel bekam sie nie; genau wie `.draft-help` einmal (ROADMAP 7.2).

### 3. `/github` lädt und fragt gleichzeitig

```tsx
{query.isPending ? <Card><p role="status">Wird geladen…</p></Card> : null}
{connection === null || connection === undefined ? <Card><h2>Konto nennen</h2>…
```

Die beiden Bedingungen schließen sich nicht aus: solange die Abfrage läuft, ist
`connection` `undefined`, also steht „Wird geladen…" **und** das Formular
„Konto nennen" auf der Seite. Wer schnell tippt, nennt ein Konto, bevor die Seite
weiß, ob schon eines verbunden ist.

## Zusagen, die kein Refactor anfassen darf

49 Testnamen, davon 24 allein auf der Löschseite. Die tragenden:

- **Der Text steht vor dem Knopf.** Was verschwindet, steht namentlich da —
  einschließlich der Bewerbung, über die jemand **eingestellt** wurde.
- **Keine Ausnahme wird versprochen**, weil es in der Voreinstellung keine gibt.
- **Zwei Schritte, zweiter Knopf anders formuliert.** Kein abzutippendes Wort,
  keine Bedenkzeit, kein erneutes Passwort. Und **kein Dialog**: zwei bewusste
  Schritte inline sind die Zusage, ein Dialog wäre eine andere.
- **Nach keinem Grund wird gefragt** — nirgends auf der Seite.
- **Danach heißt es „läuft", nicht „erledigt"**, ohne Fortschrittsbalken.
- **Der Erfolgszustand hat Vorrang vor der Anmeldeaufforderung.** Die Seite
  räumt die Sitzung weg, die Hülle rendert sofort mit `principal = null` neu —
  stünde die Aufforderung zuerst, läse man „Bitte anmelden, um dein Konto zu
  löschen" direkt nach dem Löschen. Aufgefallen ist das in der E2E-Reise,
  während die Komponententests grün waren.
- **Ein gescheiterter Versuch sagt, dass nichts gelöscht wurde**, und der Weg
  bleibt offen.
- `/consents`: **ein Fehler wird nie als leere Liste gezeigt**; eine unbekannte
  Capability wird angezeigt statt verschluckt; ein Unternehmen ohne Profil
  bekommt **keinen erfundenen Namen**; ein Widerruf trägt immer eine Begründung.
- `/settings`: **jeder Schalter wirkt sofort, es gibt keinen Speichern-Knopf** —
  `Switch` ist ein `button[role="switch"]`, gerade weil eine Checkbox
  verspricht, die Änderung gelte erst beim Absenden.
- `/github`: **Belege, keine Noten** (ADR-0022); es läuft **kein** Abgleich im
  Hintergrund; ein fehlender Gist ist von einer Störung zu unterscheiden; ein
  leeres Ergebnis ist eine Auskunft, kein Mangel.

## Entschieden (13.08.2026)

- **`getNotificationPreferences` liefert `null` bei Fehlschlag.** Die Route sagt
  dann, dass die Einstellungen nicht abrufbar sind, und **sperrt** die Schalter.
  Damit kann in **keine** Richtung etwas Erfundenes geschrieben werden — weder
  all-off noch all-on. Der Test behält seinen Namen und seine Begründung; die
  zweite Hälfte kommt dazu.
- **`my-data` fragt die Einstellungen ab und nannte sie immer „enthalten".** Mit
  `null` wird daraus ehrlich „fehlt" — der Export behauptet sonst, er enthalte
  Voreinstellungen, die niemand gesetzt hat.
- **`.form__actions` bekommt keine Regel, sondern verschwindet.** Die zwei
  Knöpfe stehen nebeneinander, dafür gibt es kein eigenes Layout — `Card`
  ordnet sie schon.
- **Keine neue Route in dieser Gruppe.** Auf der Löschseite wäre eine
  Bestätigungsseite genau der Dialog, den ADR-0027 §6 ausschließt; `/consents`
  und `/settings` sind Listen ohne Formular, und `/github` ist ein Ablauf in
  drei Zuständen, kein Formular, das man teilen will.


## Ergebnis (13.08.2026)

| Datei | vorher | nachher |
|---|---|---|
| `routes/account-deletion.tsx` | 230 | 233 |
| `routes/consents.tsx` | 183 | 160 |
| `routes/settings.tsx` | 131 | 140 |
| `routes/github.tsx` | 186 | 193 |
| `apps/web/src/styles.css` | 517 | 517 |

Tests: **49 → 56** in dieser Gruppe (Web gesamt 433 → 437; die Differenz ist
kleiner, weil ein Test **ersetzt** wurde statt hinzuzukommen).

Drei der vier Dateien sind **länger** geworden. Das ist hier durchgehend der
Preis für einen Zustand, den es vorher nicht gab: die gesperrten Schalter, der
getrennte Ladezustand, die Fehlermeldung, die vorher nicht existieren konnte.
`styles.css` bleibt gleich groß — diese Gruppe benutzte fast nur geteilte
Klassen, und `.form__actions` hatte ohnehin keine Regel zu löschen.

### Der Fund war der Spiegelfall zu E3b

In E3b gab `isGranted` bei Fehlschlag `false`. Hier gab
`getNotificationPreferences` `ALL_ON`. Beide Kommentare **begründeten die Wahl
richtig** — und beide betrachteten nur eine Richtung:

| | E3b | E3c |
|---|---|---|
| erfundener Wert | `false` = „nicht freigegeben" | `ALL_ON` = „alles abonniert" |
| Begründung im Code | „freigegeben behaupten ist die gefährlichere Lüge" | „ein Netzfehler ist keine Abbestellung" |
| was sie überging | der Schalter blieb **bedienbar** | der Schalter blieb **bedienbar** |
| Folge | `grant` auf unbekannten Stand | drei Abbestellungen stillschweigend zurückgenommen |

Beide liefern jetzt `null`, und beide Seiten sperren dann den Schalter. Die
alten Zusagen stehen weiter in den Tests — als **zweite** Behauptung neben der
neuen, damit niemand sie beim nächsten Umbau für überholt hält.

Dass es zweimal dieselbe Form war, ist die eigentliche Lehre: **ein Client, der
einen Fehlschlag in eine plausible Voreinstellung verwandelt, verschiebt eine
Entscheidung an die Stelle, die sie nicht treffen darf.** Wert, beim nächsten
Client danach zu suchen — `getMyMarketStatus` und `getMyPortfolio` sind die
nächsten Kandidaten (E3e bzw. schon umgestellt).

### Was ich nicht gemacht habe

`.transfer__actions` in `github.tsx` habe ich zuerst durch `wt-row__actions`
ersetzt und wieder zurückgenommen: das ist ein **Interna von `Row`**, und
`github.tsx` hat keine Row. Dasselbe Ausleihen in neuer Richtung. Die Klasse hat
eine Regel und drei Nutzer; umbenannt wird sie in E3e, wo ihr Eigentümer
(`transfers.tsx`) umgestellt wird.
