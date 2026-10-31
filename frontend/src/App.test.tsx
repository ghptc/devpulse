import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import App from './App';
const repository = { id: 'repo-1', name: 'owner/sample', provider: 'GitHub', scope: 'Default branch: main' };
function fixtures(commits: unknown[] = []) {
  const fetcher = vi.fn().mockImplementation(async (url: string, options?: RequestInit) => {
    if (url.endsWith('repository-aliases')) return new Response(JSON.stringify([{ alias: 'approved', name: 'Approved repository' }]));
    if (options?.method === 'POST') return new Response(JSON.stringify(repository), { status: 201 });
    return new Response(JSON.stringify({ repositoryId: 'repo-1', provider: 'GitHub', count: commits.length, commits }));
  });
  vi.stubGlobal('fetch', fetcher); return fetcher;
}
async function register() {
  fireEvent.change(screen.getByLabelText('Owner'), { target: { value: 'owner' } });
  fireEvent.change(screen.getByLabelText('Repository name'), { target: { value: 'sample' } });
  fireEvent.click(screen.getByRole('button', { name: 'Register and load commits' }));
}
it('shows honest initial state with accessible registration labels', async () => {
  fixtures(); render(<App />);
  expect(screen.getByRole('heading', { name: 'DevPulse' })).toBeVisible();
  expect(screen.getByText('Register a repository to load its commit history.')).toBeVisible();
  expect(screen.getByLabelText('Owner')).toBeVisible();
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
});
it('registers with API and renders actual returned commit metadata', async () => {
  const fetcher = fixtures([{ sha: 'a'.repeat(40), message: 'Real provider subject', author: 'Developer', authoredAt: '2026-10-30T12:00:00Z' }]);
  render(<App />); await register();
  expect(await screen.findByText('Real provider subject')).toBeVisible();
  expect(screen.getByText('Developer')).toBeVisible();
  expect(screen.getByText('aaaaaaaa').closest('[title]')).toHaveAttribute('title', 'a'.repeat(40));
  const post = fetcher.mock.calls.find(([, options]) => options?.method === 'POST');
  expect(JSON.parse(post![1].body)).toEqual({ provider: 'GitHub', owner: 'owner', name: 'sample' });
  expect(fetcher.mock.calls.some(([url]) => url.endsWith('commits?limit=20'))).toBe(true);
});
it('renders empty provider history without mock commits', async () => {
  fixtures(); render(<App />); await register();
  expect(await screen.findByText('This repository has no commits.')).toBeVisible();
});
it('shows provider authorization failure', async () => {
  fixtures(); const original = globalThis.fetch;
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => init?.method === 'POST' ? Promise.resolve(new Response(JSON.stringify({ title: 'GitHub authorization rejected.' }), { status: 403 })) : original(input, init)));
  render(<App />); await register();
  expect(await screen.findByRole('alert')).toHaveTextContent('GitHub authorization rejected.');
});
it('switches to approved alias selection without a path input', async () => {
  fixtures(); render(<App />);
  fireEvent.mouseDown(screen.getByLabelText('Provider'));
  fireEvent.click(await screen.findByText('Local Git'));
  expect(await screen.findByLabelText('Approved local repository')).toBeVisible();
  expect(screen.queryByLabelText('Owner')).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/path/i)).not.toBeInTheDocument();
});
it('validates required identity before submitting', async () => {
  const fetcher = fixtures(); render(<App />);
  fireEvent.click(screen.getByRole('button', { name: 'Register and load commits' }));
  await waitFor(() => expect(screen.getByLabelText('Owner')).toHaveAttribute('aria-invalid', 'true'));
  expect(fetcher.mock.calls.some(([, options]) => options?.method === 'POST')).toBe(false);
});
