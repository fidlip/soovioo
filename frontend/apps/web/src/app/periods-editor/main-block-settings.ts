import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import {
  ClassMainBlock,
  GradeMainBlock,
  MainBlock,
  MainBlocks,
  MainBlocksApiService,
} from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** "1.–4. hodina". */
export function mainBlockText(block: MainBlock): string {
  return block.from === block.to
    ? `${block.from}. hodina`
    : `${block.from}.–${block.to}. hodina`;
}

interface Row {
  key: string;
  label: string;
  block: MainBlock;
  /** Null = its own. */
  inherited: string | null;
  own: boolean;
  save: (block: MainBlock | null) => void;
}

/** Hlavní výukový blok: the school's, grades' in place of it and classes'
 * in place of their grade's. */
@Component({
  selector: 'app-main-block-settings',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    InputNumber,
    InfoHint,
    Tooltip,
  ],
  templateUrl: './main-block-settings.html',
})
export class MainBlockSettings {
  private readonly api = inject(MainBlocksApiService);
  private readonly notify = inject(NotifyService);

  private readonly blocks = signal<MainBlocks | null>(null);
  protected schoolFrom = 1;
  protected schoolTo = 4;
  protected readonly editing = signal<string | null>(null);
  protected editFrom: number | null = null;
  protected editTo: number | null = null;
  protected readonly text = mainBlockText;

  protected readonly grades = computed<Row[]>(() => {
    const b = this.blocks();
    return b ? b.grades.map((g) => this.gradeRow(b, g)) : [];
  });

  protected readonly classes = computed<Row[]>(() => {
    const b = this.blocks();
    return b ? b.classes.map((c) => this.classRow(b, c)) : [];
  });

  constructor() {
    this.reload();
  }

  protected saveSchool(): void {
    if (!this.valid(this.schoolFrom, this.schoolTo)) {
      return;
    }
    this.api.setSchool({ from: this.schoolFrom, to: this.schoolTo }).subscribe({
      next: () => {
        this.notify.success('Uloženo.');
        this.reload();
      },
      error: (err) => this.notify.actionFailed('set main block failed', err),
    });
  }

  protected startEdit(row: Row): void {
    this.editing.set(row.key);
    this.editFrom = row.block.from;
    this.editTo = row.block.to;
  }

  protected saveEdit(row: Row): void {
    if (this.valid(this.editFrom, this.editTo)) {
      row.save({ from: this.editFrom!, to: this.editTo! });
    }
  }

  private valid(from: number | null, to: number | null): boolean {
    if (from === null || to === null) {
      this.notify.error('Vyplňte, od které do které hodiny blok je.');
      return false;
    }
    if (to < from) {
      this.notify.error('Blok nemůže končit dřív, než začíná.');
      return false;
    }
    return true;
  }

  private gradeRow(b: MainBlocks, g: GradeMainBlock): Row {
    const own = g.from !== null && g.to !== null;
    return {
      key: `grade-${g.grade}`,
      label: `${g.grade}. ročník`,
      block: own ? { from: g.from!, to: g.to! } : b,
      inherited: own ? null : 'podle školy',
      own,
      save: (block) => this.saved(this.api.setGrade(g.grade, block)),
    };
  }

  private classRow(b: MainBlocks, c: ClassMainBlock): Row {
    const own = c.from !== null && c.to !== null;
    const grade = b.grades.find(
      (g) => g.grade === c.grade && g.from !== null && g.to !== null,
    );
    return {
      key: `class-${c.id}`,
      label: c.name,
      block: own
        ? { from: c.from!, to: c.to! }
        : grade
          ? { from: grade.from!, to: grade.to! }
          : b,
      inherited: own ? null : grade ? 'podle ročníku' : 'podle školy',
      own,
      save: (block) => this.saved(this.api.setClass(c.id, block)),
    };
  }

  private saved(request: ReturnType<MainBlocksApiService['setSchool']>): void {
    request.subscribe({
      next: () => {
        this.editing.set(null);
        this.reload();
      },
      error: (err) => this.notify.actionFailed('set main block failed', err),
    });
  }

  private reload(): void {
    this.api.get().subscribe({
      next: (b) => {
        this.blocks.set(b);
        this.schoolFrom = b.from;
        this.schoolTo = b.to;
      },
      error: (err) => this.notify.actionFailed('get main blocks failed', err),
    });
  }
}
