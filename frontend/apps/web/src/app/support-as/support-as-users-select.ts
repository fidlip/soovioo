import { Component, inject, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { AuthApiService, SupportApiService } from '@soovioo/shared';
import { FloatField } from '../forms';

interface Option {
  id: number;
  name: string;
  roles: string;
  me: boolean;
}

/** The school's users to name whom the support may enter as — the one
 * choosing first, the rest by name. */
@Component({
  selector: 'app-support-as-users-select',
  imports: [FloatField, FormsModule, MultiSelect],
  templateUrl: './support-as-users-select.html',
})
export class SupportAsUsersSelect {
  private readonly api = inject(SupportApiService);
  private readonly myId = inject(AuthApiService).getUserId();

  readonly inputId = input('supportAsUsers');
  readonly userIds = model<number[]>([]);

  protected readonly options = signal<Option[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.api.getSupportAsUsers().subscribe({
      next: (users) => {
        const options = users.map((u) => ({
          id: u.id,
          name: u.name,
          roles: u.roles.map((r) => r.name).join(', '),
          me: u.id === this.myId,
        }));
        this.options.set([
          ...options.filter((o) => o.me),
          ...options.filter((o) => !o.me),
        ]);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('getSupportAsUsers failed', err);
        this.loading.set(false);
      },
    });
  }
}
