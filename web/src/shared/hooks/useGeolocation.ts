import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";

/** How precisely a position leaves the house. */
const DECIMALS = 2;

/**
 * A place, as imprecise as it may be.
 *
 * Two decimals are a good kilometre. Finer would not be better, only more: the
 * jobs carry a PLACE NAME, and their coordinates are those of the town centre
 * — a more precise position could change no answer. It would only stand in the
 * address line and thereby in the access logs.
 */
export interface RoundedPosition {
  lat: number;
  lon: number;
}

export interface GeolocationState {
  /** The rounded position, or `null` as long as none was asked for. */
  position: RoundedPosition | null;
  /** Is a request running right now? */
  loading: boolean;
  /** What went wrong, in one sentence for humans. `null` if nothing. */
  error: string | null;
  /** Can this browser do it at all? */
  supported: boolean;
  /** Asks the browser — which asks the person. */
  request: () => void;
  /** Forgets the position again. */
  forget: () => void;
}

/**
 * One's own position — at the press of a button, never by itself.
 *
 * <strong>There is no call when the page loads.</strong> The browser asks the
 * person for permission as soon as `getCurrentPosition` runs; a fetch in an
 * effect would therefore mean opening a system dialog unasked, before anybody
 * has said they want to filter by distance. The button IS the consent — the
 * same rule as for the draft button (ADR-0024).
 *
 * <strong>Nothing is stored.</strong> The position lives in this state and
 * ends with the page: no `localStorage`, no profile field, no row in a table. A
 * remembered home town would be personal data nobody created — and about which
 * the data access page would then stay silent.
 *
 * <strong>Rounded before it goes anywhere.</strong> See `RoundedPosition`.
 */
export function useGeolocation(): GeolocationState {
  const { t } = useTranslation();
  const [position, setPosition] = useState<RoundedPosition | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const supported = typeof navigator !== "undefined" && "geolocation" in navigator;

  const request = useCallback(() => {
    if (!supported) {
      setError(t("geolocation.unsupported"));
      return;
    }

    setLoading(true);
    setError(null);

    navigator.geolocation.getCurrentPosition(
      (found) => {
        const round = (value: number) => Number(value.toFixed(DECIMALS));

        setPosition({
          lat: round(found.coords.latitude),
          lon: round(found.coords.longitude),
        });
        setLoading(false);
      },
      (failure) => {
        // Three cases, three sentences. A shared "position not available"
        // would leave open whether the person refused themselves — then the
        // advice "try again" would simply be wrong.
        setError(
          failure.code === failure.PERMISSION_DENIED
            ? t("geolocation.denied")
            : failure.code === failure.TIMEOUT
              ? t("geolocation.timeout")
              : t("geolocation.unknown")
        );
        setLoading(false);
      },
      {
        // No GPS fix: the answer is rounded to a kilometre anyway, and
        // `enableHighAccuracy` costs noticeable power on a phone for a
        // precision that is thrown away here.
        enableHighAccuracy: false,
        timeout: 10000,
        maximumAge: 300000,
      }
    );
  }, [supported, t]);

  const forget = useCallback(() => {
    setPosition(null);
    setError(null);
  }, []);

  return { position, loading, error, supported, request, forget };
}
