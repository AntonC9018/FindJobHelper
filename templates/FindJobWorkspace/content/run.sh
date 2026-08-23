#!/usr/bin/env bash
set -euo pipefail

force=false
if [[ ${1-} == "--force" ]]; then
    force=true
    shift
fi

if (( $# > 0 )); then
    if [[ $1 != "--" ]]; then
        printf "Unexpected argument '%s'. Use: run.sh [--force] [-- <CLI arguments...>]\n" "$1" >&2
        exit 2
    fi
    shift
fi

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
provider_project="$script_directory/src/FindJobWorkspace.Provider"
build_directory="$script_directory/build"
provider_dll="$build_directory/FindJobWorkspace.Provider.dll"

if [[ $force == true || ! -f $provider_dll ]]; then
    dotnet publish "$provider_project" --output "$build_directory"
fi

# Alternatively, remove these assignments and configure email and phone with
# user-secrets if you do not want to commit real contact information.
PersonalInfo__FirstName='Alex' \
PersonalInfo__LastName='Example' \
PersonalInfo__Profession='Example Software Engineer' \
PersonalInfo__City='Example City' \
PersonalInfo__Country='Example Country' \
PersonalInfo__GitHub='https://example.test/github' \
PersonalInfo__LinkedIn='https://example.test/linkedin' \
PersonalInfo__YouTube='https://example.test/youtube' \
PersonalInfo__Portfolio='https://example.test/portfolio' \
PersonalInfo__Email='alex@example.test' \
PersonalInfo__Phone='202-555-0100' \
dotnet tool run find-job-helper -- \
    --experience-database "$provider_dll" \
    --open \
    --config config.json \
    "$@"
