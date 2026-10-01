# StreamNest development rules

## Preserved single-download baseline

- Version 0.7.7 is the preserved single-download baseline. Its source is Git tag `v0.7.7`, commit `5e5eda4`; its binaries are the matching GitHub Release assets and the local `releases/0.7.7` archive.
- Never move the tag, replace those assets, or rebuild over the archived files. Start application behavior changes in a new version and a separate branch/checkout. Documentation-only changes may be made without replacing the baseline.
- The archived runtime ZIP has SHA-256 `1d33e64cadc639f82bca4271e52c93cd57ee613d643bad90c9c9f08b513c3a7e`. Use it for exact binary restoration. See `RELEASES.md` for source and verification details.

## Future version compatibility

- Preserve the 0.7.7 settings and browser profiles. The existing app stores data under LocalApplicationData in `ChzzkLocalDownloader` and `ChzzkDownloader`.
- If queue/account-management work changes storage formats or profile behavior, use a separate storage location for the new feature/version. Migrate by copying; keep the original data usable by 0.7.7. Do not modify or delete the old profiles as part of a new-version migration.
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
