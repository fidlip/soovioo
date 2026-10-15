export interface School {
  id: number;
  name: string;
  slug: string;
  createdAt: string;
  /** Matrika export fields — optional until an admin fills them in. */
  izo: string | null;
  redIzo: string | null;
}

export interface CreateSchoolRequest {
  name: string;
  slug: string;
  adminUsername: string;
  adminPassword: string;
  izo?: string;
  redIzo?: string;
}

export interface SetSchoolMatrikaIdsRequest {
  izo: string | null;
  redIzo: string | null;
}
