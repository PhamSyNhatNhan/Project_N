# Portable world persistence

PNWorldSaveEnvelope and PNWorldFileStore are Unity-free bounded data/IO contracts.
Game integration is PNWorldLifetimeController + PNWorldSaveService, reached through
PNGameSession.Saves. See Docs/WorldFoundationContracts.md and the S9/S10 reports for
lifecycle and evidence. Public API additions retain ModAPI6 ABI; rules are project_n:89.

CaptureSnapshot requires a resident authority world with no player/activity/pending
preparation. IPNPortableWorldProvider is optional; borrowed/adopted worlds without it
cannot be saved durably. CaptureLoadDescriptor is pure; validate it before Load, and
use BuildContext.LoadDescriptor to reconstruct seeds/chunks before population.
Actor/world state providers restore through the existing module-access commit scope.

SaveResident keeps source resident by default. unloadAfterCommit=true pins/freezes it
until the immutable disk generation commits, then drains world ownership. SaveCheckpoint
writes a portable snapshot already captured/stored. RestoreSaved strictly checks rules,
selected packs/DLLs/assets/providers/schema, creates a staged new world, remaps references
and publishes only after restore. Cancellation after commit reports the committed save.

IO receives immutable data on workers. Main-thread Tick is called by PNGameSession;
custom test services must Tick themselves. Do not call Unity or provider callbacks in
IO tasks. Cleanup retains pending tasks and temporary imports; retry after IO drains.
Disk root default is persistentDataPath/WorldSaves. Never use the user's save root for tests.
No full-run/account/player ownership save, occupied-world snapshot or schema auto-migration.
