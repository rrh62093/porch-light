#!/usr/bin/env bash
# Seeds a local dev database with a demo circle, profiles, requests, and a deal.
# Needs the API running in Development (dev login enabled). Usage: scripts/seed-dev.sh [api-url]
set -euo pipefail
API="${1:-http://localhost:5208}/api"
JAR=$(mktemp -d)
trap 'rm -rf "$JAR"' EXIT

login() { curl -sf -c "$JAR/$1" -H 'Content-Type: application/json' -d "{\"email\":\"$1@example.com\",\"name\":\"$2\"}" "$API/auth/dev" >/dev/null; }
call() { local who=$1 method=$2 path=$3 body=${4:-}; curl -sf -b "$JAR/$who" -X "$method" -H 'Content-Type: application/json' ${body:+-d "$body"} "$API$path"; }
field() { python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

login rob Rob; login mike Mike; login dave Dave
CIRCLE=$(call rob POST /circles '{"name":"Maple Street dads"}' | field "['id']")
for who in mike dave; do
  call rob POST "/circles/$CIRCLE/invites" "{\"email\":\"$who@example.com\"}" >/dev/null
  INV=$(call $who GET /invites | field "[0]['id']")
  call $who POST "/invites/$INV/accept" >/dev/null
done

call mike POST /me/items '{"kind":"skill","name":"Electrical","description":"Fixtures, switches, outlets"}' >/dev/null
call mike POST /me/items '{"kind":"tool","name":"24 ft extension ladder"}' >/dev/null
call dave POST /me/items '{"kind":"skill","name":"Smoking a brisket"}' >/dev/null
call rob POST /me/items '{"kind":"tool","name":"Pressure washer"}' >/dev/null
call rob PUT /me/child-profile '{"offersChildcare":true,"needsChildcare":true,"kidsAges":"4 and 7","allergies":"Peanuts","contactNumber":"555-0100","notes":"Bedtime 8pm"}' >/dev/null

# An open request from Dave, and a deal between Rob and Mike with an IOU outstanding.
call dave POST /requests "{\"circleId\":\"$CIRCLE\",\"title\":\"Need a 16 ft+ ladder Saturday\",\"description\":\"Cleaning gutters.\",\"offers\":[{\"description\":\"Brisket plate\"},{\"description\":\"Beer on me\"}]}" >/dev/null
REQ=$(call rob POST /requests "{\"circleId\":\"$CIRCLE\",\"title\":\"Hang a light fixture in the dining room\",\"offers\":[{\"description\":\"Watch your kids for an evening\",\"isChildcare\":true},{\"description\":\"\$40\"}]}" | field "['id']")
call mike POST "/requests/$REQ/respond" '{"kind":"counter","customText":"Two evenings of watching my kids","customIsChildcare":true,"message":"It is a two-person job, honestly"}' >/dev/null
echo "Seeded. Sign in as rob@example.com, mike@example.com, or dave@example.com."
