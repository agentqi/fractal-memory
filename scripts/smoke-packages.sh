#!/usr/bin/env bash
set -euo pipefail

package_dir=${1:?"usage: smoke-packages.sh <package-directory> <version>"}
version=${2:?"usage: smoke-packages.sh <package-directory> <version>"}
audit_root=$(mktemp -d "${TMPDIR:-/tmp}/fractalmem-package-smoke.XXXXXX")
tool_dir="$audit_root/tools"
workspace_dir="$audit_root/workspace"
mkdir -p "$tool_dir" "$workspace_dir"
server_pid=

cleanup() {
  exec 3>&- 2>/dev/null || true
  exec 4<&- 2>/dev/null || true
  if [[ -n "$server_pid" ]]; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  find "$audit_root" -depth -delete 2>/dev/null || true
}
trap cleanup EXIT

dotnet tool install FractalMemory.Cli \
  --tool-path "$tool_dir" \
  --add-source "$package_dir" \
  --version "$version"
dotnet tool install FractalMemory.McpServer \
  --tool-path "$tool_dir" \
  --add-source "$package_dir" \
  --version "$version"

pushd "$workspace_dir" >/dev/null
"$tool_dir/fm" init
"$tool_dir/fm" node create projects/package-smoke
validation_output=$("$tool_dir/fm" validate)
open_output=$("$tool_dir/fm" open projects/package-smoke)
search_output=$("$tool_dir/fm" search "package smoke" --scope projects)
export_output=$("$tool_dir/fm" export projects/package-smoke)

grep -Fq "Validation passed with no issues." <<<"$validation_output"
grep -Fq "Package Smoke (projects/package-smoke)" <<<"$open_output"
grep -Fq "projects/package-smoke" <<<"$search_output"
grep -Fq "title: Package Smoke" <<<"$export_output"
popd >/dev/null

request_fifo="$audit_root/mcp.in"
response_fifo="$audit_root/mcp.out"
mkfifo "$request_fifo" "$response_fifo"
FRACTALMEM_REPOSITORY_ROOT="$workspace_dir" "$tool_dir/fractalmem-mcp" \
  <"$request_fifo" >"$response_fifo" 2>"$audit_root/mcp.stderr" &
server_pid=$!
exec 3>"$request_fifo"
exec 4<"$response_fifo"
cleanup_server() {
  exec 3>&- || true
  exec 4<&- || true
  kill "$server_pid" 2>/dev/null || true
  wait "$server_pid" 2>/dev/null || true
  server_pid=
}

printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"package-smoke","version":"1.0"}}}' \
  >&3
IFS= read -r -t 15 initialize_response <&4
grep -Fq '"serverInfo":{"name":"fractalmem-mcp"' <<<"$initialize_response"

printf '%s\n' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  >&3
IFS= read -r -t 15 tools_response <&4
grep -Fq '"name":"memory_validate"' <<<"$tools_response"
grep -Fq '"name":"memory_handoff_create"' <<<"$tools_response"

printf '%s\n' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"memory_validate","arguments":{}}}' \
  >&3
IFS= read -r -t 15 validation_response <&4
grep -Fq 'hasErrors' <<<"$validation_response"
grep -Fq 'false' <<<"$validation_response"

printf '%s\n' \
  '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"memory_open","arguments":{"path":"projects/package-smoke","depth":3,"view":"State"}}}' \
  >&3
IFS= read -r -t 15 state_response <&4
grep -Fq 'Current Objective' <<<"$state_response"
grep -Fq 'Active Constraints' <<<"$state_response"
grep -Fq 'Next Best Actions' <<<"$state_response"

printf '%s\n' \
  '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"memory_open","arguments":{"path":"projects/package-smoke","depth":999}}}' \
  >&3
IFS= read -r -t 15 invalid_depth_response <&4
grep -Fq '"isError":true' <<<"$invalid_depth_response"

cleanup_server
python3 "$(dirname "$0")/demo.py" --cli "$tool_dir/fm"
python3 "$(dirname "$0")/smoke-workflows.py" "$tool_dir/fm" "$tool_dir/fractalmem-mcp"
printf 'Package smoke test passed for FractalMem %s.\n' "$version"
