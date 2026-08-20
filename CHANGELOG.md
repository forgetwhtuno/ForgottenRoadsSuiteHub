# Changelog

## 0.5.5 - Markup-free discovery chat presentation

**Fixed**

- Forgotten Roads discovery lines now emit ordinary visible text through the typed native
  `ChatLogLine` SystemMessages path. Color is supplied separately from verified native chat
  metadata; named color tokens and rich-text markup are never embedded in the payload.

## 0.5.4 - Forgotten Roads discovery hint

**Added**

- A one-time Forgotten Roads discovery chat hint, printed a few seconds after a character genuinely
  reaches stable gameplay, listing every currently installed Forgotten Roads module and its primary
  command(s)/launcher. Fires once per gameplay session: a later zone transition does not repeat it,
  but returning to character select and loading a different character starts a new session that may
  show it again.
- `/frhelp`, an on-demand chat command that prints the same installed-module help at any time,
  registered through Hub's existing `TypeText.CheckCommands` interception alongside `/mods` and
  `/suitehub`.
- `Discovery.Enabled` config toggle (default on) controlling both the automatic hint and `/frhelp`.

**Notes**

- Every command hint was read directly from that sibling module's own current chat-command parser
  (see `src/ForgottenRoadsDiscoveryCatalog.cs`) - never guessed, never carried over from docs. Party
  Tools' hint uses `/rollparty`, not `/partyroll`. Modules with no chat command (Journal, Deep Sims,
  Crafting, Contracts, Guild Life, Nemesis) show their launcher/panel instead of an invented command;
  Crafting's `/craftdiag` diagnostic surface is deliberately excluded as a developer tool, not a
  player feature.
- The hint only names modules Hub can positively prove are installed - disk discovery merged with
  live Aura registration, the same evidence the rest of Hub's UI already uses.
- The message is capped at two lines regardless of how many modules are installed.
- Chat presentation is runtime-observed, never guessed. A passive Harmony postfix on the native
  `UpdateSocialLog.LogAdd(ChatLogLine)` entry point (the same observation shape Nemesis already
  uses) learns what the running Erenshor build actually supplies as a `SystemMessages`
  `ColorString`, and only a literal hex value is accepted as a usable style. A named colour token
  the current build's TextMeshPro does not recognise is rendered as visible literal markup, which
  is why no name is hardcoded any more. Until a safe native style has been observed, Hub emits a
  plain default-coloured line - correct text always wins over colour. Both the automatic hint and
  `/frhelp` go through the single presentation helper in `src/ForgottenRoadsChatStyle.cs`, so there
  is exactly one `ChatLogLine` construction site in the whole plugin.

## 0.5.3 and earlier

See prior source history for the RC discoverability/camera-containment and earlier work; this file
starts tracking entries from 0.5.4 onward.
