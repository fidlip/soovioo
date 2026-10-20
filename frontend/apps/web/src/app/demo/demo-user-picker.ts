import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Menu } from '@openng/optimus-ui/menu';
import { Message } from '@openng/optimus-ui/message';
import {
  DemoApiService,
  DemoRole,
  DemoUser,
  SchoolType,
  localizeStudentTerm,
} from '@soovioo/shared';
import { DemoActionsService } from './demo-actions.service';

const ROLE_ORDER: DemoRole[] = ['Admin', 'Teacher', 'Parent', 'Student'];

const ROLE_GROUP_LABELS: Record<DemoRole, string> = {
  Admin: 'Vedení školy',
  Teacher: 'Učitelé',
  Parent: 'Rodiče',
  Student: 'Žáci',
};

const ROLE_LABELS: Record<DemoRole, string> = {
  Admin: 'Vedení školy',
  Teacher: 'Učitel',
  Parent: 'Rodič',
  Student: 'Žák',
};

/** The demo school's login: pick anyone, no password (DemoController). */
@Component({
  selector: 'app-demo-user-picker',
  imports: [FormsModule, Button, InputText, Menu, Message],
  templateUrl: './demo-user-picker.html',
})
export class DemoUserPicker implements OnInit {
  private readonly demoApi = inject(DemoApiService);
  protected readonly actions = inject(DemoActionsService);

  readonly slug = input.required<string>();
  readonly schoolType = input<SchoolType | null>(null);
  /** Emits once the picked user is logged in. */
  readonly loggedIn = output<void>();

  protected readonly users = signal<DemoUser[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busyId = signal<number | null>(null);
  protected readonly roleFilter = signal<DemoRole | null>(null);
  protected readonly search = signal('');

  protected readonly featured = computed(() =>
    (this.users() ?? [])
      .filter((u) => u.featured)
      .sort((a, b) => ROLE_ORDER.indexOf(a.role) - ROLE_ORDER.indexOf(b.role)),
  );

  protected readonly roleFilters = computed(() =>
    ROLE_ORDER.map((role) => ({
      role,
      label: this.term(ROLE_GROUP_LABELS[role]),
      count: (this.users() ?? []).filter((u) => u.role === role).length,
    })),
  );

  /** Long lists (hundreds of parents) are cut — the search finds the rest. */
  protected readonly filtered = computed(() => {
    const role = this.roleFilter();
    const query = normalize(this.search().trim());
    const all = (this.users() ?? []).filter(
      (u) =>
        (!role || u.role === role) &&
        (!query || normalize(`${u.name} ${u.detail}`).includes(query)),
    );
    return { shown: all.slice(0, 60), total: all.length };
  });

  protected readonly menuItems = computed(() => this.actions.menu(this.slug()));

  ngOnInit(): void {
    this.demoApi.users(this.slug()).subscribe({
      next: (users) => this.users.set(users),
      error: () => this.error.set('Uživatele se nepodařilo načíst.'),
    });
  }

  protected roleLabel(role: DemoRole): string {
    return this.term(ROLE_LABELS[role]);
  }

  protected term(text: string): string {
    return localizeStudentTerm(text, this.schoolType());
  }

  protected pick(user: DemoUser): void {
    this.busyId.set(user.id);
    this.error.set(null);
    this.demoApi.login(this.slug(), user.id).subscribe({
      next: () => this.loggedIn.emit(),
      error: () => {
        this.busyId.set(null);
        this.error.set('Přihlášení se nepodařilo.');
      },
    });
  }
}

function normalize(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase();
}
