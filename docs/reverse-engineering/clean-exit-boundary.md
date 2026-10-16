# Clean-exit native boundary

Status: audited and deliberately not exposed as an MCP mutation.

## Player-visible route

The quit button is `UIQuitButton`. Its only instance field is the rendered
`UnityEngine.UI.Button`; `Awake` owns the listener wiring and `QuitApplication`
(`0x06002507`) is the click callback.

The complete `QuitApplication` body is:

```text
call UnityEngine.Application.Quit
ret
```

`SaveStateManager.QuitGame` (`0x06000702`) has the byte-identical body. Neither method
references a game-defined field or method, starts a scene transition, changes a queryable game
fact, nor raises a save event. The installed contract fixture pins both six-byte bodies and their
single external `UnityEngine.Application.Quit` reference.

## Why there is no `game_quit`

`Application.Quit` returns `void` and exposes no game-written acceptance or transition fact before
the process exits. Calling it from a GameAction therefore has only two possible protocol shapes:

- invoke first, then risk terminating the process before the HTTP worker can deliver a terminal
  response; or
- acknowledge first, then invoke later without a game-written sentinel proving that the requested
  mutation happened.

The first is not a reliable MCP receipt. The second reports a mutation before it executes and
cannot distinguish success from a native no-op. Both violate the boundary rule that one
GameAction returns one verified terminal outcome. The capability is therefore dropped rather than
represented by an unverified acknowledged-then-exit contract.

This decision does not claim anything about the game's independent periodic or shutdown save
behavior. It records only that neither visible/native quit entry point contains an authored
save-before-exit edge, and that process termination cannot furnish a receiptable postcondition.

## Revisit condition

Revisit only if a later game build exposes an authored, game-written shutdown-request state that
can be observed after requesting exit but before termination, or an acknowledged shutdown
protocol whose acceptance is itself the player's requested outcome. Re-audit the UI callback and
`SaveStateManager.QuitGame`; do not infer the new contract from Unity behavior alone.
