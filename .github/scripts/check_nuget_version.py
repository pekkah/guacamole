#!/usr/bin/env python3
"""Refuse to publish a package version that is not higher than what nuget.org already has.

Usage:
  check_nuget_version.py --self-test
  check_nuget_version.py VERSION PACKAGE_ID [PACKAGE_ID ...]

For each package ID, reads https://api.nuget.org/v3-flatcontainer/<id lowercase>/index.json
(every version ever pushed, listed or unlisted; 404 means the package has no versions yet)
and fails unless VERSION is strictly greater than the highest existing version under NuGet's
SemVer 2.0 ordering.

Exception: if exactly VERSION already exists for that package, the check passes. That is a
re-run of a publish that already pushed this version; `dotnet nuget push --skip-duplicate`
then skips the package, so nothing on nuget.org changes.

Only the Python standard library is used, so the publish job needs no extra setup.
"""

import json
import re
import sys
import urllib.error
import urllib.request

FLATCONTAINER = "https://api.nuget.org/v3-flatcontainer"

# NuGet accepts 1-4 numeric parts (legacy 4-part versions); SemVer 2 prerelease and metadata.
_VERSION_RE = re.compile(
    r"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$"
)


def parse(version):
    """Return a comparable key for a NuGet version string.

    NuGet ordering (NuGet.Versioning, SemVer 2.0 with NuGet's rules):
    - numeric parts compare numerically (missing parts are 0)
    - a release is higher than any of its prereleases
    - prerelease identifiers compare left to right: numeric identifiers numerically,
      alphanumeric ones as strings, case-insensitively (NuGet ignores case);
      a numeric identifier is lower than an alphanumeric one
    - with all preceding identifiers equal, fewer identifiers is lower
    - build metadata (+...) is ignored
    """
    m = _VERSION_RE.match(version.strip())
    if not m:
        raise ValueError(f"not a valid NuGet version: {version!r}")
    nums = tuple(int(p) if p is not None else 0 for p in m.groups()[:4])
    label = m.group(5)
    if label is None:
        # (1,) sorts above every prerelease key (0, ...): a release beats its prereleases.
        return nums, (1,)
    idents = []
    for ident in label.split("."):
        if ident == "":
            raise ValueError(f"empty prerelease identifier in {version!r}")
        if ident.isdigit():
            idents.append((0, int(ident), ""))
        else:
            idents.append((1, 0, ident.lower()))
    # Python compares tuples element-wise and a shorter tuple that is a prefix is lower,
    # which is exactly the "fewer identifiers lose" rule.
    return nums, (0, tuple(idents))


def compare(a, b):
    ka, kb = parse(a), parse(b)
    return (ka > kb) - (ka < kb)


def self_test():
    greater = [
        ("0.2.0-preview.1", "0.1.0-alpha.41"),
        ("0.1.0-preview.1", "0.1.0-alpha.41"),
        ("0.2.0", "0.2.0-preview.9"),
        ("0.3.0-preview.1", "0.2.0"),
        ("0.2.0-preview.1", "0.1.0"),
        ("1.0.0-alpha.10", "1.0.0-alpha.9"),
        ("1.0.0-alpha.beta", "1.0.0-alpha.1"),
        ("1.0.0-alpha.1", "1.0.0-alpha"),
        ("1.0.0-beta", "1.0.0-alpha.beta"),
        ("1.0.0-rc.1", "1.0.0-beta.11"),
        ("1.0.0", "1.0.0-rc.1"),
        ("1.10.0", "1.9.0"),
        ("2.0.0", "1.99.99"),
        ("1.0.0.1", "1.0.0"),
    ]
    equal = [
        ("1.0.0+build.5", "1.0.0"),
        ("1.0.0-Preview.1", "1.0.0-preview.1"),
        ("1.0", "1.0.0"),
    ]
    failures = []
    for a, b in greater:
        if compare(a, b) != 1 or compare(b, a) != -1:
            failures.append(f"expected {a} > {b}")
    for a, b in equal:
        if compare(a, b) != 0:
            failures.append(f"expected {a} == {b}")
    for bad in ["", "v1.0.0", "1.0.0-", "1.0.0-a..b", "1.0.0.0.0"]:
        try:
            parse(bad)
            failures.append(f"expected {bad!r} to be rejected")
        except ValueError:
            pass
    if failures:
        for f in failures:
            print(f"::error::version ordering self-test failed: {f}")
        return 1
    print(f"Version ordering self-test passed ({len(greater) + len(equal)} comparisons).")
    return 0


def existing_versions(package_id):
    url = f"{FLATCONTAINER}/{package_id.lower()}/index.json"
    try:
        with urllib.request.urlopen(url, timeout=30) as resp:
            return json.load(resp).get("versions", [])
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return []
        raise


def check(version, package_ids):
    parse(version)  # reject an invalid version before touching the network
    ok = True
    for package_id in package_ids:
        versions = existing_versions(package_id)
        if not versions:
            print(f"{package_id}: no versions on nuget.org yet; {version} is allowed.")
            continue
        highest = max(versions, key=parse)
        if any(compare(v, version) == 0 for v in versions):
            print(
                f"{package_id}: {version} already exists on nuget.org (re-run); "
                "--skip-duplicate will skip it."
            )
        elif compare(version, highest) > 0:
            print(f"{package_id}: {version} > highest existing {highest}; allowed.")
        else:
            print(
                f"::error::{package_id} {version} is not higher than the highest version "
                f"already on nuget.org ({highest}). New packages must sort above every "
                "existing version (listed or unlisted). Nothing was published."
            )
            ok = False
    return 0 if ok else 1


def main(argv):
    if argv == ["--self-test"]:
        return self_test()
    if len(argv) < 2:
        print(__doc__)
        return 2
    return check(argv[0], argv[1:])


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
