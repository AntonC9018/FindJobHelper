#!/usr/bin/env bash

set -euo pipefail

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(cd -- "$script_directory/.." && pwd)
tool_root="$repository_root/.tools"
texlive_root="$tool_root/texlive/2026"
font_root="$tool_root/fonts/liberation-fonts-2.1.5"

"$script_directory/setup-latex.sh" \
  --install-root "$texlive_root" \
  --font-root "$font_root"
