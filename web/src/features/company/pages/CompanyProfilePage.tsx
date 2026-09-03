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
const liste = (roh: string): string[] =>
  roh
    .split(",")
    .map((eintrag) => eintrag.trim())
    .filter((eintrag) => eintrag !== "");

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

  const [entwurf, setEntwurf] = useState<Entwurf>(LEER);
  const [uebernommen, setUebernommen] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [gespeichert, setGespeichert] = useState(false);
  const [laeuft, setLaeuft] = useState(false);

  const profil = useAsync(
    (signal) => getOwnCompanyProfile(signal),
    [tenantId],
    fuerFirma,
  );

  useEffect(() => {
    if (uebernommen || !profil.data?.ok) return;
    const stand = profil.data.profile;
    if (stand !== null) {
      setEntwurf({
        display_name: stand.display_name,
        about: stand.about,
        website: stand.website ?? "",
        locations: stand.locations.join(", "),
        benefits: stand.benefits.join(", "),
      });
    }
    setUebernommen(true);
  }, [uebernommen, profil.data]);

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
    const ergebnis = await saveCompanyProfile({
      display_name: entwurf.display_name,
      about: entwurf.about,
      website: entwurf.website.trim() === "" ? null : entwurf.website.trim(),
      locations: liste(entwurf.locations),
      benefits: liste(entwurf.benefits),
    });
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setGespeichert(true);
    } else {
      setFehler(ergebnis.error.detail);
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
          {profil.pending ? (
            <LoadingBlock label={t("firmenprofil.laden")} />
          ) : null}

          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {gespeichert ? (
            <Alert severity="success" role="status" sx={{ mb: 2 }}>
              {t("firmenprofil.gespeichert")}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              void speichern();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("firmenprofil.anzeigename")}
              helperText={t("firmenprofil.anzeigenameHinweis")}
              value={entwurf.display_name}
              onChange={(e) => {
                setEntwurf({ ...entwurf, display_name: e.target.value });
                setGespeichert(false);
              }}
              required
            />
            <TextField
              label={t("firmenprofil.ueberUns")}
              helperText={t("firmenprofil.ueberUnsHinweis")}
              value={entwurf.about}
              onChange={(e) => {
                setEntwurf({ ...entwurf, about: e.target.value });
                setGespeichert(false);
              }}
              multiline
              minRows={4}
            />
            <TextField
              label={t("firmenprofil.website")}
              helperText={t("firmenprofil.websiteHinweis")}
              value={entwurf.website}
              onChange={(e) => {
                setEntwurf({ ...entwurf, website: e.target.value });
                setGespeichert(false);
              }}
            />
            <TextField
              label={t("firmenprofil.standorte")}
              helperText={t("firmenprofil.standorteHinweis")}
              value={entwurf.locations}
              onChange={(e) => {
                setEntwurf({ ...entwurf, locations: e.target.value });
                setGespeichert(false);
              }}
            />
            <TextField
              label={t("firmenprofil.leistungen")}
              helperText={t("firmenprofil.leistungenHinweis")}
              value={entwurf.benefits}
              onChange={(e) => {
                setEntwurf({ ...entwurf, benefits: e.target.value });
                setGespeichert(false);
              }}
            />

            <Box>
              <Button type="submit" variant="contained" disabled={laeuft}>
                {laeuft
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
