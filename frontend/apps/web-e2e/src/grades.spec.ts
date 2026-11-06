import { expect, test } from './fixtures';

async function login(
  page: import('@playwright/test').Page,
  username: string,
  password: string,
) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  // Login now lands on the Home dashboard, not Grades directly — these
  // tests need the full Grades page itself.
  await page.getByRole('link', { name: 'Známky', exact: true }).click();
  await expect(page).toHaveURL(/\/skola\/grades$/);
}

/** The grades list has rendered: its first row, or the empty-state text
 * (the table isn't rendered at all without grades). */
function gradesLoaded(page: import('@playwright/test').Page) {
  return page
    .locator('table tbody tr')
    .or(page.getByText('Zatím žádné známky.'))
    .first();
}

test('teacher can record a grade and see it appear in the table', async ({
  page,
}) => {
  await login(page, 'ucitel', 'ucitel123');

  await expect(gradesLoaded(page)).toBeVisible();

  // Other tests add grades in parallel — the new one is found by its note.
  const note = `E2E ${Date.now()}`;
  await page.getByRole('button', { name: 'Zapsat známku' }).click();
  // Not the preselected first pupil — that can be one another test just
  // added (and is about to erase).
  await page.locator('#gradeStudent').click();
  await page.getByRole('option', { name: 'Jan Novák' }).click();
  await page.locator('#gradeValue').fill('2');
  await page.locator('#gradeWeight').fill('4');
  await page.locator('#gradeNote').fill(note);
  // Not getByRole('button', { name: 'Zapsat', exact: true }) — PrimeNG's
  // icon span contributes a leading space to the computed accessible name
  // (" Zapsat"), which an exact match never finds. The dialog's submit
  // button is the only `type="submit"` button on the page.
  await page.locator('button[type="submit"]').click();

  const newRow = page.locator('table tbody tr').filter({ hasText: note });
  await expect(newRow).toHaveCount(1);
  await expect(newRow).toContainText('2');
  await expect(newRow).toContainText('4');
});

test('parent does not see the grade-entry form, only their child’s grades', async ({
  page,
}) => {
  await login(page, 'rodic', 'rodic123');

  await expect(page.getByRole('button', { name: 'Zapsat známku' })).toHaveCount(
    0,
  );
  await expect(gradesLoaded(page)).toBeVisible();
});

test('the subject filter asks the server for that subject’s grades', async ({
  page,
}) => {
  await login(page, 'ucitel', 'ucitel123');
  await expect(gradesLoaded(page)).toBeVisible();

  const filtered = page.waitForRequest((r) =>
    /\/api\/grades\?.*subjectId=\d+.*page=1/.test(r.url()),
  );
  await page.locator('#filterSubject').click();
  await page.getByRole('option').first().click();
  await filtered;
});
