import { act, renderHook, waitFor } from '@testing-library/react';
import { api, type History, type Repository } from './api';
import { useRepositories } from './useRepositories';
it('cancels stale history and retains the newer repository result', async () => {
  vi.spyOn(api, 'aliases').mockResolvedValue([]);
  let finishOld!: (value: History) => void;
  vi.spyOn(api, 'history').mockImplementation(id => id === 'old' ? new Promise(resolve => { finishOld = resolve; }) : Promise.resolve({ repositoryId: 'new', provider: 'GitHub', commits: [], count: 0 }));
  const { result } = renderHook(useRepositories);
  const repository = (id: string): Repository => ({ id, provider: 'GitHub', name: id, scope: 'Default branch: main' });
  let old!: Promise<void>;
  act(() => { old = result.current.load(repository('old')); });
  await act(async () => { await result.current.load(repository('new')); });
  await act(async () => { finishOld({ repositoryId: 'old', provider: 'GitHub', commits: [], count: 0 }); await old; });
  expect(result.current.history?.repositoryId).toBe('new');
  expect(result.current.selected?.id).toBe('new');
  expect(result.current.busy).toBe(false);
  await waitFor(() => expect(result.current.aliases).toEqual([]));
});
