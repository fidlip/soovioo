export interface MatrikaExportParams {
  cast: string;
  contactName: string;
  contactPhone: string;
  contactEmail: string;
  /** yyyy-MM-dd; omitted = server defaults to today. */
  rdat?: string;
}

export interface MatrikaExportError {
  errors: string[];
}
