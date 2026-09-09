import { type TypedUseSelectorHook, useDispatch, useSelector } from "react-redux";

import type { AppDispatch, RootState } from "./store";

/**
 * Die typisierten Zugänge zum Store. Nur diese benutzen, nie `useDispatch`
 * oder `useSelector` direkt: ohne die Typen ist ein Tippfehler im Zustandspfad
 * erst zur Laufzeit sichtbar, und dann als leerer Bildschirm.
 */
export const useAppDispatch = (): AppDispatch => useDispatch<AppDispatch>();
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector;
