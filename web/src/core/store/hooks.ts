import { type TypedUseSelectorHook, useDispatch, useSelector } from "react-redux";

import type { AppDispatch, RootState } from "./store";

/**
 * The typed accesses to the store. Use only these, never `useDispatch` or
 * `useSelector` directly: without the types a typo in the state path shows only
 * at runtime, and then as an empty screen.
 */
export const useAppDispatch = (): AppDispatch => useDispatch<AppDispatch>();
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector;
