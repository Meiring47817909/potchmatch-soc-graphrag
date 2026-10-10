import { HttpErrorResponse } from '@angular/common/http';

// Shared by every SOC panel: 401 means the token expired, anything else shows the status.
export function describeHttpError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 401) return 'Session expired — please log in again';
    return `Request failed (HTTP ${err.status}${err.statusText ? ' ' + err.statusText : ''})`;
  }
  return 'Request failed';
}
