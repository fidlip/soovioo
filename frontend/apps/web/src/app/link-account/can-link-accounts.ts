import { AuthApiService } from '@soovioo/shared';
import { isDemoSlug } from '../school-context';

/** A person's own accounts: not for the SuperAdmin, support mode or a demo
 * school, which the API refuses anyway. */
export function canLinkAccounts(
  auth: AuthApiService,
  schoolSlug: string | null,
): boolean {
  return (
    !auth.isSuperAdmin() &&
    !auth.getSupportInfo() &&
    !auth.mustChangePassword() &&
    !isDemoSlug(schoolSlug)
  );
}
