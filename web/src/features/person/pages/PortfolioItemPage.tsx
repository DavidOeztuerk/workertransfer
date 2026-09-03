import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate, useParams } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import {
  type Arbeit,
  haengeAn,
  ladeMeines,
  speichereMeines,
} from "../api/portfolio";

interface Entwurf {
  title: string;
  summary: string;
  url: string;
  role: string;
  year: string;
  attachment: string | null;
}

const LEER: Entwurf = {
  title: "",
  summary: "",
  url: "",
  role: "",
  year: "",
  attachment: null,
};

const zuEntwurf = (arbeit: Arbeit): Entwurf => ({
  title: arbeit.title,
  summary: arbeit.summary,
  url: arbeit.url ?? "",
  role: arbeit.role,
  year: arbeit.year === null ? "" : String(arbeit.year),
  attachment: arbeit.attachment,
});

/** Leere Felder werden `null`, nicht `""` — „nicht angegeben" ist kein leerer Wert. */
function zuArbeit(entwurf: Entwurf): Arbeit {
  const jahr = entwurf.year.trim();

  return {
    title: entwurf.title.trim(),
    summary: entwurf.summary.trim(),
    url: entwurf.url.trim() === "" ? null : entwurf.url.trim(),
    role: entwurf.role.trim(),
    year: jahr === "" ? null : Number(jahr),
    attachment: entwurf.attachment,
  };
}

/**
 * <c>/portfolio/new</c> und <c>/portfolio/:stelle</c> — eine Arbeit anlegen
 * oder ändern.
 *
 * <strong>Eine Arbeit hat keine eigene Kennung.</strong> Ihre Adresse ist ihre
 * Stelle in der gespeicherten Liste, und Speichern heisst deshalb: das ganze
 * Feld schicken, mit genau einem geänderten Eintrag. Wer das übersieht, löscht
 * beim Speichern die übrigen.
 *
 * <strong>Das Entfernen steht HIER und nicht in der Liste.</strong> Hier hat die
 * Person die Arbeit vor Augen, um die es geht — in einer Liste träfe der Klick
 * eine Zeile unter vielen.
 */
export function PortfolioItemPage() {
  const { t } = useTranslation();
  const { stelle } = useParams();
  const navigate = useNavigate();
  const status = useAppSelector((state) => state.auth.status);
  const sitzung = useAppSelector((state) => state.auth.session);

  const neu = stelle === undefined;
  const schaufenster = useAsync(
    (signal) => ladeMeines(signal),
    [sitzung?.userId],
    sitzung !== null,
  );

  const [entwurf, setEntwurf] = useState<Entwurf>(LEER);
  const [geladen, setGeladen] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const arbeiten = schaufenster.wert?.ok
    ? (schaufenster.wert.wert?.items ?? [])
    : [];
  const position = neu ? arbeiten.length : Number(stelle);
  const vorhanden =
    !neu &&
    Number.isInteger(position) &&
    position >= 0 &&
    position < arbeiten.length;

  useEffect(() => {
    if (geladen || !schaufenster.wert?.ok) return;
    if (vorhanden) setEntwurf(zuEntwurf(arbeiten[position] as Arbeit));
    setGeladen(true);
  }, [geladen, schaufenster.wert, vorhanden, arbeiten, position]);

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel={t("arbeiten.einzelTitel")}
        satz="arbeiten.anmelden"
      />
    );
  }

  if (schaufenster.laedt) {
    return (
      <PageShell title={t("arbeiten.einzelTitel")} narrow>
        <LoadingBlock label={t("arbeiten.einzelLaden")} />
      </PageShell>
    );
  }

  // Eine Adresse, hinter der nichts steht — etwa nach dem Entfernen in einem
  // zweiten Tab, oder wenn jemand die Zahl von Hand ändert.
  if (!neu && !vorhanden) {
    return (
      <PageShell title={t("arbeiten.einzelFehltTitel")} narrow>
        <EmptyBlock
          title={t("arbeiten.einzelFehltTitel")}
          hint={t("arbeiten.einzelFehltHinweis")}
          action={
            <Button component={RouterLink} to="/portfolio" variant="contained">
              {t("arbeiten.zurueck")}
            </Button>
          }
        />
      </PageShell>
    );
  }

  function aendere(teil: Partial<Entwurf>) {
    setEntwurf((vorher) => ({ ...vorher, ...teil }));
    setFehler(null);
  }

  async function speichern() {
    const naechste = [...arbeiten];
    if (neu) naechste.push(zuArbeit(entwurf));
    else naechste[position] = zuArbeit(entwurf);

    setLaeuft(true);
    const ergebnis = await speichereMeines(naechste);
    setLaeuft(false);

    if (ergebnis.ok) void navigate("/portfolio");
    else setFehler(ergebnis.error.detail);
  }

  async function entfernen() {
    setLaeuft(true);
    const ergebnis = await speichereMeines(
      arbeiten.filter((_, i) => i !== position),
    );
    setLaeuft(false);

    if (ergebnis.ok) void navigate("/portfolio");
    else setFehler(ergebnis.error.detail);
  }

  async function dateiWaehlen(datei: File) {
    setLaeuft(true);
    const ergebnis = await haengeAn(datei);
    setLaeuft(false);

    if (ergebnis.ok) aendere({ attachment: ergebnis.wert.name });
    else setFehler(ergebnis.error.detail);
  }

  return (
    <PageShell
      title={
        neu
          ? t("arbeiten.einzelNeu")
          : entwurf.title !== ""
            ? entwurf.title
            : t("arbeiten.einzelTitel")
      }
      narrow
    >
      <Box sx={{ mb: 2 }}>
        <Link component={RouterLink} to="/portfolio" variant="body2">
          {t("arbeiten.zurueck")}
        </Link>
      </Box>

      <Card>
        <CardContent>
          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
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
              label={t("arbeiten.feldTitel")}
              value={entwurf.title}
              onChange={(e) => aendere({ title: e.target.value })}
              required
            />
            <TextField
              label={t("arbeiten.feldWorum")}
              value={entwurf.summary}
              onChange={(e) => aendere({ summary: e.target.value })}
              helperText={t("arbeiten.feldWorumHinweis")}
              multiline
              minRows={3}
            />
            <TextField
              label={t("arbeiten.feldLink")}
              value={entwurf.url}
              onChange={(e) => aendere({ url: e.target.value })}
              helperText={t("arbeiten.feldLinkHinweis")}
            />
            <TextField
              label={t("arbeiten.feldRolle")}
              value={entwurf.role}
              onChange={(e) => aendere({ role: e.target.value })}
            />
            <TextField
              label={t("arbeiten.feldJahr")}
              value={entwurf.year}
              onChange={(e) => aendere({ year: e.target.value })}
            />

            <Box>
              <Typography variant="body2" sx={{ mb: 1 }}>
                {t("arbeiten.datei")}
              </Typography>
              {/* Der lokale Dateiname wird NICHT angezeigt: er wandert nicht zum
                  Server, und die Oberfläche soll nur sagen, was wahr ist — dass
                  eine Datei hängt. */}
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                {t(
                  entwurf.attachment !== null
                    ? "arbeiten.dateiHaengtAn"
                    : "arbeiten.keineDatei",
                )}
              </Typography>
              <Button
                component="label"
                variant="outlined"
                size="small"
                disabled={laeuft}
              >
                {t("arbeiten.dateiWaehlen")}
                <input
                  type="file"
                  hidden
                  accept="image/png,image/jpeg,application/pdf"
                  onChange={(ereignis) => {
                    const datei = ereignis.target.files?.[0];
                    // Das Feld zurücksetzen, damit dieselbe Datei erneut
                    // gewählt werden kann.
                    ereignis.target.value = "";
                    if (datei !== undefined) void dateiWaehlen(datei);
                  }}
                />
              </Button>
            </Box>

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button type="submit" variant="contained" disabled={laeuft}>
                {laeuft
                  ? t("allgemein.speichernLaeuft")
                  : t("allgemein.speichern")}
              </Button>
              {!neu ? (
                <Button
                  variant="text"
                  color="error"
                  onClick={() => void entfernen()}
                  disabled={laeuft}
                >
                  {laeuft
                    ? t("arbeiten.entfernenLaeuft")
                    : t("arbeiten.entfernen")}
                </Button>
              ) : null}
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
