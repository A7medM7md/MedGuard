import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse, OrgSettings } from '../models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getSettings(): Observable<OrgSettings> {
    return this.http.get<ApiResponse<OrgSettings>>(`${this.baseUrl}/settings`).pipe(map((res) => res.data));
  }

  updateSettings(settings: OrgSettings): Observable<OrgSettings> {
    return this.http
      .put<ApiResponse<OrgSettings>>(`${this.baseUrl}/settings`, settings)
      .pipe(map((res) => res.data));
  }
}
