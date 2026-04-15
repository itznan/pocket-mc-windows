# Changelog

This file summarizes the PocketMC Desktop release line from `v1.0.0` to `v1.2.4`.

## v1.2.4
- Added Discord/community support in the app and README.
- Added release and packaging guidance for Velopack.
- Updated the release workflow notes to use a repository secret named `RELEASE_PAT`.

## v1.2.3
- Migrated installation and update packaging from Inno Setup to Velopack.
- Added Velopack startup bootstrapping before WPF application startup.
- Added automatic update checks in the shell layer.
- Updated GitHub Actions to publish `win-x64` output, pack Velopack releases, and upload release assets.
- Removed `installer.iss` and updated the build/install documentation.

## v1.0.0
- Initial stable PocketMC Desktop release.
- Core WPF desktop shell for managing Minecraft server instances, dashboard, console, settings, backups, Java setup, Playit.gg tunneling, and notifications.