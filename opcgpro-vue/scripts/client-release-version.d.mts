export interface ClientReleaseVersionInput {
  command: string
  supplied?: string
}

export function resolveClientReleaseVersion(input: ClientReleaseVersionInput): string
