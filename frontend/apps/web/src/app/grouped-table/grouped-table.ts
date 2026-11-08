import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  DestroyRef,
  TemplateRef,
  computed,
  contentChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Loading } from '../loading/loading';

/** One grouping level — rows sharing the same `key` (and the same keys on
 * every level above) belong to one group. */
export interface GroupLevel<T> {
  /** Column header on desktop. */
  label: string;
  key: (row: T) => string | number;
  /** Shown in the group column (desktop) / as the group heading (mobile). */
  text: (row: T) => string;
}

interface GroupedRow<T> {
  row: T;
  /** Per level: does this row start a new group (= show the value)? */
  starts: boolean[];
  /** Any group changes here (always true without groups) — row divider. */
  startsAnyGroup: boolean;
}

interface GroupNode<T> {
  key: string | number;
  text: string;
  children: GroupNode<T>[];
  rows: T[];
}

const DESKTOP_QUERY = '(min-width: 768px)';

/** Table for rows pre-sorted by their group keys (no paid grid offers this
 * for free — AG Grid has row grouping only in Enterprise, Optimus/PrimeNG's
 * p-table groups by a single field). Desktop: one table, group columns first,
 * a group value repeated on the following rows is left blank. Mobile: nested
 * list (group → subgroup → rows), since wide tables don't fit there.
 *
 * Content templates: `#headerCells` (the `<th>`s of the non-group columns),
 * `#rowCells` (their `<td>`s, context = row) and `#mobileRow` (one row on
 * mobile, context = row). Optional: `#groupCell` (a group value, context =
 * row + `level` index; default = the level's `text`) and `#rowDetail` (shown
 * under a row while `isExpanded(row)`; rows then emit `rowClick`). */
@Component({
  selector: 'app-grouped-table',
  imports: [Loading, NgTemplateOutlet],
  templateUrl: './grouped-table.html',
})
export class GroupedTable<T> {
  readonly rows = input.required<T[]>();
  readonly groups = input<GroupLevel<T>[]>([]);
  readonly rowKey = input.required<(row: T) => unknown>();
  /** The rows' element ids, for `highlightElement`. */
  readonly rowElementId = input<(row: T) => string | null>(() => null);
  readonly emptyMessage = input('Žádné záznamy.');
  /** The rows haven't arrived yet — "Načítání…" instead of `emptyMessage`. */
  readonly loading = input(false);
  /** With a `#rowDetail` template: which rows show their detail. */
  readonly isExpanded = input<(row: T) => boolean>(() => false);
  readonly rowClick = output<T>();

  protected readonly headerCells =
    contentChild<TemplateRef<unknown>>('headerCells');
  protected readonly rowCells =
    contentChild<TemplateRef<{ $implicit: T }>>('rowCells');
  protected readonly mobileRow =
    contentChild<TemplateRef<{ $implicit: T }>>('mobileRow');
  protected readonly groupCell =
    contentChild<TemplateRef<{ $implicit: T; level: number }>>('groupCell');
  protected readonly rowDetail =
    contentChild<TemplateRef<{ $implicit: T }>>('rowDetail');

  protected readonly isDesktop = signal(true);

  protected readonly groupedRows = computed<GroupedRow<T>[]>(() => {
    const groups = this.groups();
    let previous: T | null = null;
    return this.rows().map((row) => {
      const starts: boolean[] = [];
      let changed = previous === null;
      for (const level of groups) {
        changed ||= level.key(row) !== level.key(previous as T);
        starts.push(changed);
      }
      previous = row;
      return {
        row,
        starts,
        startsAnyGroup: starts.length === 0 || starts[starts.length - 1],
      };
    });
  });

  protected onRowClick(row: T): void {
    if (this.rowDetail()) {
      this.rowClick.emit(row);
    }
  }

  protected readonly tree = computed(() => this.buildTree(this.rows(), 0));

  constructor() {
    const media =
      typeof window !== 'undefined' && window.matchMedia
        ? window.matchMedia(DESKTOP_QUERY)
        : null;
    if (media) {
      this.isDesktop.set(media.matches);
      const listener = (e: MediaQueryListEvent) =>
        this.isDesktop.set(e.matches);
      media.addEventListener('change', listener);
      inject(DestroyRef).onDestroy(() =>
        media.removeEventListener('change', listener),
      );
    }
  }

  /** Consecutive runs, not a global group-by — rows come pre-sorted and the
   * desktop table must show the same order. */
  private buildTree(rows: T[], depth: number): GroupNode<T>[] {
    const level = this.groups()[depth];
    if (!level) {
      return [];
    }
    const nodes: GroupNode<T>[] = [];
    for (const row of rows) {
      const key = level.key(row);
      let node = nodes[nodes.length - 1];
      if (!node || node.key !== key) {
        node = { key, text: level.text(row), children: [], rows: [] };
        nodes.push(node);
      }
      node.rows.push(row);
    }
    for (const node of nodes) {
      node.children = this.buildTree(node.rows, depth + 1);
    }
    return nodes;
  }
}
