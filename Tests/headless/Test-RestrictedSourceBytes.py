"""Read-only Git/source portability check; does not stage or write Git objects.

Default: bind current helper literal to working-tree, clean-filter and staged/
raw-exported contract bytes. --staged additionally validates the staged helper.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[2]
CONTRACT = "tools/headless/restricted-startup-contract.json"
HELPER = "tools/headless/RestrictedCliCore.psm1"


def git(*args: str, data: bytes | None = None) -> bytes:
    return subprocess.run(
        ["git", *args], cwd=ROOT, input=data, stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, check=True,
    ).stdout


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def helper_digest(data: bytes) -> str:
    matches = re.findall(rb"Get-AzuriteSha256 \$Path\) -cne '([0-9A-F]{64})'", data)
    if len(matches) != 1:
        raise AssertionError("Expected exactly one production contract digest literal")
    return matches[0].decode("ascii")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--staged", action="store_true")
    parser.add_argument("--baseline", type=Path)
    args = parser.parse_args()
    contract = (ROOT / CONTRACT).read_bytes()
    helper = (ROOT / HELPER).read_bytes()
    # Capture actual index/export bytes without any text decoding or shell redirection.
    staged_contract = git("show", ":" + CONTRACT)
    clean_oid = git("hash-object", "--path=" + CONTRACT, "--stdin", data=contract).strip()
    raw_oid = git("hash-object", "--no-filters", "--stdin", data=contract).strip()
    staged_oid = git("hash-object", "--no-filters", "--stdin", data=staged_contract).strip()
    errors = []
    if b"\r" in contract or not contract.endswith(b"\n"):
        errors.append("Contract is not LF source with a trailing newline")
    if b"\r" in helper or not helper.endswith(b"\n") or helper.endswith(b"\n\n"):
        errors.append("Helper must use LF and exactly one trailing newline")
    if raw_oid != clean_oid or contract != staged_contract or clean_oid != staged_oid:
        errors.append("Working-tree, Git-clean and staged/exported contract bytes differ")
    if helper_digest(helper) != sha(staged_contract):
        errors.append("Production literal does not match repository-normalized contract bytes")
    payload = json.loads(contract)
    if payload != json.loads(staged_contract):
        errors.append("Contract payload differs from staged payload")
    if args.baseline and payload != json.loads(args.baseline.read_bytes()):
        errors.append("Contract payload/pins/evidence changed from the supplied baseline")
    staged_helper_checked = False
    if args.staged:
        staged_helper = git("show", ":" + HELPER)
        staged_helper_checked = True
        if staged_helper != helper or helper_digest(staged_helper) != sha(staged_contract):
            errors.append("Staged helper/contract pair is stale or has an invalid literal")
        if b"\r" in staged_helper or not staged_helper.endswith(b"\n") or staged_helper.endswith(b"\n\n"):
            errors.append("Staged helper violates LF/single-final-newline policy")
    artifact_root = ROOT / "artifacts" / "restricted-source-bytes"
    artifact_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=artifact_root) as directory:
        exported = Path(directory) / "restricted-startup-contract.json"
        exported.write_bytes(staged_contract)
        exported_hash = sha(exported.read_bytes())
        if exported_hash != helper_digest(helper):
            errors.append("Raw exported contract cannot satisfy current production literal")
    print(json.dumps({
        "workingTreeSha256": sha(contract), "repositoryBytesSha256": sha(staged_contract),
        "productionLiteral": helper_digest(helper), "rawExportSha256": exported_hash,
        "cleanObjectId": clean_oid.decode("ascii"), "stagedHelperChecked": staged_helper_checked,
        "baselinePayloadEquivalent": args.baseline is not None and not any("baseline" in e for e in errors),
        "filePins": len(payload["files"]), "exactScopes": len(payload["exactDirectories"]),
        "evidencePins": len(payload["evidence"]), "errors": errors,
    }, indent=2))
    if errors:
        print("FAIL: restricted source byte portability")
        return 1
    print("PASS: restricted source byte portability; Git reads only")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
