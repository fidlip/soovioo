import { Injectable, signal } from '@angular/core';

/** Opens the "Propojit další účet" dialog, wherever it's asked from. */
@Injectable({ providedIn: 'root' })
export class LinkAccountDialogService {
  readonly open = signal(false);
}
