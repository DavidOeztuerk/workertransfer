import { useEffect } from "react";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { actForCompany, loadMemberships } from "../../../features/auth/store/authThunks";

/**
 * „Handeln als" — Person oder eines der Unternehmen.
 *
 * <strong>Der Client NENNT die Firma, der Server ENTSCHEIDET.</strong> Die
 * Auswahl schickt `POST /auth/company/{id}`; erst dort wird die Mitgliedschaft
 * geprüft und der Mandant ins Token geschrieben (ADR-0018). Was hier steht, ist
 * also nie eine Behauptung über Rechte, sondern eine Bitte.
 *
 * Zurück auf „ich selbst" geht nur über Abmelden, und das ist ehrlich: ein
 * Personentoken lässt sich nicht aus einem Firmentoken herausrechnen, es muss
 * neu ausgestellt werden.
 */
export function CompanySwitcher() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const memberships = useAppSelector((state) => state.auth.memberships);
  const tenantId = useAppSelector((state) => state.auth.session?.tenantId ?? null);

  useEffect(() => {
    void dispatch(loadMemberships());
  }, [dispatch]);

  if (memberships.length === 0) return null;

  return (
    <TextField
      select
      // NATIV, und das ist keine Nebensache: MUIs Vorgabe rendert ein `div`
      // mit `role="button"`, das ein verstecktes Feld fuellt. Ein natives
      // `<select>` ist das, was ein Screenreader als Auswahlliste ansagt, was
      // die Tastatur ohne Zusatzcode bedient, und was `selectOption` in den
      // E2E-Reisen findet. Die MUI-Fassung sieht nur so aus.
      // `inputLabel.shrink`: ein natives Feld zeigt seinen Wert sofort, die
      // schwebende Beschriftung laege sonst darueber.
      slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
      size="small"
      label={t("kopf.handelnAls")}
      value={tenantId ?? ""}
      onChange={(event) => {
        const chosen = event.target.value;
        if (chosen !== "") void dispatch(actForCompany(chosen));
      }}
      sx={{ minWidth: 200, display: { xs: "none", md: "block" } }}
    >
      <option value="" disabled>
        {t("kopf.ichSelbst")}
      </option>
      {memberships.map((firma) => (
        <option key={firma.id} value={firma.id}>
          {firma.name}
        </option>
      ))}
    </TextField>
  );
}
