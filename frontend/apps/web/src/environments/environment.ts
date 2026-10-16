/** Dev default. Swapped for environment.prod.ts in production builds (see
 * fileReplacements in project.json). */
export const environment = {
  production: false,
  /** The school's subdomain is `<slug>.<mainDomain>` in production — see
   * school-context.ts isPathBasedTenant()/subdomainSlug(). Placeholder until
   * a real domain is registered; update both environment files once one is. */
  mainDomain: 'sova.cz',
};
