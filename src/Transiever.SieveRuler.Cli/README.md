# `srtx`

This guide is the canonical command reference for the SieveRuler CLI.
See the [SieveRuler project overview](https://github.com/SeWieland/Transiever.SieveRuler#readme), [synchronization policy](https://github.com/SeWieland/Transiever.SieveRuler/blob/main/docs/synchronization-policy.md), and [rules and metadata guide](https://github.com/SeWieland/Transiever.SieveRuler/blob/main/docs/rules-and-metadata.md).

## Install

```bash
dotnet tool install --global Transiever.SieveRuler.Cli
```

The global-tool package is available on [NuGet.org](https://www.nuget.org/packages/Transiever.SieveRuler.Cli).
For a workflow overview and local Sieve export path, see [Generate and deploy Sieve rules from JSON in .NET](https://sewieland.github.io/transiever/dotnet-sieve-rules/).

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
srtx history restore latest
srtx history restore original
srtx history delete srtx-20260626121421-example
srtx history prune --dry-run
```

During development, replace `srtx` with:

```bash
dotnet run --project src/Transiever.SieveRuler.Cli --
```

Install the self-contained Linux x64 build with Homebrew:

```bash
brew install SeWieland/transiever/srtx
```

GitHub releases attach self-contained `srtx` assets for `win-x64`, `win-x86`, and `linux-x64`.
.NET does not define a portable `linux-x86` RID, so no Linux x86 asset is produced.

Optimization modes:

* `conservative` merges exact single-condition equivalents.
* `balanced` also merges action-equivalent single-condition rules across condition types and uses higher-confidence sender-domain inference.
* `aggressive` keeps balanced merge behavior and applies broader sender-domain inference and subdomain collapse.

Preview writes these artifacts without mutating the server:

* `reconciled-rules.json`.
* `candidate-rules.json`.
* `server-active.sieve`.
* `candidate.sieve`.
* `deployment-plan.json`.

* `reconciled-rules.json` is the ownership review document.
* `candidate-rules.json` contains the managed rules rendered into the candidate script.

**If the server has an active script, preview targets that script name by default.**
Use `--script-name <name>` to override the target explicitly.

Generated managed rules include provider UI metadata comments in the Open-Xchange/mailbox.org `## Flag:` style.
The CLI remains provider-agnostic in intent.
However, this metadata shape is currently validated against mailbox.org's Open-Xchange implementation.

Canonical workflow and policy details live in the [synchronization policy](https://github.com/SeWieland/Transiever.SieveRuler/blob/main/docs/synchronization-policy.md).
This file keeps the command-facing behavior and operator guidance.

Deployment validates the exact previewed candidate and active-script snapshot.
When the target is the current active script, deployment writes a server-side `srtx-backup-*` copy of the previous content.
It then replaces the active script in place.

When the target is not active, deployment uploads and activates it.
After a successful deployment, `srtx` prunes inactive history scripts in SieveRuler's reserved history namespace.
Pruned names match `srtx-YYYYMMDDHHMMSS-*` or `srtx-backup-YYYYMMDDHHMMSS-*`.
Matching names are managed regardless of who created them.

The default retention keeps the oldest backup plus the newest 5 remaining history scripts.

Use `--history-limit <count>` to change the newest-history count.
Use `--no-prune-history` to disable deletion.
If `HAVESPACE` fails before upload, deployment runs the same safe prune pass, refreshes state, and retries the quota check once.

Rollback validates the plan and current active candidate before changing the server.
Plans with a server-side backup restore that backup into the target script.
Plans without a backup reactivate the recorded source script.
They disable active Sieve processing if the preview started with no active script.

`--force` bypasses only the current-active candidate mismatch check.
Backup and content hash validation still apply.

History commands work directly from retained server scripts in SieveRuler's reserved history namespace.

`history list` shows `srtx-backup-*` backups and `srtx-*` candidates, marking the oldest backup or no-active marker as `original`.
`history show <name>` prints the retained script or writes it with `--sieve`.
`history restore <name>`, `history restore latest`, or `history restore original`
creates a fresh backup of the current active state before restoring.
`latest` resolves to the newest inactive `srtx-backup-*` script.
If `original` is a `srtx-backup-*-no-active` marker, it disables active Sieve processing.
`history delete <name>` removes one inactive history script in SieveRuler's reserved history namespace and refuses to delete the active script.
`history prune` deletes all inactive history scripts in SieveRuler's reserved history namespace, including the original backup or no-active marker.
It keeps the active script and script names outside SieveRuler's reserved history namespace.
Use `--dry-run` with delete or prune to validate without mutating the server.

ManageSieve configuration:

```text
TRANSIEVER_SIEVE_HOST=sieve.example.com
TRANSIEVER_SIEVE_PORT=4190
TRANSIEVER_SIEVE_USERNAME=user@example.com
TRANSIEVER_SIEVE_PASSWORD=secret
TRANSIEVER_SIEVE_SECURITY_MODE=StartTlsRequired
```

Use `--sieve-host`, `--sieve-port`, `--sieve-username`, and
`--sieve-security-mode` to override non-secret values for a targeted command.
Use `--sieve-password-stdin` to read one non-empty password from redirected standard input.
The selector rejects interactive terminal input; omit it to use the non-echoed password prompt.
Passwords cannot be supplied as command-line values.
The port and security mode are optional.
`ImplicitTls` is supported.
Plaintext authentication is refused.
Missing passwords are prompted only on an interactive terminal.

## Saved Accounts

Account profiles are shared through `Transiever.Common.Accounts` and contain only connection and identity settings.
The profile store uses the current user's application-data directory below `Transiever/accounts`.
The rules library does not open profiles or credential stores.

Create a local profile, then use its printed reference:

```bash
srtx account create --name Work --sieve-host sieve.example.com --sieve-username user@example.com
srtx account list
srtx account show account:<guid>
srtx account update account:<guid> --name Personal --sieve-port 4190
srtx account login account:<guid>
srtx account status account:<guid>
srtx preview --account account:<guid> --rules rules.json
srtx account logout account:<guid>
srtx account forget account:<guid>
```

Replace `account:<guid>` with the complete reference printed by `create` or `list`.
References remain stable when profile fields change.
`--account account:<guid>` also selects a profile for account commands.
`TRANSIEVER_ACCOUNT` supplies the default reference; an explicit selection takes precedence.
With no selected account, workflow commands do not access either store.
No arguments and help do not read configuration, credentials, or standard input.

`create` prompts for missing name, host, and username only when both input and output are terminals.
Headless creation requires `--name` and either explicit or environment host and username values.
`--provider` defaults to `managesieve`, and `--account-id` defaults to the username.
`create` and `update` also accept `--authentication`, `--sieve-port`, and `--sieve-security-mode`.
`update` changes non-secret fields; it retains the reference and stored credentials.
Host, username, port, and security environment variables apply to profile creation and update as well.

For a selected connection, resolution uses explicit options and selected standard input first, then the documented environment variables, then the profile and credential store, then a terminal prompt.
Headless execution never prompts.
The current SieveRuler connection uses PLAIN authentication over TLS.
An account with a different `--authentication` preference is refused before retrieving its credential.
Plaintext connections remain refused.
Stored refresh tokens are never passed to the ManageSieve password authenticator.

`login` stores or replaces a local credential; it does not authenticate with the provider or acquire or renew OAuth tokens.
The default credential kind is `Password`.
Use `--kind OAuthRefreshToken` only to store an already-acquired refresh token.
`--secret-stdin` (or `--sieve-password-stdin`) reads one non-empty line from redirected standard input.
Without that option, password input uses `TRANSIEVER_SIEVE_PASSWORD`, and refresh-token input uses `TRANSIEVER_ACCOUNT_REFRESH_TOKEN`, followed by a non-echoed terminal prompt.
Secret-valued command-line options are refused and redacted in errors.
`create`, `list`, `show`, and `update` never retrieve stored secrets.

Credential storage uses Windows Credential Manager, macOS Keychain, or Linux Secret Service when available.
There is no plaintext fallback.
`status` reports `Missing`, `Available`, `Expired`, `Rejected`, or `Unavailable` without exposing or displaying a secret.
An expired or rejected stored password is not reused; supply one-run input or replace it with `login`.
When storage is unavailable, workflow commands can still use one-run standard-input, environment, or terminal input; `login` reports failure instead of claiming persistence.

`logout` removes both local credential kinds and retains the profile.
Use `--kind Password` or `--kind OAuthRefreshToken` with `logout` to remove only that kind, or with `status` to inspect only that kind.
`forget` removes known local credentials before deleting the profile; a credential-removal failure preserves the profile and returns a nonzero exit code.
Neither operation revokes credentials at the provider.
Perform any required provider-side revocation separately.
