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

For local development, follow BTCPay's documented plugin-reference flow instead
of copying builds into the external plugin directory.

Add the plugin project to the BTCPay solution once:

```sh
cd btcpayserver
dotnet sln btcpayserver.sln add ../BTCPayServer.Plugins.Payjoin/BTCPayServer.Plugins.Payjoin.csproj -s Plugins
```

Then create `btcpayserver/BTCPayServer/appsettings.dev.json` with an absolute
path to the local Debug build:

```json
{
  "DEBUG_PLUGINS": "/absolute/path/to/btcpayserver-payjoin-plugin/BTCPayServer.Plugins.Payjoin/bin/Debug/net8.0/BTCPayServer.Plugins.Payjoin.dll"
}
```

After that, rebuild BTCPay in Debug so the plugin project is rebuilt as part of
the solution:

```sh
dotnet build btcpayserver/btcpayserver.sln -c Debug
```

Restart BTCPay to reload the updated plugin bits. `DEBUG_PLUGINS` is only read
by BTCPay Debug builds, so this is a local development workflow rather than a
replacement for packaged plugin installation.

## Related Links

- Payjoin Rust implementation: https://github.com/payjoin/rust-payjoin
- BTCPay Server plugin development docs: https://docs.btcpayserver.org/Development/Plugins/

## Licence

[MIT](LICENSE) 
