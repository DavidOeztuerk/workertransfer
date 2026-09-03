import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import {
  EMPLOYMENT_TYPES,
  type EmploymentType,
  REMOTE_MODES,
  type RemoteMode,
  createJob,
  draftJobText,
  employmentLabel,
  remoteLabel,
} from "../../work/api/jobs";

interface Entwurf {
  title: string;
  description: string;
  location: string;
  remote: RemoteMode;
  employment: EmploymentType;
  skills: string;
}

const LEER: Entwurf = {
  title: "",
  description: "",
  location: "",
  remote: "none",
  employment: "full_time",
  skills: "",
};

const faehigkeiten = (raw: string): string[] =>
  raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");

/**
 * <c>/company/jobs/new</c> — eine Anzeige anlegen.
 *
 * <strong>Sie entsteht als Entwurf.</strong> Veröffentlicht wird sie erst in
 * der Liste, mit einem eigenen Klick — was hier entsteht, sieht niemand ausser
 * dem Unternehmen.
 */
export function CompanyJobNewPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { fuerFirma } = useHandelnder();

  const [draft, setEntwurf] = useState<Entwurf>(LEER);
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  const back = (
    <Link component={RouterLink} to="/company/jobs" variant="body2">
      {t("firmenstellen.zurueck")}
    </Link>
  );

  if (!fuerFirma) {
    return (
      <PageShell title={t("firmenstellen.neu")} narrow>
        <Box sx={{ mb: 2 }}>{back}</Box>
        <Card>
          <CardContent>
            <Typography>
              {t("firmenstellen.neuNurFirma")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function anlegen() {
    setLaeuft(true);
    const result = await createJob({
      title: draft.title,
      description: draft.description,
      location: draft.location,
      remote: draft.remote,
      employment: draft.employment,
      skills: faehigkeiten(draft.skills),
    });
    setLaeuft(false);

    if (result.ok) void navigate("/company/jobs");
    else setFehler(result.error.detail);
  }

  return (
    <PageShell title={t("firmenstellen.neu")} narrow>
      <Box sx={{ mb: 2 }}>{back}</Box>

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
              void anlegen();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("firmenstellen.feldTitel")}
              value={draft.title}
              onChange={(e) =>
                setEntwurf({ ...draft, title: e.target.value })
              }
              required
            />
            <TextField
              label={t("firmenstellen.beschreibung")}
              value={draft.description}
              onChange={(e) =>
                setEntwurf({ ...draft, description: e.target.value })
              }
              multiline
              minRows={5}
            />
            <TextField
              label={t("firmenstellen.ort")}
              helperText={t("firmenstellen.ortHinweis")}
              value={draft.location}
              onChange={(e) =>
                setEntwurf({ ...draft, location: e.target.value })
              }
            />
            <TextField
              select
              label={t("firmenstellen.arbeitsform")}
              value={draft.remote}
              onChange={(e) =>
                setEntwurf({ ...draft, remote: e.target.value as RemoteMode })
              }
            >
              {REMOTE_MODES.map((value) => (
                <MenuItem key={value} value={value}>
                  {remoteLabel(value)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label={t("firmenstellen.beschaeftigung")}
              value={draft.employment}
              onChange={(e) =>
                setEntwurf({
                  ...draft,
                  employment: e.target.value as EmploymentType,
                })
              }
            >
              {EMPLOYMENT_TYPES.map((value) => (
                <MenuItem key={value} value={value}>
                  {employmentLabel(value)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label={t("firmenstellen.faehigkeiten")}
              helperText={t("firmenstellen.faehigkeitenHinweis")}
              value={draft.skills}
              onChange={(e) =>
                setEntwurf({ ...draft, skills: e.target.value })
              }
            />

            <Formulierungshilfe
              draft={draft}
              onVorschlag={(text) =>
                setEntwurf({ ...draft, description: text })
              }
            />

            <Box>
              <Button type="submit" variant="contained" disabled={running}>
                {running
                  ? t("firmenstellen.anlegenLaeuft")
                  : t("firmenstellen.anlegen")}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}

/**
 * Die Formulierungshilfe.
 *
 * <strong>Sie hilft dem Unternehmen, SEINEN EIGENEN Text zu schreiben</strong> —
 * sie sagt nie etwas über einen Menschen. Genau das macht sie baubar, wo ein
 * Kandidatenranking es nicht wäre.
 *
 * <strong><c>type="button"</c> ausgeschrieben</strong>, obwohl MUI es ohnehin so
 * vorgibt: dieser Knopf steht im selben Formular wie „Entwurf anlegen", und wer
 * hier liest, soll nicht erst die Bibliothek aufschlagen müssen, um zu wissen,
 * welcher der beiden absendet. Änderte sich die Vorgabe, legte dieser Knopf
 * sonst die Stelle an.
 */
function Formulierungshilfe({
  draft,
  onVorschlag,
}: {
  draft: Entwurf;
  onVorschlag: (text: string) => void;
}) {
  const { t } = useTranslation();
  const [wish, setWunsch] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  const hatText = draft.description.trim() !== "";

  return (
    <Box sx={{ p: 2, borderRadius: 2, bgcolor: "action.hover" }}>
      <TextField
        label={t(
          hatText ? "firmenstellen.hilfeUmformulieren" : "firmenstellen.hilfeNeu",
        )}
        helperText={t("firmenstellen.hilfeHinweis")}
        value={wish}
        onChange={(e) => setWunsch(e.target.value)}
        slotProps={{ htmlInput: { maxLength: 200 } }}
        sx={{ mb: 1.5 }}
      />

      {problem !== null ? (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {problem}
        </Alert>
      ) : null}

      <Button
        type="button"
        variant="outlined"
        size="small"
        disabled={running}
        onClick={() => {
          setLaeuft(true);
          void draftJobText({
            title: draft.title,
            description: draft.description,
            location: draft.location,
            skills: faehigkeiten(draft.skills),
            wish: wish,
          }).then((result) => {
            setLaeuft(false);
            if (result.ok) {
              setProblem(null);
              onVorschlag(result.draft);
            } else {
              setProblem(result.error.detail);
            }
          });
        }}
      >
        {running
          ? t("firmenstellen.hilfeLaeuft")
          : t(
              hatText
                ? "firmenstellen.hilfeKnopfErsetzt"
                : "firmenstellen.hilfeKnopf",
            )}
      </Button>
    </Box>
  );
}
