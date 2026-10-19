import { Suspense, lazy, type ComponentType } from "react";
import { useTranslation } from "react-i18next";

import { LoadingBlock } from "../ui";

/** A component of its own, so that the text is translated at RENDER time —
 *  at module build the language would be fixed before anybody could choose it. */
function PageLoadingBlock() {
  const { t } = useTranslation();
  return <LoadingBlock label={t("common.pageLoading")} />;
}

/**
 * A route whose page is loaded only when it is entered.
 *
 * <strong>Why at all.</strong> With MUI the bundle weighs 762 kB (236 kB
 * gzipped) — before it was 439 kB. Whoever opened the start page also loaded
 * the work-sample editor, the candidate search and the erasure page. Split up,
 * every page pays only for what it needs.
 *
 * <strong>And why a loading state of its own instead of an empty
 * screen.</strong> `React.lazy` needs a `Suspense` above it; without one React
 * throws. With an empty body a slow load would look like a broken page —
 * especially on a mobile connection, where loading really takes time.
 *
 * <strong>The `Suspense` sits per route</strong> and not once above the whole
 * application: that way the header stays while the content arrives. Were it
 * higher up, half the page would vanish while navigating.
 */
export function lazyRoute(
  load: () => Promise<Record<string, ComponentType>>,
  name: string
): React.ReactElement {
  // `lazy` expects a default export; our pages are named exports, because a
  // named export follows a rename and a default export silently becomes
  // something else.
  const Page = lazy(async () => {
    const module = await load();
    const component = module[name];

    if (component === undefined) {
      throw new Error(`The page ${name} does not export itself under that name.`);
    }

    return { default: component };
  });

  return (
    <Suspense fallback={<PageLoadingBlock />}>
      <Page />
    </Suspense>
  );
}
