import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** A demo school of the test's own, logged in as its management. */
async function newDemo(page: Page): Promise<string> {
  await page.goto('/demo?new=1');
  await page.getByRole('radio', { name: /Základní škola/ }).click();
  await page.getByRole('radio', { name: /^1/ }).click();
  await page.getByRole('button', { name: 'Další' }).click();
  await page.getByRole('radio', { name: /Vedení školy/ }).click();
  await expect(page).toHaveURL(/\/demo-[a-z0-9]{11}\/home$/, {
    timeout: 30_000,
  });
  return new URL(page.url()).pathname.split('/')[1];
}

async function deleteDemo(page: Page, slug: string): Promise<void> {
  await page.goto(`/${slug}/home`);
  await page.getByRole('button', { name: 'Možnosti dema' }).click();
  await page.getByRole('menuitem', { name: 'Smazat celé demo' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat', exact: true })
    .click();
  await expect(page).toHaveURL(/\/demo\?new=1$/);
}

// In a demo school of its own: the attendance changes Docházka, which the
// seeded school's tests count on.
test('the management writes a past lesson: its topic and an absent pupil, then calls the names one by one', async ({
  page,
}) => {
  test.slow();
  const slug = await newDemo(page);
  try {
    await page.goto(`/${slug}/absences`);
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

    // Po jednom: everyone there, and after the last one it's saved. A pupil
    // with an omluvenka is present only after a yes.
    await page.getByRole('button', { name: `Upravit ${heading}` }).click();
    await dialog.getByText('Po jednom', { exact: true }).click();
    const rollCall = dialog.getByTestId('roll-call');
    const count = Number(
      (await rollCall.getByText(/^1 \/ \d+$/).textContent())!.split('/')[1],
    );
    const warning = page.getByRole('alertdialog', {
      name: 'Zapsat jako přítomného?',
    });
    for (let i = 0; i < count; i++) {
      await expect(rollCall).toContainText(`${i + 1} / ${count}`);
      const flagged = (await rollCall.locator('.text-orange-600').count()) > 0;
      await rollCall
        .getByRole('button', { name: 'Přítomen', exact: true })
        .click();
      if (flagged) {
        await warning.getByRole('button', { name: 'Přítomen' }).click();
      }
    }
    await expect(dialog).toBeHidden();
    await expect(lessonRow.getByTestId('class-book-attendance')).toHaveText(
      'Všichni přítomni',
    );
  } finally {
    await deleteDemo(page, slug);
  }
});

test('on a phone the class book is compact: a lesson a row with its attendance as a bar, a day or a week', async ({
  page,
}) => {
  test.slow();
  await page.setViewportSize({ width: 390, height: 844 });
  const slug = await newDemo(page);
  try {
    await page.goto(`/${slug}/absences`);
    await expect(page.getByText('Kompaktně', { exact: true })).toBeVisible();
    const write = page
      .getByTestId('class-book-compact-lesson')
      .getByRole('button', { name: /^Zapsat \d+\. hodina/ });
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
    const label = (await write.first().getAttribute('aria-label'))!;
    const heading = label.replace(/^Zapsat /, '').replace(/, .*$/, '');
    await expect(write.first()).toHaveAccessibleName(/Docházka nezapsána$/);

    await write.first().click();
    const dialog = page.getByRole('dialog', { name: heading });
    const pupils = dialog.getByTestId('lesson-pupil');
    await expect(pupils.first()).toBeVisible();
    // With the pupils an omluvenka already marks absent.
    const count = dialog.getByRole('heading', { name: /chybí \d+ z/ });
    const before = Number(
      (await count.textContent())!.match(/chybí (\d+) z/)![1],
    );
    const absent = before + 1;
    await pupils
      .filter({ hasNot: page.locator('.text-orange-600') })
      .first()
      .getByText('Chybí', { exact: true })
      .click();
    await expect(count).toContainText(`chybí ${absent} z`);
    await dialog.getByRole('button', { name: 'Uložit' }).click();
    await expect(dialog).toBeHidden();
    const bar = new RegExp(`chybí ${absent}$`);
    await expect(
      page.getByRole('button', { name: `Upravit ${heading}, přítomno` }),
    ).toHaveAccessibleName(bar);

    await page.getByText('Týden', { exact: true }).click();
    await expect(page.getByTestId('class-book-week')).toBeVisible();
    await expect(
      page
        .getByTestId('class-book-week-lesson')
        .and(
          page.getByRole('button', { name: `Upravit ${heading}, přítomno` }),
        ),
    ).toHaveAccessibleName(bar);
  } finally {
    await deleteDemo(page, slug);
  }
});

test("an omluvenka's day without its lessons written is approved after a warning, or not at all where the school says so", async ({
  page,
}) => {
  test.slow();
  const slug = await newDemo(page);
  try {
    await page.goto(`/${slug}/absences`);
    await page.getByRole('tab', { name: 'Omluvenky' }).click();
    // A waiting day of the demo, its lessons not written.
    const excuse = page
      .locator('tr[id^="excuse-"]')
      .filter({ has: page.getByTestId('day-lessons') })
      .filter({ hasText: 'nezapsáno' })
      .first();
    await expect(excuse).toBeVisible();
    const id = await excuse.getAttribute('id');
    const approve = page
      .locator(`tr[id="${id}"]`)
      .getByRole('button', { name: 'Schválit' });
    await approve.click();
    const warning = page.getByRole('alertdialog', { name: 'Nezapsané hodiny' });
    await expect(warning).toContainText('V třídní knize chybí zápis');
    await warning.getByRole('button', { name: 'Zrušit' }).click();
    await expect(warning).toBeHidden();

    await page.goto(`/${slug}/admin`);
    await page.getByRole('tab', { name: 'Škola' }).click();
    await page.locator('#unverifiedExcuseApproval').click();
    await page.getByRole('option', { name: 'Nepovolit' }).click();
    const card = page.locator('p-card', { hasText: 'Třídní kniha' });
    await card.getByRole('button', { name: 'Uložit' }).click();
    await expect(page.getByText('Uloženo.')).toBeVisible();

    await page.goto(`/${slug}/absences`);
    await page.getByRole('tab', { name: 'Omluvenky' }).click();
    await expect(approve).toBeDisabled();
  } finally {
    await deleteDemo(page, slug);
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

  await page.getByRole('link', { name: 'Docházka', exact: true }).click();
  await expect(page).toHaveURL(/\/skola\/absences$/);
  await expect(page.getByRole('tab', { name: 'Třídní kniha' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByTestId('class-book-class')).toContainText(
    'Moje hodiny',
  );
});
