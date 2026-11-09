import { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from './fixtures';
import { openVersions, runDayEndJob, versionItem } from './timetable-versions';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

async function tokenOf(
  request: APIRequestContext,
  api: string,
  username: string,
  password: string,
): Promise<{ Authorization: string }> {
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username, password, schoolSlug: 'skola' },
    })
  ).json();
  return { Authorization: `Bearer ${token}` };
}

function isoInDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** ISO week of the week a timetable change opens in: a weekend counts as the
 * week after it. */
function weekOfChange(isoDate: string): number {
  const date = new Date(`${isoDate}T00:00:00Z`);
  const day = date.getUTCDay();
  if (day === 0 || day === 6) {
    date.setUTCDate(date.getUTCDate() + 2);
  }
  date.setUTCDate(date.getUTCDate() + 4 - (date.getUTCDay() || 7));
  const yearStart = Date.UTC(date.getUTCFullYear(), 0, 1);
  return Math.ceil(((date.getTime() - yearStart) / 86_400_000 + 1) / 7);
}

async function login(page: Page, username: string, password: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

async function publish(page: Page, name: string, when: string) {
  await openVersions(page);
  await versionItem(page, name)
    .getByRole('button', { name: 'Zveřejnit' })
    .click();
  const confirm = page.getByRole('dialog', { name: 'Zveřejnit verzi rozvrhu' });
  await confirm.getByRole('combobox', { name: 'Upozornit dotčené' }).click();
  await page.getByRole('option', { name: when, exact: true }).click();
  await confirm
    .getByRole('button', { name: /^(Přesto zveřejnit|Zveřejnit)$/ })
    .click();
  await openVersions(page);
  await expect(versionItem(page, name)).toContainText('Naplánovaná');
  await page.keyboard.press('Escape');
}

test('a parent hears about a timetable change when it is due and the link opens its week', async ({
  page,
  request,
}) => {
  await page.goto('/skola/login');
  const api = apiUrl(page);
  const admin = await tokenOf(request, api, 'admin', 'admin123');
  const parent = await tokenOf(request, api, 'rodic', 'rodic123');
  const name = `E2E změna ${Date.now()}`;
  // Its own thousand of days ahead (see draftCopy): published, it would
  // otherwise take over the days other tests change.
  const validFrom = isoInDays(8000 + Math.floor(Math.random() * 900));

  const notices = async () =>
    (
      await (
        await request.get(`${api}/notifications`, { headers: parent })
      ).json()
    ).items.filter((n: { targetId: number; type: string }) => {
      return n.type === 'TimetableChange' && n.targetId === version.id;
    });

  const students = await (
    await request.get(`${api}/students`, { headers: admin })
  ).json();
  const classId = students.find(
    (s: { firstName: string; lastName: string }) =>
      s.firstName === 'Jan' && s.lastName === 'Novák',
  ).schoolClassId;
  const version = await (
    await request.post(`${api}/timetable-versions`, {
      headers: admin,
      data: {
        name,
        validFrom,
        copyFromVersionId: (
          await (
            await request.get(`${api}/timetable-versions`, { headers: admin })
          ).json()
        ).find(
          (v: { status: string; validFrom: string; validTo: string | null }) =>
            v.status === 'Published' &&
            v.validFrom <= isoInDays(0) &&
            (v.validTo === null || v.validTo >= isoInDays(0)),
        ).id,
      },
    })
  ).json();
  const entries = await (
    await request.get(
      `${api}/school-classes/${classId}/timetable?versionId=${version.id}`,
      { headers: admin },
    )
  ).json();
  const dropped = entries.find(
    (e: { studentGroupId: number | null }) => e.studentGroupId === null,
  );
  expect(
    (
      await request.delete(
        `${api}/school-classes/${classId}/timetable/${dropped.id}`,
        { headers: admin },
      )
    ).ok(),
  ).toBe(true);

  try {
    // Planned a day ahead of the start: nothing yet.
    await login(page, 'admin', 'admin123');
    await page
      .getByRole('navigation', { name: 'Hlavní navigace' })
      .getByRole('link', { name: 'Rozvrh', exact: true })
      .click();
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await publish(page, name, 'Den před začátkem platnosti');
    expect(await notices()).toEqual([]);

    // Right away after publishing it again.
    expect(
      (
        await request.post(
          `${api}/timetable-versions/${version.id}/unpublish`,
          { headers: admin },
        )
      ).ok(),
    ).toBe(true);
    await page.reload();
    await publish(page, name, 'Dnes večer');
    expect(await notices()).toEqual([]);
    await runDayEndJob(page, request);
    await expect.poll(async () => (await notices()).length).toBe(1);

    await page.context().clearCookies();
    await login(page, 'rodic', 'rodic123');
    await page
      .getByRole('button', { name: /^Upozornění, \d+ nepřečtených$/ })
      .click();
    const item = page
      .getByRole('region', { name: 'Upozornění' })
      .getByRole('button', {
        name: /(Změna rozvrhu|Nový rozvrh) od .* Jan/,
      })
      .first();
    await item.click();

    await expect(page).toHaveURL(/\/skola\/timetable$/);
    await expect(page.getByTestId('timetable-version-info')).toContainText(
      name,
    );
    await expect(
      page.getByText(`(týden ${weekOfChange(validFrom)})`),
    ).toBeVisible();

    // Taken back: whoever heard, even having read it, hears it's off.
    expect(
      (
        await request.post(
          `${api}/timetable-versions/${version.id}/unpublish`,
          { headers: admin },
        )
      ).ok(),
    ).toBe(true);
    await runDayEndJob(page, request);
    await expect.poll(async () => (await notices()).length).toBe(0);
    await page.reload();
    await page
      .getByRole('button', { name: /^Upozornění, \d+ nepřečtených$/ })
      .click();
    await expect(
      page
        .getByRole('region', { name: 'Upozornění' })
        .getByRole('button', { name: /Rozvrh od .* se ruší — Jan/ })
        .first(),
    ).toBeVisible();
  } finally {
    await request.post(`${api}/timetable-versions/${version.id}/unpublish`, {
      headers: admin,
    });
    await request.delete(`${api}/timetable-versions/${version.id}`, {
      headers: admin,
    });
  }
});
