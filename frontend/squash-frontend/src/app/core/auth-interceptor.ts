import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Auth } from '../services/auth';

// Attaches the logged-in player's JWT to every outgoing HTTP request, the same
// way BookingService forwards the caller's token on to PaymentService on the
// backend. Requests made before anyone has logged in (or after logging out)
// just go through unmodified - the backend's own [Authorize] attributes are
// what actually reject those.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const token = auth.token;

  if (!token) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    })
  );
};
