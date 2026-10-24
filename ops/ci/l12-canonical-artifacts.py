"""Read-only qualification of the three outputs of verify-l12.ps1, not a packager."""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
from pathlib import Path
import re
import stat

spec = importlib.util.spec_from_file_location("card_input", Path(__file__).with_name("l12-card-assets-input.py"))
assets = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assets)


def plain(path):
    info = path.lstat()
    assets.require(not path.is_symlink() and not (getattr(info, "st_file_attributes", 0) & 0x400)
        and stat.S_ISREG(info.st_mode) and info.st_nlink == 1, "CANONICAL_FILE_NOT_PLAIN")
    assets.require(path.resolve() == path, "CANONICAL_FILE_ALIAS_REJECTED")


def qualify(root, commit, lock):
    assets.require(root.is_absolute() and root.resolve() == root and root.is_dir(), "CANONICAL_ROOT_INVALID")
    assets.require(re.fullmatch(r"[0-9a-f]{40}", commit) is not None, "CANONICAL_COMMIT_INVALID")
    assets.validate_lock(lock)
    manifest = root / commit / ("l12-release-" + commit + ".json")
    plain(manifest)
    document = assets.strict_json(assets.read_regular(manifest, 65536).decode("utf-8-sig"))
    assets.require(isinstance(document, dict) and set(document) == {
        "schema", "commit", "deploymentDrainProtocol", "requiresExternalStopPermit", "runtimeDataCompatibility",
        "runtimeSourceCompatibility", "generatedAt", "releaseArchive", "releaseSha256", "cardAssetsHash",
        "cardAssetsArchive", "cardAssetsSha256", "releaseBaseCommit", "playerReleaseNotesSha256",
    }, "CANONICAL_MANIFEST_FIELDS")
    assets.require(type(document.get("schema")) is int and document["schema"] == 3
        and document.get("commit") == commit, "CANONICAL_MANIFEST_IDENTITY")
    assets.require(type(document.get("deploymentDrainProtocol")) is int and document["deploymentDrainProtocol"] == 1
        and document.get("requiresExternalStopPermit") is True
        and document.get("runtimeDataCompatibility") == "l12-runtime-v1"
        and isinstance(document.get("runtimeSourceCompatibility"), str)
        and assets.HEX.fullmatch(document["runtimeSourceCompatibility"]), "CANONICAL_RUNTIME_CAPABILITY")
    assets.require(document.get("cardAssetsHash") == lock["assetVersion"], "CANONICAL_ASSET_VERSION")
    assets.require(isinstance(document["releaseBaseCommit"], str) and (document["releaseBaseCommit"] == ""
        or re.fullmatch(r"[0-9a-f]{40}", document["releaseBaseCommit"])), "CANONICAL_NOTES_BASE")
    assets.require(isinstance(document["generatedAt"], str), "CANONICAL_GENERATED_AT")
    generated = datetime.fromisoformat(document["generatedAt"])
    assets.require(generated.tzinfo is not None and generated.utcoffset() == timezone.utc.utcoffset(generated), "CANONICAL_GENERATED_AT")
    assets.require(isinstance(document.get("playerReleaseNotesSha256"), str)
        and assets.HEX.fullmatch(document["playerReleaseNotesSha256"]), "CANONICAL_PLAYER_NOTES_HASH")
    result = {"manifest": str(manifest)}
    for key, hash_key, expected, budget in [
        ("releaseArchive", "releaseSha256", root / commit / ("l12-release-" + commit + ".tar.gz"), 150 * 1024**2),
        ("cardAssetsArchive", "cardAssetsSha256", root / ("l12-card-assets-" + lock["assetVersion"] + ".tar.gz"), assets.MAX_TOTAL_BYTES),
    ]:
        assets.require(document.get(key) == str(expected), "CANONICAL_OUTPUT_PATH")
        plain(expected)
        expected_hash = document.get(hash_key)
        assets.require(isinstance(expected_hash, str) and assets.HEX.fullmatch(expected_hash)
            and assets.file_digest(expected, budget) == expected_hash, "CANONICAL_ARCHIVE_BYTES")
        result[key] = str(expected)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-root", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--lock", required=True)
    args = parser.parse_args()
    lock, _ = assets.load_lock(args.lock)
    assets.assert_plain_prepared_root(Path(args.output_root))
    root = assets.generated_path(args.output_root)
    print(json.dumps(qualify(root, args.commit, lock), separators=(",", ":")))
