import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
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

type Step = 'checking' | 'type' | 'school' | 'contact' | 'role' | 'generating';

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
 * (`?reset=<slug>`): same questions, same demo address. */
@Component({
  selector: 'app-demo-entry-page',
  imports: [
    FormsModule,
    Button,
    Card,
    Checkbox,
    InputText,
    Message,
    ProgressBar,
  ],
  templateUrl: './demo-entry-page.html',
})
export class DemoEntryPage {
  private readonly demoApi = inject(DemoApiService);
  private readonly lookup = inject(SchoolLookupService);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;

  /** Set when resetting an existing demo instead of creating one. */
  protected readonly resetSlug = isDemoSlug(this.query.get('reset'))
    ? this.query.get('reset')
    : null;

  protected readonly step = signal<Step>('checking');
  protected readonly error = signal<string | null>(null);
  protected readonly progress = signal(0);
  protected readonly progressLabel = signal(PROGRESS_LABELS[0]);

  protected readonly type = signal<SchoolType>('Elementary');
  protected name = DEFAULT_NAMES.Elementary;
  protected readonly classesPerGrade = signal(2);
  protected readonly role = signal<DemoRole>('Teacher');
  protected email = '';
  protected consent = false;

  protected readonly sizes = computed(() =>
    [1, 2, 3].map((perGrade) => ({
      perGrade,
      classes: perGrade * GRADES[this.type()],
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
    if (this.name === DEFAULT_NAMES[this.type()]) {
      this.name = DEFAULT_NAMES[type];
    }
    this.type.set(type);
    this.step.set('school');
  }

  protected back(): void {
    const order: Step[] = ['type', 'school', 'contact', 'role'];
    const index = order.indexOf(this.step());
    if (index > 0) {
      this.step.set(order[index - 1]);
    }
  }

  protected emailInvalid(): boolean {
    return (
      this.email.trim().length > 0 &&
      !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(this.email.trim())
    );
  }

  protected create(): void {
    this.error.set(null);
    this.step.set('generating');
    const timer = this.startProgress();
    const request = {
      type: this.type(),
      name: this.name.trim(),
      classesPerGrade: this.classesPerGrade(),
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
        this.step.set('role');
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
    const userId = demo.featuredUserIds[this.role()];
    window.location.assign(schoolUrl(demo.slug, `login?user=${userId}`));
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
