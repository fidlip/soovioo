/** Generic page of results — mirrors backend Soovioo.Api.Common.PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
