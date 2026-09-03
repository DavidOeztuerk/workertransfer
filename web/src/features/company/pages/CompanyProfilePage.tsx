import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { getOwnCompanyProfile, saveCompanyProfile } from "../api/companies";

interface Entwurf {
  display_name: string;
  about: string;
  website: string;
  locations: string;
  benefits: string;
}

const LEER: Entwurf = {
  display_name: "",
  about: "",
  website: "",
  locations: "",
  benefits: "",
};

/** Mit Komma getrennt, Leeres fällt weg — „nichts angegeben" ist keine leere Zeile. */
const list = (raw: string): string[] =>
  raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");

/**
 * <c>/company/profile</c> — wie ein Unternehmen auftritt.
 *
 * <strong>Solange hier nichts steht, bleibt eine Ausschreibung anonym</strong>
 * — Titel und Beschreibung, sonst nichts. Das ist kein Mangel, sondern die
 * Voreinstellung: ein Unternehmen entscheidet selbst, ob es sich zeigt.
 *
 * <strong>Der Stand füllt das Formular genau einmal.</strong> Liefe das bei
 * jeder Antwort, überschriebe ein Neuladen den gerade getippten Text.
 */
export function CompanyProfilePage() {
  const { t } = useTranslation();
  const { fuerFirma, tenantId } = useHandelnder();

  const [draft, setEntwurf] = useState<Entwurf>(LEER);
  const [adopted, setUebernommen] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [saved, setGespeichert] = useState(false);
  const [running, setLaeuft] = useState(false);

  const profile = useAsync(
    (signal) => getOwnCompanyProfile(signal),
    [tenantId],
    fuerFirma,
  );

  useEffect(() => {
    if (adopted || !profile.data?.ok) return;
    const current = profile.data.profile;
    if (current !== null) {
      setEntwurf({
        display_name: current.display_name,
        about: current.about,
        website: current.website ?? "",
        locations: current.locations.join(", "),
        benefits: current.benefits.join(", "),
      });
    }
    setUebernommen(true);
  }, [adopted, profile.data]);

  if (!fuerFirma) {
    return (
      <PageShell title={t("firmenprofil.titel")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("firmenprofil.nurFirma")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function speichern() {
    setLaeuft(true);
    const result = await saveCompanyProfile({
      display_name: draft.display_name,
      about: draft.about,
      website: draft.website.trim() === "" ? null : draft.website.trim(),
      locations: list(draft.locations),
      benefits: list(draft.benefits),
    });
    setLaeuft(false);

    if (result.ok) {
      setFehler(null);
      setGespeichert(true);
    } else {
      setFehler(result.error.detail);
      setGespeichert(false);
    }
  }

  return (
    <PageShell
      title={t("firmenprofil.titel")}
      narrow
      lead={t("firmenprofil.lead")}
    >
      <Card>
        <CardContent>
          {profile.pending ? (
            <LoadingBlock label={t("firmenprofil.laden")} />
          ) : null}

          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {saved ? (
            <Alert severity="success" role="status" sx={{ mb: 2 }}>
              {t("firmenprofil.gespeichert")}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void speichern();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("firmenprofil.anzeigename")}
              helperText={t("firmenprofil.anzeigenameHinweis")}
              value={draft.display_name}
              onChange={(e) => {
                setEntwurf({ ...draft, display_name: e.target.value });
                setGespeichert(false);
              }}
              required
            />
            <TextField
              label={t("firmenprofil.ueberUns")}
              helperText={t("firmenprofil.ueberUnsHinweis")}
              value={draft.about}
              onChange={(e) => {
                setEntwurf({ ...draft, about: e.target.value });
                setGespeichert(false);
              }}
              multiline
              minRows={4}
            />
            <TextField
              label={t("firmenprofil.website")}
              helperText={t("firmenprofil.websiteHinweis")}
              value={draft.website}
              onChange={(e) => {
                setEntwurf({ ...draft, website: e.target.value });
                setGespeichert(false);
              }}
            />
            <TextField
              label={t("firmenprofil.standorte")}
              helperText={t("firmenprofil.standorteHinweis")}
              value={draft.locations}
              onChange={(e) => {
                setEntwurf({ ...draft, locations: e.target.value });
                setGespeichert(false);
              }}
            />
            <TextField
              label={t("firmenprofil.leistungen")}
              helperText={t("firmenprofil.leistungenHinweis")}
              value={draft.benefits}
              onChange={(e) => {
                setEntwurf({ ...draft, benefits: e.target.value });
                setGespeichert(false);
              }}
            />

            <Box>
              <Button type="submit" variant="contained" disabled={running}>
                {running
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
