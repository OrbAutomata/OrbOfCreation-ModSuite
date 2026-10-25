#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest_path="${repository_root}/lib/game-refs/v1.0.5/manifest.json"
output_root="${repository_root}/lib/game-refs/v1.0.5"
source_root="${1:-${repository_root}/artifacts/game-v105}"
refasmer_package="JetBrains.Refasmer.CliTool"
refasmer_version="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["tools"]["jetbrains.refasmer.clitool"]["version"])' "${repository_root}/.config/dotnet-tools.json")"
temporary_root=""

fail() {
    echo "Game-reference generation failed: $*" >&2
    exit 1
}

sha256_file() {
    local path="$1"
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "${path}" | awk '{print $1}'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum "${path}" | awk '{print $1}'
    else
        fail "shasum or sha256sum is required"
    fi
}

manifest_entry() {
    local file="$1"
    local matches
    matches="$(grep -F "\"file\": \"${file}\"" "${manifest_path}" || true)"
    if [[ -z "${matches}" || "${matches}" == *$'\n'* ]]; then
        fail "manifest must contain exactly one entry for ${file}"
    fi
    printf '%s\n' "${matches}"
}

entry_value() {
    local entry="$1"
    local key="$2"
    local value
    value="$(printf '%s\n' "${entry}" | sed -n "s/.*\"${key}\": \"\([^\"]*\)\".*/\1/p")"
    if [[ -z "${value}" || "${value}" == *$'\n'* ]]; then
        fail "manifest entry has no single ${key} value"
    fi
    printf '%s\n' "${value}"
}

if [[ "$#" -gt 1 ]]; then
    echo "Usage: tools/make-game-refs.sh [audited-game-root]" >&2
    exit 2
fi
if [[ ! -f "${manifest_path}" ]]; then
    fail "manifest is missing: ${manifest_path}"
fi
manifest_tool_version="$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' "${manifest_path}")"
if [[ "${manifest_tool_version}" != "${refasmer_version}" ]]; then
    fail "manifest Refasmer version is '${manifest_tool_version}', expected '${refasmer_version}'"
fi

files=(
    Assembly-CSharp.dll
    Assembly-CSharp-firstpass.dll
    UnityEngine.dll
    UnityEngine.CoreModule.dll
    UnityEngine.UI.dll
    UnityEngine.UIModule.dll
    Unity.TextMeshPro.dll
    BepInEx.dll
    0Harmony.dll
    Newtonsoft.Json.dll
    UnityEngine.ScreenCaptureModule.dll
    UnityEngine.ImageConversionModule.dll
)
entry_count="$(grep -c '"file":' "${manifest_path}")"
if [[ "${entry_count}" -ne "${#files[@]}" ]]; then
    fail "manifest contains ${entry_count} assembly entries, expected ${#files[@]}"
fi

source_paths=()
expected_output_hashes=()
for file in "${files[@]}"; do
    entry="$(manifest_entry "${file}")"
    source_relative="$(entry_value "${entry}" source)"
    expected_input_hash="$(entry_value "${entry}" inputSha256)"
    expected_output_hashes+=("$(entry_value "${entry}" outputSha256)")
    source_path="${source_root}/${source_relative}"
    if [[ ! -f "${source_path}" ]]; then
        fail "audited input is missing: ${source_path}"
    fi
    actual_input_hash="$(sha256_file "${source_path}")"
    if [[ "${actual_input_hash}" != "${expected_input_hash}" ]]; then
        fail "input SHA-256 mismatch for ${file}: expected ${expected_input_hash}, actual ${actual_input_hash}"
    fi
    source_paths+=("${source_path}")
done

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-game-refs.XXXXXX")" ||
    fail "could not create a temporary directory"
cleanup() {
    if [[ -n "${temporary_root}" && -d "${temporary_root}" ]]; then
        rm -rf -- "${temporary_root}"
    fi
}
trap cleanup EXIT INT TERM

echo "Installing ${refasmer_package} ${refasmer_version} into the temporary tool directory..."
dotnet tool install "${refasmer_package}" \
    --tool-path "${temporary_root}/tool" \
    --version "${refasmer_version}" >/dev/null

echo "Generating full-surface, metadata-only references..."
"${temporary_root}/tool/refasmer" --refasm --all \
    --outputdir="${temporary_root}/generated" \
    "${source_paths[@]}"

for index in "${!files[@]}"; do
    file="${files[${index}]}"
    generated_path="${temporary_root}/generated/${file}"
    if [[ ! -f "${generated_path}" ]]; then
        fail "Refasmer did not produce ${file}"
    fi
    actual_output_hash="$(sha256_file "${generated_path}")"
    expected_output_hash="${expected_output_hashes[${index}]}"
    if [[ "${actual_output_hash}" != "${expected_output_hash}" ]]; then
        fail "output SHA-256 mismatch for ${file}: expected ${expected_output_hash}, actual ${actual_output_hash}"
    fi
done

mkdir -p "${output_root}"
for file in "${files[@]}"; do
    cp "${temporary_root}/generated/${file}" "${output_root}/${file}.new"
done
for file in "${files[@]}"; do
    mv "${output_root}/${file}.new" "${output_root}/${file}"
done

echo "Generated and verified ${#files[@]} references under ${output_root}."
