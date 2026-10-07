#!/usr/bin/env bash
set -euo pipefail
sudo apt-get update
sudo apt-get install -y --no-install-recommends ffmpeg aria2 curl unzip ca-certificates
tool_dir=$(mktemp -d)
trap 'rm -rf "$tool_dir"' EXIT
curl --fail --location --retry 3 --output "$tool_dir/bento4.zip" \
    https://www.bok.net/Bento4/binaries/Bento4-SDK-1-6-0-641.x86_64-unknown-linux.zip
echo "d48dc6b164941212e5614237b4d9aeff81d4d111ee8b1508892764078a0870e8  $tool_dir/bento4.zip" | sha256sum --check
unzip -q "$tool_dir/bento4.zip" -d "$tool_dir/sdk"
sudo install -m 0755 "$tool_dir/sdk/Bento4-SDK-1-6-0-641.x86_64-unknown-linux/bin/mp4decrypt" /usr/local/bin/mp4decrypt
sudo install -m 0755 "$tool_dir/sdk/Bento4-SDK-1-6-0-641.x86_64-unknown-linux/bin/mp4encrypt" /usr/local/bin/mp4encrypt
for tool in ffmpeg aria2c mp4decrypt mp4encrypt; do
    command -v "$tool"
done
