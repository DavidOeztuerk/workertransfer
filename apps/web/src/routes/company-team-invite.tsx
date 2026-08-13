import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Field, Loading, Page, Select } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import { type Role, inviteMember, listMembers } from "../auth/team";

export interface CompanyTeamInviteRouteProps {
  principal?: MeResponse | null;
}

/**
 * Jemanden ins Unternehmen einladen — auf einer eigenen Adresse.
 *
 * Die Rollenprüfung wandert mit: **nur Administratoren dürfen einladen**, und
 * das steht hier genauso wie vorher auf der Mannschaftsseite. Ein abgespaltenes
 * Formular, das die Bedingung zurücklässt, wäre eine Tür neben der verschlossenen.
 *
 * Die eigene Rolle steht in der Mitgliederliste; deshalb fragt auch diese Seite
 * sie ab. Sie aus der Kopfzeile zu nehmen wäre bequemer und falsch — dort steht,
 * was der Browser glaubt, nicht was der Server weiß.
 */
export function CompanyTeamInviteRoute({ principal = null }: CompanyTeamInviteRouteProps) {
  const queryClient = useQueryClient();
  const tenantId = principal?.tenant_id ?? null;

  const membersQuery = useQuery({
    queryKey: ["team", "members", tenantId],
    queryFn: () => listMembers(tenantId as string),
    enabled: tenantId !== null,
  });

  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("member");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const invite = useMutation({
    mutationFn: () => inviteMember(tenantId as string, email, role),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        // Bewusst dieselbe Meldung, ob die Adresse ein Konto hat oder nicht:
        // der Server antwortet in beiden Fällen gleich, und ein Unterschied
        // hier wäre genau das Leck, das er vermeidet.
        setSent(true);
        setEmail("");
        void queryClient.invalidateQueries({ queryKey: ["team", "invitations", tenantId] });
      } else {
        setSent(false);
        setError(result.message);
      }
    },
  });

  const zurueck = <a href="/company/team">Zurück zur Mannschaft</a>;
  const members = membersQuery.data;
  const isAdmin =
    members?.ok === true &&
    members.members.find((entry) => entry.user_id === principal?.user_id)?.role === "admin";

  if (tenantId === null) {
    return (
      <Page title="Einladen" narrow back={zurueck}>
        <Card>
          <p>
            Wähle oben ein Unternehmen — oder lass dich von jemandem aus deinem Unternehmen
            einladen.
          </p>
        </Card>
      </Page>
    );
  }

  if (membersQuery.isPending) {
    return (
      <Page title="Einladen" narrow back={zurueck}>
        <Card>
          <Loading label="Mitglieder werden geladen…" />
        </Card>
      </Page>
    );
  }

  if (!isAdmin) {
    return (
      <Page title="Einladen" narrow back={zurueck}>
        <Card>
          <p>
            Einladen dürfen nur Administratoren. Wende dich an jemanden aus deinem Unternehmen, der
            das ist.
          </p>
        </Card>
      </Page>
    );
  }

  return (
    <Page
      title="Einladen"
      narrow
      back={zurueck}
      lead="Wer die Einladung annimmt, kann für das Unternehmen handeln — Profile sehen, Lebensläufe anfragen."
    >
      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            invite.mutate();
          }}
        >
          <Field
            label="E-Mail-Adresse"
            type="email"
            hint="Die Person braucht ein Konto mit genau dieser Adresse — sie kann es auch nach der Einladung anlegen."
            value={email}
            onChange={(e) => {
              setSent(false);
              setEmail(e.target.value);
            }}
            required
          />
          <Select label="Rolle" value={role} onChange={(e) => setRole(e.target.value as Role)}>
            <option value="member">Mitglied</option>
            <option value="admin">Administrator</option>
          </Select>

          {error !== null ? <Alert>{error}</Alert> : null}
          {sent && error === null ? (
            <Alert variant="notice">Einladung verschickt.</Alert>
          ) : null}

          <Button type="submit" disabled={invite.isPending}>
            {invite.isPending ? "Wird verschickt…" : "Einladen"}
          </Button>
        </form>
      </Card>
    </Page>
  );
}
