# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

> Status: the project has applied to SignPath Foundation. Until it is approved, releases are unsigned and Windows SmartScreen may warn about them. Check each download against `SHA256SUMS.txt` in its release.

## What gets signed

Only files built by the [Release workflow](.github/workflows/release.yml) on GitHub's servers from a tagged commit in this repository:

- `FusionNetPro-Setup-<version>.exe` (installer)
- `FusionNetPro-<version>.exe` (standalone app)
- `FusionNetPro-<version>-portable.zip`

Nothing built on a personal computer is signed, and no third-party binaries are signed under this project's name.

## Team roles

| Role | Members |
| --- | --- |
| Committers and reviewers | [ma7oods](https://github.com/ma7oods) |
| Approvers | [ma7oods](https://github.com/ma7oods) |

Every signing request is approved manually by an approver. Outside contributions reach `main` only through pull requests reviewed by a committer.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

What it sends, and when:

- **Latency monitor:** while monitoring is on, it sends ICMP ping requests to the targets you choose (by default the public DNS servers `1.1.1.1` and `8.8.8.8`).
- **Public IP:** when you open the DNS page or press refresh there, it asks `api.ipify.org`, `ifconfig.me` and `ipinfo.io` for your public IP address, provider and region, and only shows the answer on screen.
- **DNS benchmark:** when you start a benchmark, it sends test DNS queries for `example.com` to the DNS servers being compared.
- **Optional AI analysis:** off by default. It runs only after you enter your own AI endpoint and API key and start an analysis. It then sends a minimized summary of the local scan to that endpoint. Your key is encrypted with Windows DPAPI on your PC.

It has no telemetry, analytics, crash reporting, advertising or automatic update checks. Settings, logs and the restore journal stay in `%LOCALAPPDATA%\FusionNetPro` on your PC.
