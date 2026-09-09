import {
  getApplication,
  listApplicationsForJob,
} from "../../work/api/applications";
import { listOwnJobs } from "../../work/api/jobs";

export { getApplication as getCompanyApplication };

export interface FirmenbewerbungZeile {
  id: string;
  job_id: string;
  job_title: string;
  subject_id: string;
  status: string;
  created_at: string;
}

export type Firmenliste =
  | { ok: true; items: FirmenbewerbungZeile[] }
  | { ok: false; error: { detail: string } };

export async function listCompanyApplications(
  signal?: AbortSignal
): Promise<Firmenliste> {
  const stellen = await listOwnJobs(signal);
  if (!stellen.ok) return { ok: false, error: stellen.error };

  const zeilen: FirmenbewerbungZeile[] = [];
  for (const stelle of stellen.jobs) {
    const bewerbungen = await listApplicationsForJob(stelle.id, signal);
    if (!bewerbungen.ok) continue;
    for (const b of bewerbungen.applications) {
      zeilen.push({
        id: b.id,
        job_id: b.job_id,
        job_title: stelle.title,
        subject_id: b.subject_id,
        status: b.status,
        created_at: b.created_at,
      });
    }
  }

  zeilen.sort((a, b) => b.created_at.localeCompare(a.created_at));
  return { ok: true, items: zeilen };
}
