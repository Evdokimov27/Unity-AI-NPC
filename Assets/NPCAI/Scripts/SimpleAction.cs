using System;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class SimpleAction: MonoBehaviour, IInteractable
{
	[Header("Animation")]
	public Animator animator;

	[Tooltip("Animator trigger to fire on use. Leave empty to use Bool mode below.")]
	public string triggerName = "";

	[Tooltip("If set, lever state is written to this Animator bool parameter each time.")]
	public string boolParamName = "";

	[Header("State")]
	[Tooltip("If true, lever can be pulled only once (stays ON).")]
	public bool oneShot = false;

	[Tooltip("Current ON/OFF state.")]
	public bool isPulled = false;

	[Header("Timing")]
	[Tooltip("Minimal time between uses (seconds). Protects from double-fire).")]
	[Min(0f)] public float cooldown = 0.15f;

	float _lastUseTime = -999f;
	bool _inUse = false; 

	[Header("Events")]
	public UnityEvent onPull;       
	public UnityEvent onPullAgain;  

	public string GetDefaultVerb() => "open";

	public bool CanInteract(GameObject actor)
	{
		if (_inUse) return false;
		if (Time.time - _lastUseTime < cooldown) return false;
		if (oneShot && isPulled) return false;
		return true;
	}

	public void Interact(GameObject actor, Action<bool> onComplete)
	{
		if (!CanInteract(actor)) { onComplete?.Invoke(false); return; }

		_inUse = true;
		_lastUseTime = Time.time;

		bool firstTimeUse = !isPulled;

		if (oneShot)
		{
			isPulled = true;
		}
		else
		{
			isPulled = !isPulled;
		}
		try
		{
			if (animator)
			{
				if (!string.IsNullOrEmpty(boolParamName))
				{
					animator.SetBool(boolParamName, isPulled);
				}
				if (!string.IsNullOrEmpty(triggerName))
				{
					animator.ResetTrigger(triggerName);
					animator.SetTrigger(triggerName);
				}
			}
		}
		catch {  }
		try
		{
			if (firstTimeUse)
			{
				onPull?.Invoke();
			}
			else
			{
				onPullAgain?.Invoke();
			}
		}
		catch { }
		_inUse = false;
		onComplete?.Invoke(true);
	}
}
