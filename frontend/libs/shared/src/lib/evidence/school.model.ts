export interface School {
  id: number;
  name: string;
  slug: string;
  createdAt: string;
}

export interface CreateSchoolRequest {
  name: string;
  slug: string;
  adminUsername: string;
  adminPassword: string;
}
