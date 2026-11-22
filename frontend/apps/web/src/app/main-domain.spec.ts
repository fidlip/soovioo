import { isTestInstance, mainDomain } from './main-domain';

function page(content: string | null): Document {
  const doc = document.implementation.createHTMLDocument();
  if (content !== null) {
    const meta = doc.createElement('meta');
    meta.name = 'soovioo-main-domain';
    meta.content = content;
    doc.head.append(meta);
  }
  return doc;
}

describe('mainDomain', () => {
  it('takes the domain Caddy filled in', () => {
    expect(mainDomain(page('test.soovioo.cloud'))).toBe('test.soovioo.cloud');
  });

  it("falls back to the build's domain when nothing was filled in", () => {
    expect(mainDomain(page(null))).toBe('soovioo.cloud');
    expect(mainDomain(page(''))).toBe('soovioo.cloud');
    expect(mainDomain(page('{{env `MAIN_DOMAIN`}}'))).toBe('soovioo.cloud');
  });
});

describe('isTestInstance', () => {
  it('is the test. domain only', () => {
    expect(isTestInstance('test.soovioo.cloud')).toBe(true);
    expect(isTestInstance('soovioo.cloud')).toBe(false);
  });
});
