// IInteractable.cs
// Simple interface for interactable scene objects.
// Attach a component that implements this interface to any object you want the NPC to use.

using System;
using UnityEngine;

public interface IInteractable
{
    /// <summary>Short verb to show in UI/logs, e.g., "дернуть", "нажать", "открыть".</summary>
    string GetDefaultVerb();

    /// <summary>Whether the actor can currently interact.</summary>
    bool CanInteract(GameObject actor);

    /// <summary>Perform the interaction. Must call onComplete(true/false) when finished.</summary>
    void Interact(GameObject actor, Action<bool> onComplete);
}
