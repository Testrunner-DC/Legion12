"""Reviewed, byte-pinned inputs for the existing canonical release packager.

Only prepares --CardAssetDirectory; does not build a release or change a server.
Pin from an accepted schema-3 archive, review/commit the lock, then prepare an
exact tree from that archive or the fixed public immutable-image origin.
"""
import argparse
import base64
import concurrent.futures
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tarfile
import threading
import time
import urllib.request

REPO = Path(__file__).resolve().parents[2]
ORIGIN = "https://legion-12.com/card-assets/"
METADATA = {"card-assets.manifest.json", "card-assets.preload.json"}
NAMES = {"original.webp", "thumb-240.webp", "board-480.webp", "detail-960.webp", "detail-960.avif"}
VARIANTS = {"originalWebp": "original.webp", "thumbWebp": "thumb-240.webp", "boardWebp": "board-480.webp",
    "detailWebp": "detail-960.webp", "detailAvif": "detail-960.avif"}
CARD_ID = r"(?:(?:S|ST)[0-9]{2}-[A-Za-z0-9]+|ST-DS0[123])"
IMAGE = re.compile(r"cards/[A-Za-z0-9_-]+/" + CARD_ID + r"/[0-9a-f]{20}/(?:original\.webp|thumb-240\.webp|board-480\.webp|detail-960\.webp|detail-960\.avif)\Z")
HEX = re.compile(r"[0-9a-f]{64}\Z")
MAX_FILES, MAX_FILE_BYTES, MAX_TOTAL_BYTES = 2500, 16 * 1024**2, 400 * 1024**2
MAX_LOCK_BYTES = 4 * 1024**2
MAX_TAR_BYTES = MAX_TOTAL_BYTES + MAX_FILES * 4096


def require(condition, code):
    if not condition:
        raise ValueError(code)


def digest(value):
    return hashlib.sha256(value).hexdigest()


def strict_json(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "DUPLICATE_JSON_KEY")
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=unique)


def open_regular(file, maximum):
    file = Path(file)
    before = file.lstat()
    require(stat.S_ISREG(before.st_mode) and 0 < before.st_size <= maximum, "REGULAR_INPUT_BUDGET_REJECTED")
    descriptor = os.open(file, os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NONBLOCK", 0))
    try:
        actual = os.fstat(descriptor)
        require(stat.S_ISREG(actual.st_mode) and 0 < actual.st_size <= maximum
            and (actual.st_dev, actual.st_ino) == (before.st_dev, before.st_ino), "REGULAR_INPUT_CHANGED")
        return os.fdopen(descriptor, "rb")
    except BaseException:
        os.close(descriptor)
        raise


def read_regular(file, maximum):
    with open_regular(file, maximum) as stream:
        raw = stream.read(maximum + 1)
    require(len(raw) <= maximum, "INPUT_READ_BUDGET_EXCEEDED")
    return raw


def file_digest(file, maximum=None):
    maximum = MAX_TOTAL_BYTES if maximum is None else maximum
    result = hashlib.sha256()
    size = 0
    with open_regular(file, maximum) as stream:
        for chunk in iter(lambda: stream.read(min(1024 * 1024, maximum + 1)), b""):
            size += len(chunk)
            require(size <= maximum, "INPUT_READ_BUDGET_EXCEEDED")
            result.update(chunk)
    return result.hexdigest()


def safe_path(value):
    require(isinstance(value, str) and (value in METADATA or IMAGE.fullmatch(value)), "ASSET_PATH_INVALID")
    return value


def generated_path(value):
    """Use the shared ancestor-aware guard, not a second path policy."""
    target = Path(value)
    require(target.is_absolute(), "OUTPUT_MUST_BE_ABSOLUTE")
    env = dict(os.environ, L12_CARD_GUARD_SCRIPT=str(REPO / "ops/windows/L12GeneratedStorage.ps1"),
               L12_CARD_GUARD_OUTPUT=str(target))
    executable = env.get("L12_PWSH") or shutil.which("pwsh")
    require(executable is not None, "POWERSHELL_STORAGE_GUARD_REQUIRED")
    result = subprocess.run([executable, "-NoProfile", "-NonInteractive", "-Command",
        "$ErrorActionPreference='Stop'; . $env:L12_CARD_GUARD_SCRIPT; "
        "Assert-L12GeneratedPath -Path $env:L12_CARD_GUARD_OUTPUT -Purpose 'trusted card asset input'"],
        env=env, capture_output=True, text=True, encoding="utf-8", timeout=30, check=False)
    require(result.returncode == 0, "GENERATED_STORAGE_GUARD_REJECTED")
    physical = Path(result.stdout.strip())
    require(physical.is_absolute() and target.resolve() == physical.resolve(), "PHYSICAL_OUTPUT_MISMATCH")
    return physical


def archive_records(archive, expected_sha=None):
    """Read bounded bytes; never extract tar paths, links or special files."""
    class HashedReader:
        def __init__(self, disk):
            self.disk, self.sha, self.bytes = disk, hashlib.sha256(), 0

        def read(self, size):
            require(type(size) is int and size >= 0, "BOUNDED_ARCHIVE_READ_REQUIRED")
            raw = self.disk.read(min(size, MAX_TOTAL_BYTES - self.bytes + 1))
            self.bytes += len(raw)
            require(self.bytes <= MAX_TOTAL_BYTES, "ARCHIVE_COMPRESSED_BUDGET_EXCEEDED")
            self.sha.update(raw)
            return raw

    class TarReader:
        def __init__(self, decoded):
            self.decoded, self.bytes = decoded, 0

        def read(self, size):
            # This wraps gzip BEFORE tarfile sees extension headers, which its
            # iterator expands internally before yielding a visible member.
            require(self.bytes < MAX_TAR_BYTES, "ARCHIVE_DECOMPRESSED_BUDGET_EXCEEDED")
            raw = self.decoded.read(min(size, MAX_TAR_BYTES - self.bytes + 1))
            self.bytes += len(raw)
            require(self.bytes <= MAX_TAR_BYTES, "ARCHIVE_DECOMPRESSED_BUDGET_EXCEEDED")
            return raw

    with open_regular(archive, MAX_TOTAL_BYTES) as disk:
        reader = HashedReader(disk)
        with gzip.GzipFile(fileobj=reader, mode="rb") as decoded:
            yield from read_archive(TarReader(decoded))
        # Bind the actual compressed bytes consumed, including any trailing
        # bytes, rather than trusting a hash of a previously reopened pathname.
        while reader.read(1024 * 1024):
            pass
        if expected_sha is not None:
            require(reader.sha.hexdigest() == expected_sha, "ACCEPTED_ARCHIVE_SHA_MISMATCH")


def read_archive(reader):
    seen, count, total, members = set(), 0, 0, 0
    with tarfile.open(fileobj=reader, mode="r|") as source:
        for entry in source:
            members += 1
            require(members <= MAX_FILES * 4, "ARCHIVE_MEMBER_BUDGET_EXCEEDED")
            name = entry.name
            if name in (".", "./") and entry.isdir():
                continue
            if name.startswith("./"):
                name = name[2:]
            require(not entry.issym() and not entry.islnk(), "ARCHIVE_LINK_REJECTED")
            if entry.isdir():
                require(re.fullmatch(r"(?:cards|cards/[A-Za-z0-9_-]+|cards/[A-Za-z0-9_-]+/" + CARD_ID
                    + r"|cards/[A-Za-z0-9_-]+/" + CARD_ID + r"/[0-9a-f]{20})/?", name), "ARCHIVE_DIRECTORY_INVALID")
                continue
            require(entry.isfile(), "ARCHIVE_SPECIAL_REJECTED")
            safe_path(name)
            require(name.casefold() not in seen, "ARCHIVE_DUPLICATE_REJECTED")
            seen.add(name.casefold())
            count += 1
            total += entry.size
            require(0 < entry.size <= MAX_FILE_BYTES and count <= MAX_FILES and total <= MAX_TOTAL_BYTES, "ARCHIVE_BUDGET_EXCEEDED")
            with source.extractfile(entry) as stream:
                raw = stream.read(entry.size + 1)
            require(len(raw) == entry.size, "ARCHIVE_SIZE_MISMATCH")
            yield name, raw


def expected_images(manifest):
    require(type(manifest.get("schemaVersion")) is int and manifest.get("schemaVersion") == 3 and manifest.get("complete") is True
        and manifest.get("cardCount") == 366 and manifest.get("playableCardCount") == 324
        and manifest.get("presentationCardCount") == 42, "FULL_SCHEMA3_ASSETS_REQUIRED")
    cards = manifest.get("cards")
    require(isinstance(cards, dict) and len(cards) == 366, "FULL_CARD_MAP_REQUIRED")
    required, identities, playable_count, presentation_count = {}, [], 0, 0
    for card_id, card in cards.items():
        require(card.get("cardId") == card_id and isinstance(card.get("contentHash"), str)
            and HEX.fullmatch(card["contentHash"]), "CARD_IDENTITY_INVALID")
        presentation = card.get("presentationOnly", False)
        require(type(presentation) is bool, "PRESENTATION_FLAG_INVALID")
        if presentation:
            presentation_count += 1
            base = card.get("baseCardId")
            require(isinstance(base, str) and base in cards and base != card_id
                and cards[base].get("presentationOnly", False) is False, "PRESENTATION_BASE_INVALID")
        else:
            playable_count += 1
            require(card.get("baseCardId") in (None, ""), "PLAYABLE_BASE_INVALID")
        variants, sizes = card.get("variants"), card.get("bytes")
        require(isinstance(variants, dict) and set(variants) == set(VARIANTS) and isinstance(sizes, dict)
            and set(sizes) == set(VARIANTS), "FULL_VARIANTS_REQUIRED")
        prefix = f"cards/{manifest.get('catalogVersion')}/{card_id}/{card['contentHash'][:20]}/"
        for variant, relative in variants.items():
            safe_path(relative)
            require(relative.startswith(prefix) and relative not in required and type(sizes[variant]) is int
                and 0 < sizes[variant] <= MAX_FILE_BYTES, "VARIANT_IDENTITY_INVALID")
            require(relative.rsplit('/', 1)[1] == VARIANTS[variant], "VARIANT_NAME_MISMATCH")
            required[relative] = sizes[variant]
        require({relative.rsplit('/', 1)[1] for relative in variants.values()} == NAMES, "VARIANT_NAMES_INVALID")
        identities.append(":".join([card_id, card["contentHash"], "presentation" if card.get("presentationOnly") else "playable", card.get("baseCardId") or ""]))
    require((playable_count, presentation_count) == (324, 42), "ACTUAL_CARD_CATEGORY_COUNTS_MISMATCH")
    require(sum(required.values()) == manifest.get("totalBytes") and digest("\n".join(sorted(identities)).encode()) == manifest.get("assetVersion"), "ASSET_VERSION_OR_TOTAL_MISMATCH")
    return required


def verify_bytes(raw, row):
    require(len(raw) == row["size"] and digest(raw) == row["sha256"], "ASSET_BYTES_MISMATCH")


def validate_lock(lock):
    require(isinstance(lock, dict) and set(lock) == {"schema", "origin", "acceptedRelease", "assetVersion", "files"}
        and type(lock["schema"]) is int and lock["schema"] == 1 and lock["origin"] == ORIGIN, "INPUT_LOCK_SCHEMA_INVALID")
    accepted = lock["acceptedRelease"]
    require(isinstance(accepted, dict) and set(accepted) == {"commit", "manifestSha256", "archiveSha256"}
        and isinstance(accepted["commit"], str) and re.fullmatch(r"[0-9a-f]{40}", accepted["commit"])
        and all(isinstance(accepted[key], str) and HEX.fullmatch(accepted[key]) for key in ("manifestSha256", "archiveSha256")), "ACCEPTANCE_IDENTITY_INVALID")
    rows, files, metadata, total = lock["files"], {}, {}, 0
    require(isinstance(rows, list) and 2 < len(rows) <= MAX_FILES, "INPUT_FILE_COUNT_INVALID")
    folded = set()
    for row in rows:
        require(isinstance(row, dict) and isinstance(row.get("path"), str), "INPUT_ROW_INVALID")
        name = safe_path(row["path"])
        keys = {"path", "size", "sha256", "role"} | ({"base64"} if name in METADATA else set())
        require(set(row) == keys and type(row["size"]) is int and 0 < row["size"] <= MAX_FILE_BYTES
            and isinstance(row["sha256"], str) and HEX.fullmatch(row["sha256"]), "INPUT_ROW_INVALID")
        require(name.casefold() not in folded, "INPUT_DUPLICATE_REJECTED")
        folded.add(name.casefold())
        total += row["size"]
        require(total <= MAX_TOTAL_BYTES, "INPUT_TOTAL_EXCEEDED")
        files[name] = row
        if name in METADATA:
            require(row["role"] == "metadata", "METADATA_ROLE_INVALID")
            raw = base64.b64decode(row["base64"], validate=True)
            verify_bytes(raw, row)
            metadata[name] = raw
        else:
            require(row["role"] in ("current", "compatibility"), "IMAGE_ROLE_INVALID")
    require(set(metadata) == METADATA, "METADATA_INCOMPLETE")
    manifest = strict_json(metadata["card-assets.manifest.json"])
    strict_json(metadata["card-assets.preload.json"])
    required = expected_images(manifest)
    require(lock["assetVersion"] == manifest["assetVersion"], "INPUT_VERSION_MISMATCH")
    require(set(required).issubset(files), "CURRENT_VARIANT_MISSING")
    for name, row in files.items():
        if name not in METADATA:
            require(row["role"] == ("current" if name in required else "compatibility"), "IMAGE_ROLE_MISMATCH")
            if name in required:
                require(row["size"] == required[name], "CURRENT_SIZE_MISMATCH")
    return files


def load_lock(file):
    raw = read_regular(file, MAX_LOCK_BYTES)
    lock = strict_json(raw)
    validate_lock(lock)
    return lock, digest(raw)


def pin_archive(archive, release_manifest):
    manifest_bytes = read_regular(release_manifest, MAX_LOCK_BYTES)
    accepted = strict_json(manifest_bytes)
    require(type(accepted.get("schema")) is int and accepted.get("schema") == 3 and isinstance(accepted.get("cardAssetsSha256"), str)
        and HEX.fullmatch(accepted["cardAssetsSha256"]), "ACCEPTED_SCHEMA3_MANIFEST_REQUIRED")
    require(file_digest(Path(archive)) == accepted["cardAssetsSha256"], "ACCEPTED_ARCHIVE_SHA_MISMATCH")
    rows, metadata = [], {}
    for name, raw in archive_records(archive, accepted["cardAssetsSha256"]):
        row = {"path": name, "size": len(raw), "sha256": digest(raw)}
        if name in METADATA:
            metadata[name] = raw
            row.update(role="metadata", base64=base64.b64encode(raw).decode("ascii"))
        rows.append(row)
    require(set(metadata) == METADATA, "METADATA_INCOMPLETE")
    assets = strict_json(metadata["card-assets.manifest.json"])
    required = expected_images(assets)
    for row in rows:
        if row["path"] not in METADATA:
            row["role"] = "current" if row["path"] in required else "compatibility"
    lock = {"schema": 1, "origin": ORIGIN,
        "acceptedRelease": {"commit": accepted.get("commit"), "manifestSha256": digest(manifest_bytes), "archiveSha256": accepted["cardAssetsSha256"]},
        "assetVersion": assets["assetVersion"], "files": sorted(rows, key=lambda row: row["path"])}
    require(lock["assetVersion"] == accepted.get("cardAssetsHash"), "ACCEPTED_ASSET_VERSION_MISMATCH")
    validate_lock(lock)
    return lock


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("ASSET_REDIRECT_REJECTED")


def web_bytes(row, deadline):
    require(time.monotonic() < deadline, "ASSET_DOWNLOAD_DEADLINE")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    request = urllib.request.Request(ORIGIN + safe_path(row["path"]), headers={"Accept-Encoding": "identity"})
    with opener.open(request, timeout=min(15, max(0.1, deadline - time.monotonic()))) as response:
        require(response.status == 200 and response.headers.get("Content-Encoding", "identity") == "identity", "ASSET_HTTP_RESPONSE_INVALID")
        length = response.headers.get("Content-Length")
        require(length is None or length == str(row["size"]), "ASSET_HTTP_SIZE_MISMATCH")
        chunks, size = [], 0
        while size <= row["size"]:
            require(time.monotonic() < deadline, "ASSET_DOWNLOAD_DEADLINE")
            chunk = response.read1(min(65536, row["size"] + 1 - size))
            if not chunk:
                break
            chunks.append(chunk)
            size += len(chunk)
        raw = b"".join(chunks)
    require(time.monotonic() < deadline, "ASSET_DOWNLOAD_DEADLINE")
    verify_bytes(raw, row)
    return raw


def write_asset(root, row, raw):
    verify_bytes(raw, row)
    target = root.joinpath(*row["path"].split("/"))
    target.parent.mkdir(parents=True, exist_ok=True)
    require(target.parent.resolve().is_relative_to(root) and not target.is_symlink(), "OUTPUT_PATH_ESCAPED")
    with target.open("xb") as stream:
        stream.write(raw)


def verify_tree(root, files):
    actual = set()
    for path in root.rglob("*"):
        mode = path.lstat().st_mode
        require(not path.is_symlink() and (stat.S_ISDIR(mode) or stat.S_ISREG(mode)), "OUTPUT_LINK_OR_SPECIAL_REJECTED")
        if path.is_file():
            name = path.relative_to(root).as_posix()
            require(name in files and path.stat().st_size == files[name]["size"]
                and file_digest(path, files[name]["size"]) == files[name]["sha256"], "OUTPUT_BYTES_MISMATCH")
            actual.add(name)
    require(actual == set(files), "OUTPUT_INCOMPLETE")


def materialize(lock, root, archive=None, fetch=None):
    files = validate_lock(lock)
    require(root.is_absolute() and not os.path.lexists(root) and root.parent.is_dir(), "FRESH_OUTPUT_REQUIRED")
    if archive is not None:
        require(file_digest(Path(archive)) == lock["acceptedRelease"]["archiveSha256"], "ACCEPTED_ARCHIVE_SHA_MISMATCH")
    root.mkdir(mode=0o700)
    require(root.resolve() == root and not root.is_symlink(), "FRESH_OUTPUT_IDENTITY_CHANGED")
    owned_identity = (root.stat().st_dev, root.stat().st_ino)
    try:
        if archive is not None:
            for name, raw in archive_records(archive, lock["acceptedRelease"]["archiveSha256"]):
                require(name in files, "ARCHIVE_UNDECLARED_FILE")
                write_asset(root, files[name], raw)
        else:
            deadline, stopped = time.monotonic() + 600, threading.Event()
            for name in METADATA:
                write_asset(root, files[name], base64.b64decode(files[name]["base64"], validate=True))
            def transfer(row):
                require(not stopped.is_set(), "ASSET_DOWNLOAD_CANCELLED")
                raw = fetch(row, deadline) if fetch else web_bytes(row, deadline)
                write_asset(root, row, raw)
            pool = concurrent.futures.ThreadPoolExecutor(max_workers=4)
            futures = [pool.submit(transfer, row) for name, row in files.items() if name not in METADATA]
            try:
                for future in concurrent.futures.as_completed(futures):
                    future.result()
            finally:
                stopped.set()
                pool.shutdown(wait=True, cancel_futures=True)
        verify_tree(root, files)
    except BaseException:
        # Only this fresh private generated tree, never a caller's existing root.
        require(root.resolve() == root and not root.is_symlink()
            and (root.stat().st_dev, root.stat().st_ino) == owned_identity, "OWNED_OUTPUT_CHANGED_NO_CLEANUP")
        shutil.rmtree(root)
        raise
    return {"schema": 1, "assetVersion": lock["assetVersion"], "files": len(files),
        "currentImages": sum(row["role"] == "current" for row in files.values()),
        "compatibilityImages": sum(row["role"] == "compatibility" for row in files.values()),
        "bytes": sum(row["size"] for row in files.values()), "allFileHashesVerified": True,
        "releasePackageCreated": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    pin = commands.add_parser("pin")
    pin.add_argument("--archive", required=True)
    pin.add_argument("--accepted-manifest", required=True)
    pin.add_argument("--output-lock", required=True)
    check = commands.add_parser("check")
    check.add_argument("--lock", required=True)
    prepare = commands.add_parser("prepare")
    prepare.add_argument("--lock", required=True)
    prepare.add_argument("--output-root", required=True)
    sources = prepare.add_mutually_exclusive_group(required=True)
    sources.add_argument("--archive")
    sources.add_argument("--from-web", action="store_true")
    options = parser.parse_args()
    if options.command == "pin":
        output = generated_path(options.output_lock)
        require(output.parent.is_dir() and not os.path.lexists(output), "NEW_LOCK_PATH_REQUIRED")
        lock = pin_archive(options.archive, options.accepted_manifest)
        raw = (json.dumps(lock, ensure_ascii=False, indent=2) + "\n").encode()
        require(len(raw) <= MAX_LOCK_BYTES, "LOCK_BYTES_EXCEEDED")
        with output.open("xb") as stream:
            stream.write(raw)
        result = {"schema": 1, "lockSha256": digest(raw), "files": len(lock["files"]), "assetVersion": lock["assetVersion"]}
    else:
        lock, sha = load_lock(options.lock)
        if options.command == "check":
            result = {"schema": 1, "lockSha256": sha, "files": len(lock["files"]), "valid": True}
        else:
            result = materialize(lock, generated_path(options.output_root), options.archive)
            result["lockSha256"] = sha
    print(json.dumps(result, separators=(",", ":")))


if __name__ == "__main__":
    main()
