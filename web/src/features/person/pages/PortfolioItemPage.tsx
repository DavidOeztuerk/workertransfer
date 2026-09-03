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
function zuArbeit(draft: Entwurf): Arbeit {
  const jahr = draft.year.trim();

  return {
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    url: draft.url.trim() === "" ? null : draft.url.trim(),
    role: draft.role.trim(),
    year: jahr === "" ? null : Number(jahr),
    attachment: draft.attachment,
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
  const session = useAppSelector((state) => state.auth.session);

  const neu = stelle === undefined;
  const showcase = useAsync(
    (signal) => ladeMeines(signal),
    [session?.userId],
    session !== null,
  );

  const [draft, setEntwurf] = useState<Entwurf>(LEER);
  const [loaded, setGeladen] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  const arbeiten = showcase.value?.ok
    ? (showcase.value.value?.items ?? [])
    : [];
  const position = neu ? arbeiten.length : Number(stelle);
  const vorhanden =
    !neu &&
    Number.isInteger(position) &&
    position >= 0 &&
    position < arbeiten.length;

  useEffect(() => {
    if (loaded || !showcase.value?.ok) return;
    if (vorhanden) setEntwurf(zuEntwurf(arbeiten[position] as Arbeit));
    setGeladen(true);
  }, [loaded, showcase.value, vorhanden, arbeiten, position]);

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel={t("arbeiten.einzelTitel")}
        satz="arbeiten.anmelden"
      />
    );
  }

  if (showcase.laedt) {
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

  function change(teil: Partial<Entwurf>) {
    setEntwurf((before) => ({ ...before, ...teil }));
    setFehler(null);
  }

  async function speichern() {
    const nextValue = [...arbeiten];
    if (neu) nextValue.push(zuArbeit(draft));
    else nextValue[position] = zuArbeit(draft);

    setLaeuft(true);
    const result = await speichereMeines(nextValue);
    setLaeuft(false);

    if (result.ok) void navigate("/portfolio");
    else setFehler(result.error.detail);
  }

  async function entfernen() {
    setLaeuft(true);
    const result = await speichereMeines(
      arbeiten.filter((_, i) => i !== position),
    );
    setLaeuft(false);

    if (result.ok) void navigate("/portfolio");
    else setFehler(result.error.detail);
  }

  async function dateiWaehlen(file: File) {
    setLaeuft(true);
    const result = await haengeAn(file);
    setLaeuft(false);

    if (result.ok) change({ attachment: result.value.name });
    else setFehler(result.error.detail);
  }

  return (
    <PageShell
      title={
        neu
          ? t("arbeiten.einzelNeu")
          : draft.title !== ""
            ? draft.title
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
            onSubmit={(event) => {
              event.preventDefault();
              void speichern();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("arbeiten.feldTitel")}
              value={draft.title}
              onChange={(e) => change({ title: e.target.value })}
              required
            />
            <TextField
              label={t("arbeiten.feldWorum")}
              value={draft.summary}
              onChange={(e) => change({ summary: e.target.value })}
              helperText={t("arbeiten.feldWorumHinweis")}
              multiline
              minRows={3}
            />
            <TextField
              label={t("arbeiten.feldLink")}
              value={draft.url}
              onChange={(e) => change({ url: e.target.value })}
              helperText={t("arbeiten.feldLinkHinweis")}
            />
            <TextField
              label={t("arbeiten.feldRolle")}
              value={draft.role}
              onChange={(e) => change({ role: e.target.value })}
            />
            <TextField
              label={t("arbeiten.feldJahr")}
              value={draft.year}
              onChange={(e) => change({ year: e.target.value })}
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
                  draft.attachment !== null
                    ? "arbeiten.dateiHaengtAn"
                    : "arbeiten.keineDatei",
                )}
              </Typography>
              <Button
                component="label"
                variant="outlined"
                size="small"
                disabled={running}
              >
                {t("arbeiten.dateiWaehlen")}
                <input
                  type="file"
                  hidden
                  accept="image/png,image/jpeg,application/pdf"
                  onChange={(event) => {
                    const file = event.target.files?.[0];
                    // Das Feld zurücksetzen, damit dieselbe Datei erneut
                    // gewählt werden kann.
                    event.target.value = "";
                    if (file !== undefined) void dateiWaehlen(file);
                  }}
                />
              </Button>
            </Box>

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button type="submit" variant="contained" disabled={running}>
                {running
                  ? t("allgemein.speichernLaeuft")
                  : t("allgemein.speichern")}
              </Button>
              {!neu ? (
                <Button
                  variant="text"
                  color="error"
                  onClick={() => void entfernen()}
                  disabled={running}
                >
                  {running
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
