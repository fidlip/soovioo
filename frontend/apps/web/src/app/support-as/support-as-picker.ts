import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { RoleRef, SupportAsUser } from '@soovioo/shared';
import { Observable } from 'rxjs';
import { FloatField } from '../forms';
import { Loading } from '@fidlip/soovioo-ui';
import { foldForSearch } from '../text-search';

/** Whom the support is in the school as: a user and one of their roles,
 * or (no user) the school's admin. */
export interface SupportAsPick {
  userId: number | null;
  roleId: number | null;
}

/** At most this many users listed — the filter narrows a big school down. */
const SHOWN = 100;

/** "Vstoupit do školy": the ticket's users the support may be (the author
 * first), filtered by name and role; a user with several roles is picked
 * in one of them. */
@Component({
  selector: 'app-support-as-picker',
  imports: [
    Button,
    Dialog,
    FloatField,
    FormsModule,
    InputText,
    Loading,
    Select,
  ],
  templateUrl: './support-as-picker.html',
})
export class SupportAsPicker {
  readonly header = input('Vstoupit jako…');
  readonly picked = output<SupportAsPick>();

  protected readonly visible = signal(false);
  protected readonly users = signal<SupportAsUser[] | null>(null);
  protected readonly failed = signal(false);
  protected readonly current = signal<SupportAsPick | null>(null);
  protected readonly roleId = signal<number | null>(null);
  protected readonly name = signal('');

  /** Every role someone in the list may be in. */
  protected readonly roles = computed(() => {
    const byId = new Map<number, RoleRef>();
    for (const user of this.users() ?? []) {
      for (const role of user.roles) {
        byId.set(role.id, role);
      }
    }
    return [...byId.values()].sort((a, b) =>
      a.name.localeCompare(b.name, 'cs'),
    );
  });

  private readonly matching = computed(() => {
    const roleId = this.roleId();
    const name = foldForSearch(this.name().trim());
    return (this.users() ?? []).filter(
      (u) =>
        (roleId === null || u.roles.some((r) => r.id === roleId)) &&
        (!name || foldForSearch(u.name).includes(name)),
    );
  });
  protected readonly shown = computed(() => this.matching().slice(0, SHOWN));
  protected readonly more = computed(
    () => this.matching().length - this.shown().length,
  );

  open(
    users: Observable<SupportAsUser[]>,
    current: SupportAsPick | null = null,
  ): void {
    this.users.set(null);
    this.failed.set(false);
    this.current.set(current);
    this.roleId.set(null);
    this.name.set('');
    this.visible.set(true);
    users.subscribe({
      next: (list) => this.users.set(list),
      error: (err) => {
        console.error('support-as users failed', err);
        this.failed.set(true);
      },
    });
  }

  protected isCurrent(userId: number | null, roleId: number | null): boolean {
    const current = this.current();
    return (
      !!current &&
      current.userId === userId &&
      (userId === null || current.roleId === roleId)
    );
  }

  /** The role picked by the filter, else the one clicked. */
  protected pick(userId: number | null, roleId: number | null): void {
    this.visible.set(false);
    this.picked.emit({ userId, roleId });
  }
}
