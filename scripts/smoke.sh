#!/usr/bin/env bash
# End-to-end smoke test against a running `docker compose up` stack.
# Creates a medication + a schedule due in ~1 minute, waits for the Durable timer to fire the reminder through the
# storage queue, answers "taken" (external event), checks the loop moved on, then proves poison-queue handling and that the repair job restarts a terminated loop.
set -euo pipefail
API=${API:-http://localhost:8080/api}
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print($1)"; }
step() { printf '\n== %s\n' "$*"; }

step "health"
curl -fsS "$API/health"; echo
curl -fsS "${API%/api}/healthz"

step "create medication + schedule due next minute (Africa/Cairo)"
MED=$(curl -fsS -X POST "$API/medications" -H 'content-type: application/json' \
  -d '{"name":"Metformin","strength":"500 mg","pillsOnHand":12,"refillThresholdDays":7}' | json 'd["id"]')
DUE_LOCAL=$(TZ=Africa/Cairo date -d '+1 min' +%H:%M)
SCH=$(curl -fsS -X POST "$API/schedules" -H 'content-type: application/json' \
  -d "{\"medicationId\":\"$MED\",\"timesOfDay\":[\"$DUE_LOCAL\"],\"dosePerIntake\":1,\"lateWindowMinutes\":4,\"caregiverContact\":\"caregiver@example.com\"}" | json 'd["id"]')
echo "medication $MED, schedule $SCH at $DUE_LOCAL"

step "wait for the reminder (durable timer -> activity -> queue -> queue trigger -> notifications table)"
for i in $(seq 1 60); do
  if curl -fsS "$API/notifications" | grep -q "\"scheduleId\":\"$SCH\""; then break; fi
  [ "$i" = 60 ] && { echo "FAIL: no reminder"; exit 1; }
  sleep 3
done
curl -fsS "$API/notifications" | json "[n['kind']+': '+n['text'] for n in d if n.get('scheduleId')=='$SCH']"

step "snooze 5 minutes (external event; the loop waits on a durable timer but still accepts an answer)"
DUE=$(curl -fsS "$API/doses/today" | json "[x['dueUtc'] for x in d if x['scheduleId']=='$SCH'][0]")
curl -fsS -o /dev/null -w '%{http_code}\n' -X POST "$API/doses/snooze" -H 'content-type: application/json' \
  -d "{\"scheduleId\":\"$SCH\",\"dueUtc\":\"$DUE\",\"minutes\":5}"
for i in $(seq 1 15); do
  curl -fsS "$API/schedules/$SCH/reminders" | grep -q 'snoozed until' && break
  [ "$i" = 15 ] && { echo "FAIL: not snoozed"; exit 1; }
  sleep 2
done
curl -fsS "$API/schedules/$SCH/reminders" | json 'd["customStatus"]'

step "take the dose (raises DoseResponse on the orchestration)"
curl -fsS -X POST "$API/doses/take" -H 'content-type: application/json' -d "{\"scheduleId\":\"$SCH\",\"dueUtc\":\"$DUE\"}" | json 'd["status"]'
for i in $(seq 1 20); do
  STATUS=$(curl -fsS "$API/schedules/$SCH/reminders")
  echo "$STATUS" | json 'd["customStatus"]' | grep -qv "${DUE:0:16}" && break
  [ "$i" = 20 ] && { echo "FAIL: loop did not move on: $STATUS"; exit 1; }
  sleep 2
done
echo "$STATUS" | json 'd["runtimeStatus"]+" / "+d["customStatus"]'
curl -fsS "$API/doses/today" | json "[x['status'] for x in d if x['scheduleId']=='$SCH']"
curl -fsS "$API/medications" | json "[m['pillsOnHand'] for m in d if m['id']=='$MED']"
echo "snooze on a settled dose is rejected: $(curl -s -o /dev/null -w '%{http_code}' -X POST "$API/doses/snooze" -H 'content-type: application/json' -d "{\"scheduleId\":\"$SCH\",\"dueUtc\":\"$DUE\",\"minutes\":5}")"

step "refill forecast + on-demand refill check (same code as the nightly timer)"
curl -fsS "$API/refills" | json "[f for f in d if f['medicationId']=='$MED']"
curl -fsS -X POST "$API/refills/check" -H 'content-type: application/json' -d '{}'; echo

step "weekly adherence report (fan-out/fan-in orchestration)"
RUN=$(curl -fsS -X POST "$API/reports/weekly" -H 'content-type: application/json' -d '{}' | json 'd["instanceId"]')
for i in $(seq 1 30); do
  R=$(curl -fsS "$API/reports/weekly/$RUN")
  echo "$R" | grep -q '"Completed"' && break
  [ "$i" = 30 ] && { echo "FAIL: report not completed: $R"; exit 1; }
  sleep 2
done
echo "$R" | json '"overall %s%% over %d schedule(s)" % (d["report"]["overallPercent"], len(d["report"]["schedules"]))'

step "poison queue: a malformed message is retried 3x, then parked in the deadletters table"
CS="UseDevelopmentStorage=true"
az storage message put -q notifications --content "$(printf 'not json' | base64)" --connection-string "$CS" -o none --only-show-errors
for i in $(seq 1 40); do
  N=$(curl -fsS "$API/deadletters" | json 'd["count"]')
  [ "${N:-0}" -ge 1 ] && break
  [ "$i" = 40 ] && { echo "FAIL: nothing in deadletters"; exit 1; }
  sleep 3
done
echo "deadletters: $N"

step "repair: a terminated reminder loop is restarted by the repair job, and a second run is a no-op"
# Terminating needs the host's master key; the local host keeps it unencrypted in Azurite (never do this in Azure).
FUNCS=${FUNCS:-http://localhost:7071}
KEYS=$(mktemp -d)
BLOB=$(az storage blob list -c azure-webjobs-secrets --connection-string "$CS" --only-show-errors --query "[?ends_with(name, 'host.json')].name | [0]" -o tsv)
az storage blob download -c azure-webjobs-secrets -n "$BLOB" -f "$KEYS/host.json" --connection-string "$CS" --only-show-errors --no-progress -o none
curl -fsS -o /dev/null -X POST "$FUNCS/runtime/webhooks/durabletask/instances/reminders-$SCH/terminate?reason=smoke&code=$(json 'd["masterKey"]["value"]' < "$KEYS/host.json")"
rm -rf "$KEYS"
for i in $(seq 1 20); do
  [ "$(curl -fsS "$API/schedules/$SCH/reminders" | json 'd["runtimeStatus"]')" = Terminated ] && break
  [ "$i" = 20 ] && { echo "FAIL: loop not terminated"; exit 1; }
  sleep 1
done
curl -fsS -X POST "$API/reminders/repair" -H 'content-type: application/json' -d '{}' | json 'd["restarted"]' | grep -q "$SCH" || { echo "FAIL: not restarted"; exit 1; }
for i in $(seq 1 20); do
  S=$(curl -fsS "$API/schedules/$SCH/reminders" | json 'd["runtimeStatus"]')
  [ "$S" = Running ] && break
  [ "$i" = 20 ] && { echo "FAIL: loop not running again ($S)"; exit 1; }
  sleep 1
done
echo "loop $S again; second repair restarted: $(curl -fsS -X POST "$API/reminders/repair" -H 'content-type: application/json' -d '{}' | json 'len(d["restarted"])')"

echo; echo "SMOKE PASS"
