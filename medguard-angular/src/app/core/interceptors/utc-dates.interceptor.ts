import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';

/** "2026-11-11T11:28:34.4074984": an ISO date-time with no Z and no offset. */
const NAIVE_DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?$/;

/**
 * Every timestamp MedGuard stores is UTC, but SQL Server's datetime2 has no time
 * zone, so older API builds send them without a "Z". `new Date()` reads such a
 * string as LOCAL time, which put every "last seen" and "departed" hours off for
 * anyone outside UTC (a reading from a second ago showed "2h ago" in Cairo).
 * Marking them UTC here fixes every page at once; strings that already carry a
 * zone pass through untouched.
 */
export const utcDatesInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(environment.apiBaseUrl)) return next(req);
  return next(req).pipe(
    map((event) => (event instanceof HttpResponse && event.body ? event.clone({ body: markUtc(event.body) }) : event)),
  );
};

function markUtc(value: unknown): unknown {
  if (typeof value === 'string') return NAIVE_DATE_TIME.test(value) ? `${value}Z` : value;
  if (Array.isArray(value)) return value.map(markUtc);
  if (value && typeof value === 'object') {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value)) out[k] = markUtc(v);
    return out;
  }
  return value;
}
