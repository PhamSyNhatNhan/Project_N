# Nhonn

A modular, data-driven 2D action roguelike developed with Unity and C#. The project focuses on reusable combat systems, configurable entities, multi-stage encounters, persistent dungeon runs, and extensible status and upgrade mechanics.

> **Repository scope:** this snapshot includes the Unity `Assets` and `ProjectSettings` directories, C# source code, JSON configuration, scenes, prefabs, and Unity `.meta` files. It does not include `Packages/manifest.json` or the referenced binary art, animation, and audio resources, so additional restoration and package setup may be required before the project runs correctly.

## Key Features

- **Data-driven entities:** player and enemy statistics, movement settings, and skills are loaded from JSON files in `Resources`.
- **Reusable entity architecture:** shared `Stat`, `Move`, and `Skill` base components support specialized players, enemies, and bosses.
- **Character-specific combat:** separate controller and skill implementations for multiple playable characters.
- **Enemy and boss encounters:** regular enemies, projectile patterns, arena control, and multi-phase boss behavior.
- **Dungeon progression:** configurable floors, enemy waves, shops, bosses, stat scaling, scene transitions, and run-state persistence.
- **Roguelike upgrades:** Warrior, Mage, and Assassin upgrade groups with minor upgrades, major unlocks, and save/restore support.
- **Status-effect system:** pooled and configurable effects, including burn, poison, bleed, freeze, shock, slow, shield, and immortality.
- **Projectile and hitbox framework:** reusable bullets, projectiles, lasers, and box, circle, ellipse, sector, and custom polygon hitboxes.
- **Event-driven communication:** centralized event channels reduce direct dependencies between gameplay, UI, dungeon, time, and projectile systems.
- **Time control:** pause, entity-specific time scaling, time-stop zones, and related visual effects.
- **Game UI:** character selection, health and skill displays, status-effect indicators, map selection, run results, pause screens, character information, and gacha interfaces.
- **Desktop and gamepad input:** Unity Input System actions for movement, dash, attack, skill, ultimate, and burst.

## Architecture Overview

```text
Assets/
├── Entity/
│   ├── Character/       # Player controllers and character-specific skills
│   ├── Enemy/           # Enemy and multi-phase boss behavior
│   ├── Buff/            # Normal status effects and roguelike upgrades
│   ├── Obj/             # Projectiles, lasers, meteors, and combat objects
│   └── Core/            # JSON entity loading and shared data models
├── GameManager/         # Game modes, camera, arena, and global managers
├── Enviroment/          # Interactive objects, gates, and environmental VFX
├── Scenes/              # Start, hall, combat, shop, and boss scenes
├── Settings/            # Render-pipeline and project assets
├── Pack/                # Imported UI and visual-effect prefabs
├── lib/
│   ├── Entity/          # Base stat, movement, skill, and ability systems
│   ├── RoomSystem/      # Dungeon flow, spawning, shops, and run persistence
│   ├── RougeBuff/       # Upgrade groups, registries, and save data
│   ├── Skill/           # Hitboxes and reusable skill objects
│   ├── Static/          # Shared enums and event channels
│   ├── Time/            # Time scale, pause, and time-stop mechanics
│   └── User/            # Persistent user data
├── Resources/           # JSON configuration for dungeons and entities
└── Ui/                  # Runtime, hall, character, gacha, and combat UI
ProjectSettings/         # Unity editor, build, graphics, physics, and input settings
```

## Core Design

### Data-driven entity loading

`EntityLoader` reads JSON configuration from `Resources`, passes raw data to components implementing `ILoadable<T>`, and applies the configuration at runtime. This separates balance data from character and enemy behavior, allowing statistics and skill parameters to be adjusted without rewriting the corresponding gameplay class.

### Dungeon run flow

`DungeonFlowManager` coordinates a run across scenes. It loads a dungeon configuration, tracks the current floor and room type, preserves player state and acquired upgrades, and handles progression after combat, shop, and boss rooms.

Dungeon JSON files define:

- Floor order and room type
- Scene names
- Enemy waves and spawn delays
- Boss selection
- Per-floor stat scaling

### Status effects

The status-effect system separates effect definitions from entity management. `StatusEffectFactory` registers effect constructors and reuses inactive instances through object pooling. The manager supports simple effects, independently stacking effects, and milestone-based stackable effects.

### Roguelike upgrades

`RogueBuffManager` manages active upgrade groups on the player. Upgrade state can be activated directly or through the event system, serialized into run data, and restored when a run continues.

### Event system

`EventManager` exposes typed event groups for player actions, entities, UI, dungeon flow, time control, upgrades, and projectiles. Systems communicate through these channels instead of relying only on direct object references.

## Technologies and Packages

- Unity Input System
- Cinemachine
- Universal Render Pipeline
- TextMesh Pro
- Newtonsoft Json.NET for Unity
- Unity UI and EventSystem
- Unity Adaptive Performance settings
