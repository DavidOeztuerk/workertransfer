import { RESUME_BASE_URL, TRANSFER_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";

/**
 * What is waiting for someone — assembled in the browser, not in the backend.
 *
 * A service delivering this overview would have to read across four service
 * boundaries; exactly that is what ADR-0004 rules out. The UI asks every
 * service about what it is responsible for and lays the answers side by side.
 *
 * <strong>Only what expects an ACTION is counted.</strong> An overview that
 * also shows what is running by itself is a list — and a list gets overlooked.
 *
 * <strong>Cases are counted, never people</strong> (ADR-0022/0026).
 *
 * The four reads stand here and not in a shared client: the old modules
 * (`src/market/client.ts`, `src/resume/client.ts`, `src/transfers/client.ts`)
 * moved to `features/person` and `features/work` and belong to others. An
 * import from there would couple this area to code it does not see. The
 * price: the counting rules stand twice for now.
 */

/** The wire is snake_case — it stands here unchanged. */
type RequestStatus = "PENDING" | "GRANTED" | "DECLINED";

interface RequestRow {
  status: RequestStatus;
}

type TransferStatus =
  | "interested"
  | "talking"
  | "offered"
  | "accepted"
  | "completed"
  | "declined"
  | "withdrawn";

interface TransferRow {
  status: TransferStatus;
  requires_release: boolean;
  release_confirmed: boolean;
}

export interface PendingTasks {
  /** Companies that want to know whether someone is approachable. */
  marketRequests: number;
  /** Requests for one's own CV. */
  resumeRequests: number;
  /** Transfers in which the person is to move (the UI calls them conversations). */
  ownTransfers: number;
  /** Transfers in which the company is to move. `0` without an active company. */
  companyTransfers: number;
  /**
   * At least one read failed.
   *
   * Then NOTHING is counted rather than zero: a number on a failed read lulls
   * into safety, and "nothing is pending" is exactly the statement that would
   * then be false.
   */
  incomplete: boolean;
}

const EMPTY: PendingTasks = {
  marketRequests: 0,
  resumeRequests: 0,
  ownTransfers: 0,
  companyTransfers: 0,
  incomplete: false,
};

/**
 * The person is to move: they have interest to answer, an offer in front of
 * them, or a promised release is still outstanding.
 */
function waitsForThePerson(transfer: TransferRow): boolean {
  return (
    transfer.status === "interested" ||
    transfer.status === "offered" ||
    (transfer.status === "accepted" && transfer.requires_release && !transfer.release_confirmed)
  );
}

/** The company is to move. */
function waitsForTheCompany(transfer: TransferRow): boolean {
  return (
    transfer.status === "talking" || (transfer.status === "accepted" && !transfer.requires_release)
  );
}

const open = (rows: RequestRow[]): number =>
  rows.filter((row) => row.status === "PENDING").length;

/**
 * Asks all four sources and lays the answers side by side.
 *
 * `forCompany` decides whether the company list is asked at all: without an
 * active company the service answers 403, and an error the request itself
 * produced must not mark the overview as incomplete.
 */
export async function getPendingTasks(
  forCompany: boolean,
  signal?: AbortSignal
): Promise<PendingTasks> {
  const [market, resume, own, company] = await Promise.all([
    request<RequestRow[]>(TRANSFER_BASE_URL, "/market/me/requests", { signal }),
    request<RequestRow[]>(RESUME_BASE_URL, "/resumes/me/requests", { signal }),
    request<TransferRow[]>(TRANSFER_BASE_URL, "/transfers/me", { signal }),
    forCompany
      ? request<TransferRow[]>(TRANSFER_BASE_URL, "/transfers", { signal })
      : Promise.resolve({ ok: true as const, value: [] as TransferRow[] }),
  ]);

  return {
    ...EMPTY,
    marketRequests: market.ok ? open(market.value ?? []) : 0,
    resumeRequests: resume.ok ? open(resume.value ?? []) : 0,
    ownTransfers: own.ok ? (own.value ?? []).filter(waitsForThePerson).length : 0,
    companyTransfers: company.ok ? (company.value ?? []).filter(waitsForTheCompany).length : 0,
    incomplete: !market.ok || !resume.ok || !own.ok || !company.ok,
  };
}
