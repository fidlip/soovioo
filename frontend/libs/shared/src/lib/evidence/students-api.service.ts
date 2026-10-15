import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateStudentRequest, Student } from './student.model';

@Injectable({ providedIn: 'root' })
export class StudentsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getStudents(): Observable<Student[]> {
    return this.http.get<Student[]>(`${this.baseUrl}/api/students`);
  }

  addStudent(request: CreateStudentRequest): Observable<Student> {
    return this.http.post<Student>(`${this.baseUrl}/api/students`, request);
  }
}
