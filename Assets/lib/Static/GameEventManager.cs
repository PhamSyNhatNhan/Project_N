using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public static class EventManager
{
    public static readonly PlayerEvents Player = new PlayerEvents();
    public static readonly EntityEvents Entity = new EntityEvents();
    public static readonly EnviromentEvents Enviroment = new EnviromentEvents();
    public static readonly UiEvent Ui = new UiEvent();
    public static readonly GameManagerEvent Gm = new GameManagerEvent();
    public static readonly TimeEvents Time = new TimeEvents();
    public static readonly RogueBuffEvents RogueBuff = new RogueBuffEvents();


    public class PlayerEvents
    {
        public class ControllerEvent : UnityEvent<Component> { }
        public GenericEvent<ControllerEvent> PlayerFlipCall = new GenericEvent<ControllerEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );

        public class CombatEvent : UnityEvent<Component, object> { }
        public GenericEvent<CombatEvent> OnPlayerAttack = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnPlayerSkill = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnPlayerUlti = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnPlayerDash = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnPlayerBurst = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnPlayerAttackSpeedChange = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnAttackEnd = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<CombatEvent> OnMoveToEnd = new GenericEvent<CombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
    }

    // ── Entity Events — dùng channel = nameCharacter_instanceID ───
    // object của OnEntityHealthChanged: float healthPercent (0.0 -> 1.0)
    // object của OnEntityDead: null
    public class EntityEvents
    {
        public class EntityHealthEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntityHealthEvent> OnEntityHealthChanged = new GenericEvent<EntityHealthEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );

        public class EntityDeadEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntityDeadEvent> OnEntityDead = new GenericEvent<EntityDeadEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        public class EntitySkillCdReadyEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntitySkillCdReadyEvent> OnEntitySkillCdReady = new GenericEvent<EntitySkillCdReadyEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // Fire khi: apply, remove, stack thay đổi, duration thay đổi đột ngột
        public class EntityEffectChangedEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntityEffectChangedEvent> OnEntityEffectChanged = new GenericEvent<EntityEffectChangedEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // Fire khi load xong avatar — object: Sprite
        public class EntityAvatarLoadedEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntityAvatarLoadedEvent> OnEntityAvatarLoaded = new GenericEvent<EntityAvatarLoadedEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // Fire khi MoveToEnd — object: null
        public class EntityMoveToEndEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntityMoveToEndEvent> OnEntityMoveToEnd = new GenericEvent<EntityMoveToEndEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
    }

    public class EnviromentEvents
    {
        public class InteractiveEvent : UnityEvent<Component, object> { }
        public GenericEvent<InteractiveEvent> TriggerInteractiveEvent = new GenericEvent<InteractiveEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public GenericEvent<InteractiveEvent> EndTriggerInteractiveEvent = new GenericEvent<InteractiveEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
    }
    
    public class UiEvent
    {
        // object là InteractiveUiData
        public class InteractiveUiEvent : UnityEvent<Component, object> { }
        public GenericEvent<InteractiveUiEvent> TriggerInteractiveUiEvent = new GenericEvent<InteractiveUiEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        public class LoadingScene: UnityEvent<Component, object> { }
        public GenericEvent<LoadingScene> TriggerLoadingScene = new GenericEvent<LoadingScene>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
    }
    
    public class TimeEvents
    {
        // ── Broadcast slow đến tất cả entity ─────────────────────────
        // sender: ai gây ra slow (skill, zone, enemy...)
        // object: SlowData
        public class SlowApplyEvent : UnityEvent<Component, object> { }
        public GenericEvent<SlowApplyEvent> OnSlowApply = new GenericEvent<SlowApplyEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );

        public class SlowRemoveEvent : UnityEvent<Component, object> { }
        public GenericEvent<SlowRemoveEvent> OnSlowRemove = new GenericEvent<SlowRemoveEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );

        // ── Pause game ────────────────────────────────────────────────
        // object: bool isPaused
        public class GamePauseEvent : UnityEvent<Component, object> { }
        public GenericEvent<GamePauseEvent> OnGamePause = new GenericEvent<GamePauseEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );

        // ── Notify khi 1 entity thay đổi scale (UI, debug...) ────────
        // object: float newScale
        public class EntityScaleChangedEvent : UnityEvent<Component, object> { }
        public GenericEvent<EntityScaleChangedEvent> OnEntityScaleChanged = new GenericEvent<EntityScaleChangedEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
    }
    
    public class RogueBuffEvents
    {
        public class AddGroupEvent : UnityEvent<Component, object> { }
        public GenericEvent<AddGroupEvent> OnAddGroup = new GenericEvent<AddGroupEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
 
        public class ActivateMinorEvent : UnityEvent<Component, object> { }
        public GenericEvent<ActivateMinorEvent> OnActivateMinor = new GenericEvent<ActivateMinorEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
    }
    
    
    public class GameManagerEvent
    {
        public class GenericEvent : UnityEvent<Component, object> { }
        public GenericEvent<GenericEvent> TriggerGenericEvent = new GenericEvent<GenericEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        
        // object: ShakeData
        public class CameraShakeEvent : UnityEvent<Component, object> { }
        public GenericEvent<CameraShakeEvent> OnCameraShake = new GenericEvent<CameraShakeEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // Room
        public class OnRoomClearedEvent : UnityEvent<Component, object> { }
        public GenericEvent<OnRoomClearedEvent> OnRoomCleared = new GenericEvent<OnRoomClearedEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        
        // object: null 
        public class OpenCharacterSelectEvent : UnityEvent<Component, object> { }
        public GenericEvent<OpenCharacterSelectEvent> OnOpenCharacterSelect = new GenericEvent<OpenCharacterSelectEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        public class OpenMapSelectEvent : UnityEvent<Component, object> { }
        public GenericEvent<OpenMapSelectEvent> OnOpenMapSelect = new GenericEvent<OpenMapSelectEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // object: Stat — fire khi PlayerSpawner spawn xong
        public class PlayerSpawnedEvent : UnityEvent<Component, object> { }
        public GenericEvent<PlayerSpawnedEvent> OnPlayerSpawned = new GenericEvent<PlayerSpawnedEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // object: Sprite (optional) — fire khi mở màn chọn rogue buff
        public class RogueBuffSelectOpenEvent : UnityEvent<Component, object> { }
        public GenericEvent<RogueBuffSelectOpenEvent> OnRogueBuffSelectOpen = new GenericEvent<RogueBuffSelectOpenEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // object: null — fire khi pause
        public class PauseEvent : UnityEvent<Component, object> { }
        public GenericEvent<PauseEvent> OnPause = new GenericEvent<PauseEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
 
        // object: null — fire khi resume
        public class ResumeEvent : UnityEvent<Component, object> { }
        public GenericEvent<ResumeEvent> OnResume = new GenericEvent<ResumeEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // object: null — fire khi player tương tác StartGate để bắt đầu combat
        public class StartCombatEvent : UnityEvent<Component, object> { }
        public GenericEvent<StartCombatEvent> OnStartCombat = new GenericEvent<StartCombatEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
        
        // object: float — thời gian đếm ngược trước khi spawn wave đầu
        public class SpawnCountdownEvent : UnityEvent<Component, object> { }
        public GenericEvent<SpawnCountdownEvent> OnSpawnCountdown = new GenericEvent<SpawnCountdownEvent>(
            (source, dest) => source.AddListener(dest.Invoke)
        );
    }
}

public class GenericEvent<T> where T : UnityEventBase, new()
{
    private Dictionary<string, T> map = new Dictionary<string, T>();
    private T globalEvent = new T();
    private Action<T, T> patchAction;
    
    public GenericEvent(Action<T, T> patchAction)
    {
        this.patchAction = patchAction ?? throw new ArgumentNullException(nameof(patchAction));
    }

    public T Get(string channel = "")
    {
        if (string.IsNullOrEmpty(channel))
        {
            return globalEvent;
        }

        if (!map.ContainsKey(channel))
        {
            map[channel] = new T();
            patchAction(map[channel], globalEvent);
        }

        return map[channel];
    }
}