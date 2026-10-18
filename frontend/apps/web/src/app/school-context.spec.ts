import { isPathBasedTenant, subdomainSlug } from './school-context';

describe('isPathBasedTenant', () => {
  it('treats localhost, bare IPs and LAN machine names as path-based', () => {
    expect(isPathBasedTenant('localhost', 'soovioo.cz')).toBe(true);
    expect(isPathBasedTenant('192.168.1.42', 'soovioo.cz')).toBe(true);
    expect(isPathBasedTenant('aorus', 'soovioo.cz')).toBe(true);
    expect(isPathBasedTenant('aorus.local', 'soovioo.cz')).toBe(true);
  });

  it('treats the main domain and its subdomains as subdomain-based', () => {
    expect(isPathBasedTenant('demo.soovioo.cz', 'soovioo.cz')).toBe(false);
    expect(isPathBasedTenant('soovioo.cz', 'soovioo.cz')).toBe(false);
  });

  it('does not mistake a lookalike domain for the main one', () => {
    expect(isPathBasedTenant('notsoovioo.cz', 'soovioo.cz')).toBe(true);
  });
});

describe('subdomainSlug', () => {
  const mainDomain = 'soovioo.cz';

  it('extracts the slug from a subdomain of the configured main domain', () => {
    expect(subdomainSlug('demo.soovioo.cz', mainDomain)).toBe('demo');
  });

  it('returns null for the bare main domain (no tenant — landing/SuperAdmin)', () => {
    expect(subdomainSlug('soovioo.cz', mainDomain)).toBeNull();
  });

  it('returns null for a hostname that is not under the configured main domain', () => {
    expect(subdomainSlug('example.com', mainDomain)).toBeNull();
  });

  /** The whole point of matching against the configured domain instead of
   * counting segments: a 3-label main domain (e.g. a ccTLD like soovioo.co.uk)
   * must not be mistaken for "a subdomain of a 2-label domain". */
  it('works with a multi-label main domain, not just a 2-label one', () => {
    expect(subdomainSlug('demo.soovioo.co.uk', 'soovioo.co.uk')).toBe('demo');
    expect(subdomainSlug('soovioo.co.uk', 'soovioo.co.uk')).toBeNull();
  });

  it('takes only the first label when there are multiple subdomain levels', () => {
    expect(subdomainSlug('demo.staging.soovioo.cz', mainDomain)).toBe('demo');
  });
});
