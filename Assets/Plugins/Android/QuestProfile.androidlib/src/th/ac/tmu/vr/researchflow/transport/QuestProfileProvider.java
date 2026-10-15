package th.ac.tmu.vr.researchflow.transport;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.database.Cursor;
import android.net.Uri;
import android.os.Binder;
import android.os.Build;
import android.os.Bundle;
import android.os.ParcelFileDescriptor;
import android.os.Process;
import android.os.SystemClock;
import android.os.UserManager;
import android.util.Base64;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.UUID;

public final class QuestProfileProvider extends ContentProvider {
    private static final String PACKAGE = "th.ac.tmu.vr.researchflow.quest3.technical";
    private static final String[] BINDING = {"android_user", "package", "uid", "pid", "process_start_ticks", "boot_id", "build_sha256", "apk_path", "instance_id", "session_id", "run_id"};
    private static final List<String> COMMANDS = Arrays.asList("state", "list", "create", "start", "resume", "abort", "demo", "export", "export_saved", "technical_scenario", "benchmark", "benchmark_cancel", "motion_trace", "haptic_probe", "drag_filter", "view_probe", "frame_timing", "amend_reason", "render_set", "calibrate_anchor");
    private static final List<String> FILES = Arrays.asList("manifest.json", "COMPLETE.json", "raw/events.jsonl", "raw/movement.csv", "raw/session_metadata.json", "raw/research-session.json");
    private static QuestProfileProvider live;
    private static volatile boolean focused, ready;
    private static volatile String[] scope = new String[]{null, null};
    private final String instance = UUID.randomUUID().toString();
    private String apkHash, birth, boot;
    private final LinkedHashMap<String, Long> requests = new LinkedHashMap<>();
    private final LinkedHashMap<String, JSONObject> grants = new LinkedHashMap<>();

    @Override public boolean onCreate() {
        try {
            if (!technical()) return false;
            apkHash = hash(new File(getContext().getApplicationInfo().sourceDir));
            String stat = new String(Files.readAllBytes(new File("/proc/self/stat").toPath()), StandardCharsets.UTF_8);
            birth = stat.substring(stat.lastIndexOf(") ") + 2).trim().split("\\s+")[19];
            boot = new String(Files.readAllBytes(new File("/proc/sys/kernel/random/boot_id").toPath()), StandardCharsets.UTF_8).trim();
            live = this;
            return true;
        } catch (Exception ex) { android.util.Log.e("QUEST_PROFILE_GUARD", "Native identity initialization failed", ex); return false; }
    }

    private boolean technical() {
        return getContext() != null && PACKAGE.equals(getContext().getPackageName())
            && (getContext().getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0;
    }

    private void caller() {
        int uid = Binder.getCallingUid();
        if (!technical() || (uid != 2000 && uid != Process.myUid())) throw new SecurityException("Restricted technical shell/app caller required");
        if (!foreground() || !ready || !focused) throw new SecurityException("Foreground profile and focused ready app required");
    }

    private boolean foreground() {
        return Build.VERSION.SDK_INT >= 31 && ((UserManager)getContext().getSystemService(Context.USER_SERVICE)).isUserForeground();
    }

    public static void updateState(String sessionId, String runId, boolean appFocused) {
        scope = new String[]{sessionId == null || sessionId.isEmpty() ? null : sessionId, runId == null || runId.isEmpty() ? null : runId};
        focused = appFocused; ready = true;
    }

    private JSONObject identity() throws Exception {
        JSONObject value = new JSONObject();
        String[] current = scope;
        value.put("package", PACKAGE).put("android_user", Process.myUid() / 100000).put("uid", Process.myUid()).put("pid", Process.myPid());
        value.put("process_start_ticks", birth).put("boot_id", boot).put("instance_id", instance).put("build_sha256", apkHash).put("apk_path", getContext().getApplicationInfo().sourceDir);
        value.put("foreground_user", foreground()).put("app_focused", focused).put("elapsed_ms", SystemClock.elapsedRealtime());
        value.put("session_id", current[0] == null ? JSONObject.NULL : current[0]).put("run_id", current[1] == null ? JSONObject.NULL : current[1]);
        return value;
    }

    public static String identityJson() {
        try { return live == null ? null : live.identity().toString(); } catch (Exception ex) { return null; }
    }

    private void validate(JSONObject request) throws Exception {
        if (!technical() || !foreground() || !ready || !focused) throw new SecurityException("Profile/focus no longer valid");
        UUID.fromString(request.getString("request_id"));
        long remain = request.getLong("deadline_elapsed_ms") - SystemClock.elapsedRealtime();
        if (remain <= 0 || remain > 15000) throw new SecurityException("Request expired or invalid lifetime");
        JSONObject expected = request.getJSONObject("binding"), actual = identity();
        for (String key : BINDING) if (!String.valueOf(expected.get(key)).equals(String.valueOf(actual.get(key)))) throw new SecurityException("Request binding changed at " + key);
    }

    public static String validateJson(String raw) {
        try { if (live == null) return "Native provider not initialized"; live.validate(new JSONObject(raw)); return null; }
        catch (Exception ex) { return ex.getMessage(); }
    }

    private static String text(File file, long maximum) throws Exception {
        if (!file.isFile() || file.length() > maximum) throw new SecurityException("Missing or oversized transport file");
        return new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
    }

    private static String token(String value) {
        if (value == null || !value.matches("[A-Za-z0-9][A-Za-z0-9_-]{0,159}")) throw new SecurityException("Unsafe export token");
        return value;
    }

    private static File child(File base, String relative) throws Exception {
        File canonical = base.getCanonicalFile(), result = new File(canonical, relative);
        if (!result.getCanonicalPath().equals(result.getAbsolutePath()) || !result.getAbsolutePath().startsWith(canonical.getPath() + "/"))
            throw new SecurityException("Transport path escapes or aliases known app-owned root");
        return result;
    }

    private File root(String registry) throws Exception {
        File base = getContext().getExternalFilesDir(null);
        if (base == null) throw new SecurityException("App external files unavailable");
        return child(base, "main".equals(registry) ? "ResearchSessionRegistry" : "TechnicalScenarios/" + token(registry) + "/registry");
    }

    private JSONObject job(String registry, String bundle) throws Exception {
        File path = child(root(registry), "ExportQueue/" + token(bundle) + ".json");
        JSONObject job = new JSONObject(text(path, 65536));
        if (!"researchflow.export_queue.1".equals(job.getString("schema_version")) || !bundle.equals(job.getString("bundle_id"))
            || !job.getString("participant_code").matches("(DEV|TEST|PILOT)[0-9]+")) throw new SecurityException("Only technical immutable queue jobs permitted");
        File export = child(root(registry), "Exports/" + token(bundle));
        if (!export.getCanonicalPath().equals(new File(job.getString("export_path")).getCanonicalPath())) throw new SecurityException("Queue export path changed");
        JSONObject manifest = new JSONObject(text(child(export, "manifest.json"), 65536));
        JSONObject complete = new JSONObject(text(child(export, "COMPLETE.json"), 65536));
        if (!bundle.equals(manifest.getString("bundle_id")) || !bundle.equals(complete.getString("bundle_id"))
            || !hash(child(export, "manifest.json")).equals(complete.getString("manifest_sha256"))) throw new SecurityException("Incomplete immutable export");
        JSONObject owner = manifest.getJSONObject("identity");
        for (String key : new String[]{"participant_code", "session_id", "run_id"}) if (!job.getString(key).equals(owner.getString(key))) throw new SecurityException("Queue/session identity mismatch");
        if (!owner.getBoolean("test_data")) throw new SecurityException("Study data is not exposed by technical transport");
        return job;
    }

    private static void validateReceipt(JSONObject receipt, JSONObject job, String manifestHash) throws Exception {
        String[] strings = {"schema_version", "bundle_id", "participant_code", "session_id", "run_id", "source_manifest_sha256", "pc_bundle_id", "pc_manifest_sha256", "receipt_sha256", "received_utc"};
        if (receipt.length() != strings.length + 2) throw new SecurityException("Unexpected receipt fields");
        for (String key : strings) if (!(receipt.get(key) instanceof String)) throw new SecurityException("Receipt string required: " + key);
        if (!"researchflow.pc-receipt.1".equals(receipt.getString("schema_version"))) throw new SecurityException("Unsupported PC receipt");
        for (String key : new String[]{"bundle_id", "participant_code", "session_id", "run_id"})
            if (!job.getString(key).equals(receipt.getString(key))) throw new SecurityException("Receipt identity mismatch: " + key);
        for (String key : new String[]{"source_manifest_sha256", "pc_manifest_sha256", "receipt_sha256"})
            if (!receipt.getString(key).matches("[0-9a-f]{64}")) throw new SecurityException("Receipt SHA-256 required");
        if (!manifestHash.equals(receipt.getString("source_manifest_sha256"))) throw new SecurityException("Receipt source manifest mismatch");
        token(receipt.getString("pc_bundle_id"));
        String received = receipt.getString("received_utc");
        if (!received.matches("[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{6}Z")) throw new SecurityException("UTC receipt timestamp required");
        java.time.Instant.parse(received);
        Object count = receipt.get("qc_failure_count");
        if (!(receipt.get("qc_data_complete") instanceof Boolean) || !(count instanceof Integer || count instanceof Long)
            || ((Number)count).longValue() < 0 || ((Number)count).longValue() > Integer.MAX_VALUE)
            throw new SecurityException("Typed QC outcome required");
        if (receipt.getBoolean("qc_data_complete") && ((Number)count).longValue() != 0) throw new SecurityException("Contradictory QC outcome");
    }

    private JSONObject acknowledgeDelivery(JSONObject request) throws Exception {
        String registry = request.getString("registry"), bundle = token(request.getString("bundle_id"));
        JSONObject queued = job(registry, bundle), receipt = request.getJSONObject("receipt");
        String manifestHash = hash(child(root(registry), "Exports/" + bundle + "/manifest.json"));
        validateReceipt(receipt, queued, manifestHash);
        File directory = child(root(registry), "ExportReceipts");
        if (!directory.isDirectory() && !directory.mkdirs()) throw new SecurityException("Receipt directory unavailable");
        File target = child(directory, bundle + ".json");
        android.util.AtomicFile file = new android.util.AtomicFile(target);
        if (target.isFile()) {
            JSONObject previous = new JSONObject(text(target, 16384));
            validateReceipt(previous, queued, manifestHash);
            java.util.Iterator<String> keys = receipt.keys();
            while (keys.hasNext()) { String key = keys.next(); if (!receipt.get(key).equals(previous.get(key))) throw new SecurityException("Conflicting immutable PC receipt"); }
        } else {
            FileOutputStream stream = null;
            try {
                stream = file.startWrite(); stream.write(receipt.toString().getBytes(StandardCharsets.UTF_8));
                validate(request); file.finishWrite(stream); stream = null;
            } finally { if (stream != null) file.failWrite(stream); }
        }
        JSONObject saved = new JSONObject(text(target, 16384));
        validateReceipt(saved, queued, manifestHash);
        if (!saved.getString("receipt_sha256").equals(receipt.getString("receipt_sha256"))) throw new SecurityException("Receipt readback mismatch");
        return new JSONObject().put("status", "received_on_device").put("bundle_id", bundle).put("receipt_sha256", saved.getString("receipt_sha256"));
    }

    @Override public synchronized Bundle call(String method, String arg, Bundle extras) {
        JSONObject result = new JSONObject();
        try {
            caller();
            if (extras != null && !extras.isEmpty()) throw new SecurityException("Transport extras not permitted; bound JSON must use arg");
            Object data;
            if ("identity".equals(method)) data = identity();
            else if ("response".equals(method)) {
                UUID.fromString(arg);
                File response = child(getContext().getExternalFilesDir(null), "researcher-response.json");
                JSONObject value = response.isFile() ? new JSONObject(text(response, 524288)) : null;
                data = value != null && arg.equals(value.optString("request_id")) ? value : JSONObject.NULL;
            } else {
                String raw = arg;
                if (raw == null || raw.getBytes(StandardCharsets.UTF_8).length > 16384) throw new SecurityException("Bounded request required");
                JSONObject request = new JSONObject(raw); validate(request);
                if ("dispatch".equals(method)) {
                    if (!COMMANDS.contains(request.getString("command"))) throw new SecurityException("Unknown restricted command");
                    String id = request.getString("request_id");
                    if (requests.containsKey(id)) throw new SecurityException("Duplicate request UUID");
                    File command = child(getContext().getExternalFilesDir(null), "researcher-command.json");
                    if (command.exists()) throw new SecurityException("Command mailbox busy; no existing request replaced");
                    File temp = child(getContext().getExternalFilesDir(null), ".researcher-command." + id + ".tmp");
                    try {
                        try (FileOutputStream out = new FileOutputStream(temp)) { out.write(raw.getBytes(StandardCharsets.UTF_8)); out.getFD().sync(); }
                        validate(request);
                        if (!temp.renameTo(command)) throw new SecurityException("Command commit failed");
                    } finally { if (temp.exists()) temp.delete(); }
                    requests.put(id, SystemClock.elapsedRealtime());
                    if (requests.size() > 128) requests.remove(requests.keySet().iterator().next());
                    data = new JSONObject().put("request_id", id).put("queued", true);
                } else if ("roots".equals(method)) {
                    JSONArray roots = new JSONArray().put("main");
                    File scenarios = new File(getContext().getExternalFilesDir(null), "TechnicalScenarios");
                    File[] files = scenarios.listFiles(); List<String> names = new ArrayList<>();
                    if (files != null) for (File file : files) if (file.isDirectory() && file.getName().matches("[A-Za-z0-9][A-Za-z0-9_-]{0,159}")) names.add(file.getName());
                    Collections.sort(names);
                    for (String name : names.subList(Math.max(0, names.size() - 32), names.size())) roots.put(name);
                    data = roots;
                } else if ("queues".equals(method)) {
                    String registry = request.getString("registry");
                    File[] files = child(root(registry), "ExportQueue").listFiles(); JSONArray jobs = new JSONArray();
                    if (files != null) for (File file : files) if (file.isFile() && file.getName().matches("[A-Za-z0-9][A-Za-z0-9_-]{0,159}\\.json")) {
                        String bundle = file.getName().substring(0, file.getName().length() - 5);
                        jobs.put(job(registry, bundle));
                    }
                    data = jobs;
                } else if ("delivery_ack".equals(method)) {
                    data = acknowledgeDelivery(request);
                } else if ("read_grant".equals(method)) {
                    String registry = request.getString("registry"), bundle = request.getString("bundle"), name = request.getString("name");
                    job(registry, bundle);
                    if (!FILES.contains(name)) throw new SecurityException("File not in immutable export allowlist");
                    String id = UUID.randomUUID().toString(); grants.put(id, request);
                    if (grants.size() > 32) grants.remove(grants.keySet().iterator().next());
                    data = new JSONObject().put("grant_id", id);
                } else if ("probe".equals(method)) {
                    String action = request.getString("action"); File marker = child(getContext().getExternalFilesDir(null), "technical-diagnostic.enabled");
                    if ("on".equals(action)) { if (!marker.isFile() && !marker.createNewFile()) throw new SecurityException("Marker write failed"); }
                    else if ("off".equals(action)) { if (marker.exists() && !marker.delete()) throw new SecurityException("Marker removal failed"); }
                    else if (!"status".equals(action)) throw new SecurityException("Unknown probe action");
                    data = marker.isFile() ? "ON" : "OFF";
                } else throw new SecurityException("Unsupported restricted method");
            }
            result.put("ok", true).put("data", data);
        } catch (Exception ex) { try { result.put("ok", false).put("error", ex.getMessage()); } catch (Exception ignored) {} }
        Bundle response = new Bundle(); response.putString("result", Base64.encodeToString(result.toString().getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP)); return response;
    }

    @Override public synchronized ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        try {
            caller();
            if (!"r".equals(mode) || uri.getPathSegments().size() != 2 || !"export".equals(uri.getPathSegments().get(0))) throw new SecurityException("Only grant-scoped immutable export reads permitted");
            String id = uri.getPathSegments().get(1); UUID.fromString(id);
            JSONObject request = grants.remove(id);
            if (request == null) throw new SecurityException("Missing or already consumed export grant");
            validate(request);
            String registry = request.getString("registry"), bundle = request.getString("bundle"), name = request.getString("name");
            job(registry, bundle);
            if (!FILES.contains(name)) throw new SecurityException("File outside allowlist");
            File base = child(root(registry), "Exports/" + token(bundle)); File file = child(base, name);
            if (!file.getCanonicalPath().startsWith(base.getCanonicalPath() + "/")) throw new SecurityException("Export symlink outside bundle");
            return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
        } catch (Exception ex) { throw new FileNotFoundException(ex.getMessage()); }
    }

    private static String hash(File file) throws Exception {
        MessageDigest sha = MessageDigest.getInstance("SHA-256");
        try (FileInputStream in = new FileInputStream(file)) { byte[] bytes = new byte[65536]; int n; while ((n = in.read(bytes)) != -1) sha.update(bytes, 0, n); }
        StringBuilder value = new StringBuilder(); for (byte b : sha.digest()) value.append(String.format(java.util.Locale.ROOT, "%02x", b & 255)); return value.toString();
    }

    @Override public Cursor query(Uri uri, String[] columns, String where, String[] values, String order) { throw new SecurityException("Unsupported"); }
    @Override public String getType(Uri uri) { throw new SecurityException("Unsupported"); }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new SecurityException("Unsupported"); }
    @Override public int delete(Uri uri, String where, String[] values) { throw new SecurityException("Unsupported"); }
    @Override public int update(Uri uri, ContentValues values, String where, String[] args) { throw new SecurityException("Unsupported"); }
}
