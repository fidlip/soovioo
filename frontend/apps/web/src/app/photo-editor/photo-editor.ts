import { Component, input, output } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { Avatar } from '../avatar/avatar';

/** A photo with "Nahrát" / "Smazat" next to it. The picker takes JPEG,
 * PNG and WebP — on a phone it offers the camera too. The server crops,
 * shrinks and strips the file, so it's sent as picked. */
@Component({
  selector: 'app-photo-editor',
  imports: [Avatar, Button],
  template: `
    <app-avatar [name]="name()" [photoUrl]="photoUrl()" [size]="96" />
    <div class="flex flex-col gap-2">
      @if (canUpload()) {
        <input
          #input
          type="file"
          class="hidden"
          accept="image/jpeg,image/png,image/webp"
          [attr.aria-label]="uploadLabel()"
          (change)="picked($event, input)"
        />
        <p-button
          type="button"
          [label]="uploadLabel()"
          icon="pi pi-camera"
          severity="secondary"
          [outlined]="true"
          size="small"
          (onClick)="input.click()"
        />
      }
      @if (photoUrl() && canDelete()) {
        <p-button
          type="button"
          label="Smazat fotku"
          icon="pi pi-trash"
          severity="danger"
          [text]="true"
          size="small"
          (onClick)="remove.emit()"
        />
      }
    </div>
  `,
  host: { class: 'flex items-center gap-4' },
})
export class PhotoEditor {
  readonly name = input.required<string>();
  readonly photoUrl = input<string | null | undefined>(null);
  readonly canUpload = input(true);
  readonly canDelete = input(true);
  readonly uploadLabel = input('Nahrát fotku');
  readonly upload = output<File>();
  readonly remove = output<void>();

  protected picked(event: Event, element: HTMLInputElement): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    // Cleared so picking the same file again still fires `change`.
    element.value = '';
    if (file) {
      this.upload.emit(file);
    }
  }
}
