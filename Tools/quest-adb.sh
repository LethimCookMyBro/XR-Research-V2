# Sourced by quest-next-session.sh and researcher-adb.sh. Sets ADB or exits at once.
# Priority: $ADB env -> Unity 6000.3.20f1 Android SDK -> scrcpy (WinGet) -> adb on PATH.
# Needs Git Bash (MSYS): the scripts pass Windows paths via cygpath. Under WSL
# (C:\Windows\System32\bash.exe) /c/... does not exist, which used to make every adb call
# fail silently and start-full-flow wait 180 s for an "app not running" that was really "no adb".
command -v cygpath >/dev/null 2>&1 ||
  { echo "FAIL: run from Git Bash (e.g. \"D:\\Git\\bin\\bash.exe\" $0), not WSL bash; cygpath missing" >&2; exit 3; }
if [ -n "${ADB:-}" ] && ! { [ -f "$ADB" ] && "$ADB" version >/dev/null 2>&1; }; then
  echo "FAIL: ADB=$ADB is set but is not a working adb executable" >&2; exit 3
fi
quest_adb_candidates() {
  [ -z "${ADB:-}" ] || echo "$ADB"
  echo "/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
  echo "/c/Users/DMI/AppData/Local/Microsoft/WinGet/Packages/Genymobile.scrcpy_Microsoft.Winget.Source_8wekyb3d8bbwe/scrcpy-win64-v4.1/adb.exe"
  command -v adb 2>/dev/null || true
}
quest_adb_resolve() {
  local c tried=
  while IFS= read -r c; do
    [ -n "$c" ] || continue
    if [ -f "$c" ] && "$c" version >/dev/null 2>&1; then echo "$c"; return 0; fi
    tried="$tried
  $c"
  done < <(quest_adb_candidates)
  echo "FAIL: no working adb found. Tried:$tried
Set ADB=/path/to/adb.exe to override." >&2
  return 3
}
ADB="$(quest_adb_resolve)" || exit 3
export ADB
export QUEST_SERIAL="${QUEST_SERIAL:-2G0YC5ZHBF00FJ}"

quest_profile_command() {
  local profile_script
  profile_script="$(cygpath -w "$(dirname "${BASH_SOURCE[0]}")/quest_profile.py")"
  python "$profile_script" --adb "$(cygpath -w "$ADB")" --serial "$QUEST_SERIAL" "$@"
}

quest_profile_begin() {
  if [ -n "${QUEST_PROFILE_BINDING:-}" ]; then
    quest_profile_command check >/dev/null || return 3
  else
    local binding
    binding="$(mktemp)"
    quest_profile_command identity > "$binding" || { rm -f "$binding"; return 3; }
    export QUEST_PROFILE_BINDING="$(cygpath -w "$binding")"
    trap "rm -f -- $(printf '%q' "$binding")" EXIT
  fi
}

quest_profile_check() { quest_profile_command check >/dev/null; }
quest_adb() { "$ADB" -s "$QUEST_SERIAL" "$@"; }

# Legacy /sdcard command transports address shell User 0, NOT necessarily
# the foreground Quest profile. Fail before using that transport rather than
# querying/controlling an unrelated background instance of the same package.
# This is a pre-dispatch guard, not a complete multi-user transport or lease.
quest_assert_shell_user_zero() {
  local current_user
  if ! current_user="$("$ADB" shell am get-current-user 2>/dev/null)"; then
    echo 'QUEST_PROFILE_GUARD: current Android user unavailable; legacy /sdcard transport not used.' >&2
    return 3
  fi
  current_user="${current_user//$'\r'/}"
  if [ "$current_user" != '0' ]; then
    echo "QUEST_PROFILE_GUARD: current Android user=${current_user:-unknown}; legacy /sdcard transport is User 0 only." >&2
    echo 'No app command was sent. Use a verified user-aware transport; do not switch profiles or bypass permissions automatically.' >&2
    return 3
  fi
}
