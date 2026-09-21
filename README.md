# Novolis Chat

Chat domain contracts, in-memory channel membership, live presence, and an
ASP.NET Core SignalR host facet for small encrypted conversations.

The packages keep protected message bodies opaque. The plaintext inside every
message envelope is a `MarkdownBody`: an unformatted string is still valid
Markdown, and a consumer may render the same source as rich Markdown. Public
conversation metadata, including `ChatBodyFormat.Markdown`, is carried by
`ChatFrame` beside the SecureText envelope. Storage, identity issuance,
authentication, and product policy remain host responsibilities.

Packages:

- `Novolis.Chat.Abstractions` — ids, frames, live events, and media policy.
- `Novolis.Chat.Directory` — named channels and spaces, rosters, devices, and
  approved SecureText group membership.
- `Novolis.Chat.Live` — in-memory presence, typing TTL, and receipt state.
- `Novolis.Chat.Hosting.AspNetCore` — reusable SignalR hub orchestration.

The repository intentionally has no Avalonia dependency. Avalonia controls live
in `novolis-avalonia`, while application composition and scrollback storage
remain in product hosts such as ChannelLab.
