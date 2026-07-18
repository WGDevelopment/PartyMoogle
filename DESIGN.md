# PartyMoogle — Design

A Dalamud plugin that sends **per-event, per-channel** push notifications for a wide
range of FFXIV events, to **ntfy** and/or **Discord**. Fork of
[PushyFinder](https://github.com/lostkagamine/PushyFinder) (MIT, © 2023 Luna@Nightshade);
original copyright retained in `LICENSE`.

## What's different from PushyFinder

PushyFinder notifies for a fixed set (party join/leave, duty pop) to whatever channels
are configured, gated on AFK. PartyMoogle generalizes that into:

- **A catalog of events**, each individually enable-able.
- **Per-event channel routing** — each event can go to ntfy, Discord, both, or neither.
- **Per-event filters** — e.g. chat notifications filtered by channel (Tell/Party/FC/LS)
  and optional keyword regex.
- **One global "away-only" gate** — notifications only fire when you're away
  (AFK status **or** the game window is unfocused/minimized). Single toggle, no per-event
  presence override (by design).

## Architecture

```
Event Sources ──▶ Notifier ──▶ Presence Gate ──▶ Router ──▶ Delivery
  (listeners)      (per-event    (global           (rule.Channels)  (ntfy / Discord)
                    rule lookup)  away-only)
```

- **Event Sources** (`Impl/*Listener.cs`) — one class per source group. Each subscribes
  to a Dalamud event (or existing poll), extracts a title/body, and calls
  `Notifier.Fire(kind, title, body)`. Source-specific filtering (chat channel, keyword)
  is applied in the listener before firing.
- **Notifier** (`Notification/Notifier.cs`) — looks up the event's `EventRule`; if enabled
  and the presence gate passes, forwards to delivery with the rule's channel set.
- **Presence Gate** (`Util/PresenceGate.cs`) — `AwayOnly` off ⇒ always pass; on ⇒ pass
  only when `CharacterUtil.IsClientAfk()` **or** `WindowUtil.IsGameUnfocused()`.
- **Router / Delivery** (`Delivery/MasterDelivery.cs`) — `Deliver(title, text, channels)`
  fires each active `IDelivery` whose `Channel` is in the mask.

## Config model (`Configuration.cs`)

- `Dictionary<EventKind, EventRule> EventRules` — the per-event config.
- `EventRule { Enabled, Channels, ChatTypes, KeywordRegex, Threshold }`.
- Globals: `AwayOnly`, ntfy (`NtfyServer/Topic/Token`), Discord (`DiscordWebhookToken`,
  `DiscordMessage`, `DiscordUseEmbed`, `DiscordEmbedColor`).
- `EnsureDefaults()` seeds sensible rules on first run and migrates PushyFinder config
  (`EnableForDutyPops` → `DutyPop`, `IgnoreAfkStatus` → `!AwayOnly`).

## Event catalog (`Notification/EventKind.cs` + `EventCatalog`)

| Phase | Kinds |
|---|---|
| **1 (done)** | DutyPop, DutyStarted, DutyWiped, DutyRecommenced, DutyCompleted, PartyJoin, PartyLeave, PartyFull, Chat, Login, Logout, LevelUp, JobChange, ZoneChange, EnterPvP, LeavePvP |
| **2 (TODO — polls)** | LowHp, CombatEnd, CutsceneEnd, GilThreshold, MarketSold, InventoryFull |
| **3 (TODO — hooks/addons)** | ReadyCheckStarted, RetainerVentureDone, PartyInvite, TradeRequest |

Each kind has `EventMeta { Display, Category, Filter }` where `Filter ∈ {None, Chat,
Threshold}` tells the config UI which extra widgets to draw.

## Scope decisions (locked with user)

- Base: fork PushyFinder. Channels: **ntfy + Discord only** (Pushover/Simplepush removed).
- Routing: per-event + filters. Presence: global away-only (away = AFK OR unfocused).
- Distribution: target the official Dalamud repo. Implications:
  - Keep MIT LICENSE + Luna's copyright; add project attribution.
  - Prefer `IAddonLifecycle` over raw sig-hooks for Phase 3 (patch-resilient).
  - Observe-and-notify only — never auto-act (e.g. never auto-confirm ready checks).
  - `InternalName` = `PartyMoogle` (finalize before submission; changing it post-publish
    orphans user configs).

## Build / verify

Requires the Dalamud dev environment (`DALAMUD_HOME` or `~/.xlcore/dalamud/Hooks/dev`)
to compile against the API assemblies. Not buildable in a bare checkout.
Open `PartyMoogle.sln` in an environment with Dalamud installed, or `dotnet build`
with `DALAMUD_HOME` set.

## Open items before publish

- Finalize manifest `Author`, `PackageProjectUrl`, icon asset.
- Verify `IChatGui.ChatMessage` delegate signature against the pinned Dalamud SDK.
- Phase 2/3 listeners.
- Config UI polish (per-event rules table).
