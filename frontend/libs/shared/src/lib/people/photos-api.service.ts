import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { PhotoSize, StudentPhotoState } from './photo.model';

/** Profile photos. They're never public: `<img src>` can't carry the
 * bearer token, so clients load them with `getPhoto` (as a blob). */
@Injectable({ providedIn: 'root' })
export class PhotosApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** `token` = the DTO's `photo` — a new photo has a new one. */
  userPhotoUrl(
    userId: number,
    token: string,
    size: PhotoSize = 'thumb',
  ): string {
    return `${this.baseUrl}/api/photos/users/${userId}/${size}?v=${token}`;
  }

  studentPhotoUrl(
    studentId: number,
    token: string,
    size: PhotoSize = 'thumb',
  ): string {
    return `${this.baseUrl}/api/photos/students/${studentId}/${size}?v=${token}`;
  }

  getPhoto(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' });
  }

  uploadUserPhoto(
    userId: number,
    file: Blob,
    fileName: string,
  ): Observable<{ photo: string | null }> {
    return this.http.put<{ photo: string | null }>(
      `${this.baseUrl}/api/photos/users/${userId}`,
      formData(file, fileName),
    );
  }

  /** For clients without a usable `FormData`/`Blob` (the NativeScript
   * app): the photo's bytes, sent as a hand-built multipart body. */
  getPhotoBytes(url: string): Observable<ArrayBuffer> {
    return this.http.get(url, { responseType: 'arraybuffer' });
  }

  uploadUserPhotoBytes(
    userId: number,
    jpeg: Uint8Array,
  ): Observable<{ photo: string | null }> {
    return this.putJpeg(`${this.baseUrl}/api/photos/users/${userId}`, jpeg);
  }

  uploadStudentPhotoBytes(
    studentId: number,
    jpeg: Uint8Array,
  ): Observable<StudentPhotoState> {
    return this.putJpeg(
      `${this.baseUrl}/api/photos/students/${studentId}`,
      jpeg,
    );
  }

  private putJpeg<T>(url: string, jpeg: Uint8Array): Observable<T> {
    const boundary = `soovioo${Date.now()}`;
    return this.http.put<T>(url, multipartJpeg(jpeg, boundary), {
      headers: {
        'Content-Type': `multipart/form-data; boundary=${boundary}`,
      },
    });
  }

  deleteUserPhoto(userId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/photos/users/${userId}`);
  }

  /** A parent's children, a pupil themselves. */
  getOwnStudents(): Observable<StudentPhotoState[]> {
    return this.http.get<StudentPhotoState[]>(
      `${this.baseUrl}/api/photos/students`,
    );
  }

  getStudentState(studentId: number): Observable<StudentPhotoState> {
    return this.http.get<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}`,
    );
  }

  uploadStudentPhoto(
    studentId: number,
    file: Blob,
    fileName: string,
  ): Observable<StudentPhotoState> {
    return this.http.put<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}`,
      formData(file, fileName),
    );
  }

  approveStudentPhoto(studentId: number): Observable<StudentPhotoState> {
    return this.http.post<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}/approve`,
      {},
    );
  }

  /** Rejected = deleted right away. */
  rejectStudentPhoto(studentId: number): Observable<StudentPhotoState> {
    return this.http.post<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}/reject`,
      {},
    );
  }

  deleteStudentPhoto(studentId: number): Observable<StudentPhotoState> {
    return this.http.delete<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}`,
    );
  }

  /** `givenBy` only when the school records a consent given on paper. */
  giveConsent(
    studentId: number,
    givenBy?: string,
  ): Observable<StudentPhotoState> {
    return this.http.put<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}/consent`,
      { givenBy: givenBy ?? null },
    );
  }

  /** Deletes the photo too. */
  revokeConsent(studentId: number): Observable<StudentPhotoState> {
    return this.http.delete<StudentPhotoState>(
      `${this.baseUrl}/api/photos/students/${studentId}/consent`,
    );
  }
}

function formData(file: Blob, fileName: string): FormData {
  const data = new FormData();
  data.append('file', file, fileName);
  return data;
}

/** One `file` part named `photo.jpg`. */
function multipartJpeg(jpeg: Uint8Array, boundary: string): ArrayBuffer {
  const head = new TextEncoder().encode(
    `--${boundary}\r\n` +
      'Content-Disposition: form-data; name="file"; filename="photo.jpg"\r\n' +
      'Content-Type: image/jpeg\r\n\r\n',
  );
  const tail = new TextEncoder().encode(`\r\n--${boundary}--\r\n`);
  const body = new Uint8Array(head.length + jpeg.length + tail.length);
  body.set(head, 0);
  body.set(jpeg, head.length);
  body.set(tail, head.length + jpeg.length);
  return body.buffer;
}
