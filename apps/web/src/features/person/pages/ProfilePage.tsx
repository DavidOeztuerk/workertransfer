import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import TextField from "@mui/material/TextField";

import { ConsentSwitch, ErrorBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import type { ApiError } from "../../../core/store/thunkHelpers";
import {
  type Profile,
  getMyProfile,
  getVisibility,
  saveMyProfile,
  setVisibility,
} from "../api/profile";
import { parseSkills } from "../lib/skills";
import { useAsync } from "../lib/useAsync";
import { usePerson } from "../lib/session";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { DraftHelp } from "../components/DraftHelp";

interface FormState {
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string;
}

const LEER: FormState = { headline: "", bio: "", location: "", remote_ok: false, skills: "" };

function zuFormular(profil: Profile | null): FormState {
  if (profil === null) return LEER;
  return {
    headline: profil.headline,
    bio: profil.bio,
    location: profil.location,
    remote_ok: profil.remote_ok,
    skills: profil.skills.join(", "),
  };
}

/**
 * Mein Profil — der Inhalt und, daneben, die Freigabe.
 *
 * Die beiden stehen getrennt, weil sie getrennt sind: das Profil gehört
 * profile-service, die Sichtbarkeit dem Ledger. Der Schalter schreibt deshalb
 * <strong>nur</strong> in den Ledger, und ein Speichern des Profils ändert an
 * der Freigabe nichts.
 */
export function ProfilePage() {
  const { subjectId, unbekannt } = usePerson();

  const profil = useAsync((signal) => getMyProfile(signal), [subjectId], subjectId !== null);
  const freigabe = useAsync(
    (signal) => getVisibility(subjectId ?? "", signal),
    [subjectId],
    subjectId !== null
  );

  const [formular, setzeFormular] = useState<FormState>(LEER);
  const [fehler, setzeFehler] = useState<ApiError | null>(null);
  const [gespeichert, setzeGespeichert] = useState(false);
  const [speichert, setzeSpeichert] = useState(false);
  const [schaltet, setzeSchaltet] = useState(false);

  // Das Formular folgt dem Server, solange niemand tippt. Danach gehört der
  // Zustand der Person — sonst verliert sie ihre Eingabe, sobald die Antwort
  // eintrifft.
  const geladen = profil.wert;
  useEffect(() => {
    if (geladen?.ok === true) setzeFormular(zuFormular(geladen.profile));
  }, [geladen]);

  function aendere<K extends keyof FormState>(schluessel: K, wert: FormState[K]) {
    setzeGespeichert(false);
    setzeFormular((jetzt) => ({ ...jetzt, [schluessel]: wert }));
  }

  async function speichere() {
    setzeSpeichert(true);
    const ergebnis = await saveMyProfile({
      headline: formular.headline,
      bio: formular.bio,
      location: formular.location,
      remote_ok: formular.remote_ok,
      skills: parseSkills(formular.skills),
    });
    setzeSpeichert(false);
    if (ergebnis.ok) {
      setzeFehler(null);
      setzeGespeichert(true);
      profil.setze({ ok: true, profile: ergebnis.profile });
    } else {
      setzeGespeichert(false);
      setzeFehler(ergebnis.error);
    }
  }

  async function schalte(naechster: boolean) {
    if (subjectId === null) return;
    setzeSchaltet(true);
    const ergebnis = await setVisibility(subjectId, naechster);
    setzeSchaltet(false);
    if (ergebnis.ok) {
      setzeFehler(null);
      // Der Ledger sagt, was gilt — nicht der Wunsch des Klicks.
      freigabe.setze(ergebnis.granted);
    } else {
      setzeFehler(ergebnis.error);
      // Zurückstellen: ein Schalter, der „sichtbar" zeigt, obwohl nichts
      // freigegeben wurde, wäre die gefährlichere Lüge.
      freigabe.setze(!naechster);
    }
  }

  if (unbekannt) {
    return (
      <PageShell title="Mein Profil" narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return <AnmeldungNoetig titel="Mein Profil" zweck="dein Profil zu bearbeiten" />;
  }

  if (profil.laedt || geladen === undefined) {
    // Kein leeres Formular, das sich nachträglich füllt: wer in der Zwischenzeit
    // zu tippen anfängt, verliert seine Eingabe, sobald die Antwort eintrifft.
    return (
      <PageShell title="Mein Profil" narrow>
        <LoadingBlock label="Profil wird geladen…" />
      </PageShell>
    );
  }

  if (!geladen.ok) {
    // Und schon gar kein leeres Formular über einer gescheiterten Abfrage: wer
    // darin etwas tippt und speichert, überschreibt alles, was dastand, mit
    // leer. Der Ladezustand ist dann vorbei, die Seite sähe aus wie „noch
    // nichts eingetragen".
    return (
      <PageShell title="Mein Profil" narrow>
        <ErrorBlock error={geladen.error} />
      </PageShell>
    );
  }

  const hatProfil = geladen.profile !== null;
  // Die ANZEIGE bleibt bei Nichtwissen aus — ein Schalter, der versehentlich
  // „freigegeben" behauptet, ist die gefährlichere Lüge.
  const freigegeben = freigabe.wert === true;
  // ... aber „weiss ich nicht" ist nicht „nein". Solange die Antwort aussteht
  // oder ausbleibt, darf der Schalter nicht BEDIENBAR sein: sonst schickt der
  // nächste Klick ein `grant` für eine Einwilligung, deren Zustand niemand
  // kennt.
  const ledgerSchweigt = !freigabe.laedt && freigabe.wert === null;
  const ledgerUnbekannt = freigabe.laedt || ledgerSchweigt;

  return (
    <PageShell
      title="Mein Profil"
      narrow
      lead="Was hier steht, sieht zunächst niemand. Sichtbar wird es erst, wenn du es freigibst — und unsichtbar in dem Moment, in dem du die Freigabe zurückziehst."
    >
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <ConsentSwitch
            label="Profil für Unternehmen freigeben"
            checked={freigegeben}
            disabled={!hatProfil || schaltet || ledgerUnbekannt}
            hint={
              ledgerSchweigt
                ? "Ob eine Freigabe gilt, ist gerade nicht abrufbar. Solange das so ist, ändert dieser Schalter nichts — sonst würdest du etwas freigeben, dessen Stand niemand kennt."
                : freigabe.laedt
                  ? "Freigabe wird geprüft…"
                  : hatProfil
                    ? "Wirkt sofort. Ein Widerruf entzieht den Zugriff, ohne dass du jemanden darum bitten musst."
                    : "Erst ein Profil speichern — freigeben lässt sich nur, was es gibt."
            }
            onChange={(naechster) => void schalte(naechster)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Box
            component="form"
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              void speichere();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label="Überschrift"
              helperText="Eine Zeile, die sagt, worum es dir geht."
              value={formular.headline}
              onChange={(ereignis) => aendere("headline", ereignis.target.value)}
              slotProps={{ htmlInput: { maxLength: 120 } }}
              required
              fullWidth
            />
            <TextField
              label="Über mich"
              helperText="Freitext. Was ein Lebenslauf nicht hergibt."
              multiline
              rows={6}
              value={formular.bio}
              onChange={(ereignis) => aendere("bio", ereignis.target.value)}
              slotProps={{ htmlInput: { maxLength: 4000 } }}
              fullWidth
            />
            <DraftHelp
              onDraft={(entwurf) => aendere("bio", entwurf)}
              hasText={formular.bio.trim().length > 0}
            />
            <TextField
              label="Ort"
              value={formular.location}
              onChange={(ereignis) => aendere("location", ereignis.target.value)}
              slotProps={{ htmlInput: { maxLength: 120 } }}
              fullWidth
            />
            <TextField
              label="Fähigkeiten"
              // Der Hinweis erklärt, warum aus „postgres" nach dem Speichern
              // „PostgreSQL" wird. Ohne ihn sähe es aus, als hätte die Seite
              // etwas an der Eingabe verändert, ohne zu fragen.
              helperText="Mit Komma getrennt, zum Beispiel: Python, FastAPI, PostgreSQL. Bekannte Schreibweisen vereinheitlichen wir — aus „postgres“ wird „PostgreSQL“. Was wir nicht kennen, bleibt genau so stehen."
              value={formular.skills}
              onChange={(ereignis) => aendere("skills", ereignis.target.value)}
              fullWidth
            />
            {/* Eine Ankreuzbox und KEIN Schalter: dies ist ein Profilfeld, und
                es gilt erst mit dem Speichern. Genau umgekehrt zur Freigabe
                darüber — dort wäre eine Box das falsche Versprechen. */}
            <FormControlLabel
              control={
                <Checkbox
                  checked={formular.remote_ok}
                  onChange={(ereignis) => aendere("remote_ok", ereignis.target.checked)}
                />
              }
              label="Remote-Arbeit kommt für mich in Frage"
            />

            {fehler !== null ? <ErrorBlock error={fehler} /> : null}
            {/* `role="status"` und nicht `alert`: eine Bestätigung, die den
                Vorleser unterbricht, ist Lärm. */}
            {gespeichert && fehler === null ? (
              <Alert severity="success" role="status">
                Profil gespeichert.
              </Alert>
            ) : null}

            <Box>
              <Button type="submit" variant="contained" disabled={speichert}>
                {speichert ? "Wird gespeichert…" : "Speichern"}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
