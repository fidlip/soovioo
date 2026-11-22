import { environment } from '../environments/environment';

/** The domain the app runs on — `soovioo.cloud`, or `test.soovioo.cloud` on
 * the test instance. Caddy fills it into index.html's
 * `<meta name="soovioo-main-domain">` from its MAIN_DOMAIN, so one build
 * serves either; where nothing fills it in (the dev server), the build's
 * environment.mainDomain. */
export function mainDomain(doc: Document = document): string {
  const value =
    doc
      .querySelector<HTMLMetaElement>('meta[name="soovioo-main-domain"]')
      ?.content.trim() ?? '';
  return /^[a-z0-9-]+(\.[a-z0-9-]+)+$/.test(value)
    ? value
    : environment.mainDomain;
}

/** The test instance runs on `test.<domain>` (schools at
 * `<slug>.test.<domain>`). */
export function isTestInstance(domain: string = mainDomain()): boolean {
  return domain.startsWith('test.');
}
