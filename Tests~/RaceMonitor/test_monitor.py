"""Offline tests; no external webhook is contacted."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
import urllib.error
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("race_monitor", ROOT / "Tools/RaceMonitor/monitor.py")
m = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = m
spec.loader.exec_module(m)


def snapshot(now=1000, **changes):
    value = {"sessionId": "unity-1", "serverTime": now * 1000, "inputMode": "Serial P1/P2",
             "serial": {"portOpen": True, "errors": 50},
             "hardware": {"generation": 1, "sessionOpen": True, "recent": True, "state": "READY",
                          "ads": "Ok", "imu": "Ok", "angleLocked": True, "calibrated": True,
                          "telemetryEnabled": True, "invalidTelemetry": False, "inputFresh": True,
                          "imus": [{"address": "0x68", "status": "Ok"}, {"address": "0x69", "status": "Ok"}]}}
    for key, change in changes.items():
        if isinstance(change, dict):
            value[key].update(change)
        else:
            value[key] = change
    return value


class MonitorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.settings = m.Settings(webhook_url="https://example.invalid/secret-token",
                                   state_file=str(Path(self.directory.name) / "state.json"))
        self.monitor = m.Monitor(self.settings)

    def observe(self, now, **changes):
        self.monitor.observe(snapshot(now, **changes), now)

    def test_healthy_and_historical_counts_are_silent(self):
        for now in (1000, 1100, 1200):
            self.observe(now)
            self.assertEqual(self.monitor.pending(now), [])

    def test_startup_does_not_alert_until_first_valid_snapshot(self):
        self.monitor.observe(None, 1000)
        self.monitor.observe(None, 2000)
        self.assertEqual(self.monitor.pending(2000), [])
        self.observe(2001)
        self.monitor.observe(None, 2002)
        self.assertEqual(self.monitor.pending(2031), [])
        self.assertEqual(self.monitor.pending(2032), [("unity.unavailable", "attention")])

    def test_optional_startup_outage_alert(self):
        self.settings.arm_after_first_snapshot = False
        monitor = m.Monitor(self.settings)
        monitor.observe(None, 1000)
        self.assertEqual(monitor.pending(1030), [("unity.unavailable", "attention")])

    def test_short_fault_is_ignored_and_repeat_timer_restarts(self):
        self.observe(1000, serial={"portOpen": False})
        self.observe(1029)
        self.assertEqual(self.monitor.pending(1040), [])
        self.observe(1041, serial={"portOpen": False})
        self.assertEqual(self.monitor.pending(1070), [])
        self.assertEqual(self.monitor.pending(1071), [("serial.port", "attention")])

    def test_only_successful_delivery_advances_cooldown(self):
        self.observe(1000, serial={"portOpen": False})
        events = self.monitor.pending(1030)
        self.assertEqual(events, self.monitor.pending(1040))
        self.monitor.delivered(events, 1040)
        self.assertEqual(self.monitor.pending(2839), [])
        self.assertEqual(self.monitor.pending(2840), [("serial.port", "ongoing")])

    def test_stable_recovery_and_recurrence(self):
        self.observe(1000, serial={"portOpen": False})
        self.monitor.delivered(self.monitor.pending(1030), 1030)
        self.observe(1040)
        self.assertEqual(self.monitor.pending(1069), [])
        self.observe(1069, serial={"portOpen": False})
        self.observe(1070)
        self.assertEqual(self.monitor.pending(1099), [])
        events = self.monitor.pending(1100)
        self.assertEqual(events, [("serial.port", "recovered")])
        self.monitor.delivered(events, 1100)
        self.observe(1110, serial={"portOpen": False})
        self.assertEqual(self.monitor.pending(1140), [("serial.port", "attention")])

    def test_lost_source_never_recovers_hardware_and_resets_recovery_clock(self):
        self.observe(1000, serial={"portOpen": False})
        self.monitor.delivered(self.monitor.pending(1030), 1030)
        self.observe(1040)
        self.monitor.observe(None, 1050)
        self.assertEqual(self.monitor.pending(1080), [("unity.unavailable", "attention")])
        self.observe(1090)
        self.assertEqual(self.monitor.pending(1090), [])
        self.assertEqual(self.monitor.pending(1120), [("serial.port", "recovered")])

    def test_repeated_parse_errors_use_deltas_and_expire(self):
        self.observe(1000)
        self.observe(1005, serial={"errors": 53})
        self.observe(1010, serial={"errors": 55})
        self.assertEqual(self.monitor.pending(1039), [])
        self.assertEqual(self.monitor.pending(1040), [("serial.parse", "attention")])
        self.monitor.delivered(self.monitor.pending(1040), 1040)
        self.observe(1070, serial={"errors": 55})
        self.assertEqual(self.monitor.pending(1100), [("serial.parse", "recovered")])

    def test_counter_reset_and_device_session_change_are_not_error_bursts(self):
        self.observe(1000)
        self.observe(1001, serial={"errors": 55})
        self.observe(1002, hardware={"generation": 2}, serial={"errors": 900})
        self.assertEqual(self.monitor.pending(1032), [])
        self.observe(1003, serial={"errors": 0})
        self.assertEqual(self.monitor.pending(1033), [])

    def test_keyboard_debug_does_not_require_hardware(self):
        self.observe(1000, inputMode="Keyboard P1/P2 (debug; serial disabled)", serial={"portOpen": False})
        self.assertEqual(self.monitor.pending(1030), [])

    def test_unknown_input_mode_is_actionable(self):
        self.observe(1000, inputMode="Unavailable")
        self.assertEqual(self.monitor.pending(1030), [("input.unavailable", "attention")])

    def test_stale_and_malformed_snapshots_are_unavailable(self):
        self.observe(1000)
        for data in (snapshot(1000), {}, [], {"hardware": None}, snapshot(2000, hardware={"imus": [None]})):
            self.monitor.observe(data, 2000)
            self.assertIn("unity.unavailable", self.monitor.incidents)
        self.assertEqual(self.monitor.pending(2030), [("unity.unavailable", "attention")])

    def test_preparation_has_longer_grace_than_device_faults(self):
        self.observe(1000, hardware={"state": "CALIBRATING", "calibrated": False})
        self.assertEqual(self.monitor.pending(1179), [])
        self.assertEqual(self.monitor.pending(1180), [("device.preparing", "attention")])

    def test_manual_check_conditions(self):
        for change, key in [({"state": "FATAL"}, "device.fatal"), ({"ads": "Fault"}, "sensor.ads"),
                            ({"imu": "Fault"}, "sensor.imu"), ({"angleLocked": False}, "device.reference"),
                            ({"invalidTelemetry": True}, "input.invalid"), ({"inputFresh": False}, "input.stale"),
                            ({"telemetryEnabled": False}, "input.paused"), ({"recent": False}, "serial.response")]:
            monitor = m.Monitor(self.settings)
            monitor.observe(snapshot(1000, hardware=change), 1000)
            self.assertIn((key, "attention"), monitor.pending(1030))
        self.observe(1000, hardware={"imus": [{"address": "0x68", "status": "Ok"}, {"address": "0x69", "status": "Fault"}]})
        self.assertIn(("sensor.imu.0x69", "attention"), self.monitor.pending(1030))

    def test_saved_state_suppresses_duplicate_after_restart_and_resets_recovery(self):
        self.observe(1000, serial={"portOpen": False})
        self.monitor.delivered(self.monitor.pending(1030), 1030)
        self.observe(1040)
        self.monitor.save_state()
        raw = Path(self.settings.state_file).read_text()
        self.assertNotIn("secret-token", raw)
        monitor = m.Monitor(self.settings)
        monitor.observe(snapshot(1200), 1200)
        self.assertEqual(monitor.pending(1200), [])
        self.assertEqual(monitor.pending(1230), [("serial.port", "recovered")])
        monitor.observe(snapshot(1231, serial={"portOpen": False}), 1231)
        self.assertEqual(monitor.pending(1231), [])

    def test_corrupt_state_is_reported_without_crashing(self):
        Path(self.settings.state_file).write_text("bad")
        with contextlib.redirect_stderr(io.StringIO()) as output:
            monitor = m.Monitor(self.settings)
        self.assertFalse(monitor.armed)
        self.assertIn("復元できません", output.getvalue())

    def test_formats_disable_discord_mentions_and_bound_content(self):
        self.settings.name = "@everyone"
        self.observe(1000, serial={"portOpen": False})
        events = self.monitor.pending(1030)
        body = m.payload(self.settings, self.monitor, events, 1030)
        self.assertEqual(body["allowed_mentions"], {"parse": []})
        self.assertLessEqual(len(body["content"]), 2000)
        self.assertIn("確認事項", body["content"])
        self.settings.webhook_format = "slack"
        self.assertIn("text", m.payload(self.settings, self.monitor, events, 1030))
        self.settings.webhook_format = "json"
        self.assertEqual(m.payload(self.settings, self.monitor, events, 1030)["events"][0]["key"], "serial.port")


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.transport = m.Transport(m.Settings(webhook_url="https://example.invalid/token"))
        self.transport.writer = Mock()
        self.addCleanup(self.transport.writer.reset_mock)

    def response(self, status=204):
        response = Mock()
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        response.status = status
        self.transport.writer.open.return_value = response
        return response

    def test_discord_wait_confirmation_and_success(self):
        self.response()
        self.assertTrue(self.transport.send({"content": "テスト"}, 1000))
        request = self.transport.writer.open.call_args.args[0]
        self.assertIn("wait=true", request.full_url)
        self.assertEqual(json.loads(request.data)["content"], "テスト")

    def test_429_uses_server_retry_and_does_not_loop(self):
        error = urllib.error.HTTPError("https://example.invalid/token", 429, "limit", {"Retry-After": "12"}, io.BytesIO(b'{"retry_after": 21.5}'))
        self.transport.writer.open.side_effect = error
        self.assertFalse(self.transport.send({}, 1000))
        self.assertEqual(self.transport.retry_at, 1021.5)
        self.assertFalse(self.transport.send({}, 1021))
        self.assertEqual(self.transport.writer.open.call_count, 1)

    def test_failure_backoff_and_logs_never_expose_url(self):
        self.transport.writer.open.side_effect = urllib.error.URLError("https://example.invalid/token")
        with contextlib.redirect_stderr(io.StringIO()) as output:
            self.assertFalse(self.transport.send({}, 1000))
        self.assertNotIn("token", output.getvalue())
        self.assertEqual(self.transport.retry_at, 1005)
        self.transport.writer.open.side_effect = None
        self.response()
        self.assertTrue(self.transport.send({}, 1005))
        self.assertEqual(self.transport.failures, 0)

    def test_permanent_rejection_stops_retries(self):
        for code in (400, 401, 403, 404, 410):
            self.transport.disabled = False
            self.transport.writer.open.reset_mock()
            self.transport.writer.open.side_effect = urllib.error.HTTPError("secret-token", code, "rejected", {}, io.BytesIO())
            with contextlib.redirect_stderr(io.StringIO()) as output:
                self.assertFalse(self.transport.send({}, 1000))
                self.assertFalse(self.transport.send({}, 2000))
            self.assertEqual(self.transport.writer.open.call_count, 1)
            self.assertNotIn("secret-token", output.getvalue())

    def test_redirects_are_not_followed(self):
        self.assertIsNone(m.NoRedirect().redirect_request(None, None, 302, "redirect", {}, "https://example.invalid/other"))


class SettingsTests(unittest.TestCase):
    def test_validation_and_relative_runtime_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "monitor.json"
            path.write_text('{"webhook_url":"https://example.invalid/token"}')
            settings = m.Settings.load(path)
            self.assertEqual(settings.state_file, str((Path(directory) / "monitor.state.json").resolve()))
            invalid = [{"status_url": "http://public.example/status"}, {"webhook_url": "http://example.invalid/token"},
                       {"webhook_url": "https://user:password@example.invalid/token"}, {"poll_seconds": 0},
                       {"poll_seconds": True}, {"hold_seconds": float("nan")}, {"error_threshold": 0},
                       {"webhook_format": "unknown"}, {"arm_after_first_snapshot": "true"}, {"unknown": "value"}]
            for change in invalid:
                value = {"webhook_url": "https://example.invalid/token", **change}
                path.write_text(json.dumps(value))
                with self.assertRaises((ValueError, TypeError)):
                    m.Settings.load(path)
            path.write_text("{}")
            with self.assertRaises(ValueError):
                m.Settings.load(path)
            m.Settings.load(path, dry_run=True)




class AdditionalTests(unittest.TestCase):
    setUp = MonitorTests.setUp
    observe = MonitorTests.observe
    def test_replayed_error_history_is_not_recounted(self):
        self.observe(1000, serial={"lines": [{"id": 10, "status": "ERROR"}]})
        events = [{"id": number, "status": "ERROR"} for number in range(11, 16)]
        self.observe(1005, serial={"lines": events})
        self.observe(1010, serial={"lines": events})
        self.assertEqual(len(self.monitor.detector.event_samples), 5)
        self.assertEqual(self.monitor.pending(1035), [("serial.errors", "attention")])
        self.monitor.delivered(self.monitor.pending(1035), 1035)
        self.observe(1066, serial={"lines": events})
        self.assertEqual(self.monitor.pending(1096), [("serial.errors", "recovered")])

    def test_instance_lock_prevents_second_sender_and_releases_on_exit(self):
        path = Path(self.settings.state_file + ".lock")
        with m.InstanceLock(path):
            with self.assertRaises(OSError):
                with m.InstanceLock(path):
                    pass
        with m.InstanceLock(path):
            pass

    def test_recovery_message_identifies_old_condition_as_history(self):
        self.observe(1000, serial={"portOpen": False})
        self.monitor.delivered(self.monitor.pending(1030), 1030)
        self.observe(1040)
        body = m.payload(self.settings, self.monitor, self.monitor.pending(1070), 1070)
        self.assertIn("検出条件が解消", body["content"])
        self.assertIn("対象（検出時）", body["content"])
        self.assertNotIn("確認事項", body["content"])


class ServiceTests(unittest.TestCase):
    def test_launch_agent_keeps_secret_in_config_and_uses_stable_copy(self):
        sys.path.insert(0, str(ROOT / "Tools/RaceMonitor"))
        self.addCleanup(sys.path.remove, str(ROOT / "Tools/RaceMonitor"))
        import install_service
        document = install_service.service_plist("/usr/local/bin/python3", Path("/home/test/Library/RacingMonitor"), Path("/home/test/Library/Logs"))
        self.assertTrue(document["KeepAlive"])
        self.assertNotIn("EnvironmentVariables", document)
        self.assertEqual(document["ProgramArguments"][3:5], ["--config", "/home/test/Library/RacingMonitor/monitor.local.json"])


if __name__ == "__main__":
    unittest.main()
