#!/usr/bin/env bash
# Host-only regression. The fake adb NEVER contacts a device; exit 88 marks
# reaching a command that would have used the legacy User-0 data transport.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
TOOLS="$(cd "$HERE/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -f -- "$fixture/adb-mock" "$fixture/calls" "$fixture/out" "$fixture/err"; rmdir -- "$fixture"' EXIT
cat > "$fixture/adb-mock" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$FAKE_ADB_LOG"
case "$*" in
  version) echo 'Android Debug Bridge version mock'; exit 0 ;;
  'shell am get-current-user')
    printf '%b' "$FAKE_CURRENT_USER"
    exit "${FAKE_QUERY_RC:-0}" ;;
  *) exit 88 ;;
esac
MOCK
chmod +x "$fixture/adb-mock"
export ADB="$fixture/adb-mock"
export FAKE_ADB_LOG="$fixture/calls"
passed=0
failed=0
for helper in quest_assert_shell_user_zero; do
  for scenario in secondary unknown malformed disconnected primary primary_crlf; do
    export FAKE_QUERY_RC=0
    case "$scenario" in
      secondary) export FAKE_CURRENT_USER='10\n'; expected=3 ;;
      unknown) export FAKE_CURRENT_USER=''; expected=3 ;;
      malformed) export FAKE_CURRENT_USER='user 0\n'; expected=3 ;;
      disconnected) export FAKE_CURRENT_USER=''; FAKE_QUERY_RC=1; expected=3 ;;
      primary) export FAKE_CURRENT_USER='0\n'; expected=88 ;;
      primary_crlf) export FAKE_CURRENT_USER='0\r\n'; expected=88 ;;
    esac
    : > "$FAKE_ADB_LOG"
    bash -c 'source "$1/quest-adb.sh"; quest_assert_shell_user_zero || exit 3; exit 88' _ "$TOOLS" > "$fixture/out" 2> "$fixture/err"
    code=$?
    ok=true
    [ "$code" -eq "$expected" ] || ok=false
    if [ "$expected" -eq 3 ]; then
      # Only executable resolution and the current-user inventory read are allowed.
      if grep -Ev '^(version|shell am get-current-user)$' "$FAKE_ADB_LOG" | grep -q .; then ok=false; fi
      grep -q 'QUEST_PROFILE_GUARD' "$fixture/err" || ok=false
    fi
    if $ok; then
      printf 'PASS %s %s (exit=%s)\n' "$helper" "$scenario" "$code"
      passed=$((passed + 1))
    else
      printf 'FAIL %s %s expected=%s actual=%s\n' "$helper" "$scenario" "$expected" "$code"
      cat "$FAKE_ADB_LOG" "$fixture/err"
      failed=$((failed + 1))
    fi
  done
done
printf 'PROFILE_GUARD_TESTS passed=%s failed=%s\n' "$passed" "$failed"
[ "$failed" -eq 0 ]
