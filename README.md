# DevPulse

M01 registers authorized Local Git or GitHub repositories and displays their latest 20 real commits through the DevPulse REST API. No analytics, generated commit fixtures or direct browser-to-provider requests are included.

## Structure

- `backend/src/DevPulse.Api/Application`: normalized contracts, provider dispatch and registration use cases.
- `backend/src/DevPulse.Api/Infrastructure`: controlled Git inspection, approved paths, single-owner registry and typed GitHub client.
- `backend/tests/DevPulse.Tests`: real Git fixtures, deterministic GitHub HTTP fixtures and ASP.NET Core integration tests.
- `frontend`: React, TypeScript and Ant Design UI with relative API requests and cancellation-aware hooks.
- `docs/implementation-plan.md`: phased implementation and delivery plan.

## Owner configuration

M01 supports one local owner. The API binds to `127.0.0.1:5090`, rejects remote peers, unrecognized Hosts and unapproved browser Origins. Do not expose it through a proxy, override the listener or share it with untrusted local users. Multi-user authorization is outside M01.

Configure these settings through administrator-owned ASP.NET Core configuration/environment variables, never browser fields:

```text
Repositories__AuthorizedRoots__0=/absolute/approved/root
Repositories__Aliases__sample=/absolute/approved/root/sample
Repositories__GitExecutable=/usr/bin/git
Repositories__TimeoutMilliseconds=5000
Repositories__OutputLimitBytes=2097152
```

Paths must be ordinary self-contained repositories. Symlinks, linked worktrees, external object stores, config includes and worktree overrides are rejected. The authorized root and repository must be administrator-controlled; filesystem replacement races by another local account are outside this single-owner trust boundary. Git must be administrator-installed, not supplied by a repository. Only read-only argument arrays execute; there is no clone, fetch, shell, checkout, commit or push API.

Public GitHub repositories need no credential. Private repositories require **existing owner authorization**, `GitHub__Token` in a server-side secret store/environment and an explicit `GitHub__AllowedPrivateRepositories__0=owner/name`. Do not reuse platform credentials or commit tokens. Credentials are sent only to fixed `https://api.github.com` requests for allowlisted identities; redirects are disabled. OAuth and GitHub App installation are outside this milestone.

The registration registry is process-local and bounded to 1024 entries. Restarting clears registrations. Re-registering the same identity after restart returns the same ID. Registering a duplicate during a process lifetime returns 409. No user-local path is ever accepted from the browser.

## API

`GET /api/v1/repository-aliases` returns safe administrator alias labels.

`POST /api/v1/repositories` accepts either:

```json
{"provider":"LocalGit","alias":"sample"}
```

```json
{"provider":"GitHub","owner":"ghptc","name":"devpulse"}
```

Registration returns 201 with `{id,name,provider,scope}`. Unknown fields, including a `path`, are rejected. Store the returned ID for the current server lifetime.

`GET /api/v1/repositories/{id}/commits?limit=20` returns `{repositoryId,provider,commits:[{sha,message,author,authoredAt}],count}`. Limit defaults to 20 and accepts 1–20. The local scope is registered HEAD; GitHub uses the default branch. `authoredAt` is the author date. Subjects are displayed without rendering provider text as HTML.

Invalid requests return 400; unapproved local aliases/access return 403; unknown IDs 404; duplicates/changed upstream identity 409; unsafe local layouts 422; provider rate limits 429 with bounded Retry-After; inspection/provider failures 502 or 504. Provider diagnostic bodies and paths are never returned to the browser. A provider error is never converted to fabricated successful history.

## Build and verification

Use approved isolated profiles for all repository code; do not run generated code on the workstation host. Package download preparation may run with lifecycle scripts disabled. The test profiles need a pinned .NET 10/Node 24/Git image with offline NuGet/npm dependencies, no network, host mounts or credentials, and bounded CPU, memory, time and output.

Commands inside that sandbox:

```text
dotnet build backend/src/DevPulse.Api/DevPulse.Api.csproj
dotnet test backend/tests/DevPulse.Tests/DevPulse.Tests.csproj
npm ci --prefix frontend --ignore-scripts
npm run typecheck --prefix frontend
npm test --prefix frontend
npm run build --prefix frontend
```

NuGet.Config retains the preserved baseline's cleared feeds: provision approved offline packages before restoration. Frontend versions are locked in `frontend/package-lock.json`.

For owner-run local use after review, build the frontend and copy `frontend/dist/` contents into `backend/src/DevPulse.Api/wwwroot/`, then serve the API and assets from the same origin. Static build output is not committed. During isolated development, Vite proxies relative `/api` requests to port 5090. Explicitly configure `OwnerOrigins__0=http://localhost:5173` (or the exact loopback Vite origin) to authorize that development browser origin. This does not enable CORS or remote access.

See `docs/verification.md` for executed evidence and verification limits. No test, QA or delivery approval is implied by generated source alone. Delivery requires human review of the draft PR; no automatic merge is configured.
