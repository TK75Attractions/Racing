#!/bin/sh
set -eu
rgp_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
rgp_unity=${UNITY_EDITOR_ROOT:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents}
rgp_nunit=${NUNIT_FRAMEWORK:-$rgp_unity/Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll}
rgp_temp=$(mktemp -d)
trap 'rm -rf "$rgp_temp"' EXIT
cp "$rgp_nunit" "$rgp_temp/nunit.framework.dll"
cd "$rgp_root"
mkdir -p Artifacts/Rgp
mcs -langversion:latest -r:"$rgp_temp/nunit.framework.dll" -out:"$rgp_temp/RgpTests.exe" \
    Assets/Managers/Multiplayer/Core/*.cs Assets/Tests/EditMode/*.cs 'Tests~/Rgp/RunTests.cs'
mono "$rgp_temp/RgpTests.exe" "$rgp_root/Artifacts/Rgp/test-results.xml"
