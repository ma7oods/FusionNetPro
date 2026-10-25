# FusionNetPro

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop, native WPF on .NET 10. Impeccable's web/iOS/Android platform enum does not describe this application; do not misclassify it or apply browser detectors to XAML.

## Users

Arabic-speaking Windows gamers, initially testing VALORANT, who want to understand their connection and background activity without managing many technical settings.

## Product Purpose

Make measured connection and process activity understandable, then let the user review and approve a temporary outbound block of selected eligible background applications during play. Success is an understandable, reversible session and truthful measurements, not a promised reduction in ping.

## Operating Context

The existing application has WPF shell navigation, MVVM, Arabic/English localization, network and DNS diagnostics, optional local security scans and hardware readings. CURRENT_WORK.md records the current source and verified package versions; do not infer release status from product context.

## Capabilities and Constraints

- The user confirmed on 2026-10-24: a gaming overview showing connection and what actually runs, with the sequence scan, review applications, start. Advanced settings stay on a separate page. Remaining design decisions are delegated to the developer.
- Preserve the same application, its DNS diagnosis/apply/restore, game and Riot/Vanguard protections, least privilege, explicit consent, exact Firewall ownership and independent recovery task.
- Read and analyze by default. No automatic blocking, cloud requests, system tuning, deep scans, Defender actions or live installation during development.
- Process traffic is optional ETW sampling; unavailable measurements remain unknown. Interface counters and open connections cannot substitute for bytes. Firewall blocks full outbound access per executable, not speed limiting or PID-only isolation.
- Security findings are evidence-based heuristics with coverage limits. Local analysis works without an API. Cloud AI is optional with data preview, per-request consent and protected keys.
- LibreHardwareMonitor is an external optional WMI integration; no bundled driver/library. Autoruns and Process Explorer are official optional links.

## Brand Commitments

Keep the FusionNetPro name and existing logo. The user rejected the previous interface as the final design and requested Impeccable plus awesome-design-skills for interface work. Preserve clear Arabic and familiar Windows behavior. No new commercial or performance claims.

## Evidence on Hand

The exact functional request is in docs/requirements/GAME_BOOST_AI_SECURITY.md; source architecture and limits are in PROJECT_HANDOFF.md and docs/DEVELOPMENT_STATUS.md. User screenshots and qa/screenshots show prior interfaces. QA captures use clearly synthetic data and cannot prove native Firewall, ETW, installation, API or in-game performance.

## Product Principles

Measured before recommended; reviewed before changed; unknown before invented; reversible before convenient.

## Accessibility & Inclusion

Arabic RTL with legible fonts, LTR paths/addresses/numeric measurements, keyboard access, visible focus, readable contrast and usable compact-window layouts. Status and risk must be conveyed in words as well as color.
