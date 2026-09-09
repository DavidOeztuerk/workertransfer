import { configureStore } from "@reduxjs/toolkit";

import auth from "../../features/auth/store/authSlice";
import preferences from "./preferencesSlice";

/**
 * Der Store. Je Feature ein Slice, angemeldet HIER — das ist die einzige Datei,
 * an der man sieht, woraus der Zustand der Anwendung besteht.
 */
export const store = configureStore({
  reducer: {
    auth,
    preferences,
  },
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
