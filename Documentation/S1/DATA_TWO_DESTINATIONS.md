# S1 data: Local always, approved Cloud as a second copy

Date: 16 Oct 2026. Status: DEV/synthetic tested; production cloud is OFF until the researcher and ethics confirm a provider.

## Order of custody

1. The Quest writes raw events/movement to app storage during the session (no network needed).
2. `Tools/research_export.py pull-queue` copies finished bundles to the PC over ADB, checks every hash, writes an immutable
   bundle under `D:\VR_TMU\ResearchData` and a PC receipt, then acknowledges it on the Quest. A dropped cable leaves the job
   `queued_retryable`; the next pull resumes it.
3. `Tools/research_cloud_sync.py sync` copies each verified PC bundle to a cloud-synced folder and reads every file back.
   It never edits a PC bundle. No connection means `cloud_queued_offline`; local work is unaffected.

## Cloud target

The target is a folder that the provider's own desktop client keeps in sync (OneDrive for Business, Google Drive for
desktop) or an institutional share. Sign-in stays with that client. The config file refuses any key that looks like a
token or password.

`cloud_folder_verified` means the files in the synced folder match the PC hashes. It does not prove the provider has
finished uploading. Check the provider client before deleting anything locally (nothing here deletes local data).

Remote layout, ASCII only:

```
research_s1/site_<SITE>/cohort_<TH|JP|DEV|TEST|PILOT>/participant_<CODE>/session_<ID>/run_<ID>/bundle_<ID>/...
research_s1/.../bundle_<ID>.cloud_receipt.json
```

Config (`--config`, kept outside the repository):

```json
{"schema": "research.cloud-sync-config.1", "target_id": "tmu_onedrive", "site_code": "TMU",
 "target_root": "C:\\Users\\<you>\\OneDrive - <institution>\\S1_research",
 "enabled": true, "allow_technical": true, "allow_study": false}
```

Study (TH/JP) bundles stay `cloud_pending_approval` unless `allow_study` is true and `approval` names
`approved_by`, `ethics_ref`, `approved_utc`, `region`, `access_scope` and `encryption_at_rest: true`.
Fill these only from the real approval. The tool does not check them against anything.

## Commands

```
python Tools/research_cloud_sync.py status --data-root D:\VR_TMU\ResearchData --config <cfg>
python Tools/research_cloud_sync.py sync   --data-root D:\VR_TMU\ResearchData --config <cfg> [--force]
python Tools/research_cloud_sync.py withdraw --data-root ... --config <cfg> --participant DEV300 [--execute]
python Tools/research_cloud_sync.py retention --data-root ... --before 2028-03-31
```

- `withdraw` without `--execute` lists what would be removed from the cloud copy. With `--execute` it removes the cloud
  copy and receipts, keeps the PC copy for the researcher's decision, and appends to `_audit.jsonl`.
- `retention` is a dry run only. Proposal §8.7: keep for one year after publication (or after project end if unpublished).
- Failed copies back off from 30 s, doubling up to 1 h. `--force` retries now.

## Tests

`python -B -m unittest test_research_cloud_sync` (run in `Tools/`, synthetic DEV/TH bundles in a temp folder) covers:
disabled by default, hash readback, repeated sync, offline then online, corrupted remote file, denied/failed copy with
backoff, study approval gate, credentials in config refused, tampered local bundle refused, withdrawal, retention.

## Not done

- No real provider folder was tested; that needs the researcher's approved account on this PC.
- No GitHub or chat connector is used for data.
