"""Real HTTP/CLI checks against loopback fixtures only."""
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import unittest

from test_monitor import ROOT, m, snapshot


class HttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.received = []
        self.reads = 0
        self.body = snapshot(time.time())
        fixture = self
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                fixture.reads += 1
                body = json.dumps(fixture.body).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                fixture.received.append((self.path, body))
                self.send_response(204)
                self.end_headers()
            def log_message(self, *_):
                pass
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.close_server)
        self.url = f"http://127.0.0.1:{self.server.server_port}"

    def close_server(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)

    def test_real_http_detection_delivery_deduplication_and_recovery(self):
        settings = m.Settings(status_url=self.url + "/api/status", webhook_url=self.url + "/webhook",
                              state_file=str(Path(self.temp.name) / "state.json"))
        # Loopback HTTP is permitted only by this in-memory fixture; normal settings require HTTPS.
        monitor, transport = m.Monitor(settings), m.Transport(settings)
        monitor.observe(transport.snapshot(), self.body["serverTime"] / 1000)
        base = self.body["serverTime"] / 1000
        self.body = snapshot(base + 1, serial={"portOpen": False})
        monitor.observe(transport.snapshot(), base + 1)
        self.assertEqual(monitor.pending(base + 30), [])
        events = monitor.pending(base + 31)
        self.assertTrue(transport.send(m.payload(settings, monitor, events, base + 31), base + 31))
        monitor.delivered(events, base + 31)
        self.assertEqual(monitor.pending(base + 32), [])
        self.assertEqual(len(self.received), 1)
        self.assertEqual(self.received[0][0], "/webhook?wait=true")
        self.assertEqual(self.received[0][1]["allowed_mentions"], {"parse": []})
        monitor.save_state()
        monitor = m.Monitor(settings)
        self.body = snapshot(base + 50)
        monitor.observe(transport.snapshot(), base + 50)
        events = monitor.pending(base + 80)
        self.assertEqual(events, [("serial.port", "recovered")])
        self.assertTrue(transport.send(m.payload(settings, monitor, events, base + 80), base + 80))
        self.assertIn("検出条件が解消", self.received[1][1]["content"])

    def cli(self, paused=False):
        directory = Path(self.temp.name)
        path = directory / "monitor.json"
        path.write_text(json.dumps({"webhook_url": self.url.replace("http:", "https:") + "/webhook", "status_url": self.url + "/api/status"}))
        if paused:
            (directory / "monitor.paused").touch()
        return subprocess.run([sys.executable, str(ROOT / "Tools/RaceMonitor/monitor.py"), "--config", str(path), "--dry-run", "--once"],
                              capture_output=True, text=True, timeout=10)

    def test_cli_dry_run_reads_source_without_sending_or_saving(self):
        result = self.cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('"armed": true', result.stdout)
        self.assertEqual(self.reads, 1)
        self.assertEqual(self.received, [])
        self.assertFalse((Path(self.temp.name) / "monitor.state.json").exists())
        self.assertFalse((Path(self.temp.name) / "monitor.state.json.lock").exists())

    def test_maintenance_skips_source_and_notifications(self):
        result = self.cli(paused=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("一時停止", result.stdout)
        self.assertEqual(self.reads, 0)
        self.assertEqual(self.received, [])


if __name__ == "__main__":
    unittest.main()
