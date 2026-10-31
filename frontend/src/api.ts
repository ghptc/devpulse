export type Provider = 'LocalGit' | 'GitHub';
export interface Registration { provider: Provider; alias?: string; owner?: string; name?: string }
export interface Repository { id: string; name: string; provider: Provider; scope: string }
export interface Commit { sha: string; message: string; author: string; authoredAt: string }
export interface History { repositoryId: string; provider: Provider; commits: Commit[]; count: number }
export interface Alias { alias: string; name: string }
export class ApiError extends Error { constructor(public status: number, message: string) { super(message); } }
async function request<T>(path: string, signal: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch('/api/v1/' + path, { signal, method: body ? 'POST' : 'GET', headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string };
    throw new ApiError(response.status, problem.title ?? 'The repository service is unavailable.');
  }
  return response.json() as Promise<T>;
}
export const api = {
  aliases: (signal: AbortSignal) => request<Alias[]>('repository-aliases', signal),
  register: (body: Registration, signal: AbortSignal) => request<Repository>('repositories', signal, body),
  history: (id: string, signal: AbortSignal) => request<History>(`repositories/${encodeURIComponent(id)}/commits?limit=20`, signal),
};
