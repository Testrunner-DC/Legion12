"""Pure synthetic trusted-input boundaries; no downloads or live directories."""
import argparse
import base64
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tarfile
import tempfile
import threading
import time
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("assets", Path(__file__).with_name("l12-card-assets-input.py"))
assets = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assets)
WORK_ROOT = None


def fixture():
    cards, payloads, identities, total = {}, {}, [], 0
    for index in range(366):
        card_id = f"S01-{index:04d}"
        content = assets.digest(card_id.encode())
        variants, sizes = {}, {}
        for variant, name in assets.VARIANTS.items():
            relative = f"cards/unit/{card_id}/{content[:20]}/{name}"
            raw = f"synthetic:{card_id}:{name}".encode()
            variants[variant], sizes[variant], payloads[relative] = relative, len(raw), raw
            total += len(raw)
        card = {"cardId": card_id, "contentHash": content, "variants": variants, "bytes": sizes}
        if index >= 324:
            card.update(presentationOnly=True, baseCardId="S01-0000")
        cards[card_id] = card
        identities.append(":".join([card_id, content, "presentation" if index >= 324 else "playable", card.get("baseCardId", "")]))
    version = assets.digest("\n".join(sorted(identities)).encode())
    manifest = {"schemaVersion": 3, "complete": True, "cardCount": 366, "playableCardCount": 324,
        "presentationCardCount": 42, "cards": cards, "totalBytes": total, "assetVersion": version, "catalogVersion": "unit"}
    payloads["card-assets.manifest.json"] = json.dumps(manifest).encode()
    payloads["card-assets.preload.json"] = b'{"synthetic":true}'
    for name in assets.NAMES:
        payloads[f"cards/unit/S01-0000/{'f' * 20}/{name}"] = b"old-compatibility-bytes"
    rows = []
    current = assets.expected_images(manifest)
    for name, raw in sorted(payloads.items()):
        row = {"path": name, "size": len(raw), "sha256": assets.digest(raw),
               "role": "metadata" if name in assets.METADATA else "current" if name in current else "compatibility"}
        if name in assets.METADATA:
            row["base64"] = base64.b64encode(raw).decode()
        rows.append(row)
    lock = {"schema": 1, "origin": assets.ORIGIN, "assetVersion": version,
        "acceptedRelease": {"commit": "a" * 40, "manifestSha256": "b" * 64, "archiveSha256": "c" * 64}, "files": rows}
    return lock, payloads


LOCK, PAYLOADS = fixture()


class TrustedInputTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="synthetic-", dir=WORK_ROOT)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def archive(self, members, archive_format=tarfile.PAX_FORMAT):
        file = self.root / "input.tar.gz"
        with tarfile.open(file, "w:gz", format=archive_format) as out:
            for info, raw in members:
                if isinstance(info, str):
                    info = tarfile.TarInfo(info)
                    info.size = len(raw)
                out.addfile(info, io.BytesIO(raw) if raw else None)
        return file

    def test_full_lock_and_compatibility(self):
        rows = assets.validate_lock(LOCK)
        self.assertEqual(len(rows), 1837)
        self.assertEqual(sum(row["role"] == "compatibility" for row in rows.values()), 5)

    def test_actual_category_counts_not_just_header(self):
        manifest = json.loads(PAYLOADS["card-assets.manifest.json"])
        card = manifest["cards"]["S01-0365"]
        card["presentationOnly"] = False
        card.pop("baseCardId")
        identities = [":".join([key, entry["contentHash"], "presentation" if entry.get("presentationOnly") else "playable", entry.get("baseCardId", "")]) for key, entry in manifest["cards"].items()]
        manifest["assetVersion"] = assets.digest("\n".join(sorted(identities)).encode())
        with self.assertRaisesRegex(ValueError, "ACTUAL_CARD_CATEGORY_COUNTS_MISMATCH"):
            assets.expected_images(manifest)

    def test_presentation_flags_and_bases(self):
        for flag, base in [(1, "S01-0000"), (True, "S01-0365"), (True, "missing")]:
            with self.subTest(flag=flag, base=base):
                manifest = json.loads(PAYLOADS["card-assets.manifest.json"])
                manifest["cards"]["S01-0365"].update(presentationOnly=flag, baseCardId=base)
                with self.assertRaises(ValueError):
                    assets.expected_images(manifest)

    def test_bool_lock_schema_rejected(self):
        lock = copy.deepcopy(LOCK)
        lock["schema"] = True
        with self.assertRaisesRegex(ValueError, "INPUT_LOCK_SCHEMA_INVALID"):
            assets.validate_lock(lock)

    def test_same_size_corruption_rejected(self):
        row = next(row for row in LOCK["files"] if row["role"] == "current")
        raw = PAYLOADS[row["path"]]
        self.assertEqual(len(raw), len(b"x" * len(raw)))
        with self.assertRaisesRegex(ValueError, "ASSET_BYTES_MISMATCH"):
            assets.verify_bytes(b"x" * len(raw), row)

    def test_unsafe_paths(self):
        for path in ["../x", "/cards/x", "C:/x", "cards\\unit\\x", "cards/../x", "card-assets.manifest.json?x", "cards/%2e%2e/x"]:
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, "ASSET_PATH_INVALID"):
                assets.safe_path(path)
        assets.safe_path("cards/unit/ST-DS01/" + "a" * 20 + "/original.webp")

    def test_duplicate_rows_and_unknown_fields(self):
        for mutate in [lambda lock: lock["files"].append(copy.deepcopy(lock["files"][-1])),
                       lambda lock: lock.update(skipHashes=True)]:
            lock = copy.deepcopy(LOCK)
            mutate(lock)
            with self.assertRaises(ValueError):
                assets.validate_lock(lock)

    def test_metadata_must_be_pinned(self):
        lock = copy.deepcopy(LOCK)
        row = next(row for row in lock["files"] if row["path"] == "card-assets.preload.json")
        row["base64"] = base64.b64encode(b"z" * row["size"]).decode()
        with self.assertRaisesRegex(ValueError, "ASSET_BYTES_MISMATCH"):
            assets.validate_lock(lock)

    def test_full_current_set_required(self):
        lock = copy.deepcopy(LOCK)
        lock["files"] = [row for row in lock["files"] if row["role"] != "current"]
        with self.assertRaisesRegex(ValueError, "CURRENT_VARIANT_MISSING"):
            assets.validate_lock(lock)

    def test_all_five_variants_required(self):
        manifest = json.loads(PAYLOADS["card-assets.manifest.json"])
        manifest["cards"]["S01-0000"]["variants"].pop("boardWebp")
        with self.assertRaisesRegex(ValueError, "FULL_VARIANTS_REQUIRED"):
            assets.expected_images(manifest)

    def test_variant_names_cannot_be_swapped(self):
        manifest = json.loads(PAYLOADS["card-assets.manifest.json"])
        variants = manifest["cards"]["S01-0000"]["variants"]
        variants["boardWebp"], variants["thumbWebp"] = variants["thumbWebp"], variants["boardWebp"]
        with self.assertRaisesRegex(ValueError, "VARIANT_NAME_MISMATCH"):
            assets.expected_images(manifest)

    def test_duplicate_json_keys_rejected(self):
        with self.assertRaisesRegex(ValueError, "DUPLICATE_JSON_KEY"):
            assets.strict_json('{"schema":1,"schema":2}')

    def test_origin_and_acceptance_identity(self):
        for key, value in [("origin", "https://example.com/"), ("assetVersion", "0" * 64)]:
            lock = copy.deepcopy(LOCK)
            lock[key] = value
            with self.assertRaises(ValueError):
                assets.validate_lock(lock)
        lock = copy.deepcopy(LOCK)
        lock["acceptedRelease"]["commit"] = None
        with self.assertRaisesRegex(ValueError, "ACCEPTANCE_IDENTITY_INVALID"):
            assets.validate_lock(lock)

    def test_lock_read_limit(self):
        file = self.root / "too-big.json"
        with file.open("wb") as stream:
            stream.write(b" " * (assets.MAX_LOCK_BYTES + 1))
        with self.assertRaisesRegex(ValueError, "REGULAR_INPUT_BUDGET_REJECTED"):
            assets.load_lock(file)

    def test_archive_and_manifest_are_bounded_before_read(self):
        file = self.root / "oversize"
        file.write_bytes(b"12345678")
        with self.assertRaisesRegex(ValueError, "REGULAR_INPUT_BUDGET_REJECTED"):
            assets.file_digest(file, maximum=4)
        with mock.patch.object(assets, "MAX_LOCK_BYTES", 4):
            with self.assertRaisesRegex(ValueError, "REGULAR_INPUT_BUDGET_REJECTED"):
                assets.pin_archive(self.root / "must-not-be-opened", file)
        with mock.patch.object(assets, "open_regular", return_value=io.BytesIO(b"12345678")):
            with self.assertRaisesRegex(ValueError, "INPUT_READ_BUDGET_EXCEEDED"):
                assets.file_digest(file, maximum=4)

    def test_non_regular_source_rejected_without_blocking_open(self):
        file = self.root / "fifo"
        if os.name == "posix":
            os.mkfifo(file)
            with self.assertRaisesRegex(ValueError, "REGULAR_INPUT_BUDGET_REJECTED"):
                assets.file_digest(file)
        else:
            fake = mock.Mock(st_mode=stat.S_IFIFO, st_size=0)
            with mock.patch.object(Path, "lstat", return_value=fake), mock.patch.object(assets.os, "open") as opened:
                with self.assertRaisesRegex(ValueError, "REGULAR_INPUT_BUDGET_REJECTED"):
                    assets.file_digest(file)
                opened.assert_not_called()

    def test_archive_duplicate_links_special_and_escape(self):
        for kind in ("duplicate", "symbolic", "hard", "special", "escape", "case"):
            with self.subTest(kind=kind):
                path = "cards/unit/S01-0000/" + "a" * 20 + "/original.webp"
                info = tarfile.TarInfo(path)
                members = [(path, b"one")]
                if kind in ("symbolic", "hard", "special"):
                    info.type = {"symbolic": tarfile.SYMTYPE, "hard": tarfile.LNKTYPE, "special": tarfile.FIFOTYPE}[kind]
                    info.linkname = "../../outside"
                    members = [(info, b"")]
                elif kind == "duplicate":
                    members.append((path, b"two"))
                elif kind == "case":
                    members.append((path.replace("/unit/", "/UNIT/"), b"two"))
                else:
                    members = [("../outside", b"one")]
                with self.assertRaises(ValueError):
                    list(assets.archive_records(self.archive(members)))

    def test_archive_size_budget(self):
        archive = self.archive([("card-assets.preload.json", b"12345")])
        with mock.patch.object(assets, "MAX_FILE_BYTES", 4):
            with self.assertRaisesRegex(ValueError, "ARCHIVE_BUDGET_EXCEEDED"):
                list(assets.archive_records(archive))

    def test_actual_consumed_archive_bytes_are_bound(self):
        archive = self.archive([("card-assets.preload.json", b"{}")])
        with self.assertRaisesRegex(ValueError, "ACCEPTED_ARCHIVE_SHA_MISMATCH"):
            list(assets.archive_records(archive, "0" * 64))
        self.assertEqual(len(list(assets.archive_records(archive, assets.file_digest(archive)))), 1)

    def test_hidden_pax_metadata_is_bounded_before_visible_member(self):
        for archive_format in (tarfile.PAX_FORMAT, tarfile.GNU_FORMAT):
            with self.subTest(format=archive_format):
                info = tarfile.TarInfo("card-assets.preload.json" if archive_format == tarfile.PAX_FORMAT else "x" * 16384)
                info.size = 2
                if archive_format == tarfile.PAX_FORMAT:
                    info.pax_headers = {"comment": "x" * 16384}
                archive = self.archive([(info, b"{}")], archive_format)
                with mock.patch.object(assets, "MAX_TAR_BYTES", 2048):
                    with self.assertRaisesRegex(ValueError, "ARCHIVE_DECOMPRESSED_BUDGET_EXCEEDED"):
                        list(assets.archive_records(archive))

    def test_actual_archive_roundtrip(self):
        archive = self.archive(list(PAYLOADS.items()))
        manifest = {"schema": 3, "commit": "a" * 40, "cardAssetsSha256": assets.file_digest(archive), "cardAssetsHash": LOCK["assetVersion"]}
        accepted = self.root / "accepted.json"
        accepted.write_text(json.dumps(manifest), encoding="utf-8")
        lock = assets.pin_archive(archive, accepted)
        result = assets.materialize(lock, self.root / "prepared", archive=archive)
        self.assertTrue(result["allFileHashesVerified"])
        self.assertEqual(result["compatibilityImages"], 5)
        self.assertFalse(result["releasePackageCreated"])
        self.assertEqual(len(list((self.root / "prepared").rglob("*.webp"))), 1468)
        self.assertEqual(assets.file_digest(accepted), lock["acceptedRelease"]["manifestSha256"])

    def test_web_mode_pins_metadata_without_fetching_mutable_urls(self):
        calls = []
        def fetch(row, deadline):
            self.assertNotIn(row["path"], assets.METADATA)
            self.assertGreater(deadline, time.monotonic())
            calls.append(row["path"])
            return PAYLOADS[row["path"]]
        result = assets.materialize(LOCK, self.root / "prepared", fetch=fetch)
        self.assertEqual(len(calls), 1835)
        self.assertEqual(result["files"], 1837)

    def test_bad_download_cleans_only_new_root(self):
        outside = self.root / "preserved.txt"
        outside.write_bytes(b"preserved")
        target = self.root / "failed"
        with self.assertRaisesRegex(ValueError, "ASSET_BYTES_MISMATCH"):
            assets.materialize(LOCK, target, fetch=lambda row, deadline: b"x" * row["size"])
        self.assertFalse(target.exists())
        self.assertEqual(outside.read_bytes(), b"preserved")

    def test_existing_output_never_overwritten(self):
        with self.assertRaisesRegex(ValueError, "FRESH_OUTPUT_REQUIRED"):
            assets.materialize(LOCK, self.root, fetch=lambda *_: self.fail("must not fetch"))
        self.assertTrue(self.root.is_dir())

    def test_replaced_output_is_not_removed_during_failure_cleanup(self):
        target, displaced = self.root / "prepared", self.root / "displaced-owned-tree"
        lock = threading.Lock()
        def replaced(row, deadline):
            with lock:
                if not displaced.exists():
                    target.rename(displaced)
                    target.mkdir()
                    (target / "not-owned.txt").write_bytes(b"preserve")
            return b"x" * row["size"]
        with self.assertRaisesRegex(ValueError, "OWNED_OUTPUT_CHANGED_NO_CLEANUP"):
            assets.materialize(LOCK, target, fetch=replaced)
        self.assertEqual((target / "not-owned.txt").read_bytes(), b"preserve")
        self.assertTrue(displaced.is_dir())

    def test_wrong_archive_rejected_before_output(self):
        archive = self.archive([("card-assets.preload.json", b"unaccepted")])
        output = self.root / "prepared"
        with self.assertRaisesRegex(ValueError, "ACCEPTED_ARCHIVE_SHA_MISMATCH"):
            assets.materialize(LOCK, output, archive=archive)
        self.assertFalse(output.exists())

    def test_post_write_corruption_rejected(self):
        root = self.root / "tree"
        root.mkdir()
        row = LOCK["files"][0]
        assets.write_asset(root, row, PAYLOADS[row["path"]])
        root.joinpath(row["path"]).write_bytes(b"x" * row["size"])
        with self.assertRaisesRegex(ValueError, "OUTPUT_BYTES_MISMATCH"):
            assets.verify_tree(root, assets.validate_lock(LOCK))

    def test_redirects_and_expired_deadlines_rejected(self):
        with self.assertRaisesRegex(ValueError, "ASSET_REDIRECT_REJECTED"):
            assets.NoRedirect().redirect_request(None, None, 302, "", {}, "http://other/")
        with self.assertRaisesRegex(ValueError, "ASSET_DOWNLOAD_DEADLINE"):
            assets.web_bytes(LOCK["files"][-1], time.monotonic() - 1)

    def test_http_status_encoding_length_and_bytes(self):
        row = next(row for row in LOCK["files"] if row["role"] == "current")
        raw = PAYLOADS[row["path"]]
        for status, headers, body, passes in [(200, {"Content-Length": str(len(raw))}, raw, True),
            (304, {}, b"", False), (200, {"Content-Encoding": "gzip"}, raw, False),
            (200, {"Content-Length": str(len(raw) + 1)}, raw, False),
            (200, {}, b"x" * len(raw), False), (200, {}, raw + b"x", False)]:
            with self.subTest(status=status, headers=headers, passes=passes):
                response = io.BytesIO(body)
                response.status, response.headers = status, headers
                opener = mock.Mock()
                opener.open.return_value = response
                with mock.patch.object(assets.urllib.request, "build_opener", return_value=opener):
                    if passes:
                        self.assertEqual(assets.web_bytes(row, time.monotonic() + 15), raw)
                    else:
                        with self.assertRaises(ValueError):
                            assets.web_bytes(row, time.monotonic() + 15)

    def test_real_shared_storage_guard(self):
        self.assertEqual(assets.generated_path(self.root), self.root.resolve())
        if os.name == "nt":
            with self.assertRaisesRegex(ValueError, "GENERATED_STORAGE_GUARD_REJECTED"):
                assets.generated_path(Path("C:/l12-card-input-must-not-be-created"))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--work-root", required=True)
    options = parser.parse_args()
    WORK_ROOT = assets.generated_path(options.work_root)
    WORK_ROOT.mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[__file__], verbosity=2)
