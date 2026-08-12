export interface SkeletonProps {
  /** Wie viele Balken. Standard: drei. */
  lines?: number;
  className?: string;
}

/**
 * Ein Platzhalter, solange geladen wird — reine Dekoration.
 *
 * `aria-hidden`, und zwar zwingend: ein sichtbares Skeleton ohne Ansage ist
 * für einen Screenreader ein stummer Bildschirm. Es gehört deshalb IMMER neben
 * ein `Loading` oder eine `LiveRegion`, die sagt, was lädt. Kein `role`, kein
 * `aria-label` — dann wäre die Dekoration plötzlich Inhalt.
 *
 * Kein Fortschrittsbalken: dieses Bauteil weiß nicht, wie weit etwas ist, und
 * über einen Menschen darf es das ohnehin nie sagen (ADR-0022).
 */
export function Skeleton({ lines = 3, className }: SkeletonProps) {
  return (
    <div className={["wt-skeleton", className].filter(Boolean).join(" ")} aria-hidden="true">
      {Array.from({ length: lines }, (_unused, index) => (
        <span className="wt-skeleton__bar" key={index} />
      ))}
    </div>
  );
}
