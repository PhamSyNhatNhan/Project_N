# Interactables / Vật tương tác

EN: Kind `interactable` of the shared `instances` registry (`PNInteractableDefinition`, `Docs/InteractablePlan.md`). Done (I1-I4 and the room handlers): the definition, `IPNInteractionHandler` (`CanUse`/`Interact` over `PNInteractionContext`), the game side in `Assets/Game/Interaction/`
(`PNInteractable` on a `PNWorldObject` with the sync block `project_n:interactable` and the command `project_n:interact`, the authority check `PNInteractCommandHandler`, the scanner, the room handlers `room_start`, `room_exit`, `rest_heal`, `shop_heal`) and the list view `Assets/Game/UI/PNRunViews.cs`.
Not done: the client path over the wire (sync transport N3), mobile touch check, a real handler for chests/events.

VI: Kind `interactable` của registry `instances`. Đã có định nghĩa và hợp đồng payload; chưa có component, dịch vụ, nhập, UI danh sách.

## Payload (`data`)

`range` metres in [0.3, 6] (flat distance to the object's anchor) · `permission` `host` | `everyone` (others see the entry dimmed) · `uses` `once` | `repeat` ·
`label` namespaced localization key (<= 128) · optional `icon` Resources path (<= 128) · optional `params` object that only the handler understands. Unknown fields are refused.

The handler is a class implementing `IPNInteractionHandler` (an empty marker until I2) made by the factory the definition was registered with. `params` is checked by the validator given at
registration (`validateParams`); without one `params` must be absent or empty. A JSON `instances` entry derives a new interactable from a registered one:
`{"id": "pack:door_switch", "factoryId": "project_n:room_start", "data": {...}}`.

No handler is registered by the game yet (the room handlers come with step F5 of `Docs/RogueliteFramePlan.md`); the tests register their own.
