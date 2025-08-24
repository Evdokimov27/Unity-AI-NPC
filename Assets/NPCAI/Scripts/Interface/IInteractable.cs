using System;
using UnityEngine;

public interface IInteractable
{
    string GetDefaultVerb();

    bool CanInteract(GameObject actor);

    void Interact(GameObject actor, Action<bool> onComplete);
}
