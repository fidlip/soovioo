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
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Zprávy', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/messages$/);
  // The list opens filtered to unread conversations; a sender's own new
  // conversation is read, so look at all of them.
  await page.getByRole('combobox', { name: 'Stav' }).click();
  await page.getByRole('option', { name: 'Vše' }).click();
}

/** Teacher sends a new message about Jan Novák (5.A's demo student). */
async function sendAsTeacher(
  page: import('@playwright/test').Page,
  mode: string,
  subject: string,
  body: string,
) {
  await page.getByRole('button', { name: 'Nová zpráva' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová zpráva' });
  await dialog.locator('#sendMode').click();
  await page.getByRole('option', { name: mode }).click();
  await dialog.locator('#newStudent').click();
  await page.getByRole('option', { name: 'Jan Novák' }).click();
  await dialog.locator('#subject').fill(subject);
  await dialog.locator('#body').fill(body);
  await dialog.getByRole('button', { name: 'Odeslat' }).click();
  await expect(dialog).toBeHidden();
  // The new conversation opens right away.
  await expect(page.getByRole('heading', { name: subject })).toBeVisible();
}

const conversation = (page: import('@playwright/test').Page, subject: string) =>
  page.getByRole('region', { name: 'Konverzace' }).getByRole('button', {
    name: subject,
  });

// Unique per run: earlier runs leave their conversations behind locally.
const runId = Date.now();

// A conversation goes back and forth: teacher → parent → teacher.
test.describe.serial('teacher and parent exchange messages', () => {
  const subject = `E2E rodině ${runId}`;

  test('teacher writes to the student’s family', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    await sendAsTeacher(
      page,
      'Rodině vybraného žáka',
      subject,
      'Zítra s sebou výtvarné potřeby.',
    );
    await expect(conversation(page, subject)).toBeVisible();
  });

  test('parent reads it and replies', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');
    await conversation(page, subject).click();
    await expect(
      page.getByText('Zítra s sebou výtvarné potřeby.', { exact: true }),
    ).toBeVisible();

    await page.getByPlaceholder('Odpovědět…').fill('Děkujeme, připravíme.');
    await page.getByRole('button', { name: 'Odeslat' }).click();
    await expect(
      page.getByText('Děkujeme, připravíme.', { exact: true }),
    ).toBeVisible();
  });

  test('teacher finds the unread reply on Home', async ({ page }) => {
    await page.goto('/skola/login');
    await page.locator('#username').fill('ucitel');
    await page.locator('#password').fill('ucitel123');
    await page.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(page).toHaveURL(/\/skola\/home$/);

    await page
      .getByRole('link', {
        name: new RegExp(`${subject}.*Děkujeme, připravíme\\.`),
      })
      .click();
    await expect(page).toHaveURL(/\/skola\/messages\?conversation=\d+$/);
    await expect(page.getByRole('heading', { name: subject })).toBeVisible();
  });

  test('teacher sees the reply', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    await conversation(page, subject).click();
    await expect(
      page.getByText('Děkujeme, připravíme.', { exact: true }),
    ).toBeVisible();
  });
});

test('a long conversation in the list does not widen the page on a phone', async ({
  page,
}) => {
  await login(page, 'ucitel', 'ucitel123');
  await page.setViewportSize({ width: 375, height: 800 });
  const subject = `E2E dlouhý předmět ${runId} ${'a'.repeat(60)}`;
  await sendAsTeacher(
    page,
    'Pouze vybranému žákovi',
    subject,
    `Dlouhý text bez mezer ${'b'.repeat(120)}`,
  );
  await page.getByRole('button', { name: 'Zpět na seznam' }).click();
  await expect(conversation(page, subject)).toBeVisible();

  await page.getByRole('button', { name: 'Nová zpráva' }).click();
  await expect(page.getByRole('dialog', { name: 'Nová zpráva' })).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  );
  expect(overflow).toBe(false);
});

test.describe.serial('message only for the student', () => {
  const subject = `E2E jen žákovi ${runId}`;

  test('teacher writes to the student alone', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    await sendAsTeacher(
      page,
      'Pouze vybranému žákovi',
      subject,
      'Přines prosím opravený test.',
    );
    await expect(conversation(page, subject)).toBeVisible();
  });

  test('student sees it', async ({ page }) => {
    await login(page, 'zak', 'zak123');
    await expect(conversation(page, subject)).toBeVisible();
  });

  test('parent does not see it and cannot write to a whole class', async ({
    page,
    waitForApiIdle,
  }) => {
    await login(page, 'rodic', 'rodic123');
    await waitForApiIdle();
    await expect(conversation(page, subject)).toHaveCount(0);

    // Parents write only to staff of their own child — no "Komu" choice
    // between a student, a class or the whole school.
    await page.getByRole('button', { name: 'Nová zpráva' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nová zpráva' });
    await expect(dialog.getByText('Příjemci')).toBeVisible();
    await expect(dialog.locator('#sendMode')).toHaveCount(0);
  });
});
