# Network tuning decisions — 2026-10-23

The recommended registry set now contains only the two existing Windows Game Mode settings. Global TCP/MMCSS changes and disabling recording are opt-in. This is a conservative product default, not a claim that Game Mode reduces network latency. Saved user selections and every existing tweak ID remain valid; normalization does not replace them with the new recommended set.

New settings keep the DNS cache when Fusion Mode starts. Existing `FlushDnsOnEngage` preferences are preserved. DNS flush remains available for troubleshooting: the cache speeds repeated lookups, while clearing it discards those entries. [Microsoft ipconfig documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ipconfig).

The descriptions and defaults follow these documented limits:

- `TcpAckFrequency=1` increases ACK frequency. Microsoft advises evaluating the environment before changing the default. This is a TCP behavior, with no fixed reduction in ping. [TCP acknowledgment behavior](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/registry-entry-control-tcp-acknowledgment-behavior).
- Nagle is controlled by `TCP_NODELAY` on a TCP socket. The legacy per-interface registry value is not presented as a verified override for modern applications. UDP is unaffected. [Winsock TCP options](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-tcp-socket-options), [Socket.NoDelay](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.nodelay).
- MMCSS applies to registered multimedia threads. Its GPU and SFIO priority fields are unused, and the High scheduling category is intended for Pro Audio. [MMCSS documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service).
- QoS policy limits application bandwidth reservations; setting it to zero does not unlock a fixed hidden percentage of Internet speed. [QoS policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-qos#qosnonbesteffortlimit).
- Local adapter latency tuning cannot reduce time in transit across the Internet. Preserve Windows offload/autotuning defaults unless measurements identify a specific problem. [Network adapter performance tuning](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-performance-tuning-nics).
- Port exhaustion requires diagnosis; modern dynamic ranges are configured separately for TCP and UDP. [Windows client port exhaustion](https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/tcp-ip-port-exhaustion-troubleshooting).
- Hardware GPU scheduling depends on GPU and driver support; it is not network optimization. [Microsoft DirectX explanation](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/).

The existing `power-throttling` ID writes to a legacy path which differs from the documented `Power\PowerThrottling` policy path. It is retained, labelled as legacy, and excluded from recommendations. Changing its path under the same ID would make old journal entries restore into the wrong key: journals store the ID and previous value, while `RegistryTweakService.RestorePrevious` resolves the current catalog path. A future implementation needs explicit journal compatibility before changing this behavior. [Documented policy mapping](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-power#powerthrottlingturnoff).

Tests guard conservative new defaults, preservation of saved opt-ins/master switches/DNS flushing, and continued availability of all legacy IDs for restoration. These checks do not establish a performance gain on a particular ISP, router, game or adapter.
