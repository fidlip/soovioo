import { isPathBasedTenant, subdomainSlug } from './school-context';

describe('isPathBasedTenant', () => {
  it('treats localhost and bare IPs as path-based (dev/LAN)', () => {
    expect(isPathBasedTenant('localhost')).toBe(true);
    expect(isPathBasedTenant('192.168.1.42')).toBe(true);
  });

  it('treats a real hostname as subdomain-based', () => {
    expect(isPathBasedTenant('demo.sova.cz')).toBe(false);
  });
});

describe('subdomainSlug', () => {
  const mainDomain = 'sova.cz';

  it('extracts the slug from a subdomain of the configured main domain', () => {
    expect(subdomainSlug('demo.sova.cz', mainDomain)).toBe('demo');
  });

  it('returns null for the bare main domain (no tenant — landing/SuperAdmin)', () => {
    expect(subdomainSlug('sova.cz', mainDomain)).toBeNull();
  });

  it('returns null for a hostname that is not under the configured main domain', () => {
    expect(subdomainSlug('example.com', mainDomain)).toBeNull();
  });

  /** The whole point of matching against the configured domain instead of
   * counting segments: a 3-label main domain (e.g. a ccTLD like sova.co.uk)
   * must not be mistaken for "a subdomain of a 2-label domain". */
  it('works with a multi-label main domain, not just a 2-label one', () => {
    expect(subdomainSlug('demo.sova.co.uk', 'sova.co.uk')).toBe('demo');
    expect(subdomainSlug('sova.co.uk', 'sova.co.uk')).toBeNull();
  });

  it('takes only the first label when there are multiple subdomain levels', () => {
    expect(subdomainSlug('demo.staging.sova.cz', mainDomain)).toBe('demo');
  });
});
