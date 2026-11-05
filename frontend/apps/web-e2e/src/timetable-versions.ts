import { APIRequestContext, Page, expect } from '@playwright/test';

/** Lessons change only in a draft: the tests prepare their own far ahead of
 * what applies, so that the shared timetable stays as it is, and the admin's
 * API is the short way there. */
export interface VersionsApi {
  api: string;
  headers: Record<string, string>;
  request: APIRequestContext;
}

function isoInDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Call with a page on the app: its address says where the API is (see devApiPort). */
export async function versionsApi(
  page: Page,
  request: APIRequestContext,
): Promise<VersionsApi> {
  const url = new URL(page.url());
  const api = `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
    })
  ).json();
  return { api, headers: { Authorization: `Bearer ${token}` }, request };
}

/** A copy of the version that applies, starting `base` days ahead and up to a
 * thousand more, by the time. Two versions can't be published for one day: give
 * each test its own thousand; a run cut short leaves its version in no one's way. */
export async function draftCopy(
  admin: VersionsApi,
  name: string,
  base: number,
): Promise<number> {
  const versions: {
    id: number;
    status: string;
    validFrom: string;
    validTo: string | null;
  }[] = await (
    await admin.request.get(`${admin.api}/timetable-versions`, {
      headers: admin.headers,
    })
  ).json();
  const today = isoInDays(0);
  const current = versions.find(
    (v) =>
      v.status === 'Published' &&
      v.validFrom <= today &&
      (v.validTo === null || v.validTo >= today),
  );
  const response = await admin.request.post(`${admin.api}/timetable-versions`, {
    headers: admin.headers,
    data: {
      name,
      validFrom: isoInDays(base + (Math.floor(Date.now() / 1000) % 1000)),
      copyFromVersionId: current?.id,
    },
  });
  expect(response.ok()).toBe(true);
  return (await response.json()).id;
}

async function act(
  admin: VersionsApi,
  id: number,
  action: string,
  data?: object,
) {
  const response = await admin.request.post(
    `${admin.api}/timetable-versions/${id}/${action}`,
    { headers: admin.headers, data },
  );
  expect(response.ok()).toBe(true);
}

/** With the notice as late as can be: the test's change bothers nobody. */
export function publishQuietly(admin: VersionsApi, id: number) {
  return act(admin, id, 'publish', { notifyDaysBefore: 30 });
}

export function unpublish(admin: VersionsApi, id: number) {
  return act(admin, id, 'unpublish');
}

export function archive(admin: VersionsApi, id: number) {
  return act(admin, id, 'archive');
}

/** The evening's job right now, through the Hangfire dashboard that answers
 * local requests; it runs in the background, so poll for what it sends. */
export async function runDayEndJob(page: Page, request: APIRequestContext) {
  const url = new URL(page.url());
  const response = await request.post(
    `http://${url.hostname}:${Number(url.port || 4200) + 880}/hangfire/recurring/trigger`,
    { form: { 'jobs[]': 'timetable-change-notice' } },
  );
  expect(response.ok()).toBe(true);
}

/** The timetable as a notification about the version's start opens it. */
export function openVersionWeek(page: Page, id: number) {
  return page.goto(`/skola/timetable?version=${id}&change=start`);
}

/** Administrace → Výuka's choice of a version. */
export async function showVersion(page: Page, name: string) {
  await page.getByRole('combobox', { name: 'Verze rozvrhu' }).click();
  await page.getByRole('option', { name }).click();
  await expect(page.getByTestId('timetable-version-info')).toContainText(name);
}
