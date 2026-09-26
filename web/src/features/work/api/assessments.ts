// The work sample — assessment-service (ADR-0042).
//
// <strong>One type for both sides, and that is not saving lines.</strong>
// `GET /assessments/{id}` answers person and company byte-identically with the
// same document; two interfaces here would be the place where the UI starts to
// leave something out for one side. "The person sees the evaluation, always" is
// a property of the build and not a rule one has to remember.
//
// <strong>And there is no number.</strong> No score, no percentage, no stars.
// `hours` is a number and may be one — it is about the TASK, not the human
// being. That is exactly the dividing line of ADR-0022.

import { request } from "../../../core/api/client";
import { ASSESSMENT_BASE_URL } from "../../../env";
import {
  type ErrorReading,
  type ErrorKey,
  type Failure,
  interpretError,
} from "../../../shared/api/errors";

/**
 * Where a case stands.
 *
 * `expired` means: the deadline passed without anything arriving. It does NOT
 * mean "declined" — whoever does not want a task does nothing, and that is
 * indistinguishable from "did not make it". The UI must not build a piece of
 * information out of it.
 */
export type CaseState = "set" | "submitted" | "evaluated" | "expired";

/** How the company answers the work. Two values, no scale. */
export type Outcome = "accepted" | "rejected";

/** The submitted solution. */
export interface Submission {
  text: string;
  /**
   * Where it points.
   *
   * <strong>The server never fetches this address</strong>, and this page does
   * not embed it: it stands there as a link, and a human being clicks it.
   */
  url: string | null;
  submitted_at: string;
}

/**
 * The feedback — text and an outcome.
 *
 * <strong>The person reads exactly this field</strong>, and there is no second
 * one beside it. Where there were two, the truth would stand in the second.
 */
export interface Evaluation {
  outcome: Outcome;
  text: string;
  evaluated_at: string;
}

/**
 * A case as BOTH sides see it.
 *
 * `submission` and `evaluation` are missing as long as they do not exist — here
 * expressly not to hide something, but because a field without a value is no
 * field.
 */
export interface Case {
  id: string;
  subject_id: string;
  tenant_id: string;
  title: string;
  task: string;
  /** The stated effort. It stands in the assignment, and that is the promise. */
  hours: number;
  due_at: string;
  state: CaseState;
  created_at: string;
  updated_at: string;
  submission?: Submission;
  evaluation?: Evaluation;
}

/** What a company sends to set a task. */
export interface TaskInput {
  subject_id: string;
  title: string;
  task: string;
  hours: number;
  due_at: string;
}

/** The limits the server enforces — here only so that the form names them. */
export const MIN_HOURS = 1;

/**
 * Eight hours, and that is a decision.
 *
 * More than one working day is no longer a sample but work — and for work there
 * is a contract and not a form (ADR-0042 §3). The server refuses beyond it;
 * this number stands here only so that the form says so instead of letting
 * somebody try it out.
 */
export const MAX_HOURS = 8;

/** And the deadline lies at least two days ahead. */
export const MIN_NOTICE_DAYS = 2;

export type CaseList = { ok: true; items: Case[] } | Failure<"no-company" | "unavailable">;
export type CaseResult = { ok: true; caseItem: Case } | Failure<"gone" | "unavailable">;
export type StepResult =
  | { ok: true; caseItem: Case }
  | Failure<"gone" | "conflict" | "invalid" | "unavailable" | "offline">;

const LEDGER_SILENT: ErrorReading<"unavailable"> = {
  reason: "unavailable",
  titleKey: "errors.ledgerSilent",
  detailKey: "errors.tryLater",
};

/**
 * One's own cases.
 *
 * <strong>This list does not ask the ledger</strong>, and therefore there is
 * no 503 case from the ledger here: whoever has revoked their visibility goes
 * on reading their evaluations. That is the promise from ADR-0042 §2, and it
 * depends on this page not restricting it afterwards.
 */
export async function getMyCases(signal?: AbortSignal): Promise<CaseList> {
  const answer = await request<{ items: Case[] }>(
    ASSESSMENT_BASE_URL,
    "/assessments/me",
    { signal },
    "errors.listNotLoaded"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  return interpretError<"no-company" | "unavailable">(answer.error, { 503: LEDGER_SILENT }, "unavailable");
}

/** The company's cases. */
export async function getCompanyCases(signal?: AbortSignal): Promise<CaseList> {
  const answer = await request<{ items: Case[] }>(
    ASSESSMENT_BASE_URL,
    "/assessments",
    { signal },
    "errors.listNotLoaded"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  // Do NOT show `503` as an empty list: that would be the claim that nobody got
  // a task — and at this moment nobody knows that.
  return interpretError<"no-company" | "unavailable">(
    answer.error,
    {
      403: {
        reason: "no-company",
        titleKey: "errors.onlyCompaniesAsk",
        detailKey: "errors.chooseCompany",
      },
      503: LEDGER_SILENT,
    },
    "unavailable"
  );
}

/**
 * A 2xx answer without a body.
 *
 * It must never be read as "does not exist" — a service that answers and says
 * nothing while doing so is SILENT, and silence is 503 (ADR-0020 §3).
 */
const EMPTY_ANSWER = { status: 503, title: "", detail: "" };

/** A single case — the same address for both sides. */
export async function getCase(id: string, signal?: AbortSignal): Promise<CaseResult> {
  const answer = await request<Case>(
    ASSESSMENT_BASE_URL,
    `/assessments/${id}`,
    { signal },
    "errors.assessmentNotLoaded"
  );

  if (answer.ok && answer.value != null) return { ok: true, caseItem: answer.value };

  return interpretError<"gone" | "unavailable">(
    answer.ok ? EMPTY_ANSWER : answer.error,
    // `404` deliberately does not say WHY. Four situations, one answer.
    { 404: { reason: "gone", titleKey: "errors.assessmentGone" }, 503: LEDGER_SILENT },
    "unavailable"
  );
}

async function send(path: string, body: unknown, fallbackKey: ErrorKey): Promise<StepResult> {
  const answer = await request<Case>(
    ASSESSMENT_BASE_URL,
    path,
    { method: "POST", body },
    fallbackKey
  );

  if (answer.ok && answer.value != null) return { ok: true, caseItem: answer.value };

  return interpretError<"gone" | "conflict" | "invalid" | "unavailable" | "offline">(
    answer.ok ? EMPTY_ANSWER : answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      // `404` means: this person has released nothing to this company — or the
      // case does not exist, or it belongs to somebody else. The UI builds no
      // piece of information out of it.
      404: { reason: "gone", titleKey: "errors.assessmentGone" },
      409: { reason: "conflict", titleKey: "errors.stepNotPossible" },
      503: LEDGER_SILENT,
    },
    // `422` is DELIBERATELY not in the table, and here that is more than a
    // convention: the server's problem document names the rule that was
    // broken — "the stated effort is between 1 and 8 hours", "an evaluation
    // carries a text, a rejection too". That is exactly what somebody filling
    // in a form needs. A sentence of our own ("please check the entries") would
    // be shorter and would cut the information off.
    //
    // The reason falls back to `invalid`: it serves only the branching, the
    // SENTENCE comes from the server.
    "invalid"
  );
}

/** Set a task. Effort and deadline are required — the server invents none. */
export const setTask = (input: TaskInput) =>
  send("/assessments", input, "errors.taskNotSet");

/** Submit. Text, address, or both — but not nothing. */
export const submitSolution = (id: string, text: string, url: string | null) =>
  send(`/assessments/${id}/submission`, { text, url }, "errors.notSubmitted");

/**
 * Evaluate.
 *
 * <strong>Both fields are required, also for <c>rejected</c>.</strong> The
 * server refuses a rejection without text with 422, and this form therefore
 * does not even arm the button: rejecting and giving a reason are one step.
 */
export const evaluate = (id: string, outcome: Outcome, text: string) =>
  send(`/assessments/${id}/evaluation`, { outcome, text }, "errors.evaluationNotSent");
