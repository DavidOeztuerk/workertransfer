import { useState } from "react";
import Button from "@mui/material/Button";
import TextField from "@mui/material/TextField";
import { Link as RouterLink } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import MenuItem from "@mui/material/MenuItem";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

import {
  ConsentSwitch,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { DarstellungsAbschnitte } from "../../../shared/components/layout/darstellung";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { Zivilidentitaet } from "../components/Zivilidentitaet";
import { useAsync } from "../lib/useAsync";
import {
  type KiAnbieter,
  type Kontoeinstellungen,
  ladeEinstellungen,
  speichereEinstellungen,
  speichereSchluessel,
} from "../api/kontoeinstellungen";
import {
  ALLES_AN,
  type Benachrichtigungswahl,
  ladeWahl,
  speichereWahl,
} from "../api/settings";

/**
 * Die fünf Schalter. Der fünfte ist der Eingang einer Bewerbung beim Unternehmen.
 *
 * Der Hinweis unter jedem sagt, was das Abschalten <em>kostet</em>, nicht was
 * der Schalter tut. Wer „Marktstatus-Anfragen" abschaltet, soll wissen, dass er
 * von der Anfrage dann nur erfährt, wenn er zufällig vorbeischaut.
 */
const SCHALTER: {
  key: keyof Benachrichtigungswahl;
  name: string;
}[] = [
  { key: "market_request", name: "markt" },
  { key: "resume_request", name: "lebenslauf" },
  { key: "transfer_update", name: "transfer" },
  { key: "application_update", name: "bewerbung" },
  { key: "application_received", name: "eingang" },
];

/**
 * <c>/settings</c> — was in einer Mail stehen darf.
 *
 * <strong>Der Text oben ist die eigentliche Aussage der Seite</strong> und
 * wurde wörtlich übernommen: eine Mail sagt nur, <em>dass</em> es etwas Neues
 * gibt. Kein Firmenname, kein Vorgang, keine Anzahl. Eine Mail kann in einem
 * Postfach landen, das nicht nur der Person gehört — und dann wäre der Satz,
 * der sie nützlicher machte, genau der, der jemanden den Arbeitsplatz kostet.
 *
 * <strong>Unbekannt ist nicht aus.</strong> Antwortet der Dienst nicht, stehen
 * die Schalter zwar auf ihrer Voreinstellung, sind aber <em>gesperrt</em>. Wer
 * sie in diesem Zustand bedienen könnte, speicherte eine Einstellung, die er
 * nie getroffen hat — und überschriebe womöglich eine, die er einmal getroffen
 * hat.
 */
export function SettingsPage() {
  const { t } = useTranslation();
  const session = useAppSelector((state) => state.auth.session);
  const status = useAppSelector((state) => state.auth.status);
  const [fehler, setFehler] = useState<string | null>(null);
  const [saving, setSpeichert] = useState(false);

  const choice = useAsync(
    (signal) => ladeWahl(signal),
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel={t("einstellungen.titel")}
        satz="einstellungen.anmelden"
      />
    );
  }

  const unbekannt = choice.laedt || choice.value === null;
  const werte = choice.value ?? ALLES_AN;

  async function umschalten(
    key: keyof Benachrichtigungswahl,
    neu: boolean,
  ) {
    const nextValue = { ...werte, [key]: neu };
    setSpeichert(true);
    choice.setze(nextValue);

    const result = await speichereWahl(nextValue);
    setSpeichert(false);

    if (result.ok) {
      setFehler(null);
      choice.setze(result.choice);
    } else {
      setFehler(result.error.detail);
      // Zurueck auf das, was der Dienst wirklich hat — die Anzeige darf keine
      // Einstellung behaupten, die nicht gespeichert wurde.
      choice.again();
    }
  }

  return (
    <PageShell
      title={t("einstellungen.titel")}
      narrow
      lead={t("einstellungen.lead")}
    >
      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("einstellungen.benachrichtigungen")}
          </Typography>

          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {choice.laedt ? (
            <LoadingBlock label={t("einstellungen.laden")} />
          ) : null}

          {choice.value === null && !choice.laedt ? (
            <Alert severity="warning" sx={{ mb: 2 }}>
              {t("einstellungen.nichtAbrufbar")}
            </Alert>
          ) : null}

          <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
            {SCHALTER.map((entry) => (
              <ConsentSwitch
                key={entry.key}
                label={t(`einstellungen.${entry.name}Label`)}
                hint={t(`einstellungen.${entry.name}Hinweis`)}
                checked={werte[entry.key]}
                disabled={saving || unbekannt}
                onChange={(neu) => void umschalten(entry.key, neu)}
              />
            ))}
          </Box>

          <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
            {t("einstellungen.takt")}
          </Typography>
        </CardContent>
      </Card>

      <Darstellungsblock />
      <Zivilidentitaet />
      <Datenschutzblock />
      <KiBlock />
      <Nachweisblock />
    </PageShell>
  );
}

/**
 * Darstellung und Sprache — die ausführliche Fassung.
 *
 * <strong>Im Kontomenü steht die knappe („Darstellung ▸ Dunkel"), hier die
 * ganze.</strong> Ein Menü ist eine Liste von Wegen; ein Abschnitt mit
 * Überschrift, erklärendem Satz und Auswahlfeld ist eine Seite. Beides in
 * dasselbe Menü zu legen stapelte zwei Bedienarten übereinander — Einträge, die
 * einen wegbringen, und Felder, die einen dabehalten — und liess die Vorlieben
 * ausgerechnet unter „Abmelden" landen.
 *
 * Beide Fassungen lesen dieselbe Quelle (`useVorlieben`), damit die eine nicht
 * beim nächsten Eintrag eine Möglichkeit kennt, die die andere nicht hat.
 */
function Darstellungsblock() {
  const { t } = useTranslation();

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 2.5 }}>
          {t("einstellungen.darstellungTitel")}
        </Typography>
        <DarstellungsAbschnitte />
      </CardContent>
    </Card>
  );
}

/** Wie lange das Konto ohne Anmeldung bestehen bleibt. */
function Datenschutzblock() {
  const { t } = useTranslation();
  const stand = useAsync((signal) => ladeEinstellungen(signal), []);
  const [saved, setSaved] = useState(false);

  const einstellungen = stand.value ?? null;

  async function setze(monate: number | null) {
    if (einstellungen === null) return;
    const result = await speichereEinstellungen({ ...einstellungen, deleteAfterMonths: monate });
    if (result.ok) {
      stand.setze(result.einstellungen);
      setSaved(true);
    }
  }

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 2 }}>
          {t("einstellungen.datenschutz")}
        </Typography>

        {stand.laedt ? <LoadingBlock label={t("einstellungen.laden")} /> : null}

        {einstellungen !== null ? (
          <>
            <TextField
              select
              label={t("einstellungen.verfall")}
              value={String(einstellungen.deleteAfterMonths ?? "")}
              helperText={t("einstellungen.verfallHinweis")}
              onChange={(event) =>
                void setze(event.target.value === "" ? null : Number(event.target.value))
              }
              sx={{ maxWidth: 360 }}
            >
              <MenuItem value="">{t("einstellungen.verfallNie")}</MenuItem>
              {[6, 12, 24, 36].map((monate) => (
                <MenuItem key={monate} value={monate}>
                  {t("einstellungen.verfallMonate", { count: monate })}
                </MenuItem>
              ))}
            </TextField>

            {saved ? (
              <Alert severity="success" role="status" sx={{ mt: 2 }}>
                {t("einstellungen.gespeichertKurz")}
              </Alert>
            ) : null}
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}

/**
 * Wer gefragt werden darf — und der Schlüssel, der nur hineingeht.
 *
 * <strong>Der Schlüssel hat ein eigenes Feld und einen eigenen Knopf.</strong>
 * Er kommt nie zurück, kann also gar nicht im Formular stehen; ihn zusammen mit
 * dem Rest zu speichern hiesse, ihn bei jedem Speichern erneut über die Leitung
 * zu schicken.
 *
 * <strong>Ein eigener Server braucht keinen</strong>, und dann verschwindet das
 * Feld — statt leer dazustehen und eine Pflicht anzudeuten, die es nicht gibt.
 * Das ist zugleich die einzige Wahl, bei der der Text die Maschine nicht
 * verlässt.
 */
function KiBlock() {
  const { t } = useTranslation();
  const stand = useAsync((signal) => ladeEinstellungen(signal), []);
  const [schluessel, setSchluessel] = useState("");
  const [saved, setSaved] = useState(false);

  const einstellungen = stand.value ?? null;

  async function setze(teil: Partial<Kontoeinstellungen>) {
    if (einstellungen === null) return;
    const result = await speichereEinstellungen({ ...einstellungen, ...teil });
    if (result.ok) {
      stand.setze(result.einstellungen);
      setSaved(true);
    }
  }

  async function speichereDenSchluessel() {
    if (await speichereSchluessel(schluessel)) {
      setSchluessel("");
      stand.again();
      setSaved(true);
    }
  }

  const eigenerServer = einstellungen?.provider === "openai_compatible";

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("einstellungen.ki")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("einstellungen.kiLead")}
        </Typography>

        {stand.laedt ? <LoadingBlock label={t("einstellungen.laden")} /> : null}

        {einstellungen !== null ? (
          <Box sx={{ display: "grid", gap: 2.5 }}>
            <TextField
              select
              label={t("einstellungen.anbieter")}
              value={einstellungen.provider ?? "none"}
              helperText={t("einstellungen.anbieterHinweis")}
              onChange={(event) => void setze({ provider: event.target.value as KiAnbieter })}
            >
              <MenuItem value="none">{t("einstellungen.anbieterKeiner")}</MenuItem>
              <MenuItem value="openai_compatible">
                {t("einstellungen.anbieterOllama")}
              </MenuItem>
              <MenuItem value="anthropic">{t("einstellungen.anbieterAnthropic")}</MenuItem>
            </TextField>

            {einstellungen.provider !== "none" ? (
              <>
                <TextField
                  label={t("einstellungen.adresse")}
                  helperText={t("einstellungen.adresseHinweis")}
                  value={einstellungen.baseUrl ?? ""}
                  onChange={(event) =>
                    stand.setze({ ...einstellungen, baseUrl: event.target.value })
                  }
                  onBlur={() => void setze({ baseUrl: einstellungen.baseUrl ?? "" })}
                />
                <TextField
                  label={t("einstellungen.modell")}
                  helperText={t("einstellungen.modellHinweis")}
                  value={einstellungen.model ?? ""}
                  onChange={(event) =>
                    stand.setze({ ...einstellungen, model: event.target.value })
                  }
                  onBlur={() => void setze({ model: einstellungen.model ?? "" })}
                />

                {!eigenerServer ? (
                  <Box>
                    <TextField
                      label={t("einstellungen.schluessel")}
                      type="password"
                      autoComplete="off"
                      value={schluessel}
                      onChange={(event) => setSchluessel(event.target.value)}
                      helperText={t("einstellungen.schluesselHinweis")}
                    />
                    <Box sx={{ display: "flex", gap: 1.5, mt: 1.5, alignItems: "center" }}>
                      <Button variant="outlined" onClick={() => void speichereDenSchluessel()}>
                        {t("einstellungen.schluesselSpeichern")}
                      </Button>
                      <Typography variant="body2" color="text.secondary">
                        {einstellungen.keyPresent
                          ? t("einstellungen.schluesselDa", { endung: einstellungen.keyTail })
                          : t("einstellungen.schluesselKeiner")}
                      </Typography>
                    </Box>
                  </Box>
                ) : null}

                <ConsentSwitch
                  label={t("einstellungen.kiProtokoll")}
                  hint={t("einstellungen.kiProtokollHinweis")}
                  checked={einstellungen.auditLog === true}
                  onChange={(next) => void setze({ auditLog: next })}
                />
              </>
            ) : null}

            {saved ? (
              <Alert severity="success" role="status">
                {t("einstellungen.gespeichertKurz")}
              </Alert>
            ) : null}
          </Box>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** Die Wege zu dem, was über einen festgehalten ist. */
function Nachweisblock() {
  const { t } = useTranslation();

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("einstellungen.nachweise")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("einstellungen.nachweiseLead")}
        </Typography>
        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          <Button component={RouterLink} to="/consents" variant="outlined">
            {t("einstellungen.nachweiseFreigaben")}
          </Button>
          <Button component={RouterLink} to="/my-data" variant="outlined">
            {t("einstellungen.nachweiseDaten")}
          </Button>
          {/* DIE LÖSCHUNG GEHÖRT IN DIE EINSTELLUNGEN, und das ist keine
              Geschmacksfrage: es ist die Stelle, an der Menschen ihr Konto
              verwalten und an der sie danach suchen. Sie stand einmal nur im
              Kontomenü zwischen „Profil" und „Abmelden" — im Menü, das man
              täglich öffnet, neben dem Weg, den man täglich geht.
              Umrandet in der Warnfarbe: auffindbar, ohne die naheliegende
              Handlung zu sein. */}
          <Button
            component={RouterLink}
            to="/delete-account"
            variant="outlined"
            color="error"
          >
            {t("kopf.kontoLoeschen")}
          </Button>
        </Box>

        {/* Was NICHT geht, steht auch da. Eine Einstellungsseite, die eine
            Lücke verschweigt, sieht vollständig aus und ist es nicht. */}
        <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
          <strong>{t("einstellungen.firma")}.</strong> {t("einstellungen.firmaLead")}
        </Typography>
      </CardContent>
    </Card>
  );
}

