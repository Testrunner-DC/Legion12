"""Synthetic read-only boundaries; never claims a real canonical build succeeded."""
import argparse
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest

spec = importlib.util.spec_from_file_location("canonical", Path(__file__).with_name("l12-canonical-artifacts.py"))
owner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(owner)
assets = owner.assets
LOCK, _ = assets.load_lock(Path(__file__).with_name("l12-card-assets-input.lock.json"))
COMMIT = "a" * 40


class Boundaries(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(dir=WORK)
        self.root = Path(self.directory.name).resolve()
        folder = self.root / COMMIT
        folder.mkdir()
        self.manifest = folder / ("l12-release-" + COMMIT + ".json")
        self.release = folder / ("l12-release-" + COMMIT + ".tar.gz")
        self.cards = self.root / ("l12-card-assets-" + LOCK["assetVersion"] + ".tar.gz")
        self.release.write_bytes(b"synthetic-runtime-not-a-package")
        self.cards.write_bytes(b"synthetic-public-images-not-a-package")
        self.document = dict(schema=3, commit=COMMIT, deploymentDrainProtocol=1,
            requiresExternalStopPermit=True, runtimeDataCompatibility="l12-runtime-v1",
            runtimeSourceCompatibility="c" * 64, releaseArchive=str(self.release),
            releaseSha256=assets.file_digest(self.release), cardAssetsHash=LOCK["assetVersion"],
            cardAssetsArchive=str(self.cards), cardAssetsSha256=assets.file_digest(self.cards),
            playerReleaseNotesSha256="d" * 64, releaseBaseCommit="", generatedAt="2026-10-24T05:00:00.1234567+00:00")
        self.save()

    def tearDown(self):
        self.directory.cleanup()

    def save(self):
        self.manifest.write_text(json.dumps(self.document), encoding="utf-8")

    def check(self):
        return owner.qualify(self.root, COMMIT, LOCK)

    def test_exact_three_read_only(self):
        paths = [self.manifest, self.release, self.cards]
        before = [(p.stat().st_mtime_ns, p.read_bytes()) for p in paths]
        self.assertEqual(self.check(), dict(manifest=str(self.manifest), releaseArchive=str(self.release), cardAssetsArchive=str(self.cards)))
        self.assertEqual(before, [(p.stat().st_mtime_ns, p.read_bytes()) for p in paths])

    def test_bom_manifest_from_windows(self):
        self.manifest.write_bytes(b"\xef\xbb\xbf" + self.manifest.read_bytes())
        self.check()

    def test_schema_not_boolean(self):
        for value in [True, "3", 2, 3.0]:
            with self.subTest(value=value):
                self.document["schema"] = value
                self.save()
                with self.assertRaisesRegex(ValueError, "MANIFEST_IDENTITY"): self.check()

    def test_wrong_commit(self):
        self.document["commit"] = "b" * 40
        self.save()
        with self.assertRaisesRegex(ValueError, "MANIFEST_IDENTITY"): self.check()

    def test_runtime_capability(self):
        for key, value in [("deploymentDrainProtocol", True), ("requiresExternalStopPermit", "true"),
            ("runtimeDataCompatibility", "old"), ("runtimeSourceCompatibility", "bad")]:
            original = copy.deepcopy(self.document)
            self.document[key] = value
            self.save()
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "RUNTIME_CAPABILITY"): self.check()
            self.document = original

    def test_asset_version(self):
        self.document["cardAssetsHash"] = "0" * 64
        self.save()
        with self.assertRaisesRegex(ValueError, "ASSET_VERSION"): self.check()

    def test_notes_hash(self):
        self.document["playerReleaseNotesSha256"] = None
        self.save()
        with self.assertRaisesRegex(ValueError, "PLAYER_NOTES_HASH"): self.check()

    def test_arbitrary_output_path(self):
        self.document["releaseArchive"] = str(self.root / "private-database.db")
        self.save()
        with self.assertRaisesRegex(ValueError, "OUTPUT_PATH"): self.check()

    def test_lexical_traversal(self):
        self.document["cardAssetsArchive"] = str(self.root / "dir" / ".." / self.cards.name)
        self.save()
        with self.assertRaisesRegex(ValueError, "OUTPUT_PATH"): self.check()

    def test_runtime_hash_mismatch(self):
        self.release.write_bytes(b"corrupt")
        with self.assertRaisesRegex(ValueError, "ARCHIVE_BYTES"): self.check()
        self.assertEqual(self.release.read_bytes(), b"corrupt")

    def test_cards_hash_mismatch(self):
        self.cards.write_bytes(b"corrupt")
        with self.assertRaisesRegex(ValueError, "ARCHIVE_BYTES"): self.check()

    def test_missing_archive(self):
        self.release.unlink()
        with self.assertRaises(FileNotFoundError): self.check()
        self.assertFalse(self.release.exists())

    def test_runtime_budget(self):
        with self.release.open("wb") as stream: stream.truncate(150 * 1024**2 + 1)
        with self.assertRaisesRegex(ValueError, "BUDGET_REJECTED"): self.check()

    def test_cards_budget(self):
        with self.cards.open("wb") as stream: stream.truncate(assets.MAX_TOTAL_BYTES + 1)
        with self.assertRaisesRegex(ValueError, "BUDGET_REJECTED"): self.check()

    def test_duplicate_json(self):
        self.manifest.write_text('{"schema":3,"schema":3}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "DUPLICATE_JSON_KEY"): self.check()

    def test_manifest_budget(self):
        self.manifest.write_bytes(b" " * 65537)
        with self.assertRaisesRegex(ValueError, "BUDGET_REJECTED"): self.check()

    def test_hard_link(self):
        old = self.root / "original"
        self.release.rename(old)
        os.link(old, self.release)
        with self.assertRaisesRegex(ValueError, "FILE_NOT_PLAIN"): self.check()

    def test_directory_is_not_archive(self):
        self.release.unlink()
        self.release.mkdir()
        with self.assertRaisesRegex(ValueError, "FILE_NOT_PLAIN"): self.check()

    def test_invalid_commit_cannot_select_path(self):
        with self.assertRaisesRegex(ValueError, "COMMIT_INVALID"): owner.qualify(self.root, "../private", LOCK)

    def test_relative_root(self):
        with self.assertRaisesRegex(ValueError, "ROOT_INVALID"): owner.qualify(Path("relative"), COMMIT, LOCK)

    def test_unknown_manifest_field(self):
        self.document["runtimeCredentials"] = "synthetic-must-not-export"
        self.save()
        with self.assertRaisesRegex(ValueError, "MANIFEST_FIELDS"): self.check()

    def test_invalid_release_base(self):
        self.document["releaseBaseCommit"] = "../private"
        self.save()
        with self.assertRaisesRegex(ValueError, "NOTES_BASE"): self.check()

    def test_missing_utc(self):
        self.document["generatedAt"] = "2026-10-24T05:00:00"
        self.save()
        with self.assertRaisesRegex(ValueError, "GENERATED_AT"): self.check()

    def link_directory(self, link, target):
        if os.name != "nt": os.symlink(target, link, target_is_directory=True)
        else:
            env = dict(os.environ, L12_SYNTHETIC_LINK=str(link), L12_SYNTHETIC_TARGET=str(target))
            result = subprocess.run([env["L12_PWSH"], "-NoProfile", "-NonInteractive", "-Command",
                "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:L12_SYNTHETIC_LINK -Target $env:L12_SYNTHETIC_TARGET | Out-Null"],
                env=env, capture_output=True, timeout=15000)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_root_directory_link(self):
        link = self.root / "root-alias"
        self.link_directory(link, self.root)
        try:
            with self.assertRaisesRegex(ValueError, "ROOT_INVALID"): owner.qualify(link, COMMIT, LOCK)
        finally:
            if os.name == "nt": os.rmdir(link)
            else: link.unlink()

    def test_commit_directory_link(self):
        original = self.root / COMMIT
        target = self.root / "held-original"
        original.rename(target)
        self.link_directory(original, target)
        try:
            with self.assertRaisesRegex(ValueError, "ALIAS_REJECTED"): self.check()
        finally:
            if os.name == "nt": os.rmdir(original)
            else: original.unlink()

    def cli(self, root):
        return subprocess.run([sys.executable, "-I", "-B", str(Path(owner.__file__)),
            "--output-root", str(root), "--commit", COMMIT, "--lock", str(Path(__file__).with_name("l12-card-assets-input.lock.json"))],
            capture_output=True, text=True, encoding="utf-8", timeout=30000)

    def test_real_cli_output(self):
        result = self.cli(self.root)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), self.check())

    def test_real_cli_root_link_rejected(self):
        link = self.root / "cli-root-alias"
        self.link_directory(link, self.root)
        try:
            result = self.cli(link)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("PREPARED_ROOT_NOT_PLAIN", result.stderr)
        finally:
            if os.name == "nt": os.rmdir(link)
            else: link.unlink()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--work-root", required=True)
    args = parser.parse_args()
    WORK = assets.generated_path(args.work_root)
    WORK.mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[__file__])
