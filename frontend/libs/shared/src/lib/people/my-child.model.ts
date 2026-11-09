import { VisibilitySetting } from './visibility.model';

/** "Mé děti" — a parent's child and who sees the child's data. */
export interface MyChild {
  studentId: number;
  name: string;
  className: string;
  visibility: VisibilitySetting[];
  nickname: string | null;
  /** The child's own, waiting for the parent's approval. */
  nicknameProposal: string | null;
}
