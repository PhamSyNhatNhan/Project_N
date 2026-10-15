# Enemy classification

`PNEnemyStat` shares the full `PNStat` stat/combat system. Classification is registered
metadata, not an enum, stat modifier, Ability or automatic combat mechanic.

`PNContentModule` registers the named `enemy_types` registry with:

| Original ID | Public alias | Display name |
| --- | --- | --- |
| project_n:normal | base:normal | Normal |
| project_n:elite | base:elite | Elite |
| project_n:boss | base:boss | Boss |

Miniboss uses Boss. All three public bindings are locked against overrides. Trusted
code modules can register additional `PNEnemyTypeDefinition` entries during startup;
registrations seal with the session and participate in compatibility fingerprints.

## Character content and runtime

```json
{
  "schemaVersion": 1,
  "characters": [{
    "id": "my_mod:enemy",
    "data": { "enemyTypeId": "base:elite" },
    "stats": [{ "statId": "base:health", "base": 1000 }],
    "skill": []
  }]
}
```

The example pack must depend on `project_n`. Explicit external references require
the definition/binding owner's dependency, including `base:` references. Unknown,
empty, malformed or non-string `enemyTypeId` values fail content validation before
the session is published. Omitted metadata or an omitted field defaults to registered
`base:normal`, preserving old character documents. An explicit null field is invalid.
Character `data` is a game-owned object; other metadata fields are reserved for their
own game parsers. The engine does not interpret enemy classification.

`EveCharacterDefinition.Data` uses defensive JSON copies, matching skill payloads.
Existing constructors remain available; the new five-argument constructor accepts
code-authored metadata. Non-JSON payload objects must be immutable; Project_N's
character parser currently requires a JObject. `EveICharacterContentReferenceValidator`
lets game modules validate character references during the JSON batch, before commit.
`PNContentModule.ValidateContent` also validates code-registered characters after seal.

`PNEnemyStat.EnemyTypeId` exposes the canonical original ID; `EnemyType` exposes the
registered definition. Both require initialized classification. There is no setter
or runtime reclassification API. Enemy initialization resolves classification before
initializing stats; Loader reaches Ready only after the load steps succeed. Repeated
Ready Load does not reset HP or classification. Unload clears the cached definition;
the next Load resolves its character content again. To change type during a fight,
spawn a different enemy. Direct `Initialize(PNStatSet)` has no character metadata and
therefore defaults to registered Normal. All paths require the classification registry.

```csharp
hub.EnemyTypes().Get(EveId.Parse("base:elite"));
hub.EnemyTypes().Register(new PNEnemyTypeDefinition(EveId.Parse("my_mod:champion"), "Champion"));
// Register only in a content module's Register hook, before sealing.
var typeId = enemy.EnemyTypeId;
```

Buffs, reward counters and game modes may read classification later. No scaling,
shield, immunity, stagger, damage bonus or default hit reaction is attached to it.
Scaling is deferred until gameplay work. Shield/break and Buff rules remain undecided.
Player classification is not inferred from character metadata: only PNEnemyStat
exposes this API. Dummy JSON explicitly declares Normal; authored scenes are unchanged.

## Verification

Standalone coverage: `Tests/EveEngine/PNEnemyTypeTests.cs` in the smoke suite, including
aliases, locking, custom definitions, invalid data/dependencies, atomic JSON rejection,
defensive copies, compatibility hashes, entity loading and cleanup.
Native: `PNEnemyTypeEditorChecks.Run()` uses a disposable preview scene and real base
content; it verifies Dummy, Elite, Boss, legacy fallback, entity isolation, unchanged
stats/player behavior, idempotent Load, unload/reload and invalid initialization.

Executable RulesVersion is `project_n:42`; DLL Mod API remains 2 (additive APIs).
