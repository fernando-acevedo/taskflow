export type TenantRole = 'Member' | 'Admin' | 'Owner';

/** A workspace the signed-in user belongs to (`GET /api/tenants/mine`), with the user's role in it. */
export interface Tenant {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly isDemo: boolean;
  readonly role: TenantRole;
}

/** A tenant as the super-admin sees it (`GET /api/tenants`). */
export interface TenantSummary {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly isDemo: boolean;
  readonly createdAt: string;
}

export interface NewTenantInput {
  readonly name: string;
  readonly slug: string;
  readonly ownerEmail?: string;
}

export interface CreatedTenant {
  readonly tenant: TenantSummary;
  readonly owner: { readonly accountCreated: boolean; readonly invitationSent: boolean } | null;
}

/** The header that tells the API which tenant a call is about. */
export const TENANT_HEADER = 'X-Tenant';
