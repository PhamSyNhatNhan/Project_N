#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// The transition screen a cycle (or the loading handoff) uses: chain transfer request > world policy > mode > default, a missing candidate
// falls to the next. One place for the controller (inside a play) and the handoff (before the play exists), so both pick the same screen
// from the same registry. Only the registry and plain ids are needed, so it works with the content registry before any session starts.
public static class PNTransitionResolver
{
    public readonly struct Choice
    {
        public readonly PNTransitionScreenDefinition Definition;
        public readonly PNTransitionPayload Payload;
        public Choice(PNTransitionScreenDefinition definition) { Definition = definition; Payload = definition.Payload; }
    }

    // worldDefinition: the world the screen leads to, when known (invalid id = no world policy). modeId: the selection name (e.g. "roguelite", "hall").
    // `failure` collects the first problem; the default screen being absent is the only exception thrown.
    public static Choice Resolve(EveRegistryHub registry, EveId request, EveId worldDefinition, string modeId, PNNetworkProfile network, out string failure)
    {
        failure = null;
        var screens = registry.Get<PNTransitionScreenDefinition>();
        EveId worldScreen = default, modeScreen = default;
        try
        {
            if (worldDefinition.IsValid && registry.Get<PNWorldDefinition>().TryGet(worldDefinition, out var world)) worldScreen = world.Policy.TransitionScreen;
        }
        catch (Exception error) { Debug.LogException(error); }
        try { if (!string.IsNullOrEmpty(modeId)) modeScreen = PNModeDefinition.Select(registry, modeId, network ?? new PNNetworkProfile()).TransitionScreen; }
        catch (Exception error) { failure = error.Message; }
        foreach (var id in PNTransitionSelector.Chain(request, worldScreen, modeScreen))
        {
            if (screens.TryGet(id, out var definition)) return new Choice(definition);
            failure = "Transition screen is not registered: " + id; Debug.LogWarning(failure);
        }
        throw new InvalidOperationException("Default transition screen is missing from the content registry.");
    }
}
#endif
