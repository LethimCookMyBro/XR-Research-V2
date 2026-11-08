# S1 Quest direct upload — DEV technical candidate

Status: Code ready for review/tests; **not deployed, not linked to Google Drive, not approved for study use**.

The current .technical APK records ResearchSessionRegistry snapshots locally first, unchanged. If provisioned, an additional Unity component uploads completed immutable bundles in 256 KiB chunks by HTTPS. A cloud relay verifies every file SHA256, and a server-side Google Drive worker stores the original files in the approved restricted Drive folder. No Google token goes inside the Quest.

## Safety gates

- The Quest app never embeds Google credentials or a relay token; absent post-install provisioning means **no network requests**.
- Relay requires enrolled device-specific SHA256-hashed bearer tokens in server-side S1_RELAY_DEVICE_TOKENS_JSON (example: {"QUEST3_DEV01":"<sha256>"}); the single-token S1_RELAY_TOKEN_SHA256 is a legacy one-device DEV fallback. Study uploads are rejected even with valid token; accepted test_data only for DEV/TEST/PILOT.
- Relay storage must be a private persistent volume, with HTTPS + restricted authentication. Without server-side Google OAuth, **staged_verified is only a relay receipt**; do not claim cloud completion.
- The owner-supplied Google Drive destination still had "Anyone with link, Editor" as of 17 October. Its folder ID is kept privately outside the public repository. Change access to Restricted and verify permissions before OAuth setup.
- Google Drive readback verifies returned provider size and MD5 against original local file; the relay independently verifies SHA256.
- This is not yet approved for real participant data, which also requires multi-device credential revocation, security audit, consent handling and permitted data residency. Never store raw captures/identities/credentials in GitHub.

## Deploy relay safely

Railway: build Dockerfile from Tools/s1_cloud_relay; attach persistent volume at /data, set S1_RELAY_DATA_ROOT=/data, bind PORT. Without a valid enrolled device registry or legacy DEV token, all upload APIs return 401. /health reports authentication configuration readiness, not provider delivery.
For a DEV device, generate 32+ random characters, provision SHA256 lowercase hash in S1_RELAY_TOKEN_SHA256, and add plaintext token to the **device's app-private configuration only**. Keep provider OAuth in server environment:

S1_GOOGLE_ENABLED=true
S1_GOOGLE_CLIENT_ID=(from user's Google Cloud OAuth project)
S1_GOOGLE_CLIENT_SECRET=(secret from OAuth project)
S1_GOOGLE_REFRESH_TOKEN=(owner-approved offline grant)
S1_GOOGLE_FOLDER_ID=(folder ID, after Restricted check)

A ChatGPT Google Drive connector login cannot be reused automatically by the app or cloud relay; separate OAuth is necessary. The Drive permission check fails closed if folder access is broad or verification cannot be performed.

## Provision DEV Quest once

Place JSON in Application.persistentDataPath/s1_cloud_relay.json (on current Quest technical package this is /sdcard/Android/data/th.ac.tmu.vr.researchflow.quest3.technical/files/s1_cloud_relay.json):

~~~json
{"schema":"s1.quest-relay-config.1","enabled":true,"technical_only":true,
 "endpoint":"https://YOUR-APP.up.railway.app","device_id":"QUEST3_DEV01",
 "device_token":"LONG_PER_DEVICE_TOKEN"}
~~~

Use guarded adb push per-device; restart Unity app. Do not uninstall/clear data. Wi-Fi can then send without PC, and failed/incomplete chunks retry from server offsets. The relay supports a JSON map of **per-device token hashes**, so different Quest units can be independently disabled/rotated. This is bearer authentication, NOT hardware attestation; study use still needs audited enrollment and review. Status "cloud_verified" is written to CloudReceipts only after confirmed Drive receipt, not after relay receipt.

Drive hierarchy (2026-11-08, same tree as the PC route with a pinned folder ID; no extra research_s1 level):
site_<S1_SITE_CODE>/device_<device_id>/cohort_<DEV|TEST|PILOT|TH|JP>/participant_<code>/session_<id>/run_<id>/bundle_<id>/
  manifest.json, COMPLETE.json, raw/events.jsonl, raw/movement.csv, raw/session_metadata.json, raw/research-session.json

- device_id is the authenticated enrolled ID (provision with Tools/delivery/Provision-QuestCloud.ps1, which uses the headset serial);
  the relay stores each device under its own device_<id>/ folder and every receipt records device_id and device_label.
- States: receiving -> staged_verified (relay SHA-256 verified; Drive pending, pending_reason explains) -> provider_verified
  (each file downloaded back from Google by file ID and its SHA-256 matches the device manifest). The Quest writes CloudReceipts only
  for provider_verified.
- Folder access: a folder shared to anyone/domain is refused unless S1_GOOGLE_BROAD_FOLDER_ACK records the owner decision; even then only
  DEV/TEST/PILOT bundles are written there. Study bundles need S1_RELAY_STUDY_APPROVAL_JSON (approved_by, ethics_ref, approved_utc,
  region, access_scope, encryption_at_rest=true) and a Restricted folder.
- Owner setup guide (Thai): Tools/delivery/CLOUD_AUTO_UPLOAD_TH.md

## Tests
python -B Tools/s1_cloud_relay/test_relay.py
python -B Tools/s1_cloud_relay/test_multidevice.py
python -B Tools/s1_cloud_relay/test_http_transport.py
The existing PC / USB route Tools/research_export.py pull-queue remains an independent backup and QC path; it does not need internet and was not modified.
