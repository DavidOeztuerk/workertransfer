import type { ApiError } from "../../../core/store/thunkHelpers";
import {
  getApplication,
  listApplicationsForJob,
} from "../../work/api/applications";
import { listOwnJobs } from "../../work/api/jobs";

export { getApplication as getCompanyApplication };

export interface CompanyApplicationRow {
  id: string;
  job_id: string;
  job_title: string;
  subject_id: string;
  status: string;
  created_at: string;
}

export type CompanyApplicationList =
  | { ok: true; items: CompanyApplicationRow[] }
  | { ok: false; error: ApiError };

export async function listCompanyApplications(
  signal?: AbortSignal
): Promise<CompanyApplicationList> {
  const jobs = await listOwnJobs(signal);
  if (!jobs.ok) return { ok: false, error: jobs.error };

  const rows: CompanyApplicationRow[] = [];
  for (const job of jobs.jobs) {
    const applications = await listApplicationsForJob(job.id, signal);
    if (!applications.ok) continue;
    for (const application of applications.applications) {
      rows.push({
        id: application.id,
        job_id: application.job_id,
        job_title: job.title,
        subject_id: application.subject_id,
        status: application.status,
        created_at: application.created_at,
      });
    }
  }

  rows.sort((a, b) => b.created_at.localeCompare(a.created_at));
  return { ok: true, items: rows };
}
