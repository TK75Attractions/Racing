#!/usr/bin/env python3
"""Exercise production dashboard/bridge copies without touching running cabinet services."""
import argparse
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--browser', action='store_true', help='Also check desktop/mobile in Playwright + Chrome')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = root / 'Logs/Maintenance'
output.mkdir(parents=True, exist_ok=True)
for port in (18765, 18766):
    with socket.socket() as probe:
        try:
            probe.bind(('127.0.0.1', port))
        except OSError:
            raise SystemExit(f'Test port {port} is occupied. Leave the running service intact and use another test environment.')
with (output / 'dashboard-unit.log').open('w') as log:
    subprocess.run(['node', '--test', 'Tests~/Dashboard/dashboard-ui.test.cjs'], cwd=root, stdout=log, stderr=subprocess.STDOUT, check=True)
with tempfile.TemporaryDirectory(prefix='racing-maintenance-') as temporary:
    isolated = Path(temporary)
    for folder in ['Assets/Managers', 'Assets/StreamingAssets/Dashboard', 'Tools/DashboardRelay', 'Tests~/DashboardRelay', 'Tests~/DashboardPush']:
        shutil.copytree(root / folder, isolated / folder, ignore=shutil.ignore_patterns('bin', 'obj', '.DS_Store'))
    # Replace only loopback test ports. Request validation and production logic are preserved.
    for folder in ['Assets/Managers', 'Tools/DashboardRelay', 'Tests~/DashboardRelay']:
        for source in (isolated / folder).rglob('*'):
            if source.suffix in ('.cs', '.cjs', '.sh'):
                source.write_text(source.read_text().replace('8765', '18765').replace('8766', '18766'))
    with (output / 'dashboard-relay.log').open('w') as log:
        subprocess.run(['sh', 'Tools/DashboardRelay/test.sh'], cwd=isolated, stdout=log, stderr=subprocess.STDOUT, check=True)
    if args.browser:
        with (output / 'browser.log').open('w') as log:
            subprocess.run(['node', str(root / 'Tests~/Dashboard/maintenance-preview.cjs'), str(isolated), str(root / 'Artifacts/Maintenance')], cwd=root, stdout=log, stderr=subprocess.STDOUT, check=True)
print('MAINTENANCE_DASHBOARD_PASS: unit and isolated bridge/relay checks' + (' plus Chrome desktop/mobile' if args.browser else ''))
