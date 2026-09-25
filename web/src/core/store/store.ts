import { configureStore } from "@reduxjs/toolkit";

import auth from "../../features/auth/store/authSlice";
import preferences from "./preferencesSlice";

/**
 * The store. One slice per feature, registered HERE — this is the only file
 * that shows what the application's state consists of.
 */
export const store = configureStore({
  reducer: {
    auth,
    preferences,
  },
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
