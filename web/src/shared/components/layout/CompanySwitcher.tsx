import { useEffect } from "react";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";

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
      size="small"
      label="Handeln als"
      value={tenantId ?? ""}
      onChange={(event) => {
        const gewaehlt = event.target.value;
        if (gewaehlt !== "") void dispatch(actForCompany(gewaehlt));
      }}
      sx={{ minWidth: 200, display: { xs: "none", md: "block" } }}
    >
      <MenuItem value="" disabled>
        Ich selbst (Abmelden nötig)
      </MenuItem>
      {memberships.map((firma) => (
        <MenuItem key={firma.id} value={firma.id}>
          {firma.name}
        </MenuItem>
      ))}
    </TextField>
  );
}
