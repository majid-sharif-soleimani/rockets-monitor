"""Computes the expected rocket states from captured messages (an implementation independent
of the C# one) and compares them with the service's fleet API."""
import json
import sys
import urllib.request
from collections import Counter

messages = [json.loads(line) for line in open(sys.argv[1])]
api = sys.argv[2]

seen = set()
duplicates = 0
out_of_order = 0
last_number = {}
rockets = {}
types = Counter()
for m in messages:
    md, body = m["metadata"], m["message"]
    ch, n, t = md["channel"], md["messageNumber"], md["messageType"]
    types[t] += 1
    if n < last_number.get(ch, 0):
        out_of_order += 1
    last_number[ch] = max(last_number.get(ch, 0), n)
    if (ch, n) in seen:
        duplicates += 1
        continue
    seen.add((ch, n))
    r = rockets.setdefault(ch, {"type": None, "launchSpeed": 0, "delta": 0, "mission": None,
                                "missionN": 0, "exploded": None, "launched": False})
    if t == "RocketLaunched":
        if r["launched"]:
            print("conflicting launch", ch, n)
            continue
        r.update(launched=True, type=body["type"], launchSpeed=body["launchSpeed"])
        if n > r["missionN"]:
            r.update(mission=body["mission"], missionN=n)
    elif t == "RocketSpeedIncreased":
        r["delta"] += body["by"]
    elif t == "RocketSpeedDecreased":
        r["delta"] -= body["by"]
    elif t == "RocketMissionChanged":
        if n > r["missionN"]:
            r.update(mission=body["newMission"], missionN=n)
    elif t == "RocketExploded":
        if r["exploded"] is None:
            r["exploded"] = body["reason"]

print(f"messages={len(messages)} unique={len(seen)} duplicates={duplicates} "
      f"out_of_order={out_of_order} rockets={len(rockets)}")
print("by type:", dict(types))

actual = {}
page = 1
while True:
    with urllib.request.urlopen(f"{api}/api/fleet/rockets?pageSize=500&page={page}") as resp:
        data = json.load(resp)
    actual.update({r["channel"]: r for r in data["items"]})
    if page >= data["totalPages"]:
        break
    page += 1

mismatches = 0
for ch, r in rockets.items():
    status = "exploded" if r["exploded"] else "active" if r["launched"] else "notLaunched"
    expected = {"launched": r["launched"], "type": r["type"], "speed": r["launchSpeed"] + r["delta"],
                "mission": r["mission"], "status": status, "explosionReason": r["exploded"]}
    got = actual.get(ch)
    got = {k: got[k] for k in expected} if got else None
    if got != expected:
        mismatches += 1
        print("MISMATCH", ch, "expected", expected, "got", got)

print(f"compared={len(rockets)} api_rockets={len(actual)} mismatches={mismatches}")
