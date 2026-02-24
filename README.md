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

Generate the C# bindings from the Rust FFI crate, then build the .NET solution:

```sh
cd rust-payjoin/payjoin-ffi/csharp
bash ./scripts/generate_bindings.sh
cd ../../..

dotnet restore BTCPayServer.Plugins.Payjoin.sln
dotnet build BTCPayServer.Plugins.Payjoin.sln -c Release
```

### Running Tests

```sh
dotnet test BTCPayServer.Plugins.Payjoin.sln -c Release
```

## Related Links

- Payjoin Rust implementation: https://github.com/payjoin/rust-payjoin
- BTCPay Server plugin development docs: https://docs.btcpayserver.org/Development/Plugins/

## Licence

[MIT](LICENSE) 
