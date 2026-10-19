import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { AuthApiService } from '@soovioo/shared';
import { SchoolContextService } from '../school-context';
import { FORM_FIELDS } from '../forms';

/** Lands here from the link in an invite e-mail (see `UsersController`'s
 * invite endpoint) — the one place in the app a brand new user, who has
 * never had a password, can set one and get straight in. */
@Component({
  selector: 'app-accept-invite-page',
  imports: [
    ...FORM_FIELDS,FormsModule, Button, Card, Message, Password],
  templateUrl: './accept-invite-page.html',
})
export class AcceptInvitePage {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly schoolContext = inject(SchoolContextService);

  // Read once at construction, same as LoginPage's schoolSlug — this page
  // never needs to react to the token changing underneath it.
  protected readonly token = this.route.snapshot.queryParamMap.get('token');

  protected newPassword = '';
  protected confirmNewPassword = '';
  protected readonly error = signal<string | null>(null);

  protected acceptInvite(): void {
    this.error.set(null);

    if (!this.token) {
      return;
    }
    if (this.newPassword !== this.confirmNewPassword) {
      this.error.set('Nové heslo a jeho potvrzení se neshodují.');
      return;
    }

    this.authApi.acceptInvite(this.token, this.newPassword).subscribe({
      next: () => this.router.navigate(this.schoolContext.link('home')),
      error: (err) =>
        this.error.set(
          (err as { error?: string })?.error ??
            'Pozvánka je neplatná nebo už vypršela.',
        ),
    });
  }
}
