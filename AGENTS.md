# StreamNest development rules

## Preserved single-download baseline

- Version 0.7.8 is the final single-download baseline. Its source is Git tag `v0.7.8`; its binaries are the matching GitHub Release assets and the local `releases/0.7.8` archive.
- Never move the tag, replace those assets, or rebuild over the archived files. Start application behavior changes in a new version and a separate branch/checkout. Documentation-only changes may be made without replacing the baseline.
- Use the archived runtime ZIP and its `.sha256` file for exact binary restoration. See `RELEASES.md` for source and verification details.
- On 2026-10-10 the user explicitly requested withdrawing the public 0.7.7 release and replacing it with 0.7.8. Keep `v0.7.7` at commit `5e5eda4` and retain its local archive. Do not republish it without a user request.

## Future version compatibility

- Preserve the existing 0.7.7/0.7.8 settings and browser profiles. The app stores data under LocalApplicationData in `ChzzkLocalDownloader` and `ChzzkDownloader`.
- If queue/account-management work changes storage formats or profile behavior, use a separate storage location for the new feature/version. Migrate by copying; keep the original data usable by 0.7.8. Do not modify or delete the old profiles as part of a new-version migration.
- Keep login sessions separated by service. Credentials collected for one service must not be passed to another service or to general web downloads.
- Preserve the fast completion check: do not reintroduce a whole-file FFmpeg decode in normal app downloads. Full decoding may be used in relevant short-media tests.
- Keep existing internal web-video identifiers and extraction functions intact while preserving the generic user-facing labels.

## Publication and privacy

- Upload audited source and prepared release packages only. Never sweep the working folder into Git or a ZIP.
- Exclude user settings, browser profiles, cookies, tokens, diagnostics, test outputs and downloaded media. Keep `artifacts`, `releases`, build outputs and tool binaries out of source commits.
- Use synthetic IDs in offline tests and local environment variables for authorized real-video tests. Never hardcode the user's viewed/downloaded video IDs, account names, personal paths or email addresses.
- Use the GitHub noreply address for publication commits. Preserve third-party license notices.
- Verify source manifests, package hashes and relevant tests before publishing. Report actual test scope; do not describe a subset as a passing full WPF or external-service suite.

Explicit user instructions take precedence over these project conventions.
