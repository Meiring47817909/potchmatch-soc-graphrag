import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { AskRequest, RagAnswer, RagStatus } from './soc-models';

// RagService via the Gateway (Admin only). Consumed by the Ask panel.
@Service()
export class RagApi {
  private readonly http = inject(HttpClient);

  status(): Observable<RagStatus> {
    return this.http.get<RagStatus>(`${API_BASE_URL}/api/rag/status`);
  }

  ask(req: AskRequest): Observable<RagAnswer> {
    return this.http.post<RagAnswer>(`${API_BASE_URL}/api/rag/ask`, req);
  }
}
