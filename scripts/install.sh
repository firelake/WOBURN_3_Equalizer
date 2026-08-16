#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
install_dir="${HOME}/.local/share/marshall-wake"
unit_dir="${HOME}/.config/systemd/user"
unit_file="${unit_dir}/marshall-wake.service"

command -v dotnet >/dev/null || {
  echo ".NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
}
command -v bluetoothctl >/dev/null || {
  echo "BlueZ is required. Install the bluez package first." >&2
  exit 1
}

case "$(uname -m)" in
  x86_64) runtime="linux-x64" ;;
  aarch64|arm64) runtime="linux-arm64" ;;
  *)
    echo "Unsupported Linux architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

mkdir -p "${install_dir}" "${unit_dir}"
dotnet publish "${repo_root}/MarshallWake.Service/MarshallWake.Service.csproj" \
  -c Release -r "${runtime}" --self-contained true -o "${install_dir}"

cat > "${unit_file}" <<EOF
[Unit]
Description=Marshall Bluetooth wake service
After=bluetooth.target

[Service]
ExecStart=${install_dir}/MarshallWake.Service
WorkingDirectory=${install_dir}
Restart=on-failure
Environment=ASPNETCORE_URLS=http://127.0.0.1:5050

[Install]
WantedBy=default.target
EOF

systemctl --user daemon-reload
systemctl --user enable --now marshall-wake.service
echo "Marshall Wake is running at http://127.0.0.1:5050"
