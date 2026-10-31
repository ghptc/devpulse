import { useEffect, useRef, useState } from 'react';
import { api, type Alias, type History, type Registration, type Repository } from './api';
export function useRepositories() {
  const [aliases, setAliases] = useState<Alias[]>([]);
  const [repositories, setRepositories] = useState<Repository[]>([]);
  const [selected, setSelected] = useState<Repository>();
  const [history, setHistory] = useState<History>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [aliasError, setAliasError] = useState<string>();
  const current = useRef<AbortController | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api.aliases(controller.signal).then(setAliases).catch(error => { if (!controller.signal.aborted) setAliasError(error.message); });
    return () => { controller.abort(); current.current?.abort(); };
  }, []);
  async function load(input: Registration | Repository) {
    current.current?.abort();
    const controller = new AbortController(); current.current = controller;
    setBusy(true); setError(undefined); setHistory(undefined); setSelected(undefined);
    try {
      const repository = 'id' in input ? input : await api.register(input, controller.signal);
      if (controller.signal.aborted) return;
      setSelected(repository);
      setRepositories(previous => previous.some(item => item.id === repository.id) ? previous : [...previous, repository]);
      const result = await api.history(repository.id, controller.signal);
      if (!controller.signal.aborted) setHistory(result);
    } catch (error) { if (!controller.signal.aborted) setError(error instanceof Error ? error.message : 'Repository request failed.'); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }
  return { aliases, repositories, selected, history, busy, error, aliasError, load };
}
