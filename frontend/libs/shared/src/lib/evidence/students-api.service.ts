import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Permission } from '../auth/role.model';
import { StudentReach } from './student-reach';
import { CreateStudentRequest, Student } from './student.model';

@Injectable({ providedIn: 'root' })
export class StudentsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getStudents(): Observable<Student[]> {
    return this.http.get<Student[]>(`${this.baseUrl}/api/students`);
  }

  /** Whom (and in which subjects) the caller's `permission` reaches. */
  getReach(permission: Permission): Observable<StudentReach> {
    return this.http.get<StudentReach>(`${this.baseUrl}/api/students/reach`, {
      params: { permission },
    });
  }

  addStudent(request: CreateStudentRequest): Observable<Student> {
    return this.http.post<Student>(`${this.baseUrl}/api/students`, request);
  }

  updateStudent(
    id: number,
    request: CreateStudentRequest,
  ): Observable<Student> {
    return this.http.put<Student>(
      `${this.baseUrl}/api/students/${id}`,
      request,
    );
  }

  /** Also removes the student's grades, absences, messages and parent
   * links — see StudentsController.DeleteStudent. */
  deleteStudent(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/students/${id}`);
  }
}
