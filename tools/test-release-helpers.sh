#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-common.sh
source "${repository_root}/tools/release-common.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-release-helpers.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM

fail() {
    echo "release-helper test failed: $*" >&2
    exit 1
}

initialize_repository() {
    local root="$1"
    mkdir -p "${root}"
    git -C "${root}" init -q -b main
    git -C "${root}" config user.name "Release helper test"
    git -C "${root}" config user.email "release-helper@example.invalid"
}

write_release_state() {
    local root="$1"
    local version="$2"
    printf '%s\n' "${version}" >"${root}/VERSION"
    cat >"${root}/CHANGELOG.md" <<EOF
# Changelog

## Orb Of Creation ModSuite ${version} — 2026-08-01

- Released.
EOF
}

if ! validate_release_version "0.5.0-beta.1" ||
    validate_release_version "01.5.0" ||
    validate_release_version "0.5.0-beta..1"; then
    fail "SemVer validation"
fi
if ! validate_stable_release_version "0.5.0" ||
    validate_stable_release_version "0.5.0-beta.1" ||
    validate_stable_release_version "0.5.0+build.1"; then
    fail "stable-version validation"
fi
if ! stable_version_is_greater "0.6.0" "0.5.9" ||
    ! stable_version_is_greater "1.0.0" "0.99.99" ||
    stable_version_is_greater "0.5.0" "0.5.0" ||
    stable_version_is_greater "0.4.9" "0.5.0"; then
    fail "stable-version ordering"
fi

changelog_fixture="${temporary_root}/CHANGELOG.md"
cat >"${changelog_fixture}" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- First release note.
- Second release note.

## Orb Of Creation ModSuite 0.4.0 Beta 1 — 2026-07-29

- Older note.
EOF
expected_section='## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- First release note.
- Second release note.'
actual_section="$(extract_changelog_section "${changelog_fixture}" "0.5.0")"
[[ "${actual_section}" == "${expected_section}" ]] || fail "changelog extraction"
if extract_changelog_section "${changelog_fixture}" "9.9.9" >/dev/null 2>&1; then
    fail "missing changelog section was accepted"
fi
assert_no_unreleased_changelog "${changelog_fixture}" ||
    fail "released changelog was rejected"
printf '\n## Unreleased\n' >>"${changelog_fixture}"
if assert_no_unreleased_changelog "${changelog_fixture}" >/dev/null 2>&1; then
    fail "Unreleased changelog section was accepted"
fi

classification_root="${temporary_root}/classification"
initialize_repository "${classification_root}"
write_release_state "${classification_root}" "0.5.0"
git -C "${classification_root}" add VERSION CHANGELOG.md
git -C "${classification_root}" commit -q -m "release: 0.5.0"
git -C "${classification_root}" tag -a suite-v0.5.0 -m "0.5.0"
printf 'one\n' >"${classification_root}/one.txt"
git -C "${classification_root}" add one.txt
git -C "${classification_root}" commit -q -m "feature: one"
git -C "${classification_root}" tag -a suite-v0.5.0+main.1 -m "beta"
printf 'two\n' >"${classification_root}/two.txt"
git -C "${classification_root}" add two.txt
git -C "${classification_root}" commit -q -m "feature: two"
classification_head="$(git -C "${classification_root}" rev-parse HEAD)"

[[ "$(newest_stable_release_version "${classification_root}")" == "0.5.0" ]] ||
    fail "newest stable tag included a beta tag"
[[ "$(publication_kind "${classification_root}" "0.5.0")" == "beta" ]] ||
    fail "existing VERSION tag was not classified as beta"
[[ "$(publication_kind "${classification_root}" "0.6.0")" == "release" ]] ||
    fail "missing VERSION tag was not classified as release"
derived_beta_tag="$(derive_beta_tag "${classification_root}" "0.6.0" "${classification_head}")"
[[ "${derived_beta_tag}" == "suite-v0.6.0+main.2" ]] ||
    fail "beta derivation did not count from newest stable tag: ${derived_beta_tag}"
printf 'three\n' >"${classification_root}/three.txt"
git -C "${classification_root}" add three.txt
git -C "${classification_root}" commit -q -m "fix: retry"
[[ "$(publication_kind "${classification_root}" "0.6.0")" == "release" ]] ||
    fail "missing VERSION tag did not retry release after another push"

policy_root="${temporary_root}/policy"
initialize_repository "${policy_root}"
write_release_state "${policy_root}" "0.5.0"
git -C "${policy_root}" add VERSION CHANGELOG.md
git -C "${policy_root}" commit -q -m "release: 0.5.0"
git -C "${policy_root}" tag -a suite-v0.5.0 -m "0.5.0"
policy_base="$(git -C "${policy_root}" rev-parse HEAD)"

initialization_root="${temporary_root}/initialization-policy"
initialize_repository "${initialization_root}"
printf '# Changelog\n' >"${initialization_root}/CHANGELOG.md"
git -C "${initialization_root}" add CHANGELOG.md
git -C "${initialization_root}" commit -q -m "docs: initial history"
initialization_base="$(git -C "${initialization_root}" rev-parse HEAD)"
git -C "${initialization_root}" tag -a suite-v0.5.0 -m "0.5.0"
write_release_state "${initialization_root}" "0.5.0"
git -C "${initialization_root}" add VERSION CHANGELOG.md
git -C "${initialization_root}" commit -q -m "release: invalid bootstrap"
initialization_head="$(git -C "${initialization_root}" rev-parse HEAD)"
if ORB_RELEASE_POLICY_ROOT="${initialization_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${initialization_base}" "${initialization_head}" \
    "release: invalid bootstrap" >/dev/null 2>&1; then
    fail "initial VERSION equal to the newest stable tag was accepted"
fi

printf 'ordinary\n' >"${policy_root}/README.md"
git -C "${policy_root}" add README.md
git -C "${policy_root}" commit -q -m "docs: ordinary"
ordinary_head="$(git -C "${policy_root}" rev-parse HEAD)"
ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${ordinary_head}" "docs: ordinary" >/dev/null ||
    fail "ordinary PR without release files was rejected"

git -C "${policy_root}" switch -q -c bad-title "${policy_base}"
printf '\nEditorial correction.\n' >>"${policy_root}/CHANGELOG.md"
git -C "${policy_root}" add CHANGELOG.md
git -C "${policy_root}" commit -q -m "docs: edit released notes"
bad_title_head="$(git -C "${policy_root}" rev-parse HEAD)"
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${bad_title_head}" "docs: edit notes" >/dev/null 2>&1; then
    fail "ordinary PR title was allowed to change CHANGELOG.md"
fi
ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${bad_title_head}" "release: correct notes" >/dev/null ||
    fail "release PR title was rejected for a changelog-only correction"
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    push "${policy_base}" "${bad_title_head}" >/dev/null 2>&1; then
    fail "non-release push subject was allowed to change CHANGELOG.md"
fi

git -C "${policy_root}" switch -q -c valid-release "${policy_base}"
printf '0.6.0\n' >"${policy_root}/VERSION"
cat >"${policy_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.6.0 — 2026-08-02

- New release.

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- Released.
EOF
git -C "${policy_root}" add VERSION CHANGELOG.md
git -C "${policy_root}" commit -q -m "release: promote 0.6.0"
valid_release_head="$(git -C "${policy_root}" rev-parse HEAD)"
ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${valid_release_head}" "release: promote 0.6.0" >/dev/null ||
    fail "valid greater release PR was rejected"
ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    push "${policy_base}" "${valid_release_head}" >/dev/null ||
    fail "release squash subject was rejected on push"
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${valid_release_head}" "Release: promote 0.6.0" >/dev/null 2>&1; then
    fail "case-mismatched release PR title was accepted"
fi

git -C "${policy_root}" switch -q -c non-greater "${policy_base}"
printf '0.4.0\n' >"${policy_root}/VERSION"
cat >"${policy_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.4.0 — 2026-08-02

- Invalid rollback.
EOF
git -C "${policy_root}" add VERSION CHANGELOG.md
git -C "${policy_root}" commit -q -m "release: invalid rollback"
non_greater_head="$(git -C "${policy_root}" rev-parse HEAD)"
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${non_greater_head}" "release: invalid rollback" >/dev/null 2>&1; then
    fail "VERSION not greater than newest stable tag was accepted"
fi

git -C "${policy_root}" switch -q -c missing-notes "${policy_base}"
printf '0.6.0\n' >"${policy_root}/VERSION"
git -C "${policy_root}" add VERSION
git -C "${policy_root}" commit -q -m "release: missing notes"
missing_notes_head="$(git -C "${policy_root}" rev-parse HEAD)"
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${missing_notes_head}" "release: missing notes" >/dev/null 2>&1; then
    fail "VERSION promotion without changelog section was accepted"
fi

zip_assets="${temporary_root}/zip-assets"
mkdir -p "${zip_assets}"
printf 'release bytes\n' >"${zip_assets}/OrbModSuite-release.dll"
printf '%s  OrbModSuite-release.dll\n' "$(sha256_file "${zip_assets}/OrbModSuite-release.dll")" \
    >"${zip_assets}/OrbModSuite-SHA256SUMS.txt"
"${repository_root}/tools/package-install-zip.sh" "${zip_assets}" "0.6.0.main.2" >/dev/null ||
    fail "install ZIP packaging failed"
[[ "$(unzip -Z1 "${zip_assets}/OrbOfCreation-ModSuite-0.6.0.main.2.zip")" == \
    "BepInEx/plugins/OrbModSuite/OrbModSuite.dll" ]] || fail "install ZIP layout"
[[ "$(unzip -p "${zip_assets}/OrbOfCreation-ModSuite-0.6.0.main.2.zip")" == "release bytes" ]] ||
    fail "install ZIP DLL differs from the release asset"
[[ "$(sed -n '2p' "${zip_assets}/OrbModSuite-SHA256SUMS.txt")" == \
    "$(sha256_file "${zip_assets}/OrbOfCreation-ModSuite-0.6.0.main.2.zip")  OrbOfCreation-ModSuite-0.6.0.main.2.zip" ]] ||
    fail "install ZIP checksum line"
if "${repository_root}/tools/package-install-zip.sh" "${zip_assets}" "0.6.0.main.2" >/dev/null 2>&1; then
    fail "install ZIP overwrote an existing asset"
fi
if "${repository_root}/tools/package-install-zip.sh" "${zip_assets}" "0.6.0+main.2" >/dev/null 2>&1; then
    fail "install ZIP accepted a filename-unsafe version"
fi

echo "release-helpers=pass title-policy-red-green=pass state-classifier=pass version-ordering=pass beta-fallback=pass install-zip=pass"
