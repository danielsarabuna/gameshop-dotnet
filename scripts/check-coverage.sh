#!/usr/bin/env bash
set -euo pipefail

results_dir="${1:-TestResults}"
minimum_percent="${2:-30}"

python3 - "$results_dir" "$minimum_percent" <<'PY'
import sys
from pathlib import Path
from xml.etree import ElementTree

results_dir = Path(sys.argv[1])
minimum_percent = float(sys.argv[2])
reports = sorted(results_dir.rglob("coverage.cobertura.xml"))
lines: dict[tuple[str, int], int] = {}

for report in reports:
    for class_node in ElementTree.parse(report).iter("class"):
        filename = class_node.attrib["filename"]
        for line_node in class_node.iter("line"):
            key = (filename, int(line_node.attrib["number"]))
            lines[key] = max(lines.get(key, 0), int(line_node.attrib["hits"]))

if not lines:
    raise SystemExit(f"No Cobertura coverage reports found in {results_dir}.")

covered = sum(hits > 0 for hits in lines.values())
coverage = covered * 100 / len(lines)
print(f"Line coverage: {coverage:.2f}% ({covered}/{len(lines)})")

if coverage < minimum_percent:
    raise SystemExit(
        f"Coverage is below the {minimum_percent:g}% no-regression baseline."
    )
PY
