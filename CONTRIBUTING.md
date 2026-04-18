# Contributing to PocketMC

Thank you for your interest in contributing to PocketMC! This guide will help you get started with the development environment and contribution process.

## 🛠️ Development Setup

### Prerequisites

- **.NET 8 SDK**
- **Visual Studio 2022** (with "Desktop development with .NET" workload) or **JetBrains Rider**
- **Windows 10 1809+** or **Windows 11**

### Building from Source

1. Clone the repository:

   ```bash
   git clone https://github.com/PocketMC/pocket-mc-windows.git
   cd pocket-mc-windows
   ```

2. Restore dependencies and build:

   ```bash
   dotnet build
   ```

3. Run the desktop application:

   ```bash
   dotnet run --project PocketMC.Desktop
   ```

### Packaging (Velopack)

PocketMC uses **Velopack** for updates and installation. To create a release package locally:

1. Install the Velopack CLI:

   ```bash
   dotnet tool install -g vpk
   ```

2. Build the project in Release mode and publish:

   ```bash
   dotnet build -c Release
   dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj -c Release -r win-x64 --self-contained false -o publish
   ```

3. Pack the release:

   ```bash
   vpk pack --packId PocketMC --packVersion 1.4.3 --packDir publish --mainExe PocketMC.Desktop.exe
   ```

## 🧪 Testing

We use xUnit for unit testing. Please ensure all tests pass before submitting a Pull Request.

Run tests via CLI:

```bash
dotnet test
```

Key areas to cover in tests:

- **Process Lifecycle**: Crash recovery, orphan process cleanup, and graceful shutdowns.
- **Path Safety**: Path traversal prevention in mod/plugin imports.
- **Provisioning**: JRE and PHP runtime download and isolation.

## 📜 Contribution Guidelines

1. **Fork and Branch**: Always create a new branch from `main` for your changes.
2. **Atomic Commits**: Keep your commits focused on a single logical change.
3. **Draft PRs**: Feel free to open a Draft PR if you want early feedback on an implementation.
4. **Issue First**: For significant architectural changes, please open an issue to discuss the approach first.

## 🏗️ Project Structure

- `PocketMC.Desktop`: The main UI layer (WPF with WPF-UI).
- `PocketMC.Desktop/Features`: Core business logic organized by feature (Console, Instances, Tunnel, etc.).
- `PocketMC.Desktop/Composition`: Dependency injection and service registration.
- `PocketMC.Desktop.Tests`: Unit and integration tests.

## ⚖️ License

By contributing, you agree that your contributions will be licensed under the MIT License.
