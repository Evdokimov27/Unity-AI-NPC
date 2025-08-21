// SimpleLever.cs
// Example interactable: a lever that plays an animation and fires events when pulled.

using System;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class SimpleLever : MonoBehaviour, IInteractable
{
    [Header("Animation")]
    [Tooltip("Animator with a Trigger parameter to play the pull animation (optional).")]
    public Animator animator;

    [Tooltip("Name of the Animator Trigger parameter to activate on pull.")]
    public string triggerName = "Pull";

    [Tooltip("Seconds to wait before completing the interaction (match animation length).")]
    public float interactionDuration = 0.8f;

    [Header("State")]
    [Tooltip("If true, lever can be pulled only once.")]
    public bool oneShot = false;

    [Tooltip("Current pulled state.")]
    public bool isPulled = false;

    [Header("Events")]
    public UnityEvent onPull;
    public UnityEvent onPullAgain; // fired if pulled again when oneShot == false

    public string GetDefaultVerb() => "дернуть";

    public bool CanInteract(GameObject actor)
    {
        if (oneShot && isPulled) return false;
        return true;
    }

    public void Interact(GameObject actor, Action<bool> onComplete)
    {
        // Update state
        bool firstTime = !isPulled;
        isPulled = true;

        // Play animation if present
        try
        {
            if (animator && !string.IsNullOrEmpty(triggerName))
                animator.SetTrigger(triggerName);
        }
        catch { /* non-fatal */ }

        // Fire events
        try
        {
            if (firstTime) onPull?.Invoke();
            else onPullAgain?.Invoke();
        }
        catch { /* user event errors shouldn't hard-fail */ }

        // Finish after delay (simulate animation time)
        if (interactionDuration > 0f)
        {
            StartCoroutine(CompleteLater(onComplete, true, interactionDuration));
        }
        else
        {
            onComplete?.Invoke(true);
        }
    }

    private System.Collections.IEnumerator CompleteLater(Action<bool> cb, bool result, float delay)
    {
        yield return new WaitForSeconds(delay);
        cb?.Invoke(result);
    }
}
