import { expect, test } from './fixtures';

// In a demo school of its own: the attendance changes Docházka, which the
// seeded school's tests count on.
test('the management writes a past lesson: its topic and an absent pupil, then calls the names one by one', async ({
  page,
}) => {
  test.slow();
  await page.goto('/demo?new=1');
  await page.getByRole('radio', { name: /Základní škola/ }).click();
  await page.getByRole('radio', { name: /^1/ }).click();
  await page.getByRole('button', { name: 'Další' }).click();
  await page.getByRole('radio', { name: /Vedení školy/ }).click();
  await expect(page).toHaveURL(/\/demo-[a-z0-9]{11}\/home$/, {
    timeout: 30_000,
  });
  const slug = new URL(page.url()).pathname.split('/')[1];
  try {
    await page.goto(`/${slug}/class-book`);
    const write = page.getByRole('button', { name: /^Zapsat \d+\. hodina/ });
    const day = page.getByTestId('class-book-day');
    for (let i = 0; i < 30; i++) {
      await expect(page.locator('app-loading')).toHaveCount(0);
      if ((await write.count()) > 0) {
        break;
      }
      const shown = await day.textContent();
      await page.getByRole('button', { name: 'Předchozí den' }).click();
      await expect(day).not.toHaveText(shown!);
    }
    test.skip((await write.count()) === 0, 'No past lesson in the demo.');
    const heading = (await write.first().getAttribute('aria-label'))!.replace(
      /^Zapsat /,
      '',
    );
    const topic = `Téma ${Date.now()}`;

    await write.first().click();
    const dialog = page.getByRole('dialog', { name: heading });
    await dialog.getByLabel('Téma hodiny').fill(topic);
    // A pupil nothing says is away.
    const row = dialog
      .getByTestId('lesson-pupil')
      .filter({ hasNot: page.locator('.text-orange-600') })
      .first();
    const [lastName, firstName] = (await row
      .locator('span.font-medium')
      .textContent())!
      .trim()
      .split(' ');
    await row.getByText('Chybí', { exact: true }).click();
    await dialog.getByRole('button', { name: 'Uložit' }).click();
    await expect(dialog).toBeHidden();
    const lessonRow = page
      .getByTestId('class-book-lesson')
      .filter({ hasText: topic });
    await expect(lessonRow.getByTestId('class-book-attendance')).toContainText(
      new RegExp(`Chybí: .*${firstName} ${lastName}`),
    );

    // Po jednom: everyone there, and after the last one it's saved.
    await page.getByRole('button', { name: `Upravit ${heading}` }).click();
    await dialog.getByText('Po jednom', { exact: true }).click();
    const rollCall = dialog.getByTestId('roll-call');
    const count = Number(
      (await rollCall.getByText(/^1 \/ \d+$/).textContent())!.split('/')[1],
    );
    for (let i = 0; i < count; i++) {
      await expect(rollCall).toContainText(`${i + 1} / ${count}`);
      await rollCall
        .getByRole('button', { name: 'Přítomen', exact: true })
        .click();
    }
    await expect(dialog).toBeHidden();
    await expect(lessonRow.getByTestId('class-book-attendance')).toHaveText(
      'Všichni přítomni',
    );
  } finally {
    await page.goto(`/${slug}/home`);
    await page.getByRole('button', { name: 'Možnosti dema' }).click();
    await page.getByRole('menuitem', { name: 'Smazat celé demo' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Smazat', exact: true })
      .click();
    await expect(page).toHaveURL(/\/demo\?new=1$/);
  }
});

test("a teacher's třídní kniha opens at the lessons they teach", async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('ucitel');
  await page.locator('#password').fill('ucitel123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);

  await page.getByRole('link', { name: 'Třídní kniha' }).first().click();
  await expect(page).toHaveURL(/\/skola\/class-book$/);
  await expect(page.getByTestId('class-book-class')).toContainText(
    'Moje hodiny',
  );
});
