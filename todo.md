# TODO

This file is the ordered execution plan for this repository.

It is meant for contributors working from the plugin repo alone. Everything needed to understand the current direction is summarized here directly.

## Alpha Release Checklist (High-Level)

- [x] session persistence
- [x] checkout model integration and payment toggle
- [x] basic plugin integration-test CI enablement
- [ ] history integration
- [ ] restart/replay integration test
- [ ] payjoin-cli sender/receiver e2e test integrated
- [ ] release decision on the external-payer dev/test flow
- [ ] a demo video
- [ ] mainnet successes

## Project Goal

Build a BTCPay Payjoin plugin that:

- integrates into the normal BTC invoice checkout path
- can be enabled per store
- emits Payjoin-capable payment URLs with safe fallback to plain BIP21
- survives BTCPay restart during an active receiver negotiation
- has automated coverage for the receiver and checkout paths that should not regress
- is structurally sound enough to keep building on

The current plan is intentionally phased:

- close the current foundation work
- harden, automate, and reduce dependency risk
- extend toward more treasury-oriented behavior

## Current Baseline

The current foundation baseline is Valera `master`.

The durable receiver-session persistence branch and the checkout-model branch have both merged upstream. Contributors should treat those features as baseline code now, not as pending feature branches. Current open work should be based on `master` unless a PR explicitly says otherwise.

The remaining open upstream PRs are:

- PR #16: external-payer dev/test payment flow
- PR #20: broader plugin error-handling hardening

The remaining open upstream issues are:

- issue #17: initialized OHTTP long-poll timeout diagnostics
- issue #18: restart during in-flight payjoin receiver-session integration test
- issue #19: old template database scaffolding cleanup

## What Is Already Done

These items are complete enough that they should not be reopened without a concrete bug or architecture decision:

- Valera's plugin was adopted as the working base instead of starting greenfield
- durable DB-backed receiver-session persistence was implemented
- receiver-session persistence was merged upstream in PR #6
- receiver sessions now persist event history and close-request state
- contributed receiver input identity is persisted so replay can resume after restart
- the database is now authoritative for receiver-session state
- BTC checkout ownership for Payjoin URL fields was moved into BTCPay's payment-method checkout model seam
- checkout model support, the Payjoin/Standard BTC checkout toggle, and Greenfield API Payjoin URLs were merged upstream in PR #22
- the old `checkout-end` mutation path is no longer the source of truth
- `RunTestPayment` is protected by BTCPay's `CheatModeRoute` pattern
- same-wallet payjoin integration coverage was added
- plugin integration tests are enabled in CI
- poller policy unit coverage was added
- unit coverage exists for persistence behavior and checkout URL merge behavior

## Ordered Remaining Work

### 1. Resolve Error-Handling Hardening

Finish review and follow-up for PR #20. The key correctness point is that `PayjoinReceiverPoller.ExecuteAsync` must also guard the session-loading boundary, not only per-session processing. `_sessionStore.GetSessions()` can throw before the current per-session `try/catch` runs, especially if plugin DB migration failed or the receiver-session tables are unavailable.

Expected outcome:

- session enumeration failures are logged and contained at the poller tick level
- shutdown cancellation is still handled as shutdown, not logged as a plugin failure
- a focused test covers `GetSessions()` throwing before any session is processed

### 2. Fix Initialized Long-Poll Timeout Diagnostics

Issue #17 tracks receiver sessions that remain in `Initialized` state while polling the OHTTP relay for a sender proposal. A local timeout while waiting for the relay should not produce repeated scary failure logs if it is an expected idle long-poll result.

Expected outcome:

- distinguish expected local poll timeout from real transport or relay failures
- keep real cancellation during shutdown quiet or debug-level
- avoid hiding sender-proposal processing failures after a proposal has actually arrived

### 3. Add Restart/Replay Integration Coverage

Issue #18 should turn the manual restart/replay validation into a repeatable integration test.

Expected outcome:

- start a receiver session for a Payjoin-enabled invoice
- restart or recreate the BTCPay/plugin host while the session is in flight
- verify the session reloads from durable storage
- verify replay can continue deterministically enough for the receiver path
- verify terminal invoice/session cleanup still happens

### 4. Clean Template Database Scaffolding

Issue #19 should remove leftover template database scaffolding that is not part of the payjoin plugin's real data model.

Expected outcome:

- remove unused template entities, migrations, snapshots, factories, or registrations
- keep only payjoin-owned tables and migrations
- verify a clean plugin database migration still initializes correctly

### 5. Decide The External-Payer Dev/Test Flow

PR #16 should be reviewed as dev/test infrastructure, not as user-facing checkout behavior. It should stay aligned with the `CheatModeRoute` pattern introduced by PR #13.

Expected outcome:

- decide whether the external-payer flow should merge now, be narrowed, or be deferred
- keep any test-payment endpoint cheat-mode only
- avoid reintroducing manual `_env.CheatMode` checks inside controller actions if routing can enforce the dev-only constraint

### 6. Integrate Payjoin-CLI E2E Coverage

The payjoin-cli harness exists, but the full sender/receiver E2E coverage is not yet a default automated gate.

Expected outcome:

- decide whether this belongs in regular CI, a scheduled/manual CI job, or documented local validation
- cover the sender wallet, receiver invoice, OHTTP relay, and regtest mining requirements
- make failures actionable enough for maintainers to debug without reproducing the whole setup manually

### 7. Finish History And Release Validation

Before broader release claims, make the merchant-visible history story explicit and validate the plugin outside the narrow local development path.

Expected outcome:

- define what "history integration" means for alpha release
- record payjoin availability, fallback, completion, and failure states where merchants can understand them
- produce a demo video
- complete at least one testnet or mainnet validation pass
- document any known release limitations instead of leaving them implicit

## Follow-Up Work After The Current Foundation Pass

This next phase is about making the current base easier to validate, maintain, and operate.

### 1. Fork Alignment

- inventory the plugin's required `rust-payjoin` FFI surface
- compare that surface against the current local `rust-payjoin` line
- confirm direct close-request support
- confirm receiver outputs or original proposal PSBT access
- confirm selected receiver input identification for `proposal.TryPreservingPrivacy(receiverInputs)`
- confirm selected-input metadata support needed to replace BTCPay-specific persisted metadata
- decide whether alignment should happen immediately or in stages
- execute the chosen alignment plan
- revalidate the plugin after the alignment lands

### 2. Automation

Turn the high-value manual cases into reliable automated coverage:

- stable Payjoin URL generation
- fallback to plain BIP21
- restart-safe receiver replay
- cleanup on invoice state transitions
- successful session completion and removal
- configured cold-change routing

### 3. Correctness Hardening

Tighten the remaining rough edges in the receiver path:

- seen-outpoint tracking
- better fee-range sourcing
- better receiver-owned script handling
- removal of temporary input-contribution shortcuts
- tighter tracking of truly contributed receiver inputs
- remove the receiver-output fallback to `invoice.Due` and source those values from the incoming proposal
- validate that the invoice amount matches the receiver amount in the incoming proposal before replacement outputs are built
- simplify receiver proposal signing, PSBT normalization, and signing-context ownership if the poller flow keeps growing
- clarify the proposal-normalization pipeline if receiver-input finalization and sender-input cleanup continue to diverge

### 4. Diagnostics

Improve observability so failures are easier to understand:

- replay failure reasons
- cleanup reasons
- initialization fallback reasons
- integration-test failure visibility
- merchant-facing failure reasons for payjoin setup, fallback, and cleanup paths
- payer-facing failure reasons when payjoin negotiation is rejected, abandoned, or falls back to plain BIP21
- clearer explanation of when payjoin was unavailable versus when plain BIP21 was intentionally used

### 5. Shipping Cutoff

Decide which remaining issues are must-fix before broader shipping claims and which can remain temporary debt.

- decide whether falling back to unconfirmed merchant coins is acceptable when advertising payjoin availability
- decide whether the OHTTP key cache lifetime should remain fixed or become configurable
- decide whether `RunTestPayment` should remain cheat-mode only or be removed before making broader shipping claims

## Later Treasury-Oriented Work

This later phase is where the plugin should start taking on more treasury-oriented behavior.

Planned work:

- define the minimum acceptable treasury story for this phase
- strengthen cold-output and descriptor-oriented behavior
- clarify hot-wallet versus cold-wallet role boundaries
- improve accounting and reconciliation support where justified
- retire earlier compromises that should not survive treasury-facing usage

## Guardrails

- do not confuse "cold-change routing exists" with "full treasury architecture is solved"
- if you are working without access to planning materials, record meaningful results in PR descriptions, PR comments, or checked-in repo docs so the next contributor does not have to rediscover them
