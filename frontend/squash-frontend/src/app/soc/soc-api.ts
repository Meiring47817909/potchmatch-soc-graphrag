import { Service, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { AlertFilter, EventFilter, SocAlert, SocEvent } from './soc-models';

// SocService events and alerts via the Gateway (Admin only).
@Service()
export class SocApi {
  private readonly http = inject(HttpClient);

  getAlerts(filter: AlertFilter = {}): Observable<SocAlert[]> {
    let params = new HttpParams();
    if (filter.status) params = params.set('status', filter.status);
    if (filter.severity) params = params.set('severity', filter.severity);
    if (filter.ruleName) params = params.set('ruleName', filter.ruleName);
    return this.http.get<SocAlert[]>(`${API_BASE_URL}/api/soc/alerts`, { params });
  }

  getEvents(filter: EventFilter = {}): Observable<SocEvent[]> {
    let params = new HttpParams();
    if (filter.type) params = params.set('type', filter.type);
    if (filter.user) params = params.set('user', filter.user);
    if (filter.excludeType) params = params.set('excludeType', filter.excludeType);
    if (filter.alertId) params = params.set('alertId', filter.alertId);
    if (filter.page) params = params.set('page', filter.page);
    if (filter.pageSize) params = params.set('pageSize', filter.pageSize);
    return this.http.get<SocEvent[]>(`${API_BASE_URL}/api/soc/events`, { params });
  }

  setStatus(id: string, status: 'ACKNOWLEDGED' | 'RESOLVED'): Observable<SocAlert> {
    return this.http.patch<SocAlert>(`${API_BASE_URL}/api/soc/alerts/${id}/status`, { status });
  }
}
