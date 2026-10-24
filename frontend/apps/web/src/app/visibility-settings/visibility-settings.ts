import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Select } from '@openng/optimus-ui/select';
import {
  MySchoolApiService,
  VisibilityOwner,
  VisibilityRule,
  VisibilityScope,
} from '@soovioo/shared';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';
import {
  VISIBILITY_AUDIENCE_LABELS,
  VISIBILITY_FIELDS,
  VISIBILITY_FIELD_LABELS,
  VISIBILITY_OWNER_LABELS,
  VISIBILITY_SCOPE_LABELS,
  scopeRank,
  scopesBetween,
} from '../visibility-options';

type Bound = 'min' | 'default' | 'max';

/** Administrace → Viditelnost údajů: for every field of staff, parents
 * and pupils, the range individuals choose from next to their data and
 * the default until they do. The API enforces it — what isn't shared
 * never leaves the backend. */
@Component({
  selector: 'app-visibility-settings',
  imports: [FormsModule, Button, Card, Select, InfoHint, TermPipe],
  templateUrl: './visibility-settings.html',
})
export class VisibilitySettings {
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly notify = inject(NotifyService);

  /** All rules as loaded — photo ones, not shown yet, are saved as they are. */
  protected readonly rules = signal<VisibilityRule[] | null>(null);

  protected readonly groups = computed(() => {
    const rules = this.rules();
    if (!rules) {
      return [];
    }
    const owners: VisibilityOwner[] = ['Staff', 'Parent', 'Student'];
    return owners.map((owner) => ({
      owner,
      label: VISIBILITY_OWNER_LABELS[owner],
      rules: rules.filter(
        (r) => r.owner === owner && VISIBILITY_FIELDS.includes(r.field),
      ),
    }));
  });

  protected readonly fieldLabels = VISIBILITY_FIELD_LABELS;
  protected readonly audienceLabels = VISIBILITY_AUDIENCE_LABELS;
  protected readonly bounds: Bound[] = ['min', 'default', 'max'];
  protected readonly boundLabels: Record<Bound, string> = {
    min: 'Nejméně',
    default: 'Výchozí',
    max: 'Nejvíce',
  };

  constructor() {
    this.mySchoolApi.getVisibility().subscribe({
      next: (settings) => this.rules.set(settings.rules),
      error: (err) => this.notify.actionFailed('getVisibility failed', err),
    });
  }

  protected options(
    rule: VisibilityRule,
    bound: Bound,
  ): { label: string; value: VisibilityScope }[] {
    const scopes =
      bound === 'default'
        ? scopesBetween(rule.min, rule.max)
        : scopesBetween('Nobody', 'School');
    return scopes.map((value) => ({
      label: VISIBILITY_SCOPE_LABELS[value],
      value,
    }));
  }

  protected inputId(rule: VisibilityRule, bound: Bound): string {
    return `vis-${rule.owner}-${rule.field}-${rule.audience}-${bound}`;
  }

  /** Keeps min ≤ default ≤ max: the bound just set wins. */
  protected set(rule: VisibilityRule, bound: Bound, scope: VisibilityScope) {
    const next = { ...rule, [bound]: scope };
    const rank = scopeRank;
    if (bound === 'min') {
      if (rank(next.default) < rank(scope)) next.default = scope;
      if (rank(next.max) < rank(scope)) next.max = scope;
    } else if (bound === 'max') {
      if (rank(next.default) > rank(scope)) next.default = scope;
      if (rank(next.min) > rank(scope)) next.min = scope;
    }
    this.rules.update(
      (rules) => rules?.map((r) => (r === rule ? next : r)) ?? null,
    );
  }

  protected save(): void {
    const rules = this.rules();
    if (!rules) {
      return;
    }
    this.mySchoolApi.setVisibility({ rules }).subscribe({
      next: (saved) => {
        this.rules.set(saved.rules);
        this.notify.success('Uloženo.');
      },
      error: (err) => this.notify.actionFailed('setVisibility failed', err),
    });
  }
}
