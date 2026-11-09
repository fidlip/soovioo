import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Popover } from '@openng/optimus-ui/popover';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import {
  SetVisibilityRequest,
  VISIBILITY_SCOPES,
  VisibilityField,
  VisibilityOwner,
  VisibilityScope,
  VisibilitySetting,
} from '@soovioo/shared';
import {
  VISIBILITY_AUDIENCE_LABELS,
  VISIBILITY_FIELD_LABELS,
  VISIBILITY_OWNER_ROLE_LABELS,
  VISIBILITY_SCOPE_ICONS,
  scopeLabel,
  scopeRank,
  scopeShort,
  widestScope,
  widestScopeFor,
} from '../visibility-options';

interface ScopeOption {
  label: string;
  value: VisibilityScope;
  disabled: boolean;
}

interface Row {
  setting: VisibilitySetting;
  label: string;
  options: ScopeOption[];
}

interface Group {
  owner: VisibilityOwner;
  label: string;
  rows: Row[];
}

/** Sits next to a field of someone's data: shows who sees it, and opens
 * a popover to choose — per audience, within the school's range and never
 * wider than the name ("Viditelnost údajů"). Renders nothing for a field
 * without settings. */
@Component({
  selector: 'app-visibility-control',
  imports: [FormsModule, Button, Popover, SelectButton, Tooltip],
  templateUrl: './visibility-control.html',
})
export class VisibilityControl {
  /** All of the person's settings — the name's scope caps the others. */
  readonly settings = input.required<VisibilitySetting[]>();
  readonly field = input.required<VisibilityField>();
  readonly changed = output<SetVisibilityRequest>();

  protected readonly own = computed(() =>
    this.settings().filter((s) => s.field === this.field()),
  );

  protected readonly scope = computed(() =>
    widestScope(this.own().map((s) => s.scope)),
  );

  protected readonly editable = computed(() =>
    this.own().some((s) => s.editable),
  );

  protected readonly fieldLabel = computed(
    () => VISIBILITY_FIELD_LABELS[this.field()],
  );

  protected readonly icon = computed(
    () => VISIBILITY_SCOPE_ICONS[this.scope()],
  );
  protected readonly short = computed(() =>
    scopeShort(
      { field: this.field(), audience: this.own()[0]?.audience ?? 'Parents' },
      this.scope(),
    ),
  );

  /** Who sees regardless of the choice. */
  protected readonly footnote = computed(() => {
    if (this.field() !== 'Nickname') {
      return 'Vedení školy a učitelé vidí podle svých oprávnění.';
    }
    return this.own().some((s) => s.owner === 'Student')
      ? 'Rodiče přezdívku vidí vždy, vedení školy nikdy. Učitel si přezdívky může vypnout.'
      : 'Přezdívku rodiče vidí nejvýš jeho děti.';
  });

  /** "Rodiče: Moje třídy, Žáci: Nikdo" */
  protected readonly summary = computed(() => {
    const parts = this.own().map(
      (s) =>
        `${VISIBILITY_AUDIENCE_LABELS[s.audience]}: ${scopeLabel(s, s.scope)}`,
    );
    return `${this.fieldLabel()} — vidí ${parts.join(', ')}`;
  });

  protected readonly groups = computed<Group[]>(() => {
    const owners = [...new Set(this.own().map((s) => s.owner))];
    return owners.map((owner) => ({
      owner,
      label: owners.length > 1 ? VISIBILITY_OWNER_ROLE_LABELS[owner] : '',
      rows: this.own()
        .filter((s) => s.owner === owner)
        .map((s) => ({
          setting: s,
          label: VISIBILITY_AUDIENCE_LABELS[s.audience],
          options: this.optionsFor(s),
        })),
    }));
  });

  protected choose(setting: VisibilitySetting, scope: VisibilityScope): void {
    if (!scope || scope === setting.scope) {
      return;
    }
    this.changed.emit({
      owner: setting.owner,
      field: setting.field,
      audience: setting.audience,
      scope,
    });
  }

  protected resetToDefault(setting: VisibilitySetting): void {
    this.changed.emit({
      owner: setting.owner,
      field: setting.field,
      audience: setting.audience,
      scope: setting.default,
    });
  }

  /** Outside the school's range, or wider than the name, is disabled. */
  private optionsFor(setting: VisibilitySetting): ScopeOption[] {
    const name =
      setting.field === 'Name'
        ? undefined
        : this.settings().find(
            (s) =>
              s.owner === setting.owner &&
              s.audience === setting.audience &&
              s.field === 'Name',
          );
    const widest = scopeRank(widestScopeFor(setting));
    return VISIBILITY_SCOPES.filter((value) => scopeRank(value) <= widest).map(
      (value) => ({
        label: scopeLabel(setting, value),
        value,
        disabled:
          !setting.editable ||
          scopeRank(value) < scopeRank(setting.min) ||
          scopeRank(value) > scopeRank(setting.max) ||
          (name !== undefined && scopeRank(value) > scopeRank(name.scope)),
      }),
    );
  }
}
