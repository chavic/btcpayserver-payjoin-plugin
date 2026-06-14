using BTCPayServer.Plugins.Payjoin.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace BTCPayServer.Plugins.Payjoin.Services;

/// <summary>
/// Persistent record of input outpoints the receiver has already seen across payjoin sessions.
/// Backs <c>check_no_inputs_seen_before</c> so the receiver can reject probing attempts and
/// re-entrant payjoins that replay a prior proposal's inputs.
/// </summary>
public sealed class PayjoinSeenInputStore
{
    private readonly PayjoinPluginDbContextFactory _pluginDbContextFactory;
    private readonly IPayjoinUniqueConstraintViolationDetector _uniqueConstraintViolationDetector;

    internal PayjoinSeenInputStore(
        PayjoinPluginDbContextFactory pluginDbContextFactory,
        IPayjoinUniqueConstraintViolationDetector uniqueConstraintViolationDetector)
    {
        ArgumentNullException.ThrowIfNull(pluginDbContextFactory);
        ArgumentNullException.ThrowIfNull(uniqueConstraintViolationDetector);
        _pluginDbContextFactory = pluginDbContextFactory;
        _uniqueConstraintViolationDetector = uniqueConstraintViolationDetector;
    }

    /// <summary>
    /// Records the outpoint as seen and reports whether it had already been recorded before this call.
    /// Returns <c>true</c> when the outpoint was already present (i.e. seen before).
    /// </summary>
    public bool MarkSeenAndWasPresent(string transactionId, long outputIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        using var context = _pluginDbContextFactory.CreateContext();
        var alreadyPresent = context.ReceiverSeenInputs
            .AsNoTracking()
            .Any(x => x.TransactionId == transactionId && x.OutputIndex == outputIndex);
        if (alreadyPresent)
        {
            return true;
        }

        context.ReceiverSeenInputs.Add(new PayjoinReceiverSeenInputData
        {
            TransactionId = transactionId,
            OutputIndex = outputIndex,
            SeenAt = DateTimeOffset.UtcNow
        });

        try
        {
            context.SaveChanges();
            return false;
        }
        catch (DbUpdateException ex) when (IsSeenInputConflict(ex))
        {
            // A concurrent session recorded the same outpoint first; treat it as seen before.
            return true;
        }
    }

    private bool IsSeenInputConflict(DbUpdateException exception)
    {
        return _uniqueConstraintViolationDetector.IsUniqueConstraintViolation(exception, PayjoinPluginDbSchema.ReceiverSeenInputsOutPointIndex);
    }
}
