# M01 verification and review

Status: implemented and verified by executable backend/frontend tests; draft delivery review pending. No deployment or merge is authorized automatically.

## Source and execution identity

Remote baseline: `main` at `0c6c5415864bc0f9ccb56959477b98d2d9f4fb97`. Preserved source provenance is in `preserved-baseline.json`; those original files and AEP checkpoints were never modified.

The four successful final profiles executed the same source snapshot, `verify-0788419f20a14bb9`, with executor source revision `7fe6d56da0ef000452a6a62285d63e1e40aabe25ad6b4601be42300226f4bb05`. `verification/source-manifest.json` records each tested source file's SHA-256. Verification records were added afterward; they do not change application or test code. Check that manifest against the checked-out revision when reviewing.

The unchanged included AEP host daemon ran separately in its test environment with a dedicated Unix socket, token, fixture remote and `/tmp/devpulse-verification` state. A trusted client invoked `IExecutionSandbox` through `RemoteExecutionSandbox`. These were executor verification jobs, not model/orchestration executions. Existing AEP execution records, budgets, workspaces, operation receipts and checkpoints were not accessed for mutation. The only daemon stopped/restarted was this temporary verifier.

The approved fixed profiles in `verification/profiles.json` use pinned image `sha256:021d69e67499bfcf6e16f17ed189108b46ef0f524dc75b7132896fb66b24c88a`, a credential-free offline dependency layer over pinned executor base `sha256:89f6f286a57a6f2a7b09a1e6233e4cd21e0aa3cc4be75d3257d936e4db7f8ba1`. Isolation was checked by the included daemon: no network or host mounts, read-only root, user 65532, dropped capabilities, seccomp, PID limit 128, bounded tmpfs, 2 CPUs, 2048 MiB, 120 seconds and 100000 output bytes, plus independent container cleanup. No DevPulse code executed on the workstation host. Host npm dependency preparation disabled lifecycle scripts; a trusted verification client, package cache preparation and fixed Docker image assembly ran on the host.

## Executed results

| Profile | Result | Receipt evidence |
|---|---|---|
| .NET API build | Passed, 0 warnings, 0 errors | `local-b44e34f801e6443c91a4bb9b6d583cf8` |
| Backend tests | 22 passed, 0 failed, 0 skipped | `local-ac3a8bb855f94f62836419b8582bd9de` |
| Frontend component/hook tests | 7 passed, 0 failed | `local-b364ac2abb42473caa5621f59c4184f0` |
| TypeScript and production build | Passed: `tsc --noEmit` and Vite | `local-3c7b99dfc66746b4a6889e7b57ffe0b7` |

Original safe requests, commands, actual stdout/stderr, exit codes and source revision are retained in `verification/receipts/`. Earlier failed attempts also remain in the separate verifier's evidence directory; no receipts were overwritten. Initial backend restoration failed on missing exact offline package versions (zero tests, not a pass); the dependency image was corrected. Two frontend DOM assertions were corrected. The first timeout test fixture was rejected by `/tmp`'s noexec mount; the fixture moved to the executable workspace, preserving isolation. The successful timeout test exercises real process termination.

Backend coverage uses actual temporary Git repositories: registration authorization, 23-commit extraction bounded to 20, metadata, limit 1, unborn history, corrupt detached HEAD, config includes, malformed output, cancellation, timeout, duplicate registration, REST endpoints, safe aliases, Host/Origin rejection and deterministic GitHub HTTP/provider mapping, private authorization and status failures (401/403/404/429/500). GitHub API registration/history also runs through ASP.NET Core integration tests with a deterministic HTTP handler confined to the test assembly.

Frontend coverage verifies provider-specific fields and approved aliases, required identity validation, actual API response rendering, empty history, provider errors, accessible labels and stale-response cancellation. Responsive CSS provides narrow-screen form stacking and bounded horizontal table scrolling.

## Review and limits

The source diff was reviewed for layer dependencies, fixed read-only arguments, alias-only local registration, upstream identity checks, redirect/credential boundaries, cancellation, UI request ownership, fake-data fallbacks and generated artifacts. Staged changes passed `git diff --check` and credential-pattern inspection. Application integration contracts contain no Git process or HTTP SDK dependencies. Production loads no test adapters.

Known verification limits: no live GitHub public/private request, private credential certification, browser/Playwright run, screenshot or deployed end-to-end session was performed. Component tests run in jsdom; they verify labels and interaction, not browser layout or an accessibility audit. The production bundle emits a size warning (about 894 kB uncompressed); jsdom emits an unsupported pseudo-element style-query notice; the SDK emits an installed-workload advisory. None were hidden or counted as test failures.

The single-owner registry is deliberately process-local; restart requires re-registration. This M01 does not implement multi-user access, registry durability, OAuth, GitHub App setup, analytics, autonomous deployment or automatic merge. Owner review must validate operational configuration and perform the live browser/provider acceptance check before accepting delivery.
