#!/usr/bin/env bash
set -euo pipefail

force=false
port=5058
while (( $# > 0 )); do
    case "$1" in
        --force)
            force=true
            shift
            ;;
        --port)
            if [[ -z ${2-} || ! $2 =~ ^[0-9]+$ ]]; then
                printf "Missing numeric value for '%s'. Use: run.sh [--force] [--port N] [-- <CLI arguments...>]\n" "$1" >&2
                exit 2
            fi
            port="$2"
            shift 2
            ;;
        --)
            shift
            break
            ;;
        *)
            printf "Unexpected argument '%s'. Use: run.sh [--force] [--port N] [-- <CLI arguments...>]\n" "$1" >&2
            exit 2
            ;;
    esac
done

open_browser() {
    if [[ -n ${WSL_DISTRO_NAME-} ]]; then
        powershell.exe -c Start-Process "$1"
    elif command -v xdg-open >/dev/null 2>&1; then
        xdg-open "$1" >/dev/null 2>&1
    elif command -v open >/dev/null 2>&1; then
        open "$1" >/dev/null 2>&1
    else
        printf 'Open %s in your browser.\n' "$1"
    fi
}

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
provider_project="$script_directory/src/FindJobWorkspace.Provider"
build_directory="$script_directory/build"
provider_dll="$build_directory/FindJobWorkspace.Provider.dll"

if [[ $force == true || ! -f "$provider_dll" ]]; then
    dotnet publish "$provider_project" --output "$build_directory"
fi

# Single-instance gate for the web UI: a bound port means a server is already
# up, so the caller must not spawn a second copy. (Best-effort like any
# port probe: two truly simultaneous launches can still race.)
ui_url="http://localhost:$port"
if (exec 3<>"/dev/tcp/localhost/$port") 2>/dev/null; then
    printf 'FindJob web UI is already running on %s.\n' "$ui_url"
else
    # Detached: the UI outlives generation (nohup plus background). --no-browser
    # because this script opens the browser itself below, so one launch always
    # means exactly one tab.
    nohup "$script_directory/run-webui.sh" --workspace "$script_directory" --port "$port" --no-browser >"$build_directory/webui.log" 2>&1 &
    printf 'Starting the FindJob web UI in the background on %s.\n' "$ui_url"
fi

# Unconditional: a double-launch reuses the single server yet still opens the
# browser. On a cold start the server takes a while to build; refresh once.
open_browser "$ui_url" >/dev/null 2>&1 &

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
