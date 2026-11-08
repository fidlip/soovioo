import { inject } from '@angular/core';
import { AbsencesApiService, ExcuseAttachment } from '@soovioo/shared';

/** What an omluvenka takes: a doctor's note as a PDF or a photo. */
export const EXCUSE_FILE_EXTENSIONS = [
  '.pdf',
  '.jpg',
  '.jpeg',
  '.png',
  '.webp',
  '.heic',
  '.heif',
];
export const EXCUSE_FILE_MAX_BYTES = 10 * 1024 * 1024;

/** Null when the file can go with an omluvenka, the reason otherwise. */
export function excuseFileError(file: File): string | null {
  if (file.size > EXCUSE_FILE_MAX_BYTES) {
    return 'Soubor je příliš velký (max 10 MB).';
  }
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
  return EXCUSE_FILE_EXTENSIONS.includes(extension)
    ? null
    : 'Přiložit jde PDF nebo obrázek.';
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Downloads an omluvenka's file (same blob pattern as homework). Create
 * in an injection context. */
export function excuseFileDownloader(): (
  excuseId: number,
  attachment: ExcuseAttachment,
) => void {
  const api = inject(AbsencesApiService);
  return (excuseId, attachment) =>
    api.downloadExcuseAttachment(excuseId, attachment.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = attachment.fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => console.error('downloadExcuseAttachment failed', err),
    });
}
