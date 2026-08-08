#!/usr/bin/env bash
# Adapted from AntonC9018/uni_thesisTemplate setup.sh at
# fd89f20731117f379f392ec584a374541ac36704 (MIT License).
# Copyright (c) 2025 Anton Curmanschii
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in all
# copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
# SOFTWARE.

set -euo pipefail

texlive_year=2026
default_root="${HOME}/.local/share/findjobhelper/texlive/${texlive_year}"
install_root="${FINDJOBHELPER_TEXLIVE_ROOT:-$default_root}"
check_only=0
font_version=2.1.5
font_url='https://github.com/liberationfonts/liberation-fonts/files/7261482/liberation-fonts-ttf-2.1.5.tar.gz'
font_sha256='7191c669bf38899f73a2094ed00f7b800553364f90e2637010a69c0e268f25d0'
font_root="${HOME}/.local/share/fonts/findjobhelper/liberation-fonts-${font_version}"
font_marker="$font_root/.archive-sha256"
packages=(babel-romanian xifthen ifmtarg moresize zref needspace multirow wrapfig varwidth environ)
temporary_directories=()

cleanup() {
  local temporary_directory
  for temporary_directory in "${temporary_directories[@]}"; do
    rm -rf -- "$temporary_directory"
  done
}
trap cleanup EXIT

usage() {
  printf 'Usage: %s [--check] [--install-root PATH]\n' "$0"
}

while (($#)); do
  case "$1" in
    --check) check_only=1; shift ;;
    --install-root)
      (($# >= 2)) || { printf '%s\n' '--install-root requires a path.' >&2; exit 2; }
      install_root=$2; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$(uname -s)" in
  Linux) ;;
  *) printf '%s\n' 'This installer supports Linux and WSL only; native Windows is unsupported.' >&2; exit 1 ;;
esac

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'Required host command is missing: %s\n' "$1" >&2
    exit 1
  }
}

find_platform_directory() {
  local candidate
  shopt -s nullglob
  local candidates=("$install_root"/bin/*)
  shopt -u nullglob
  for candidate in "${candidates[@]}"; do
    if [[ -x "$candidate/tlmgr" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  return 1
}

verify_release() {
  local tlmgr=$1
  "$tlmgr" --version | grep -Eq "TeX Live .*${texlive_year}" || {
    printf 'Selected installation is not TeX Live %s: %s\n' "$texlive_year" "$install_root" >&2
    exit 1
  }
}

verify_package() {
  local tlmgr=$1 package=$2
  "$tlmgr" info --only-installed "$package" | grep -Eq "^package:[[:space:]]+${package}$" || {
    printf 'Required TeX Live package is missing: %s\n' "$package" >&2
    return 1
  }
}

verify_font() {
  local family=$1
  fc-match -f '%{family}\n' "$family" | grep -Fq "$family" || {
    printf 'Required font family is unavailable: %s\n' "$family" >&2
    return 1
  }
}

check_installation() {
  require_command fc-match
  local bin_directory
  bin_directory=$(find_platform_directory) || {
    printf 'TeX Live %s executables were not found beneath %s/bin.\n' "$texlive_year" "$install_root" >&2
    exit 1
  }
  verify_release "$bin_directory/tlmgr"
  [[ -x "$bin_directory/latexmk" ]] || { printf '%s\n' 'Required executable is missing: latexmk' >&2; exit 1; }
  [[ -x "$bin_directory/xelatex" ]] || { printf '%s\n' 'Required executable is missing: xelatex' >&2; exit 1; }
  "$bin_directory/latexmk" --version >/dev/null
  "$bin_directory/xelatex" --version >/dev/null
  local package
  for package in "${packages[@]}"; do verify_package "$bin_directory/tlmgr" "$package"; done
  verify_font 'Liberation Serif'
  verify_font 'Liberation Sans'
  printf 'TeX Live root: %s\n' "$install_root"
  printf 'TeX Live platform: %s\n' "${bin_directory##*/}"
  printf 'LaTeX binary directory: %s\n' "$bin_directory"
  printf "Current-session PATH: export PATH='%s':\$PATH\n" "$bin_directory"
}

if ((check_only)); then
  check_installation
  exit 0
fi

require_command perl
require_command tar
require_command sha256sum
require_command fc-cache
require_command fc-match

if [[ -e "$install_root" ]]; then
  if [[ ! -d "$install_root" ]]; then
    printf 'Installation root is not a directory: %s\n' "$install_root" >&2
    exit 1
  fi
  if [[ -n "$(find "$install_root" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    existing_bin=$(find_platform_directory || true)
    [[ -n "$existing_bin" ]] || {
      printf 'Refusing populated non-TeX-Live root: %s\n' "$install_root" >&2
      exit 1
    }
    verify_release "$existing_bin/tlmgr"
  fi
fi

bin_directory=$(find_platform_directory || true)
if [[ -z "$bin_directory" ]]; then
  installer_work_directory=$(mktemp -d)
  temporary_directories+=("$installer_work_directory")
  archive="$installer_work_directory/install-tl-unx.tar.gz"
  if command -v curl >/dev/null 2>&1; then
    curl --fail --location --retry 3 --output "$archive" https://mirror.ctan.org/systems/texlive/tlnet/install-tl-unx.tar.gz
  elif command -v wget >/dev/null 2>&1; then
    wget -O "$archive" https://mirror.ctan.org/systems/texlive/tlnet/install-tl-unx.tar.gz
  else
    printf '%s\n' 'Either curl or wget is required.' >&2; exit 1
  fi
  tar -xzf "$archive" -C "$installer_work_directory"
  installer=$(find "$installer_work_directory" -mindepth 2 -maxdepth 2 -type f -name install-tl -print -quit)
  [[ -n "$installer" ]] || { printf '%s\n' 'TeX Live installer was not found in the archive.' >&2; exit 1; }
  profile="$installer_work_directory/texlive.profile"
  cat >"$profile" <<EOF
selected_scheme scheme-small
TEXDIR $install_root
TEXMFLOCAL $install_root/texmf-local
TEXMFSYSCONFIG $install_root/texmf-config
TEXMFSYSVAR $install_root/texmf-var
TEXMFCONFIG $install_root/texmf-config
TEXMFVAR $install_root/texmf-var
option_doc 0
option_src 0
tlpdbopt_install_docfiles 0
tlpdbopt_install_srcfiles 0
tlpdbopt_autobackup 0
EOF
  perl "$installer" -repository https://mirror.ctan.org/systems/texlive/tlnet -profile "$profile"
  bin_directory=$(find_platform_directory)
fi

verify_release "$bin_directory/tlmgr"
"$bin_directory/tlmgr" install "${packages[@]}"

if [[ ! -f "$font_marker" ]] || [[ "$(<"$font_marker")" != "$font_sha256" ]]; then
  font_work_directory=$(mktemp -d)
  temporary_directories+=("$font_work_directory")
  font_archive="$font_work_directory/liberation-fonts-ttf-${font_version}.tar.gz"
  if command -v curl >/dev/null 2>&1; then
    curl --fail --location --retry 3 --output "$font_archive" "$font_url"
  else
    require_command wget
    wget -O "$font_archive" "$font_url"
  fi
  printf '%s  %s\n' "$font_sha256" "$font_archive" | sha256sum --check --status
  mkdir -p "$font_root"
  tar -xzf "$font_archive" -C "$font_root" --strip-components=1 --wildcards '*/LiberationSerif-*.ttf' '*/LiberationSans-*.ttf'
  printf '%s\n' "$font_sha256" >"$font_marker"
fi
fc-cache -f "$font_root" >/dev/null

printf "export PATH='%s':\$PATH\n" "$bin_directory" >"$install_root/findjobhelper-env.sh"
check_installation
