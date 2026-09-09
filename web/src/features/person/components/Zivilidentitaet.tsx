import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";

import { LoadingBlock } from "../../../shared/components/ui";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { loadSession } from "../../auth/store/authThunks";
import { useAsync } from "../lib/useAsync";
import {
  LEERE_ANSCHRIFT,
  ladeAnschrift,
  speichereAnschrift,
  speichereKlarname,
} from "../api/zivilidentitaet";

/**
 * Klarname und Bewerbungsanschrift — eine Quelle für Profil und Einstellungen.
 *
 * Der Matching-Ort auf dem Profil ist etwas anderes: er ist frei, sichtbar
 * nach Freigabe, und nie eine Straße (ADR-0038).
 */
export function Zivilidentitaet() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const session = useAppSelector((state) => state.auth.session);
  const anschrift = useAsync((signal) => ladeAnschrift(signal), [session?.userId]);

  const [vorname, setVorname] = useState(session?.givenName ?? "");
  const [nachname, setNachname] = useState(session?.familyName ?? "");
  const [form, setForm] = useState(LEERE_ANSCHRIFT);
  const [fehler, setFehler] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setVorname(session?.givenName ?? "");
    setNachname(session?.familyName ?? "");
  }, [session?.givenName, session?.familyName]);

  useEffect(() => {
    if (anschrift.value) setForm(anschrift.value);
  }, [anschrift.value]);

  async function speichern() {
    setSaving(true);
    setFehler(null);
    const name = await speichereKlarname(vorname, nachname);
    const adresse = await speichereAnschrift(form);
    setSaving(false);
    if (!name.ok) {
      setFehler(name.detail);
      return;
    }
    if (!adresse.ok) {
      setFehler(adresse.detail);
      return;
    }
    setForm(adresse.anschrift);
    setSaved(true);
    void dispatch(loadSession());
  }

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("zivil.titel")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("zivil.zweck")}
        </Typography>

        {anschrift.laedt ? <LoadingBlock label={t("allgemein.laden")} /> : null}

        <Box
          component="form"
          onSubmit={(event) => {
            event.preventDefault();
            void speichern();
          }}
          sx={{ display: "grid", gap: 2 }}
        >
          <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 1fr" } }}>
            <TextField
              label={t("zivil.vorname")}
              autoComplete="given-name"
              value={vorname}
              onChange={(event) => setVorname(event.target.value)}
              fullWidth
            />
            <TextField
              label={t("zivil.nachname")}
              autoComplete="family-name"
              value={nachname}
              onChange={(event) => setNachname(event.target.value)}
              fullWidth
            />
          </Box>
          <Typography variant="body2" color="text.secondary">
            {t("zivil.klarnameHinweis")}
          </Typography>

          <TextField
            label={t("zivil.strasse")}
            autoComplete="address-line1"
            value={form.line1}
            onChange={(event) => setForm({ ...form, line1: event.target.value })}
            fullWidth
          />
          <TextField
            label={t("zivil.zusatz")}
            autoComplete="address-line2"
            value={form.line2}
            onChange={(event) => setForm({ ...form, line2: event.target.value })}
            fullWidth
          />
          <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 2fr 1fr" } }}>
            <TextField
              label={t("zivil.plz")}
              autoComplete="postal-code"
              value={form.postalCode}
              onChange={(event) => setForm({ ...form, postalCode: event.target.value })}
            />
            <TextField
              label={t("zivil.ort")}
              autoComplete="address-level2"
              value={form.city}
              onChange={(event) => setForm({ ...form, city: event.target.value })}
            />
            <TextField
              label={t("zivil.land")}
              autoComplete="country"
              value={form.country}
              onChange={(event) => setForm({ ...form, country: event.target.value })}
              slotProps={{ htmlInput: { maxLength: 2 } }}
            />
          </Box>
          <TextField
            label={t("zivil.telefon")}
            autoComplete="tel"
            helperText={t("zivil.telefonHinweis")}
            value={form.phone}
            onChange={(event) => setForm({ ...form, phone: event.target.value })}
            fullWidth
          />

          {fehler !== null ? <Alert severity="error">{fehler}</Alert> : null}
          {saved ? (
            <Alert severity="success" role="status">
              {t("zivil.gespeichert")}
            </Alert>
          ) : null}

          <Button type="submit" variant="contained" disabled={saving}>
            {saving ? t("allgemein.speichernLaeuft") : t("zivil.speichern")}
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}
