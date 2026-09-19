/**
 * Generic envelope every MedGuard.API endpoint returns. `data` holds the
 * actual payload — its shape is given by the generic parameter T.
 */
export interface ApiResponse<T> {
  succeeded: boolean;
  statusCode: number;
  message: string;
  errors: string[];
  meta: string | null;
  data: T;
}

/** Generic paged-result envelope MedGuard.API wraps list endpoints in (e.g. GET /api/alerts). */
export interface PagedResultDto<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
