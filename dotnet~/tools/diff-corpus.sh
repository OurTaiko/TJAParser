#!/usr/bin/env bash
# Compares two builds of the parser over a chart library: `tjaparser inspect` of every .tja
# under a directory, by the build of a git ref and by the working tree. An optimisation must
# show no difference; a change meant to alter behaviour shows exactly the files it should.
#
#   dotnet~/tools/diff-corpus.sh <baseline-ref> <chart-dir> [out-dir]
#
# The files must be UTF-8, as the servers hand them over after their own normalisation; files
# the CLI cannot read are compared by their exit code and message like any other.
set -euo pipefail

if [ $# -lt 2 ]; then
  echo "usage: $0 <baseline-ref> <chart-dir> [out-dir]" >&2
  exit 2
fi
ref=$1
charts=$(cd "$2" && pwd)
out=${3:-$(mktemp -d)}
repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)

mkdir -p "$out"
worktree="$out/baseline-src"
rm -rf "$worktree"
git -C "$repo" worktree add --detach "$worktree" "$ref" >/dev/null
trap 'git -C "$repo" worktree remove --force "$worktree" >/dev/null 2>&1 || true' EXIT

build() { # <source root> <output dir>
  dotnet build "$1/dotnet~/tjaparser/tjaparser.csproj" -c Release -o "$2" -nologo -v quiet >/dev/null
}
build "$worktree" "$out/baseline-bin"
build "$repo" "$out/current-bin"

run() { # <bin dir> <result dir>
  local n=0
  while IFS= read -r -d '' file; do
    local rel=${file#"$charts"/}
    mkdir -p "$2/$(dirname "$rel")"
    { "$1/tjaparser" inspect "$file" 2>&1; echo "exit $?"; } >"$2/$rel.json" || true
    n=$((n + 1))
  done < <(find "$charts" -type f -iname '*.tja' -print0 | sort -z)
  echo "$n"
}
count=$(run "$out/baseline-bin" "$out/baseline")
run "$out/current-bin" "$out/current" >/dev/null

if diff -rq "$out/baseline" "$out/current" >"$out/diff.txt"; then
  echo "$count files, no difference ($ref vs working tree)"
else
  changed=$(wc -l <"$out/diff.txt")
  echo "$count files, $changed differ ($ref vs working tree); see $out/diff.txt and diff -r $out/baseline $out/current"
  exit 1
fi
