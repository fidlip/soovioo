import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { ProgressBar } from '@openng/optimus-ui/progressbar';
import {
  DemoApiService,
  DemoCreated,
  DemoRole,
  SchoolType,
} from '@soovioo/shared';
import { catchError, of } from 'rxjs';
import { SchoolLookupService } from '../school-lookup.service';
import { isDemoSlug, schoolUrl } from '../school-context';
import { storeDemoSlug, storedDemoSlug } from './demo-storage';
import { FloatField } from '../forms';

type Step = 'checking' | 'type' | 'school' | 'contact' | 'role' | 'generating';

const STEPS: Step[] = ['type', 'school', 'contact', 'role'];

const TYPES: SchoolType[] = ['Elementary', 'Secondary'];

const DEFAULT_NAMES: Record<SchoolType, string> = {
  Elementary: 'ZŠ Ukázkov',
  Secondary: 'Gymnázium Ukázkov',
};

const GRADES: Record<SchoolType, number> = { Elementary: 9, Secondary: 4 };

const PROGRESS_LABELS = [
  'Zakládám třídy a učebny…',
  'Zapisuji žáky a jejich rodiny…',
  'Skládám rozvrh…',
  'Rozdávám známky…',
  'Zapisuji docházku…',
  'Posílám první zprávy…',
  'Chystám kalendář a platby…',
];

/** "Vyzkoušet": demo.soovioo.cloud. A returning visitor goes straight back
 * to their demo; a new one answers a few questions and gets a freshly
 * generated school, logged in as whoever they chose. Also the reset flow
 * (`?reset=<slug>`): same questions, same demo address.
 *
 * Picking an option moves on (the last one creates the demo); arrows pick,
 * Enter confirms, Backspace goes a step back. */
@Component({
  selector: 'app-demo-entry-page',
  imports: [
    FloatField,
    FormsModule,
    Button,
    Card,
    Checkbox,
    InputText,
    Message,
    ProgressBar,
  ],
  templateUrl: './demo-entry-page.html',
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class DemoEntryPage {
  private readonly demoApi = inject(DemoApiService);
  private readonly lookup = inject(SchoolLookupService);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** Set when resetting an existing demo instead of creating one. */
  protected readonly resetSlug = isDemoSlug(this.query.get('reset'))
    ? this.query.get('reset')
    : null;

  protected readonly step = signal<Step>('checking');
  protected readonly error = signal<string | null>(null);
  protected readonly progress = signal(0);
  protected readonly progressLabel = signal(PROGRESS_LABELS[0]);

  protected readonly type = signal<SchoolType | null>(null);
  protected name = '';
  protected readonly classesPerGrade = signal<number | null>(null);
  protected readonly role = signal<DemoRole | null>(null);
  protected email = '';
  protected consent = false;

  protected readonly sizes = computed(() =>
    [1, 2, 3].map((perGrade) => ({
      perGrade,
      classes: perGrade * GRADES[this.type() ?? 'Elementary'],
    })),
  );

  protected readonly roles = computed(() => {
    const pupil = this.type() === 'Secondary' ? 'student' : 'žák';
    return [
      {
        role: 'Admin' as const,
        label: 'Vedení školy',
        hint: 'administrace, nastavení, všechny agendy',
        icon: 'pi-briefcase',
      },
      {
        role: 'Teacher' as const,
        label: 'Učitel',
        hint: 'třídní učitel — známky, docházka, zprávy',
        icon: 'pi-book',
      },
      {
        role: 'Parent' as const,
        label: 'Rodič',
        hint: 'rodič dvou dětí — přehled, omluvenky, platby',
        icon: 'pi-users',
      },
      {
        role: 'Student' as const,
        label: pupil[0].toUpperCase() + pupil.slice(1),
        hint: `pohled ${pupil === 'žák' ? 'žáka' : 'studenta'} — rozvrh, úkoly, známky`,
        icon: 'pi-user',
      },
    ];
  });

  constructor() {
    const stored = storedDemoSlug();
    if (this.resetSlug || this.query.has('new') || !stored) {
      this.step.set('type');
      return;
    }
    // Returning visitor: back to their demo, if it still exists.
    this.lookup
      .lookup(stored)
      .pipe(catchError(() => of(null)))
      .subscribe((school) => {
        if (school?.isDemo) {
          window.location.assign(schoolUrl(stored, 'login'));
          return;
        }
        storeDemoSlug(null);
        this.step.set('type');
      });
  }

  protected chooseType(type: SchoolType): void {
    this.selectType(type);
    this.goTo('school');
  }

  protected chooseSize(perGrade: number): void {
    if (!this.name.trim()) {
      return;
    }
    this.classesPerGrade.set(perGrade);
    this.goTo('contact');
  }

  protected chooseRole(role: DemoRole): void {
    this.role.set(role);
    this.create();
  }

  protected toRole(): void {
    if (!this.emailInvalid()) {
      this.goTo('role');
    }
  }

  protected back(): void {
    const index = STEPS.indexOf(this.step());
    if (index > 0) {
      this.goTo(STEPS[index - 1]);
    }
  }

  /** Enter in the name field hands over to the size options. */
  protected focusOptions(event: Event): void {
    // Otherwise the same Enter would click the option it lands on.
    event.preventDefault();
    this.optionButtons()[0]?.focus();
  }

  protected onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (
      event.defaultPrevented ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      target?.closest('input, textarea, select, [contenteditable]')
    ) {
      return;
    }
    if (event.key === 'Backspace') {
      if (STEPS.indexOf(this.step()) > 0) {
        event.preventDefault();
        this.back();
      }
      return;
    }
    const choice = this.currentChoice();
    if (!choice) {
      return;
    }
    if (event.key === 'Enter') {
      // On an option or a button Enter clicks it already.
      if (!target?.closest('button, a') && choice.index >= 0) {
        event.preventDefault();
        this.optionButtons()[choice.index]?.click();
      }
      return;
    }
    const buttons = this.optionButtons();
    const columns = buttons.filter(
      (b) => b.offsetTop === buttons[0]?.offsetTop,
    ).length;
    const step =
      { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -columns, ArrowDown: columns }[
        event.key
      ] ?? 0;
    if (!step || buttons.length === 0) {
      return;
    }
    event.preventDefault();
    const focused = buttons.indexOf(document.activeElement as HTMLElement);
    const from = focused >= 0 ? focused : choice.index;
    const next =
      from < 0 ? 0 : Math.min(buttons.length - 1, Math.max(0, from + step));
    choice.select(next);
    buttons[next].focus();
  }

  protected emailInvalid(): boolean {
    return (
      this.email.trim().length > 0 &&
      !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(this.email.trim())
    );
  }

  private create(): void {
    const type = this.type();
    const classesPerGrade = this.classesPerGrade();
    if (!type || !classesPerGrade) {
      return;
    }
    this.error.set(null);
    this.step.set('generating');
    const timer = this.startProgress();
    const request = {
      type,
      name: this.name.trim(),
      classesPerGrade,
      email: this.email.trim() || undefined,
      consent: this.consent,
    };
    const call = this.resetSlug
      ? this.demoApi.reset(this.resetSlug, request)
      : this.demoApi.create(request);
    call.subscribe({
      next: (demo) => {
        clearInterval(timer);
        this.progress.set(100);
        this.open(demo);
      },
      error: (err: unknown) => {
        clearInterval(timer);
        this.goTo('role');
        this.error.set(
          err instanceof HttpErrorResponse && err.status === 429
            ? 'Dnes už jste vytvořili několik ukázkových škol. Zkuste to prosím zítra, nebo nám napište na info@soovioo.cz.'
            : err instanceof HttpErrorResponse &&
                typeof err.error === 'string' &&
                err.error
              ? err.error
              : 'Ukázkovou školu se nepodařilo vytvořit. Zkuste to prosím znovu.',
        );
      },
    });
  }

  /** Straight into the demo, logged in as the chosen role's user. */
  private open(demo: DemoCreated): void {
    storeDemoSlug(demo.slug);
    const userId = demo.featuredUserIds[this.role() as DemoRole];
    window.location.assign(schoolUrl(demo.slug, `login?user=${userId}`));
  }

  private selectType(type: SchoolType): void {
    const previous = this.type();
    if (!this.name || (previous && this.name === DEFAULT_NAMES[previous])) {
      this.name = DEFAULT_NAMES[type];
    }
    this.type.set(type);
  }

  /** The options of the current step, the picked one and how to pick. */
  private currentChoice(): {
    index: number;
    select: (index: number) => void;
  } | null {
    switch (this.step()) {
      case 'type':
        return {
          index: TYPES.indexOf(this.type() as SchoolType),
          select: (i) => this.selectType(TYPES[i]),
        };
      case 'school': {
        const sizes = this.sizes().map((s) => s.perGrade);
        return {
          index: sizes.indexOf(this.classesPerGrade() as number),
          select: (i) => this.classesPerGrade.set(sizes[i]),
        };
      }
      case 'role': {
        const roles = this.roles().map((r) => r.role);
        return {
          index: roles.indexOf(this.role() as DemoRole),
          select: (i) => this.role.set(roles[i]),
        };
      }
      default:
        return null;
    }
  }

  private optionButtons(): HTMLElement[] {
    return Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>(
        '.demo-option:not(:disabled)',
      ),
    );
  }

  /** Coming back to a step, its earlier pick has the focus. */
  private goTo(step: Step): void {
    this.step.set(step);
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>('.demo-option.selected')
          ?.focus(),
      { injector: this.injector },
    );
  }

  /** Generation takes a few seconds without reporting progress — the bar
   * creeps towards the end and the label tells what's (roughly) going on. */
  private startProgress(): ReturnType<typeof setInterval> {
    this.progress.set(3);
    let ticks = 0;
    const timer = setInterval(() => {
      ticks++;
      this.progress.update((p) => Math.min(95, p + (100 - p) * 0.12));
      this.progressLabel.set(
        PROGRESS_LABELS[
          Math.min(PROGRESS_LABELS.length - 1, Math.floor(ticks / 2))
        ],
      );
    }, 400);
    return timer;
  }
}
