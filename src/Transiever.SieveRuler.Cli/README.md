# `srtx`

This guide is the canonical command reference for the SieveRuler CLI.
Repository overview lives in [../../README.md](../../README.md), deployment-policy detail lives in [../../docs/synchronization-policy.md](../../docs/synchronization-policy.md),
and generated metadata constraints live in [../../docs/rules-and-metadata.md](../../docs/rules-and-metadata.md).

Commands:

```bash
srtx inspect --rules rules.json
srtx optimize balanced --rules rules.json
srtx generate --rules rules.json --sieve rules.sieve
srtx preview --rules rules.json --preserve-compatible
srtx deploy --plan deployment-plan.json
srtx deploy --plan deployment-plan.json --history-limit 3
srtx deploy --plan deployment-plan.json --no-prune-history
srtx rollback --plan deployment-plan.json
srtx history list
srtx history show srtx-backup-20260626121421-example --sieve restored.sieve
srtx history restore original
srtx history delete srtx-20260626121421-example
srtx history prune --dry-run
```

During development, replace `srtx` with:

```bash
dotnet run --project src/Transiever.SieveRuler.Cli --
```

Preview writes `reconciled-rules.json`, `candidate-rules.json`, `server-active.sieve`, `candidate.sieve`, and `deployment-plan.json` without mutating the server.

* `reconciled-rules.json` is the ownership review document.
* `candidate-rules.json` contains the managed rules rendered into the candidate script.

**If the server has an active script, preview targets that script name by default.**
Use `--script-name <name>` to override the target explicitly.

Generated managed rules include provider UI metadata comments in the Open-Xchange/mailbox.org `## Flag:` style.
The CLI remains provider-agnostic in intent.
However, this metadata shape is currently validated against mailbox.org's Open-Xchange implementation.

Canonical workflow and policy details live in [../../docs/synchronization-policy.md](../../docs/synchronization-policy.md).
This file keeps the command-facing behavior and operator guidance.

Deployment validates the exact previewed candidate and active-script snapshot.
When the target is the current active script, deployment writes a server-side `srtx-backup-*` copy of the previous content,
then replaces the active script in place.

When the target is not active, deployment uploads and activates it.
After a successful deployment, `srtx` prunes inactive SieveRuler-owned history scripts named `srtx-YYYYMMDDHHMMSS-*` or `srtx-backup-YYYYMMDDHHMMSS-*`.

The default retention keeps the oldest backup plus the newest 5 remaining history scripts.

Use `--history-limit <count>` to change the newest-history count, or `--no-prune-history` to disable deletion.
If `HAVESPACE` fails before upload, deployment runs the same safe prune pass, refreshes state, and retries the quota check once.

Rollback validates the plan and current active candidate before changing the server.
Plans with a server-side backup restore that backup into the target script.
Plans without a backup reactivate the recorded source script or disable active Sieve processing if the preview started with no active script.

`--force` bypasses only the current-active candidate mismatch check.
Backup and content hash validation still apply.

History commands work directly from retained SieveRuler-owned server scripts.

`history list` shows `srtx-backup-*` backups and `srtx-*` candidates, marking the oldest backup or no-active marker as `original`.
`history show <name>` prints the retained script or writes it with `--sieve`.
`history restore <name>` or `history restore original` creates a fresh backup of the current active state before restoring.
If `original` is a `srtx-backup-*-no-active` marker, it disables active Sieve processing.
`history delete <name>` removes one inactive SieveRuler-owned history script and refuses to delete the active script.
`history prune` deletes all inactive SieveRuler-owned history scripts, including the original backup or no-active marker.
It keeps the active script and non-SieveRuler script names.
Use `--dry-run` with delete or prune to validate without mutating the server.

ManageSieve configuration:

```text
SIEVERULER_SIEVE_HOST=sieve.example.com
SIEVERULER_SIEVE_PORT=4190
SIEVERULER_SIEVE_USERNAME=user@example.com
SIEVERULER_SIEVE_PASSWORD=secret
SIEVERULER_SIEVE_SECURITY_MODE=StartTlsRequired
```

The port and security mode are optional.
`ImplicitTls` is supported.
Plaintext authentication is refused.
Missing passwords are prompted only on an interactive terminal.
