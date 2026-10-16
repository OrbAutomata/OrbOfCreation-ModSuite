#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-common.sh
source "${repository_root}/tools/release-common.sh"

if [[ "$#" -ne 2 ]]; then
    echo "Usage: tools/package-install-zip.sh <asset-directory> <asset-version>" >&2
    exit 2
fi

for command_name in zip unzip cmp mktemp; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command is unavailable: ${command_name}" >&2
        exit 1
    fi
done

asset_directory="$(cd "$1" && pwd)"
asset_version="$2"
if [[ ! "${asset_version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9A-Za-z]+)*$ ]]; then
    echo "Install ZIP version must be a filename-safe dotted version: ${asset_version}" >&2
    exit 1
fi

release_asset="${asset_directory}/OrbModSuite-release.dll"
checksums_asset="${asset_directory}/OrbModSuite-SHA256SUMS.txt"
zip_name="OrbOfCreation-ModSuite-${asset_version}.zip"
zip_asset="${asset_directory}/${zip_name}"
plugin_entry="BepInEx/plugins/OrbModSuite/OrbModSuite.dll"
for required_path in "${release_asset}" "${checksums_asset}"; do
    if [[ ! -f "${required_path}" ]]; then
        echo "Required release asset is missing: ${required_path}" >&2
        exit 1
    fi
done
if [[ -e "${zip_asset}" ]]; then
    echo "Refusing to overwrite release asset: ${zip_asset}" >&2
    exit 1
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-install-zip.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM

stage="${temporary_root}/stage"
mkdir -p "${stage}/$(dirname "${plugin_entry}")"
cp "${release_asset}" "${stage}/${plugin_entry}"
(
    cd "${stage}"
    zip -X -D -q "${temporary_root}/${zip_name}" "${plugin_entry}"
)

actual_entries="$(unzip -Z1 "${temporary_root}/${zip_name}")"
if [[ "${actual_entries}" != "${plugin_entry}" ]]; then
    echo "Install ZIP entries are not exactly ${plugin_entry}:" >&2
    printf '%s\n' "${actual_entries}" >&2
    exit 1
fi
mkdir "${temporary_root}/verified"
unzip -q "${temporary_root}/${zip_name}" -d "${temporary_root}/verified"
if ! cmp -s "${release_asset}" "${temporary_root}/verified/${plugin_entry}"; then
    echo "Install ZIP DLL differs from ${release_asset}." >&2
    exit 1
fi

mv "${temporary_root}/${zip_name}" "${zip_asset}"
zip_sha="$(sha256_file "${zip_asset}")"
printf '%s  %s\n' "${zip_sha}" "${zip_name}" >>"${checksums_asset}"

echo "Install ZIP: ${zip_name}"
echo "Install ZIP entry: ${plugin_entry} (identical to OrbModSuite-release.dll, SHA-256 $(sha256_file "${release_asset}"))"
echo "Install ZIP SHA-256: ${zip_sha}"
