#!/usr/bin/env python3
"""Pneuma Python SDK test harness.

Runs a small end-to-end smoke test against a live Pneuma server:
  - health check
  - login as admin@pneuma / password
  - list tenants, roles, subjects
  - create a subject
  - submit a link for that subject
  - list ingestion jobs

Behavior:
  - Prints PASS/FAIL for each step.
  - Exits non-zero if any step fails.
  - Exits 0 (SKIP) if the server is unreachable.

Runnable directly::

    python tests/test_harness.py [base_url]

Also collectable by pytest (``test_harness`` skips gracefully when the server
is unreachable).
"""

import os
import sys
import uuid

# Make the SDK importable when run directly from the tests/ directory or repo root.
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import requests  # noqa: E402

from pneuma_sdk import PneumaClient, PneumaError  # noqa: E402

BASE_URL = os.environ.get("PNEUMA_BASE_URL", "http://127.0.0.1:8080")
ADMIN_EMAIL = os.environ.get("PNEUMA_ADMIN_EMAIL", "admin@pneuma")
ADMIN_PASSWORD = os.environ.get("PNEUMA_ADMIN_PASSWORD", "password")


class _ServerUnreachable(Exception):
    """Raised when the Pneuma server cannot be contacted at all."""


def _pass(name):
    print(f"  [PASS] {name}")


def _fail(name, error):
    print(f"  [FAIL] {name}")
    print(f"         {error}")


def _run(base_url):
    """Run the smoke test. Returns (passed, failed) counts.

    Raises _ServerUnreachable if the server cannot be contacted.
    """
    passed = 0
    failed = 0
    client = PneumaClient(base_url)

    # Health first -- this doubles as the reachability probe.
    try:
        health = client.health()
    except (requests.ConnectionError, requests.Timeout) as exc:
        raise _ServerUnreachable(str(exc))

    try:
        assert health is not None, "health returned no payload"
        _pass("health")
        passed += 1
    except Exception as exc:  # noqa: BLE001
        _fail("health", exc)
        failed += 1

    # Login.
    try:
        result = client.login(ADMIN_EMAIL, ADMIN_PASSWORD)
        assert client.token, "no token stored after login"
        assert isinstance(result, dict) and result.get("token"), "login payload missing token"
        _pass("login")
        passed += 1
    except (requests.ConnectionError, requests.Timeout) as exc:
        raise _ServerUnreachable(str(exc))
    except Exception as exc:  # noqa: BLE001
        _fail("login", exc)
        failed += 1
        # Without a token the rest cannot run meaningfully.
        client.close()
        return passed, failed

    # Read-only listings.
    for name, fn in (
        ("list_tenants", client.list_tenants),
        ("list_roles", client.list_roles),
        ("list_subjects", client.list_subjects),
    ):
        try:
            fn()
            _pass(name)
            passed += 1
        except Exception as exc:  # noqa: BLE001
            _fail(name, exc)
            failed += 1

    # Create a subject.
    created_id = None
    try:
        subject = client.create_subject(
            {
                "displayName": f"SDK Harness Subject {uuid.uuid4().hex[:8]}",
                "type": "Person",
                "description": "Created by the Pneuma Python SDK test harness.",
            }
        )
        created_id = _extract_id(subject)
        assert created_id, f"could not determine subject id from response: {subject!r}"
        _pass("create_subject")
        passed += 1
    except Exception as exc:  # noqa: BLE001
        _fail("create_subject", exc)
        failed += 1

    # Submit a link (only if we have a subject).
    if created_id:
        try:
            client.submit_link(
                created_id,
                url="https://example.com/sdk-harness-sample",
                title="SDK Harness Sample",
            )
            _pass("submit_link")
            passed += 1
        except Exception as exc:  # noqa: BLE001
            _fail("submit_link", exc)
            failed += 1
    else:
        _fail("submit_link", "skipped: no subject id")
        failed += 1

    # List jobs.
    try:
        client.list_jobs()
        _pass("list_jobs")
        passed += 1
    except Exception as exc:  # noqa: BLE001
        _fail("list_jobs", exc)
        failed += 1

    client.close()
    return passed, failed


def _extract_id(obj):
    """Best-effort extraction of an entity id from an API response."""
    if isinstance(obj, dict):
        for key in ("id", "guid", "subjectId", "identifier"):
            if obj.get(key):
                return obj[key]
    return None


def main(argv=None):
    argv = argv if argv is not None else sys.argv[1:]
    base_url = argv[0] if argv else BASE_URL

    print("=" * 60)
    print("  Pneuma SDK Test Harness")
    print(f"  Base URL: {base_url}")
    print("=" * 60)

    try:
        passed, failed = _run(base_url)
    except _ServerUnreachable as exc:
        print(f"  [SKIP] Server unreachable at {base_url}: {exc}")
        print("  Skipping harness (exit 0).")
        return 0

    print("-" * 60)
    print(f"  Passed: {passed}  Failed: {failed}")
    print(f"  Result: {'SUCCESS' if failed == 0 else 'FAILURE'}")
    return 0 if failed == 0 else 1


def test_harness():
    """Pytest entry point. Skips when the server is unreachable, fails on any error."""
    try:
        passed, failed = _run(BASE_URL)
    except _ServerUnreachable as exc:
        try:
            import pytest

            pytest.skip(f"Pneuma server unreachable at {BASE_URL}: {exc}")
        except ImportError:
            return
    assert failed == 0, f"{failed} harness step(s) failed"


if __name__ == "__main__":
    sys.exit(main())
