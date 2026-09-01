import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Provider } from "react-redux";

import "@fontsource-variable/inter";

import { AppRoot } from "./AppRoot";
import { store } from "./core/store/store";

const container = document.getElementById("root");

if (container === null) {
  throw new Error("WorkerTransfer could not find the application root.");
}

createRoot(container).render(
  <StrictMode>
    <Provider store={store}>
      <AppRoot />
    </Provider>
  </StrictMode>
);
