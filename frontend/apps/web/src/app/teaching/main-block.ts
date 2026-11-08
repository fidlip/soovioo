import { MainBlock, TeachingClass, mainBlockText } from '@soovioo/shared';

/** A main block being edited; an end null = as the level above. */
export interface MainBlockEdit {
  from: number | null;
  to: number | null;
}

export function ownMainBlock(edit: MainBlockEdit | undefined): boolean {
  return (edit?.from ?? null) !== null || (edit?.to ?? null) !== null;
}

/** The edited block, its empty ends `inherited`'s. */
export function resolveMainBlock(
  edit: MainBlockEdit | undefined,
  inherited: MainBlock,
): MainBlock {
  return { from: edit?.from ?? inherited.from, to: edit?.to ?? inherited.to };
}

/** Lessons a week for the whole class the block needs: each of its periods
 * every school day. */
export function mainBlockLessons(block: MainBlock): number {
  return Math.max(0, block.to - block.from + 1) * 5;
}

/** "podle ročníku", "vlastní · ročník 1.–4. hodina". */
export function classMainBlockNote(c: TeachingClass): string {
  if (!c.ownMainBlock) {
    return c.grade === null ? 'podle školy' : 'podle ročníku';
  }
  return `vlastní · ${c.grade === null ? 'škola' : 'ročník'} ${mainBlockText(c.inheritedMainBlock)}`;
}
