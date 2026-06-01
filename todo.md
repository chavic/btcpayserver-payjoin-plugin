# TODO

This file is the ordered execution plan for this repository.

It is meant for contributors working from the plugin repo alone. Everything needed to understand the current direction is summarized here directly.

## Alpha Release Checklist (High-Level)

- [x] session persistence
- [x] checkout model integration and payment toggle
- [x] basic plugin integration-test CI enablement
- [x] receiver poller/service hardening pass
- [x] receiver input reservations
- [x] atomic bootstrap event persistence
- [x] restart/replay integration coverage
- [x] release mode for native FFI package generation
- [ ] accounting bridge and final receiver-output reconciliation
- [ ] Payjoin v2 default/availability UX
- [ ] wallet compatibility behavior for non-v2 senders
- [ ] history and notification parity with normal BTCPay payments
- [ ] payjoin-cli sender/receiver e2e test integrated as an intentional validation path
- [ ] a demo video
- [ ] testnet or mainnet successes

## Project Goal

Build a BTCPay Payjoin plugin that:

- integrates into the normal BTC invoice checkout path
- can be enabled per store
- emits Payjoin-capable payment URLs with safe fallback to plain BIP21
- survives BTCPay restart during an active receiver negotiation
- records payment/accounting state in a way that matches BTCPay expectations
- clearly tells merchants and payers when Payjoin v2 is available, unavailable, used, or bypassed
- is structurally sound enough to keep building on

The current plan is intentionally phased:

- keep the merged foundation stable
- finish receiver accounting, default availability, and compatibility work
- harden automation, diagnostics, and release validation
- extend toward more treasury-oriented behavior later

## Current Baseline

The current baseline is Valera `master`.

The durable receiver-session persistence branch, checkout-model branch, receiver hardening stack, restart replay test, release packaging mode, and template database cleanup have merged upstream. Contributors should treat those features as baseline code now, not as pending feature branches. Current open work should be based on `master` unless a PR explicitly says otherwise.

Recently merged upstream work includes:

- PR #6: receiver-session persistence
- PR #16: external-payer support for RunTestPayment
- PR #20: receiver error handling
- PR #22: checkout model support and payment toggle
- PR #23: PayjoinReceiverPoller refactor
- PR #25: receiver input reservations
- PR #26: atomic bootstrap event persistence
- PR #29: migrations project and EF configuration cleanup
- PR #30: initialized poll timeout handling
- PR #31: template database scaffolding removal
- PR #32: FFI bitcoin type and unit alignment
- PR #33: release FFI packaging mode
- PR #34: restart/replay integration coverage

The remaining open upstream PRs are:

- PR #41: accounting bridge and receiver output reconciliation
- PR #43: enable Payjoin v2 by default
- PR #44: Payjoin v2 overview page

The remaining open upstream issues that matter for release readiness are:

- issue #35: PayJoin availability tracking
- issue #37: enable Payjoin v2 by default
- issue #38: Payjoin v2 invoices fail with wallets that only support v1
- issue #39: output selection for BTCPay Server is magnifying UIH
- issue #42: migrate from Newtonsoft.Json to System.Text.Json for .NET 10 compatibility

## What Is Already Done

These items are complete enough that they should not be reopened without a concrete bug or architecture decision:

- Valera's plugin was adopted as the working base instead of starting greenfield
- durable DB-backed receiver-session persistence was implemented and merged
- receiver sessions persist event history, close-request state, and contributed receiver input identity
- the database is authoritative for receiver-session state
- BTC checkout ownership for Payjoin URL fields lives in BTCPay's payment-method checkout model seam
- the old `checkout-end` mutation path is no longer the source of truth
- checkout model support, the Payjoin/Standard BTC checkout toggle, and Greenfield API Payjoin URLs were merged
- `RunTestPayment` is protected by BTCPay's `CheatModeRoute` pattern
- same-wallet payjoin integration coverage was added
- plugin integration tests are enabled in CI
- receiver polling was decomposed into focused services
- receiver input reservations and bootstrap event persistence are in the mainline
- restart/replay has baseline integration coverage
- FFI bitcoin naming/unit alignment and release package generation support have landed

## Ordered Remaining Work

### 1. Finish Accounting Bridge Review

PR #41 should settle how the plugin accounts Payjoin v2 final transactions when the final receiver output does not look like the original invoice output.

Expected outcome:

- do not create synthetic BTCPay payments from fallback metadata before the final transaction is observed
- use the actual final transaction RBF flag when creating BTCPay payment details
- reconcile the accounted payment to the final Payjoin transaction output
- keep the fallback transaction as bridge metadata unless it is actually observed as the payment path
- make any remaining lifecycle gaps explicit, especially received-payment event/webhook parity and proposal-derived accounting amount

### 2. Review Payjoin v2 Defaults And Availability UX

PR #43 and PR #44 should be reviewed together with issues #35, #37, and #38.

Expected outcome:

- decide whether Payjoin v2 should be enabled by default for stores
- show merchant-facing Payjoin v2 availability/status clearly on the plugin page
- show payer-facing availability or fallback states clearly on checkout
- avoid surprising wallets that only support v1 or plain BIP21
- make unavailable states understandable, such as insufficient merchant UTXOs or unsupported sender capabilities

### 3. Finish .NET 10 Compatibility Cleanup

Issue #42 tracks removing Newtonsoft.Json usage where it blocks .NET 10 compatibility.

Expected outcome:

- migrate relevant plugin JSON handling to System.Text.Json
- keep API payloads and persisted data compatible where required
- add focused tests if behavior changes are possible

### 4. Close History And Notification Parity

The plugin should not silently diverge from BTCPay's normal payment lifecycle.

Expected outcome:

- define what history integration means for alpha release
- make Payjoin v2 received/settled/fallback states visible where merchants expect payment history
- preserve webhook/notification behavior expected from normal on-chain BTC payments
- document any intentional deviations before broader release claims

### 5. Integrate Payjoin-CLI E2E Coverage

The payjoin-cli harness exists, but the full sender/receiver E2E path still needs an intentional validation home.

Expected outcome:

- decide whether this belongs in regular CI, scheduled/manual CI, or documented local validation
- cover sender wallet, receiver invoice, OHTTP relay, and regtest mining requirements
- make failures actionable enough for maintainers to debug without manually reconstructing the whole environment

### 6. Complete Release Validation

Before broader release claims, validate the plugin outside the narrow local development path.

Expected outcome:

- produce a demo video
- complete at least one testnet or mainnet validation pass
- document known release limitations
- decide which remaining issues are alpha blockers versus acceptable follow-up debt

## Follow-Up Work After Alpha Readiness

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
- accounting bridge reconciliation
- Payjoin v2 unavailable/fallback UI states

### 3. Correctness Hardening

Tighten the remaining rough edges in the receiver path:

- seen-outpoint tracking
- better fee-range sourcing
- better receiver-owned script handling
- removal of temporary input-contribution shortcuts
- tighter tracking of truly contributed receiver inputs
- remove any receiver-output fallback to live `invoice.Due` where proposal-derived values are available
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
