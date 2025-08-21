using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class InteractAction : MonoBehaviour, IActionStep
{
	public string StepName => "Interact";

	[Header("Behavior")]
	public bool tryAlternativeIfInactive = true;
	public bool failIfNoInteractable = true;

	[Header("Range")]
	[Tooltip("Maximum distance from the actor to the target's collider/position to allow interaction.")]
	public float interactionRadius = 1.2f;
	public bool failIfOutOfRange = true;

	private Action<bool> _onComplete;

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;

		GameObject actor = context?.actor;
		GameObject target = context?.explicitTarget;
		if (!target)
		{
			Finish(!failIfNoInteractable);
			return;
		}

		Type desiredType = GetDesiredInteractableType(target);

		IInteractable interactOn = null;
		GameObject useObject = target;

		if (target.activeInHierarchy)
			interactOn = GetInteractable(target, desiredType);

		if (interactOn == null && tryAlternativeIfInactive)
		{
			interactOn = FindActiveSiblingInteractable(target, desiredType, out useObject);
			if (interactOn == null && HasMeaningfulTag(target))
				interactOn = FindByTag(target.tag, desiredType, actor, out useObject);
			if (interactOn == null && desiredType != null)
				interactOn = FindNearestOfType(desiredType, actor, out useObject);
			if (interactOn == null)
				interactOn = FindNearestAny(actor, out useObject);
		}

		if (interactOn == null)
		{
			Debug.LogWarning($"InteractAction: No suitable interactable for '{target.name}'.");
			Finish(!failIfNoInteractable);
			return;
		}

		if (!IsWithinRange(actor, useObject, interactionRadius))
		{
			Debug.LogWarning($"InteractAction: Too far from '{useObject.name}'. Need <= {interactionRadius:0.##}m.");
			Finish(!failIfOutOfRange ? true : false);
			return;
		}

		if (!interactOn.CanInteract(actor))
		{
			Debug.LogWarning($"InteractAction: Interactable on '{useObject.name}' is not available now.");
			Finish(false);
			return;
		}

		try { interactOn.Interact(actor, ok => Finish(ok)); }
		catch (Exception e)
		{
			Debug.LogError($"InteractAction: Exception during Interact() on '{useObject.name}': {e}");
			Finish(false);
		}
	}

	public void Tick(ActionContext context) { }
	public void Cancel(ActionContext context) { _onComplete = null; }
	private void Finish(bool ok) { _onComplete?.Invoke(ok); _onComplete = null; }

	private static bool HasMeaningfulTag(GameObject go) => go && !go.CompareTag("Untagged");

	private static Type GetDesiredInteractableType(GameObject go)
	{
		if (!go) return null;
		var mb = go.GetComponents<MonoBehaviour>().FirstOrDefault(c => c is IInteractable);
		return mb != null ? mb.GetType() : null;
	}

	private static IInteractable GetInteractable(GameObject go, Type preferredType)
	{
		if (!go) return null;

		if (preferredType != null)
		{
			var exact = go.GetComponent(preferredType) as IInteractable;
			if (IsUsable(exact)) return exact;
			exact = go.GetComponentInChildren(preferredType) as IInteractable;
			if (IsUsable(exact)) return exact;
			exact = go.GetComponentInParent(preferredType) as IInteractable;
			if (IsUsable(exact)) return exact;
		}

		var any = go.GetComponent<IInteractable>()
				 ?? go.GetComponentInChildren<IInteractable>()
				 ?? go.GetComponentInParent<IInteractable>();
		return IsUsable(any) ? any : null;
	}

	private static IInteractable FindActiveSiblingInteractable(GameObject target, Type desiredType, out GameObject used)
	{
		used = null;
		if (!target || target.transform.parent == null) return null;
		var parent = target.transform.parent;

		foreach (Transform child in parent)
		{
			if (!child.gameObject.activeInHierarchy) continue;
			var inter = GetInteractable(child.gameObject, desiredType);
			if (inter != null) { used = child.gameObject; return inter; }
		}
		return null;
	}

	private class Candidate { public GameObject go; public IInteractable inter; }

	private static IInteractable FindByTag(string tag, Type desiredType, GameObject actor, out GameObject used)
	{
		used = null;
		var pool = GameObject.FindGameObjectsWithTag(tag)
			.Where(g => g && g.activeInHierarchy)
			.Select(g => new Candidate { go = g, inter = GetInteractable(g, desiredType) })
			.Where(c => c.inter != null).ToList();
		return SelectNearest(pool, actor, out used);
	}

	private static IInteractable FindNearestOfType(Type desiredType, GameObject actor, out GameObject used)
	{
		used = null;
		var pool = GameObject.FindObjectsOfType<MonoBehaviour>(false)
			.Where(mb => mb && mb.isActiveAndEnabled && desiredType.IsAssignableFrom(mb.GetType()))
			.Select(mb => new Candidate { go = mb.gameObject, inter = (IInteractable)mb })
			.ToList();
		return SelectNearest(pool, actor, out used);
	}

	private static IInteractable FindNearestAny(GameObject actor, out GameObject used)
	{
		used = null;
		var pool = GameObject.FindObjectsOfType<MonoBehaviour>(false)
			.Where(mb => mb && mb.isActiveAndEnabled && mb is IInteractable)
			.Select(mb => new Candidate { go = mb.gameObject, inter = (IInteractable)mb })
			.ToList();
		return SelectNearest(pool, actor, out used);
	}

	private static IInteractable SelectNearest(List<Candidate> candidates, GameObject actor, out GameObject used)
	{
		used = null;
		if (candidates == null || candidates.Count == 0) return null;
		Vector3 origin = actor ? actor.transform.position : Vector3.zero;

		Candidate best = null;
		float bestDist2 = float.MaxValue;

		foreach (var c in candidates)
		{
			if (c == null || c.go == null || c.inter == null) continue;
			float d2 = (c.go.transform.position - origin).sqrMagnitude;
			if (d2 < bestDist2) { bestDist2 = d2; best = c; }
		}

		if (best == null) return null;
		used = best.go;
		return best.inter;
	}

	private static bool IsUsable(IInteractable inter)
	{
		if (inter == null) return false;
		var mb = inter as MonoBehaviour;
		return mb != null && mb.isActiveAndEnabled && mb.gameObject.activeInHierarchy;
	}

	private static bool IsWithinRange(GameObject actor, GameObject target, float radius)
	{
		if (!actor || !target) return false;
		Vector3 a = actor.transform.position;
		Vector3 b = target.transform.position;

		float dist = Vector3.Distance(a, b);

		var col = target.GetComponentInChildren<Collider>();
		if (col != null)
		{
			Vector3 cp = col.ClosestPoint(a);
			dist = Vector3.Distance(a, cp);
		}

		return dist <= Mathf.Max(0f, radius);
	}
}
