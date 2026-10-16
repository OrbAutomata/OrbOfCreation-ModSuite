#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_directory="${repository_root}/artifacts/releases"
game_root="${OOC_GAME_DIR:-${repository_root}/lib}"
checked_in_references="${repository_root}/lib/game-refs/v1.0.5"
# shellcheck source=release-common.sh
source "${repository_root}/tools/release-common.sh"

if [[ "$#" -ne 0 ]]; then
    echo "Usage: ./script/package" >&2
    exit 2
fi

for command_name in dotnet git zip unzip shasum env; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command is unavailable: ${command_name}" >&2
        exit 1
    fi
done

initial_head="$(git -C "${repository_root}" rev-parse HEAD)"
assert_clean_head() {
    local current_head
    current_head="$(git -C "${repository_root}" rev-parse HEAD)"
    if [[ "${current_head}" != "${initial_head}" ]]; then
        echo "Repository HEAD changed during package validation." >&2
        exit 1
    fi
    if [[ -n "$(git -C "${repository_root}" status --porcelain --untracked-files=normal)" ]]; then
        echo "Package rehearsal requires a clean tracked and untracked working tree." >&2
        exit 1
    fi
}
assert_clean_head

for required_reference in \
    Assembly-CSharp.dll \
    Assembly-CSharp-firstpass.dll \
    UnityEngine.dll \
    UnityEngine.CoreModule.dll \
    UnityEngine.UI.dll \
    UnityEngine.UIModule.dll \
    Unity.TextMeshPro.dll \
    BepInEx.dll \
    0Harmony.dll; do
    if [[ ! -f "${checked_in_references}/${required_reference}" ]]; then
        echo "Required checked-in reference is missing: ${checked_in_references}/${required_reference}" >&2
        exit 1
    fi
done

managed_directory="${game_root}/Orb Of Creation_Data/Managed"
bepinex_core_directory="${game_root}/BepInEx/core"
for required_reference in \
    "${managed_directory}/Assembly-CSharp.dll" \
    "${managed_directory}/Assembly-CSharp-firstpass.dll" \
    "${managed_directory}/UnityEngine.dll" \
    "${managed_directory}/UnityEngine.CoreModule.dll" \
    "${managed_directory}/UnityEngine.UI.dll" \
    "${managed_directory}/UnityEngine.UIModule.dll" \
    "${managed_directory}/Unity.TextMeshPro.dll" \
    "${bepinex_core_directory}/BepInEx.dll" \
    "${bepinex_core_directory}/0Harmony.dll"; do
    if [[ ! -f "${required_reference}" ]]; then
        echo "Required real-reference file is missing: ${required_reference}" >&2
        exit 1
    fi
done

suite_version="$(read_released_version "${repository_root}/VERSION")"

package_name="OrbOfCreation-ModSuite-${suite_version}-rehearsal"
zip_path="${output_directory}/${package_name}.zip"
checksums_path="${output_directory}/${package_name}-SHA256SUMS.txt"
if [[ -e "${zip_path}" || -e "${checksums_path}" ]]; then
    echo "Package output already exists; refusing to overwrite ${package_name}." >&2
    exit 1
fi

echo "Running the bounded portable gate..."
"${repository_root}/script/test"

# Installed contracts are intentionally local and inspect the audited full assemblies. CI
# publication does not use this package path and must not claim metadata-only refs repeat them.
echo "Restoring the installed-game contract configuration..."
OOC_GAME_DIR="${game_root}" dotnet restore \
    "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
    -p:Configuration=Release

echo "Running installed-game contracts against the audited game assemblies..."
OOC_GAME_DIR="${game_root}" dotnet test \
    "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
    --configuration Release --no-restore

echo "Cleaning the canonical checked-in-reference build..."
env -u OOC_GAME_DIR dotnet clean \
    "${repository_root}/src/OrbModSuite.csproj" \
    --configuration Release \
    -p:EnableServiceCycleProfiler=false \
    -p:ContinuousIntegrationBuild=true

echo "Restoring the canonical checked-in-reference build..."
env -u OOC_GAME_DIR dotnet restore \
    "${repository_root}/src/OrbModSuite.csproj" \
    --force-evaluate \
    --disable-build-servers \
    -p:Configuration=Release \
    -p:EnableServiceCycleProfiler=false \
    -p:ContinuousIntegrationBuild=true

echo "Building the canonical suite against checked-in references..."
env -u OOC_GAME_DIR dotnet build \
    "${repository_root}/src/OrbModSuite.csproj" \
    --configuration Release \
    --disable-build-servers \
    -m:1 \
    --no-incremental \
    --no-restore \
    -p:EnableServiceCycleProfiler=false \
    -p:ContinuousIntegrationBuild=true

assert_plugin_output() {
    local output="${repository_root}/src/bin/Release/netstandard2.1"
    if [[ ! -f "${output}/OrbModSuite.dll" ]]; then
        echo "Required suite output is missing: OrbModSuite.dll" >&2
        exit 1
    fi
    if find "${output}" -maxdepth 1 -type f \( \
        -name 'Assembly-CSharp*.dll' -o \
        -name 'BepInEx.dll' -o \
        -name '0Harmony.dll' -o \
        -name 'UnityEngine*.dll' -o \
        -name 'OrbModding.GameStubs.dll' \) | grep -q .; then
        echo "Game or loader assemblies leaked into the suite output." >&2
        exit 1
    fi
}

assert_plugin_output
assert_clean_head

mkdir -p "${output_directory}"
temporary_root="$(mktemp -d "${output_directory}/.package.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM
stage="${temporary_root}/${package_name}"
temporary_zip="${temporary_root}/${package_name}.zip"
temporary_checksums="${temporary_root}/${package_name}-SHA256SUMS.txt"

mkdir -p "${stage}/BepInEx/plugins/OrbModSuite"
cp "${repository_root}/src/bin/Release/netstandard2.1/OrbModSuite.dll" \
    "${stage}/BepInEx/plugins/OrbModSuite/"
for document in README.md CHANGELOG.md LICENSE THIRD_PARTY_NOTICES.md; do
    cp "${repository_root}/${document}" "${stage}/"
done

expected_entries=(
    "BepInEx/plugins/OrbModSuite/OrbModSuite.dll"
    "CHANGELOG.md"
    "LICENSE"
    "README.md"
    "THIRD_PARTY_NOTICES.md"
)
printf '%s\n' "${expected_entries[@]}" | LC_ALL=C sort > "${temporary_root}/expected-entries.txt"
(
    cd "${stage}"
    find . -type f -print | sed 's#^\./##' | LC_ALL=C sort | \
        zip -X -q "${temporary_zip}" -@
)
unzip -Z1 "${temporary_zip}" | LC_ALL=C sort > "${temporary_root}/actual-entries.txt"
if ! cmp -s "${temporary_root}/expected-entries.txt" "${temporary_root}/actual-entries.txt"; then
    echo "Package archive entries do not match the supported allowlist:" >&2
    diff -u "${temporary_root}/expected-entries.txt" "${temporary_root}/actual-entries.txt" >&2 || true
    exit 1
fi

for entry in "${expected_entries[@]}"; do
    (
        cd "${stage}"
        shasum -a 256 "${entry}"
    ) >> "${temporary_checksums}"
done
zip_hash="$(shasum -a 256 "${temporary_zip}" | awk '{print $1}')"
printf '%s  %s\n' "${zip_hash}" "${package_name}.zip" >> "${temporary_checksums}"

verification_stage="${temporary_root}/verified"
mkdir "${verification_stage}"
unzip -q "${temporary_zip}" -d "${verification_stage}"
head -n "${#expected_entries[@]}" "${temporary_checksums}" | (
    cd "${verification_stage}"
    shasum -a 256 --check >/dev/null
)
tail -n 1 "${temporary_checksums}" | (
    cd "${temporary_root}"
    shasum -a 256 --check >/dev/null
)

assert_clean_head
if ! ln "${temporary_zip}" "${zip_path}"; then
    echo "Package ZIP appeared during validation; refusing to overwrite it." >&2
    exit 1
fi
if ! ln "${temporary_checksums}" "${checksums_path}"; then
    rm -f -- "${zip_path}"
    echo "Package checksum manifest appeared during validation; refusing partial publication." >&2
    exit 1
fi

echo "Created ${zip_path}"
echo "Created ${checksums_path}"
echo "Commit: ${initial_head}"
echo "Archive entries:"
sed 's/^/  /' "${temporary_root}/actual-entries.txt"
