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
  EMPTY_POSTAL_ADDRESS,
  getPostalAddress,
  saveLegalName,
  savePostalAddress,
} from "../api/civilIdentity";
import { failureKey } from "../../../shared/api/errors";

/**
 * Legal name and postal address for applications — one source for profile and
 * settings.
 *
 * The matching location on the profile is something else: it is free text,
 * visible after release, and never a street (ADR-0038).
 */
export function CivilIdentity() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const session = useAppSelector((state) => state.auth.session);
  const address = useAsync((signal) => getPostalAddress(signal), [session?.userId]);

  const [givenName, setGivenName] = useState(session?.givenName ?? "");
  const [familyName, setFamilyName] = useState(session?.familyName ?? "");
  const [form, setForm] = useState(EMPTY_POSTAL_ADDRESS);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setGivenName(session?.givenName ?? "");
    setFamilyName(session?.familyName ?? "");
  }, [session?.givenName, session?.familyName]);

  useEffect(() => {
    if (address.value) setForm(address.value);
  }, [address.value]);

  async function save() {
    setSaving(true);
    setError(null);
    const name = await saveLegalName(givenName, familyName);
    const postal = await savePostalAddress(form);
    setSaving(false);
    // A catalogue text, never identity-service's sentence (English on the wire).
    if (!name.ok) {
      setError(t(failureKey(name.error, "errors.settingsNotSaved")));
      return;
    }
    if (!postal.ok) {
      setError(t(failureKey(postal.error, "errors.settingsNotSaved")));
      return;
    }
    setForm(postal.address);
    setSaved(true);
    void dispatch(loadSession());
  }

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("civilIdentity.title")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("civilIdentity.purpose")}
        </Typography>

        {address.loading ? <LoadingBlock label={t("common.loading")} /> : null}

        <Box
          component="form"
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
          sx={{ display: "grid", gap: 2 }}
        >
          <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 1fr" } }}>
            <TextField
              label={t("civilIdentity.firstName")}
              autoComplete="given-name"
              value={givenName}
              onChange={(event) => setGivenName(event.target.value)}
              fullWidth
            />
            <TextField
              label={t("civilIdentity.lastName")}
              autoComplete="family-name"
              value={familyName}
              onChange={(event) => setFamilyName(event.target.value)}
              fullWidth
            />
          </Box>
          <Typography variant="body2" color="text.secondary">
            {t("civilIdentity.legalNameHint")}
          </Typography>

          <TextField
            label={t("civilIdentity.street")}
            autoComplete="address-line1"
            value={form.line1}
            onChange={(event) => setForm({ ...form, line1: event.target.value })}
            fullWidth
          />
          <TextField
            label={t("civilIdentity.addressLine2")}
            autoComplete="address-line2"
            value={form.line2}
            onChange={(event) => setForm({ ...form, line2: event.target.value })}
            fullWidth
          />
          <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 2fr 1fr" } }}>
            <TextField
              label={t("civilIdentity.postalCode")}
              autoComplete="postal-code"
              value={form.postalCode}
              onChange={(event) => setForm({ ...form, postalCode: event.target.value })}
            />
            <TextField
              label={t("civilIdentity.city")}
              autoComplete="address-level2"
              value={form.city}
              onChange={(event) => setForm({ ...form, city: event.target.value })}
            />
            <TextField
              label={t("civilIdentity.country")}
              autoComplete="country"
              value={form.country}
              onChange={(event) => setForm({ ...form, country: event.target.value })}
              slotProps={{ htmlInput: { maxLength: 2 } }}
            />
          </Box>
          <TextField
            label={t("civilIdentity.phone")}
            autoComplete="tel"
            helperText={t("civilIdentity.phoneHint")}
            value={form.phone}
            onChange={(event) => setForm({ ...form, phone: event.target.value })}
            fullWidth
          />

          {error !== null ? <Alert severity="error">{error}</Alert> : null}
          {saved ? (
            <Alert severity="success" role="status">
              {t("civilIdentity.saved")}
            </Alert>
          ) : null}

          <Button type="submit" variant="contained" disabled={saving}>
            {saving ? t("common.saving") : t("civilIdentity.save")}
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}
