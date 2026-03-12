using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Payjoin.Models;

public class PayjoinStoreSettingsViewModel
{
    public required string StoreId { get; set; }

    public bool EnabledByDefault { get; set; }

    [Required]
    public Uri? DirectoryUrl { get; set; } = PayjoinStoreSettings.DefaultDirectoryUrl;

    [Required]
    public Uri? OhttpRelayUrl { get; set; } = PayjoinStoreSettings.DefaultOhttpRelayUrl;

    [BindNever]
    [ValidateNever]
    public LayoutModel LayoutModel { get; set; } = default!;
}
