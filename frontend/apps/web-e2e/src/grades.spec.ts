import { expect, test } from './fixtures';

async function login(
  page: import('@playwright/test').Page,
  username: string,
  password: string,
) {
  await page.goto('/demo/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/demo\/home$/);
  // Login now lands on the Home dashboard, not Grades directly — these
  // tests need the full Grades page itself.
  await page.getByRole('link', { name: 'Známky', exact: true }).click();
  await expect(page).toHaveURL(/\/demo\/grades$/);
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

  // The grades table fetch is still in flight right after navigation —
  // wait for it to actually render (rows, or the empty state on a fresh
  // database, as in CI) before counting rows, or rowsBefore can be
  // captured as 0 and make the +1 assertion below flaky.
  await expect(gradesLoaded(page)).toBeVisible();
  const rowsBefore = await page.locator('table tbody tr').count();

  await page.getByRole('button', { name: 'Zapsat známku' }).click();
  await page.locator('#gradeValue').fill('2');
  await page.locator('#gradeWeight').fill('4');
  // Not getByRole('button', { name: 'Zapsat', exact: true }) — PrimeNG's
  // icon span contributes a leading space to the computed accessible name
  // (" Zapsat"), which an exact match never finds. The dialog's submit
  // button is the only `type="submit"` button on the page.
  await page.locator('button[type="submit"]').click();

  // Grades are listed by GradedOn (defaults to today) descending, then
  // student surname, then CreatedAt descending — the dialog preselects the
  // first student (Jan Novák, first by surname too), so the row just
  // submitted is always the first one, even when earlier runs left other
  // grades with the same values.
  await expect(page.locator('table tbody tr')).toHaveCount(rowsBefore + 1);
  const newRow = page.locator('table tbody tr').first();
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
