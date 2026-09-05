#!/usr/bin/env bash
set -euo pipefail

# Tool-based launcher for the FindJob web UI (bash port of run-webui.ps1):
# template workspaces have no WebUi source, so the UI runs from the
# find-job-webui dotnet tool. The sqlite job store (data/jobs.db) is created
# on first run by the tool itself from these defaults.
#
# Use: run-webui.sh [--workspace DIR] [--port N] [--rebuild-database] [--no-browser]

workspace=""
port=5058
rebuild_database=false
no_browser=false

while (( $# > 0 )); do
    case "$1" in
        --workspace)
            if [[ -z ${2-} ]]; then
                printf "Missing value for '%s'. Use: run-webui.sh [--workspace DIR] [--port N] [--rebuild-database] [--no-browser]\n" "$1" >&2
                exit 2
            fi
            workspace="$2"
            shift 2
            ;;
        --port)
            if [[ -z ${2-} || ! $2 =~ ^[0-9]+$ ]]; then
                printf "Missing numeric value for '%s'. Use: run-webui.sh [--workspace DIR] [--port N] [--rebuild-database] [--no-browser]\n" "$1" >&2
                exit 2
            fi
            port="$2"
            shift 2
            ;;
        --rebuild-database)
            rebuild_database=true
            shift
            ;;
        --no-browser)
            no_browser=true
            shift
            ;;
        *)
            printf "Unexpected argument '%s'. Use: run-webui.sh [--workspace DIR] [--port N] [--rebuild-database] [--no-browser]\n" "$1" >&2
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
if [[ $workspace == "" ]]; then
    workspace="$script_directory"
fi
provider_project="$script_directory/src/FindJobWorkspace.Provider"
build_directory="$script_directory/build"
provider_dll="$build_directory/FindJobWorkspace.Provider.dll"

# Same personal data the CV generator consumes via run.sh.
export PersonalInfo__FirstName='Alex'
export PersonalInfo__LastName='Example'
export PersonalInfo__Profession='Example Software Engineer'
export PersonalInfo__City='Example City'
export PersonalInfo__Country='Example Country'
export PersonalInfo__GitHub='https://example.test/github'
export PersonalInfo__LinkedIn='https://example.test/linkedin'
export PersonalInfo__YouTube='https://example.test/youtube'
export PersonalInfo__Portfolio='https://example.test/portfolio'
# Alternatively, remove these assignments and configure email and phone with
# user-secrets if you do not want to commit real contact information.
export PersonalInfo__Email='alex@example.test'
export PersonalInfo__Phone='202-555-0100'

if [[ $rebuild_database == true || ! -f "$provider_dll" ]]; then
    dotnet publish "$provider_project" --output "$build_directory"
fi

ui_url="http://localhost:$port"
printf 'Starting FindJob web UI on %s (workspace: %s)\n' "$ui_url" "$workspace"

# The server blocks the foreground, so a background poller opens the browser
# once the port answers (a cold build takes a while). The open is unconditional:
# even a start that never becomes healthy surfaces in the browser instead of
# failing silently in this console.
if [[ $no_browser == false ]]; then
    (
        deadline=$((SECONDS + 120))
        while (( SECONDS < deadline )); do
            if (exec 3<>"/dev/tcp/localhost/$port") 2>/dev/null; then
                break
            fi
            sleep 0.5
        done
        open_browser "$ui_url"
    ) &
    browser_poller=$!
    trap 'kill "$browser_poller" 2>/dev/null || true' EXIT
fi

# The sqlite job store (data/jobs.db) is created on first run by the tool
# itself from these defaults. --database points at this workspace's provider
# build because the default ExperienceDatabase.dll name from a real workspace
# does not exist here.
dotnet tool run find-job-webui -- --workspace "$workspace" --database "$provider_dll" --urls "http://localhost:$port"
