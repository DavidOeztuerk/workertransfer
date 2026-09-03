import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import TextField from "@mui/material/TextField";

import {
  ConsentSwitch,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
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

const LEER: FormState = {
  headline: "",
  bio: "",
  location: "",
  remote_ok: false,
  skills: "",
};

function zuFormular(profile: Profile | null): FormState {
  if (profile === null) return LEER;
  return {
    headline: profile.headline,
    bio: profile.bio,
    location: profile.location,
    remote_ok: profile.remote_ok,
    skills: profile.skills.join(", "),
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
  const { t } = useTranslation();
  const { subjectId, unbekannt } = usePerson();

  const profile = useAsync(
    (signal) => getMyProfile(signal),
    [subjectId],
    subjectId !== null,
  );
  const freigabe = useAsync(
    (signal) => getVisibility(subjectId ?? "", signal),
    [subjectId],
    subjectId !== null,
  );

  const [form, setzeFormular] = useState<FormState>(LEER);
  const [fehler, setzeFehler] = useState<ApiError | null>(null);
  const [saved, setzeGespeichert] = useState(false);
  const [saving, setzeSpeichert] = useState(false);
  const [toggling, setzeSchaltet] = useState(false);

  // Das Formular folgt dem Server, solange niemand tippt. Danach gehört der
  // Zustand der Person — sonst verliert sie ihre Eingabe, sobald die Antwort
  // eintrifft.
  const loaded = profile.value;
  useEffect(() => {
    if (loaded?.ok === true) setzeFormular(zuFormular(loaded.profile));
  }, [loaded]);

  function change<K extends keyof FormState>(
    key: K,
    value: FormState[K],
  ) {
    setzeGespeichert(false);
    setzeFormular((now) => ({ ...now, [key]: value }));
  }

  async function save() {
    setzeSpeichert(true);
    const result = await saveMyProfile({
      headline: form.headline,
      bio: form.bio,
      location: form.location,
      remote_ok: form.remote_ok,
      skills: parseSkills(form.skills),
    });
    setzeSpeichert(false);
    if (result.ok) {
      setzeFehler(null);
      setzeGespeichert(true);
      profile.setze({ ok: true, profile: result.profile });
    } else {
      setzeGespeichert(false);
      setzeFehler(result.error);
    }
  }

  async function toggle(naechster: boolean) {
    if (subjectId === null) return;
    setzeSchaltet(true);
    const result = await setVisibility(subjectId, naechster);
    setzeSchaltet(false);
    if (result.ok) {
      setzeFehler(null);
      // Der Ledger sagt, was gilt — nicht der Wunsch des Klicks.
      freigabe.setze(result.granted);
    } else {
      setzeFehler(result.error);
      // Zurückstellen: ein Schalter, der „sichtbar" zeigt, obwohl nichts
      // freigegeben wurde, wäre die gefährlichere Lüge.
      freigabe.setze(!naechster);
    }
  }

  if (unbekannt) {
    return (
      <PageShell title={t("profil.titel")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return (
      <AnmeldungNoetig titel={t("profil.titel")} satz="profil.anmelden" />
    );
  }

  if (profile.laedt || loaded === undefined) {
    // Kein leeres Formular, das sich nachträglich füllt: wer in der Zwischenzeit
    // zu tippen anfängt, verliert seine Eingabe, sobald die Antwort eintrifft.
    return (
      <PageShell title={t("profil.titel")} narrow>
        <LoadingBlock label={t("profil.laden")} />
      </PageShell>
    );
  }

  if (!loaded.ok) {
    // Und schon gar kein leeres Formular über einer gescheiterten Abfrage: wer
    // darin etwas tippt und speichert, überschreibt alles, was dastand, mit
    // leer. Der Ladezustand ist dann vorbei, die Seite sähe aus wie „noch
    // nichts eingetragen".
    return (
      <PageShell title={t("profil.titel")} narrow>
        <ErrorBlock error={loaded.error} />
      </PageShell>
    );
  }

  const hatProfil = loaded.profile !== null;
  // Die ANZEIGE bleibt bei Nichtwissen aus — ein Schalter, der versehentlich
  // „freigegeben" behauptet, ist die gefährlichere Lüge.
  const freigegeben = freigabe.value === true;
  // ... aber „weiss ich nicht" ist nicht „nein". Solange die Antwort aussteht
  // oder ausbleibt, darf der Schalter nicht BEDIENBAR sein: sonst schickt der
  // nächste Klick ein `grant` für eine Einwilligung, deren Zustand niemand
  // kennt.
  const ledgerSchweigt = !freigabe.laedt && freigabe.value === null;
  const ledgerUnbekannt = freigabe.laedt || ledgerSchweigt;

  return (
    <PageShell
      title={t("profil.titel")}
      narrow
      lead={t("profil.lead")}
    >
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <ConsentSwitch
            label={t("profil.freigabeLabel")}
            checked={freigegeben}
            disabled={!hatProfil || toggling || ledgerUnbekannt}
            hint={
              ledgerSchweigt
                ? t("profil.freigabeSchweigt")
                : freigabe.laedt
                  ? t("profil.freigabePruefung")
                  : t(
                      hatProfil
                        ? "profil.freigabeWirkt"
                        : "profil.freigabeOhneProfil",
                    )
            }
            onChange={(naechster) => void toggle(naechster)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("profil.ueberschrift")}
              helperText={t("profil.ueberschriftHinweis")}
              value={form.headline}
              onChange={(event) =>
                change("headline", event.target.value)
              }
              slotProps={{ htmlInput: { maxLength: 120 } }}
              required
              fullWidth
            />
            <TextField
              label={t("profil.ueberMich")}
              helperText={t("profil.ueberMichHinweis")}
              multiline
              rows={6}
              value={form.bio}
              onChange={(event) => change("bio", event.target.value)}
              slotProps={{ htmlInput: { maxLength: 4000 } }}
              fullWidth
            />
            <DraftHelp
              onDraft={(draft) => change("bio", draft)}
              hasText={form.bio.trim().length > 0}
            />
            <TextField
              label={t("profil.ort")}
              value={form.location}
              onChange={(event) =>
                change("location", event.target.value)
              }
              slotProps={{ htmlInput: { maxLength: 120 } }}
              fullWidth
            />
            <TextField
              label={t("profil.faehigkeiten")}
              // Der Hinweis erklärt, warum aus „postgres" nach dem Speichern
              // „PostgreSQL" wird. Ohne ihn sähe es aus, als hätte die Seite
              // etwas an der Eingabe verändert, ohne zu fragen.
              helperText={t("profil.faehigkeitenHinweis")}
              value={form.skills}
              onChange={(event) => change("skills", event.target.value)}
              fullWidth
            />
            {/* Eine Ankreuzbox und KEIN Schalter: dies ist ein Profilfeld, und
                es gilt erst mit dem Speichern. Genau umgekehrt zur Freigabe
                darüber — dort wäre eine Box das falsche Versprechen. */}
            <FormControlLabel
              control={
                <Checkbox
                  checked={form.remote_ok}
                  onChange={(event) =>
                    change("remote_ok", event.target.checked)
                  }
                />
              }
              label={t("profil.remote")}
            />

            {fehler !== null ? <ErrorBlock error={fehler} /> : null}
            {/* `role="status"` und nicht `alert`: eine Bestätigung, die den
                Vorleser unterbricht, ist Lärm. */}
            {saved && fehler === null ? (
              <Alert severity="success" role="status">
                {t("profil.gespeichert")}
              </Alert>
            ) : null}

            <Box>
              <Button type="submit" variant="contained" disabled={saving}>
                {saving
                  ? t("allgemein.speichernLaeuft")
                  : t("allgemein.speichern")}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
