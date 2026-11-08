import { expect, test } from './fixtures';
import {
  archive,
  draftCopy,
  openVersionWeek,
  publishQuietly,
  showVersion,
  unpublish,
  versionsApi,
} from './timetable-versions';

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
    .getByRole('link', { name: 'Rozvrh', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/timetable$/);
}

/** The grid cell of one day (row, by its full day name) and period. */
function cell(
  page: import('@playwright/test').Page,
  day: string,
  period: string,
) {
  return page
    .locator('tbody tr')
    .filter({ has: page.locator(`th[title="${day}"]`) })
    .locator(`td[data-period="${period}"]`);
}

/** Jan Novák's class, whose parent then checks the lesson — looked up in
 * the class teacher's Administrace (ucitel's) rather than hard-coded: the
 * seed calls it 5.A, but a local database may have it renamed. */
async function jansClassName(
  page: import('@playwright/test').Page,
): Promise<string> {
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Administrace', exact: true })
    .click();
  return (
    await page
      .getByRole('row')
      .filter({ hasText: 'Jan Novák' })
      .first()
      .getByRole('cell')
      .nth(1)
      .innerText()
  ).trim();
}

/** Lessons are added per class, by the admin. */
async function showClass(
  page: import('@playwright/test').Page,
  className: string,
) {
  await page.locator('p-select[name="viewMode"]').click();
  await page.getByRole('option', { name: 'Třída', exact: true }).click();
  await page.locator('p-select[name="classPicker"]').click();
  await page.getByRole('option', { name: className, exact: true }).click();
}

// One lesson goes through its life in a draft far ahead: the admin adds it to
// Jan Novák's class in a free slot, published Jan's parent sees it, and back
// among drafts the admin removes it — so the timetable that applies stays as
// it was. A teacher only looks.
test.describe.serial('timetable lesson', () => {
  const slot = { day: '', period: '', text: '', className: '' };
  const name = `E2E hodina ${Date.now()}`;
  let versionId = 0;

  test('a teacher sees the timetable but does not edit it', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');
    await expect(
      page.getByRole('button', { name: 'Upravit rozvrh' }),
    ).toHaveCount(0);
    slot.className = await jansClassName(page);
  });

  test('a published version offers no editing, a draft does', async ({
    page,
    request,
  }) => {
    await login(page, 'admin', 'admin123');
    versionId = await draftCopy(await versionsApi(page, request), name, 1000);
    await showClass(page, slot.className);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await expect(page.getByTestId('timetable-version-read-only')).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Přidat hodinu' }),
    ).toHaveCount(0);

    await showVersion(page, name);
    await expect(page.getByTestId('timetable-version-read-only')).toHaveCount(
      0,
    );
    await expect(
      page.getByRole('button', { name: 'Přidat hodinu' }).first(),
    ).toBeAttached();
  });

  test('admin adds a lesson to a free slot', async ({ page }) => {
    await login(page, 'admin', 'admin123');

    await showClass(page, slot.className);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await expect(page).toHaveURL(/\/admin\?group=teaching&section=timetable/);
    await expect(page.getByRole('tab', { name: 'Rozvrh' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await expect(page.getByRole('combobox', { name: 'Třída' })).toHaveText(
      slot.className,
    );
    await showVersion(page, name);

    const addButton = page
      .getByRole('button', { name: 'Přidat hodinu' })
      .first();
    const target = await addButton.evaluate((button) => {
      const td = button.closest('td') as HTMLElement;
      const th = td.closest('tr')?.querySelector('th');
      return { day: th?.title ?? '', period: td.dataset['period'] ?? '' };
    });
    slot.day = target.day;
    slot.period = target.period;
    // Hover first: the button is only visible on hover (or focus).
    await cell(page, slot.day, slot.period).hover();
    await addButton.click();

    const dialog = page.getByRole('dialog', { name: 'Nová hodina' });
    // Jana Učitelová has the aprobace for M (and Čj), not for Aj.
    const openList = async (name: string) => {
      const combobox = dialog.getByRole('combobox', { name });
      await combobox.click();
      const list = page.locator(
        `#${await combobox.getAttribute('aria-controls')}`,
      );
      await expect(list.getByRole('option').first()).toBeVisible();
      return { combobox, list };
    };
    const teacherOptions = async () => {
      const { combobox, list } = await openList('Vyučující');
      const labels = await list.getByRole('option').allInnerTexts();
      await combobox.press('Escape');
      await expect(list).toBeHidden();
      return labels.map((l) => l.trim());
    };
    const pickSubject = async (name: string) => {
      const { list } = await openList('Předmět');
      await list.getByRole('option', { name, exact: true }).click();
      await expect(list).toBeHidden();
    };
    await pickSubject('Anglický jazyk');
    expect((await teacherOptions()).join()).not.toContain(
      'Učitelová (aprobace)',
    );
    await pickSubject('Matematika');
    const options = await teacherOptions();
    const qualified = options.filter((o) => o.endsWith(' (aprobace)'));
    expect(qualified).toContain('Jana Učitelová (aprobace)');
    expect(options.slice(0, qualified.length)).toEqual(qualified);
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(
      cell(page, slot.day, slot.period).getByRole('button', {
        name: 'Smazat hodinu',
      }),
    ).toHaveCount(1);
    slot.text = (
      await cell(page, slot.day, slot.period)
        .locator('div.font-semibold')
        .innerText()
    ).trim();
    expect(slot.text).not.toBe('');
  });

  test('parent sees it published and cannot edit the timetable', async ({
    page,
    request,
  }) => {
    await login(page, 'rodic', 'rodic123');
    await publishQuietly(await versionsApi(page, request), versionId);
    await openVersionWeek(page, versionId);

    await expect(
      page.getByRole('button', { name: 'Upravit rozvrh' }),
    ).toHaveCount(0);
    await expect(cell(page, slot.day, slot.period)).toContainText(slot.text);
  });

  test('admin deletes the lesson back in the draft', async ({
    page,
    request,
  }) => {
    await login(page, 'admin', 'admin123');
    const admin = await versionsApi(page, request);
    await unpublish(admin, versionId);
    await page.reload();

    await showClass(page, slot.className);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await showVersion(page, name);
    await cell(page, slot.day, slot.period).hover();
    await cell(page, slot.day, slot.period)
      .getByRole('button', { name: 'Smazat hodinu' })
      .click();
    await page
      .getByRole('alertdialog', { name: 'Smazat hodinu' })
      .getByRole('button', { name: 'Smazat' })
      .click();

    await expect(
      cell(page, slot.day, slot.period).getByRole('button', {
        name: 'Přidat hodinu',
      }),
    ).toHaveCount(1);
    await archive(admin, versionId);
  });
});

function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

// A version goes through its life: the admin prepares a draft as a copy,
// a parent doesn't see it, the admin publishes it far ahead and archives it
// again — so the timetable that applies stays as it was.
test('admin arranges a draft by dragging lessons', async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  await login(page, 'admin', 'admin123');
  const admin = await versionsApi(page, request);
  const id = await draftCopy(admin, `E2E tahání ${Date.now()}`, 9000);
  // The plan as the timetable has it: what's taken out is what's missing.
  await request.post(`${admin.api}/timetable-versions/${id}/teaching/import`, {
    headers: admin.headers,
  });
  // The whole board in the window: a drag near its edge scrolls the page,
  // and the drop lists then stand elsewhere than where the mouse goes.
  await page.setViewportSize({ width: 1280, height: 1600 });
  await page.goto(
    `/skola/admin?group=teaching&section=timetable&version=${id}`,
  );

  const lessons = page.getByTestId('timetable-lesson');
  const unplaced = page.getByTestId('unplaced-lesson');
  const stack = page.getByTestId('unplaced-lessons');
  await expect(lessons.first()).toBeVisible();
  await expect(page.getByTestId('timetable-assessment')).toContainText(
    'Okna učitelů',
  );
  const startLessons = await lessons.count();
  const startUnplaced = await unplaced.count();

  /** Starts dragging, waits for the slots to show. */
  async function grab(from: import('@playwright/test').Locator): Promise<void> {
    const box = (await from.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 10, box.y + box.height / 2, {
      steps: 3,
    });
  }
  async function release(
    over: import('@playwright/test').Locator,
  ): Promise<void> {
    const box = (await over.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, {
      steps: 8,
    });
    await page.mouse.up();
  }

  // Back among the unplaced.
  await grab(lessons.first());
  await expect(page.locator('td[data-drop]').first()).toBeVisible();
  await release(stack);
  await expect(lessons).toHaveCount(startLessons - 1);
  await expect(unplaced).toHaveCount(startUnplaced + 1);

  // Where the class has a lesson it can't go.
  await grab(unplaced.first());
  await expect(page.locator('td[data-drop="Blocked"]').first()).toBeVisible();
  await release(page.locator('td[data-drop="Blocked"]').first());
  await expect(page.locator('td[data-drop]')).toHaveCount(0);
  await expect(lessons).toHaveCount(startLessons - 1);

  // Into a free slot.
  await grab(unplaced.first());
  const free = page.locator('td[data-drop="Free"], td[data-drop="Unsuitable"]');
  await expect(free.first()).toBeVisible();
  await release(free.first());
  await expect(lessons).toHaveCount(startLessons);
  await expect(unplaced).toHaveCount(startUnplaced);

  // The lock on the lesson: a locked one stays, unlocked it moves again.
  const lesson = lessons.first();
  const lock = lesson.getByTestId('lesson-lock');
  await expect(lock).toHaveAttribute('aria-pressed', 'false');
  await lock.click();
  await expect(lock).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await grab(lesson);
  await page.mouse.move(10, 10, { steps: 3 });
  await expect(page.locator('td[data-drop]')).toHaveCount(0);
  await expect(lesson.getByTestId('lesson-locked-attempt')).toBeVisible();
  expect(await page.evaluate(() => getSelection()?.toString())).toBe('');
  await page.mouse.up();
  await expect(lesson.getByTestId('lesson-locked-attempt')).toHaveCount(0);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await lock.click();
  await expect(lock).toHaveAttribute('aria-pressed', 'false');
  await grab(lesson);
  await expect(page.locator('td[data-drop]').first()).toBeVisible();
  await page.mouse.move(10, 10, { steps: 3 });
  await page.mouse.up();
  await archive(admin, id);
});

test.describe.serial('timetable version', () => {
  const name = `E2E verze ${Date.now()}`;

  test('admin prepares a draft as a copy of the current timetable', async ({
    page,
  }) => {
    // The generator runs until the timetable meets the requirements.
    test.setTimeout(420_000);
    await login(page, 'admin', 'admin123');
    // The page only shows the timetable, its versions are where it's edited.
    await expect(page.getByRole('button', { name: 'Generovat' })).toHaveCount(
      0,
    );
    await expect(
      page.getByRole('button', { name: 'Verze rozvrhu' }),
    ).toHaveCount(0);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    const dialog = page.getByRole('dialog', { name: 'Verze rozvrhu' });
    await dialog.getByRole('button', { name: 'Nový koncept' }).click();
    await dialog.getByLabel('Název').fill(name);
    // Typed key by key — the DatePicker ignores a pasted value.
    await dialog
      .getByLabel('Platí od')
      .pressSequentially(inDays(400 + Math.floor(Math.random() * 300)));
    await dialog.getByRole('button', { name: 'Vytvořit koncept' }).click();

    // The new draft is what the page shows now.
    await expect(dialog).toBeHidden();
    const info = page.getByTestId('timetable-version-info');
    await expect(info).toContainText(name);
    await expect(info).toContainText('Koncept');

    // The generator places the lessons again from the plan.
    await page.getByRole('button', { name: 'Generovat' }).click();
    const generate = page.getByRole('dialog', { name: 'Generovat rozvrh' });
    await generate.getByText('Přegenerovat vše').click();
    await generate.getByRole('button', { name: /Generovat$/ }).click();
    const result = page.getByRole('dialog', { name: 'Výsledek generování' });
    await expect(result.getByTestId('generation-result')).toContainText(
      'Přidáno',
      { timeout: 180_000 },
    );
    await result.getByRole('button', { name: 'Zavřít' }).click();
    await expect(result).toBeHidden();

    // Then brought in line with the data, changing only what's concerned.
    await page.getByRole('button', { name: 'Generovat' }).click();
    await generate.getByText('Upravit podle změn').click();
    await generate.getByText('Jen dotčené hodiny').click();
    await generate.getByText('Ponechat a upozornit').click();
    await generate.getByRole('button', { name: /Generovat$/ }).click();
    await expect(result.getByTestId('generation-result')).toContainText(
      'Upraveno podle změn',
      { timeout: 180_000 },
    );
    await result.getByRole('button', { name: 'Zavřít' }).click();
    await expect(result).toBeHidden();
  });

  test('a parent does not see the draft', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');
    await expect(page.getByText(name)).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: /Verze/ })).toHaveCount(0);
  });

  test('admin publishes it ahead, unpublishes, publishes and archives it', async ({
    page,
  }) => {
    await login(page, 'admin', 'admin123');
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    const dialog = page.getByRole('dialog', { name: 'Verze rozvrhu' });
    const version = dialog
      .getByTestId('timetable-version')
      .filter({ hasText: name });
    const picker = page.getByRole('combobox', { name: 'Verze rozvrhu' });

    // Stays the chosen one, a draft or published.
    await version.getByRole('button', { name: 'Zobrazit' }).click();
    await expect(picker).toContainText(name);
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    await version.getByRole('button', { name: 'Zveřejnit' }).click();
    await page
      .getByRole('dialog', { name: 'Zveřejnit verzi rozvrhu' })
      .getByRole('button', { name: /^(Přesto zveřejnit|Zveřejnit)$/ })
      .click();
    await expect(version).toContainText('Naplánovaná');
    await expect(picker).toContainText(name);

    // Unpublished it's a draft again, and published once more.
    await version.getByRole('button', { name: 'Zrušit zveřejnění' }).click();
    await page
      .getByRole('alertdialog', { name: 'Zrušit zveřejnění verze rozvrhu' })
      .getByRole('button', { name: 'Zrušit zveřejnění' })
      .click();
    await expect(version).toContainText('Koncept');
    await version.getByRole('button', { name: 'Zveřejnit' }).click();
    await page
      .getByRole('dialog', { name: 'Zveřejnit verzi rozvrhu' })
      .getByRole('button', { name: /^(Přesto zveřejnit|Zveřejnit)$/ })
      .click();
    await expect(version).toContainText('Naplánovaná');

    await version.getByRole('button', { name: 'Archivovat' }).click();
    await page
      .getByRole('alertdialog', { name: 'Archivovat verzi rozvrhu' })
      .getByRole('button', { name: 'Archivovat' })
      .click();
    await expect(version).toContainText('Archivovaná');
    await expect(
      version.getByRole('button', { name: 'Vrátit mezi koncepty' }),
    ).toBeVisible();
  });
});
