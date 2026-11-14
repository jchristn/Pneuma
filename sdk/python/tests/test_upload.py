#!/usr/bin/env python3
"""Offline tests for the file upload helpers.

A loopback HTTP server records what the SDK sends, so these run without a Pneuma server:
  - upload_file sends the raw bytes with the file name, labels, and tags in the query string
  - the content type is guessed from the file name, and a path upload is named after the file
  - an error status raises PneumaError with the parsed body
  - download_link_source returns the bytes, type, and file name, and can write them to disk

Runnable directly (``python tests/test_upload.py``) or with pytest.
"""

import json
import os
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from pneuma_sdk import PneumaClient, PneumaError  # noqa: E402

RECORDED = []


class _Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):  # silence the default stderr logging
        pass

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length)
        url = urlparse(self.path)
        RECORDED.append({
            "path": url.path,
            "query": parse_qs(url.query, keep_blank_values=True),
            "contentType": self.headers.get("Content-Type"),
            "auth": self.headers.get("Authorization"),
            "body": body,
        })
        if "/sub_missing/" in url.path:
            payload = json.dumps({"error": "NotFound", "message": "Subject not found."}).encode()
            self.send_response(404)
        else:
            name = parse_qs(url.query).get("filename", [""])[0]
            payload = json.dumps({"index": 0, "statusCode": 201, "replaced": False, "jobId": "job_1", "link": {"id": "lnk_1", "fileName": name}}).encode()
            self.send_response(201)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/v1.0/links/lnk_1/source":
            payload = b"%PDF-1.4 bytes"
            self.send_response(200)
            self.send_header("Content-Type", "application/pdf")
            self.send_header("Content-Disposition", "attachment; filename=\"Q3 r_sum_.pdf\"; filename*=UTF-8''Q3%20r%C3%A9sum%C3%A9.pdf")
        else:
            payload = json.dumps({"maxUploadBytes": 1024, "maxFileNameLength": 256, "supportedTypes": []}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


def _server():
    server = HTTPServer(("127.0.0.1", 0), _Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server


def test_upload_and_download():
    RECORDED.clear()
    server = _server()
    try:
        client = PneumaClient(f"http://127.0.0.1:{server.server_port}", token="tok")

        result = client.upload_file("sub_1", b"# Notes", filename="Release notes.md", labels=["one", "two,three"], tags={"team": "finance"}, external_key="notes")
        assert result["link"]["fileName"] == "Release notes.md"
        sent = RECORDED[-1]
        assert sent["path"] == "/v1.0/subjects/sub_1/files"
        assert sent["query"]["filename"] == ["Release notes.md"]
        assert sent["query"]["labels"] == ["one", "two,three"]
        assert sent["query"]["tag.team"] == ["finance"]
        assert sent["query"]["externalKey"] == ["notes"]
        assert sent["contentType"] == "text/markdown"
        assert sent["auth"] == "Bearer tok"
        assert sent["body"] == b"# Notes"

        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "deck.pdf")
            with open(path, "wb") as handle:
                handle.write(b"%PDF-1.4 deck")
            client.upload_file("sub_1", path)
            sent = RECORDED[-1]
            assert sent["query"]["filename"] == ["deck.pdf"]
            assert sent["contentType"] == "application/pdf"
            assert sent["body"] == b"%PDF-1.4 deck"

            with open(path, "rb") as handle:
                client.upload_file("sub_1", handle, content_type="application/octet-stream")
            assert RECORDED[-1]["query"]["filename"] == ["deck.pdf"]
            assert RECORDED[-1]["contentType"] == "application/octet-stream"

            downloaded = client.download_link_source("lnk_1", dest=folder)
            assert downloaded["data"] == b"%PDF-1.4 bytes"
            assert downloaded["contentType"] == "application/pdf"
            assert downloaded["fileName"] == "Q3 résumé.pdf"
            with open(downloaded["path"], "rb") as handle:
                assert handle.read() == b"%PDF-1.4 bytes"

        try:
            client.upload_file("sub_missing", b"x", filename="a.txt")
            raise AssertionError("an error status should raise")
        except PneumaError as err:
            assert err.status == 404
            assert err.body["message"] == "Subject not found."

        try:
            client.upload_file("sub_1", b"x")
            raise AssertionError("raw bytes without a file name should raise")
        except ValueError:
            pass

        assert client.get_upload_settings()["maxUploadBytes"] == 1024
    finally:
        server.shutdown()


if __name__ == "__main__":
    test_upload_and_download()
    print("  [PASS] upload and download")
