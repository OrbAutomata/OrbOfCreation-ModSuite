#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-common.sh
source "${repository_root}/tools/release-common.sh"

if [[ "$#" -ne 1 ]]; then
    echo "Usage: tools/build-release-assets.sh <output-directory>" >&2
    exit 2
fi

for command_name in dotnet env grep cp mkdir awk; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command is unavailable: ${command_name}" >&2
        exit 1
    fi
done

version="$(read_released_version "${repository_root}/VERSION")"
echo "Building publication assets for VERSION=${version}."

output_directory="$1"
mkdir -p "${output_directory}"
output_directory="$(cd "${output_directory}" && pwd)"
release_asset="${output_directory}/OrbModSuite-release.dll"
profile_asset="${output_directory}/OrbModSuite-perf-debug.dll"
checksums_asset="${output_directory}/OrbModSuite-SHA256SUMS.txt"
for output_path in "${release_asset}" "${profile_asset}" "${checksums_asset}"; do
    if [[ -e "${output_path}" ]]; then
        echo "Refusing to overwrite release asset: ${output_path}" >&2
        exit 1
    fi
done

build_flavor() {
    local label="$1"
    local configuration="$2"
    local profile_enabled="$3"
    local built_dll="$4"
    local retained_dll="$5"

    echo "Cleaning ${label}..."
    env -u OOC_GAME_DIR dotnet clean \
        "${repository_root}/src/OrbModSuite.csproj" \
        --configuration "${configuration}" \
        --disable-build-servers \
        -p:EnableServiceCycleProfiler="${profile_enabled}" \
        -p:ContinuousIntegrationBuild=true

    echo "Restoring ${label} from the committed reference closure..."
    env -u OOC_GAME_DIR dotnet restore \
        "${repository_root}/src/OrbModSuite.csproj" \
        --force-evaluate \
        --disable-build-servers \
        -p:Configuration="${configuration}" \
        -p:EnableServiceCycleProfiler="${profile_enabled}" \
        -p:ContinuousIntegrationBuild=true \
        -p:TreatWarningsAsErrors=true \
        -p:WarningsNotAsErrors=NU1900

    echo "Building ${label} from the committed reference closure..."
    env -u OOC_GAME_DIR dotnet build \
        "${repository_root}/src/OrbModSuite.csproj" \
        --configuration "${configuration}" \
        --no-restore \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        -p:EnableServiceCycleProfiler="${profile_enabled}" \
        -p:ContinuousIntegrationBuild=true \
        -p:TreatWarningsAsErrors=true \
        -p:WarningsNotAsErrors=NU1900

    if [[ ! -f "${built_dll}" ]]; then
        echo "${label} output is missing: ${built_dll}" >&2
        exit 1
    fi
    cp "${built_dll}" "${retained_dll}"
}

build_flavor \
    "release flavor" Release false \
    "${repository_root}/src/bin/Release/netstandard2.1/OrbModSuite.dll" \
    "${release_asset}"
if grep -a -q "AutomataServiceCycleProfileController" "${release_asset}" ||
    grep -a -q "ServiceCycleProfileRuntimeSession" "${release_asset}"; then
    echo "Release flavor unexpectedly contains ServiceCycle profiling components." >&2
    exit 1
fi

build_flavor \
    "perf-debug flavor" Debug true \
    "${repository_root}/src/bin-profile/Debug/netstandard2.1/OrbModSuite.dll" \
    "${profile_asset}"
if ! grep -a -q "AutomataServiceCycleProfileController" "${profile_asset}" ||
    ! grep -a -q "ServiceCycleProfileRuntimeSession" "${profile_asset}"; then
    echo "Perf-debug flavor is missing ServiceCycle profiling components." >&2
    exit 1
fi

release_sha="$(sha256_file "${release_asset}")"
profile_sha="$(sha256_file "${profile_asset}")"
{
    printf '%s  %s\n' "${release_sha}" "$(basename "${release_asset}")"
    printf '%s  %s\n' "${profile_sha}" "$(basename "${profile_asset}")"
} >"${checksums_asset}"

echo "Release asset SHA-256: ${release_sha}"
echo "Perf-debug asset SHA-256: ${profile_sha}"
