export interface BadgeProps {
  /** Wie viele Vorgänge warten. Bei 0 oder weniger zeigt das Bauteil nichts. */
  count: number;
  /**
   * Der ganze Satz, den ein Vorleser sagen soll — „3 offene Anfragen".
   *
   * Pflichtfeld: eine nackte Zahl ist für einen Vorleser bedeutungslos, man
   * hört „3" und weiß nicht, drei von was.
   */
  label: string;
  className?: string;
}

/**
 * Ein Zähler an einem Navigationslink.
 *
 * Klein, aber nicht dekorativ: er ist heute der einzige Weg, auf dem eine
 * Anfrage die Person erreicht.
 *
 * **Er zählt Vorgänge, niemals Personen** (ADR-0022). „3 offene Anfragen" ist
 * erlaubt. „Profil 60 % vollständig", „Rang 4 von 12" oder irgendeine Zahl
 * über einen Menschen sind es nicht — dafür ist dieses Bauteil nicht da und
 * darf es nicht werden.
 */
export function Badge({ count, label, className }: BadgeProps) {
  // Eine Null ist keine Nachricht, sondern ein Aufmerksamkeitsanspruch ohne
  // Anlass.
  if (count <= 0) return null;

  return (
    <span className={["wt-badge", className].filter(Boolean).join(" ")} aria-label={label}>
      {count}
    </span>
  );
}
