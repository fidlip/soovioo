import { Request, expect, test as base } from '@playwright/test';

const isApiRequest = (request: Request) =>
  new URL(request.url()).port === '5080';

/**
 * Drop-in replacement for `@playwright/test`'s `test` that fails any test
 * whose page logged a console error or warning, or threw an uncaught
 * exception — keeps the console clean (ROADMAP: "Audit konzole").
 *
 * A test that deliberately triggers one (e.g. a 401 from wrong credentials,
 * which Chrome itself logs as "Failed to load resource") lists the expected
 * messages via `test.use({ allowedConsoleMessages: [/401/] })`.
 */
export const test = base.extend<{
  allowedConsoleMessages: RegExp[];
  consoleGuard: void;
  /** Resolves once the page has had no API request in flight for ~300 ms
   * — lets late responses (and whatever they log) land before moving on. */
  waitForApiIdle: () => Promise<void>;
}>({
  allowedConsoleMessages: [[], { option: true }],
  consoleGuard: [
    async ({ page, allowedConsoleMessages }, use) => {
      const problems: string[] = [];
      const record = (kind: string, text: string) => {
        if (!allowedConsoleMessages.some((re) => re.test(text))) {
          problems.push(`${kind}: ${text}`);
        }
      };
      page.on('console', (msg) => {
        if (msg.type() === 'error' || msg.type() === 'warning') {
          record(msg.type(), msg.text());
        }
      });
      page.on('pageerror', (error) => record('pageerror', error.message));

      await use();

      if (problems.length > 0) {
        throw new Error(`Console errors/warnings:\n${problems.join('\n')}`);
      }
    },
    { auto: true },
  ],
  waitForApiIdle: async ({ page }, use) => {
    let pending = 0;
    page.on('request', (r) => isApiRequest(r) && pending++);
    const settled = (r: Request) => isApiRequest(r) && pending--;
    page.on('requestfinished', settled);
    page.on('requestfailed', settled);

    await use(async () => {
      for (let quietTicks = 0; quietTicks < 3;) {
        await new Promise((resolve) => setTimeout(resolve, 100));
        quietTicks = pending === 0 ? quietTicks + 1 : 0;
      }
    });
  },
});

export { expect };
