import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ProblemDetails } from '../models/problem-details.model';
import { ToastService } from '../../shared/toast/toast.service';
import { environment } from '../../../environments/environment';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!req.url.startsWith(environment.apiBaseUrl)) {
    return next(req);
  }

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 401) {
          auth.logout();
          toast.error('Your session has expired. Please sign in again.');
          void router.navigate(['/login']);
        } else if (error.status === 0) {
          toast.error('Network error — the API could not be reached.');
        } else {
          toast.error(describeProblem(error));
        }
      }

      return throwError(() => error);
    }),
  );
};

function describeProblem(error: HttpErrorResponse): string {
  const problem = error.error as ProblemDetails | undefined;

  if (problem?.errors) {
    const firstField = Object.keys(problem.errors)[0];
    const firstMessage = problem.errors[firstField]?.[0];
    if (firstMessage) {
      return firstMessage;
    }
  }

  return problem?.detail ?? problem?.title ?? `Request failed (${error.status}).`;
}
