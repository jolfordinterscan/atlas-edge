# Atlas Edge — Codex Working Instructions

## Project purpose

Atlas Edge is the outbound-only local runtime for InterScan Atlas.

It collects approved local telemetry, queues events safely, enrolls devices,
and communicates with Atlas through authenticated HTTPS transport.

## Core architecture rules

- Keep Atlas Edge outbound-only.
- Do not add inbound listeners, externally exposed ports, or remote-control functionality.
- Do not add production Atlas URLs unless explicitly requested.
- Do not hardcode credentials, tokens, enrollment codes, tenant IDs, or secrets.
- Preserve strict separation between local mock tooling and production integration.
- Preserve tenant and device identity boundaries.
- Keep enrollment, security, transport, configuration, queue, telemetry, and runtime concerns separated.
- Prefer dependency injection and testable interfaces.
- Avoid unrelated refactors.

## Scope discipline

Before editing:

1. Inspect the relevant files.
2. Summarize the intended changes.
3. Identify risks and affected projects.
4. Create a checkpoint with `git status` and `git diff`.

Only modify files required for the requested task.

Do not modify scanner discovery, remote control, production Atlas services,
database migrations, deployment configuration, or unrelated UI unless explicitly requested.

## Security requirements

- Never log access tokens, refresh tokens, enrollment codes, passwords, or raw secrets.
- Use approved redaction or fingerprinting helpers.
- Do not bypass TLS validation.
- Do not use `DangerousAcceptAnyServerCertificateValidator`.
- Reject invalid, expired, or hostname-mismatched certificates.
- Store credentials through the platform credential-store abstraction.
- Keep Windows credential storage marked incomplete until properly implemented.
- Sanitized identifiers may be logged when necessary for diagnostics.

## Testing and validation

A task is not complete until all applicable commands succeed:

```bash
dotnet restore &&
dotnet build -c Release &&
dotnet test -c Release &&
dotnet format --verify-no-changes &&
git diff --check
```

Required result:

- Build: 0 warnings and 0 errors
- Tests: 0 failed
- Formatting validation: passed
- `git diff --check`: no output

Do not describe validation as successful unless the commands actually ran and passed.

If a command cannot run:

- Diagnose the cause.
- Report the exact command and error.
- Do not claim completion.
- Do not weaken or remove tests merely to obtain a passing result.

## Runtime proof

For enrollment or transport work, demonstrate where applicable:

1. First startup with no stored credentials.
2. Successful enrollment.
3. Credential persistence.
4. Heartbeat generation and queueing.
5. Authenticated event transmission.
6. Accepted event acknowledgement.
7. Graceful shutdown.
8. Second startup loading stored credentials.
9. Enrollment skipped after restart.
10. No secrets present in logs.

## Test-change rules

- Fix root causes rather than changing assertions blindly.
- When an assertion and implementation disagree, determine the intended contract first.
- Preserve meaningful regression coverage.
- Add tests for new success, failure, retry, validation, and security behavior.
- Do not delete failing tests without explicit approval.

## Git rules

- Do not commit.
- Do not push.
- Do not switch branches.
- Do not reset, clean, stash, revert, or discard user work without explicit approval.
- Do not amend existing commits.
- Show `git status --short` and a concise diff summary at the end.

## Completion report

Every final report must include:

### Summary

What was implemented.

### Files changed

Grouped by project or responsibility.

### Validation

Exact commands and outcomes.

### Runtime evidence

Observed behavior, not expected behavior.

### Known limitations

Anything deferred, mocked, unverified, or intentionally absent.

### Git status

Modified and untracked files.

Never state that the task is complete when validation is failing or incomplete.
