import { Locator, Page } from '@playwright/test';
import { expect, test } from './fixtures';

async function pick(page: Page, field: Locator, option: string) {
  await field.click();
  const list = page.getByRole('listbox');
  await expect(list).toBeVisible();
  await list.getByRole('option', { name: option, exact: true }).click();
  await expect(async () => {
    await page.keyboard.press('Escape');
    await expect(list).toBeHidden({ timeout: 1_000 });
  }).toPass();
}

/** An InputNumber takes typing, not fill() — that keeps what's there. */
async function setNumber(field: Locator, value: string) {
  await field.click();
  // Its own key handling deletes one character at a time, selection or not.
  // The caret to the end by hand: End is the spinbutton's "set to maximum".
  await field.evaluate((input: HTMLInputElement) =>
    input.setSelectionRange(input.value.length, input.value.length),
  );
  for (let i = 0; i < 3; i++) {
    await field.press('Backspace');
  }
  if (value) {
    await field.pressSequentially(value);
  }
  await expect(field).toHaveValue(value);
}

async function setSchoolMaxGaps(page: Page, value: string) {
  await page.getByRole('tab', { name: 'Škola a matrika' }).click();
  const card = page.locator('p-card', { hasText: 'Přání učitelů k rozvrhu' });
  const gaps = card.getByRole('spinbutton', { name: 'Nejvýš oken denně' });
  await setNumber(gaps, value);
  await card.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Uloženo.')).toBeVisible();
}

// The school's default wish is shared with nothing else in e2e; it's put
// back at the end.
test('a teacher’s availability and wishes, the school’s default as a hint', async ({
  page,
}) => {
  test.slow();
  const lastName = `Dostupná${Date.now()}`;
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();

  await setSchoolMaxGaps(page, '1');

  // A new teacher who can't teach the first period on Monday, nor on Friday at all.
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const people = page.getByRole('tabpanel', { name: 'Osoby' });
  await people.getByRole('button', { name: 'Nová osoba' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová osoba' });
  await dialog.getByRole('textbox', { name: /^Jméno/ }).fill('Eva');
  await dialog.getByRole('textbox', { name: /^Příjmení/ }).fill(lastName);
  await pick(page, dialog.locator('p-multiselect[name="profiles"]'), 'Učitel');
  await expect(
    dialog.getByRole('spinbutton', { name: 'Nejvýš oken denně' }),
  ).toHaveAttribute('placeholder', 'škola: 1');
  const monday1 = dialog.getByRole('button', { name: /^pondělí 1\. hod\./ });
  await monday1.click();
  await expect(monday1).toHaveAccessibleName('pondělí 1. hod.: raději ne');
  await monday1.click();
  await expect(monday1).toHaveAccessibleName('pondělí 1. hod.: nemůže');
  await dialog.getByRole('button', { name: 'Celý den nemůže — pátek' }).click();
  await expect(
    dialog.getByRole('button', { name: /^pátek 1\. hod\./ }),
  ).toBeDisabled();
  await setNumber(
    dialog.getByRole('spinbutton', { name: 'Nejvýš hodin denně' }),
    '5',
  );
  await dialog.getByRole('button', { name: 'Vytvořit osobu' }).click();
  await expect(dialog).toBeHidden();

  // Reopened, it's all there.
  await people
    .locator('tr', { hasText: lastName })
    .getByRole('button', { name: 'Upravit' })
    .click();
  const edit = page.getByRole('dialog', { name: 'Upravit osobu' });
  await expect(
    edit.getByRole('button', { name: 'pondělí 1. hod.: nemůže' }),
  ).toBeVisible();
  await expect(
    edit.getByRole('button', { name: 'Celý den nemůže — pátek' }),
  ).toHaveAttribute('aria-pressed', 'true');
  await expect(
    edit.getByRole('button', { name: 'úterý 1. hod.: může' }),
  ).toBeVisible();
  await expect(
    edit.getByRole('spinbutton', { name: 'Nejvýš hodin denně' }),
  ).toHaveValue('5');
  await page.keyboard.press('Escape');
  await expect(edit).toBeHidden();

  await setSchoolMaxGaps(page, '');
});
