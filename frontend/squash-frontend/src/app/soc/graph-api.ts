import { Service, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { GraphIncident, GraphQueryResult } from './soc-models';

// GraphService named queries via the Gateway.
@Service()
export class GraphApi {
  private readonly http = inject(HttpClient);

  // Incidents are the row columns of GraphQueries.Incidents.
  getIncidents(status?: string): Observable<GraphIncident[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    return this.http
      .get<GraphQueryResult>(`${API_BASE_URL}/api/graph/incidents`, { params })
      .pipe(map((r) => r.rows as unknown as GraphIncident[]));
  }
}
