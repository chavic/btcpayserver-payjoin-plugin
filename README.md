# BTCPay Server # BTCPay Server Payjoin Plugin

A [BTCPay Server](https://github.com/btcpayserver) plugin that adds [Async Payjoin (BIP 77)](https://github.com/bitcoin/bips/blob/master/bip-0077.mediawiki) support to the checkout flow. The plugin uses C# bindings to the Rust [Payjoin Dev Kit](https://github.com/payjoin/rust-payjoin) generated via UniFFI.

> [!WARNING]
> This plugin is under active development and is intended for demo/testing purposes. Do not use in production with real funds.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Rust 1.85+](https://rustup.rs/)
- Git

## Getting Started

### Cloning the Project

```sh
git clone --recurse-submodules https://github.com/ValeraFinebits/btcpayserver-payjoin-plugin
cd btcpayserver-payjoin-plugin
```

### Building

The plugin build auto-generates the C# bindings and native FFI library when the
tracked Rust inputs change:

```sh
dotnet restore BTCPayServer.Plugins.Payjoin.sln
dotnet build BTCPayServer.Plugins.Payjoin.sln -c Release
```

Manual fallback:

```sh
cd rust-payjoin/payjoin-ffi/csharp
bash ./scripts/generate_bindings.sh
cd ../../..

dotnet build BTCPayServer.Plugins.Payjoin.sln -c Release -p:PayjoinAutoGenerateNativeBindings=false
```

### Running Tests

```sh
dotnet test BTCPayServer.Plugins.Payjoin.sln -c Release
```

### Local BTCPay Plugin Loop

If you want BTCPay Server to load the current local plugin build, you still need
to stage the plugin into BTCPay's external plugin directory and restart BTCPay.
The repository includes helper scripts for that:

Unix shells:

```sh
dotnet build BTCPayServer.Plugins.Payjoin/BTCPayServer.Plugins.Payjoin.csproj -c Debug
bash ./scripts/stage-local-plugin.sh --configuration Debug
```

PowerShell:

```powershell
dotnet build .\BTCPayServer.Plugins.Payjoin\BTCPayServer.Plugins.Payjoin.csproj -c Debug
powershell -ExecutionPolicy Bypass -File .\scripts\stage-local-plugin.ps1 -Configuration Debug
```

By default the staging scripts follow BTCPay's standard plugin directory
convention for the current OS. If you want the script to inspect a specific
BTCPay config file or your local instance overrides `plugindir`, pass either:

- `--plugins-root <path>` / `-PluginsRoot <path>`
- `--settings-config <path>` / `-SettingsConfig <path>`

The scripts replace the staged Payjoin plugin directory so stale local artifacts
do not survive between rebuilds. Restart BTCPay after staging to load the new
plugin bits.

## Related Links

- Payjoin Rust implementation: https://github.com/payjoin/rust-payjoin
- BTCPay Server plugin development docs: https://docs.btcpayserver.org/Development/Plugins/

## Licence

[MIT](LICENSE) 
