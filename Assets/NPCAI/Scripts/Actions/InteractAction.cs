using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class InteractAction : MonoBehaviour, IActionStep
{
	public string StepName => "Interact";

	[Header("Behavior")]
	[Tooltip("Try to find alternative interactable if the target is inactive or unsuitable.")]
	public bool tryAlternativeIfInactive = true;

	[Tooltip("If no interactable found, treat as failure. If false, step completes successfully even if nothing is done.")]
	public bool failIfNoInteractable = true;

	[Header("Range")]
	[Tooltip("Maximum distance from actor to target's collider/position to allow interaction.")]
	public float interactionRadius = 1.2f;

	[Tooltip("If true, step fails when out of range; if false, step completes (no-op) when wait/approach disabled.")]
	public bool failIfOutOfRange = true;

	[Header("Approach & Waiting")]
	[Tooltip("If actor is out of range when Begin() is called, wait until in-range instead of failing.")]
	public bool waitUntilInRange = true;

	[Tooltip("Automatically approach the target using NavMeshAgent until within interactionRadius.")]
	public bool autoApproach = true;

	[Tooltip("Max seconds to wait/approach to get in range. 0 = unlimited.")]
	[Min(0f)] public float approachTimeout = 10f;

	private bool sampleToNavMesh = true;

	[Min(0f)] public float sampleMaxDistance = 2f;

	[Header("Timing")]
	[Tooltip("Delay before calling Interact() (seconds).")]
	[Min(0f)] public float preDelayBeforeInteract = 0f;

	[Tooltip("Delay after Interact() before finishing the step (seconds).")]
	[Min(0f)] public float postDelayAfterInteract = 0.25f;

	[Header("Dialogue Integration")]
	private bool waitWhileDialogue = true;
	[Header("Wander Integration")]
	private bool suspendWanderDuringStep = true;

	private bool verboseLogs = false;
	private Action<bool> _onComplete;

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;

		var actorGO = context?.actor != null ? context.actor : gameObject;
		var target = context?.explicitTarget;

		if (!target)
		{
			Log("InteractAction: no explicitTarget.");
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
			interactOn = FindActiveSiblingInteractable(target, desiredType, out useObject)
					  ?? (HasMeaningfulTag(target) ? FindByTag(target.tag, desiredType, actorGO, out useObject) : null)
					  ?? (desiredType != null ? FindNearestOfType(desiredType, actorGO, out useObject) : null)
					  ?? FindNearestAny(actorGO, out useObject);
		}

		if (interactOn == null)
		{
			Log($"InteractAction: no suitable interactable for '{target.name}'.");
			Finish(!failIfNoInteractable);
			return;
		}

		NPCDialogueManager dlg = null;
		if (waitWhileDialogue)
		{
			var root = actorGO.transform.root;
			dlg = root.GetComponent<NPCDialogueManager>() ?? root.GetComponentInChildren<NPCDialogueManager>(true);
		}

		StartCoroutine(Flow(actorGO, useObject, interactOn, dlg));
	}

	public void Tick(ActionContext context) { }
	public void Cancel(ActionContext context) { _onComplete = null; }
	private void Finish(bool ok) { var cb = _onComplete; _onComplete = null; cb?.Invoke(ok); }

	private IEnumerator Flow(GameObject actor, GameObject useObject, IInteractable interactOn, NPCDialogueManager dlg)
	{
		// — удерживаем Wander на паузе на весь шаг
		NPCWander wander = null;
		bool wanderWasEnabled = false;
		bool weHoldAutonomy = false;

		if (suspendWanderDuringStep)
		{
			var root = actor.transform.root;
			wander = root.GetComponentInChildren<NPCWander>(true);
			if (wander != null)
			{
				wanderWasEnabled = wander.enabled;
				wander.SetAutonomySuspended(true); // ключевое: мы держим паузу
				weHoldAutonomy = true;
				if (wander.enabled) wander.enabled = false;
				Log("InteractAction: Wander suspended.");
			}
		}

		// === A) Подойти/дождаться радиуса (с паузой на диалог)
		if (!IsWithinRange(actor, useObject, interactionRadius))
		{
			if (!waitUntilInRange && !autoApproach)
			{
				Log($"InteractAction: out of range and wait/approach disabled (need <= {interactionRadius:0.##} m).");
				RestoreWander(wander, wanderWasEnabled, weHoldAutonomy);
				Finish(!failIfOutOfRange ? true : false);
				yield break;
			}

			var agent = ResolveAgent(actor);
			if (autoApproach && agent == null)
			{
				Log("InteractAction: NavMeshAgent not found for auto-approach.");
				RestoreWander(wander, wanderWasEnabled, weHoldAutonomy);
				Finish(false);
				yield break;
			}

			float startTime = Time.time;
			Log("InteractAction: approaching / waiting until in-range...");

			while (!IsWithinRange(actor, useObject, interactionRadius))
			{
				if (waitWhileDialogue && dlg != null && dlg.IsInDialogue)
				{
					if (agent) agent.isStopped = true;
					yield return null;
					continue;
				}
				else if (agent)
				{
					agent.isStopped = false;
				}

				if (autoApproach && agent)
				{
					Vector3 goal = GetClosestPointToActor(useObject, actor.transform.position);
					if (sampleToNavMesh && NavMesh.SamplePosition(goal, out var hit, sampleMaxDistance, NavMesh.AllAreas))
						goal = hit.position;

					if (!agent.hasPath || (agent.destination - goal).sqrMagnitude > 0.04f * 0.04f)
						agent.SetDestination(goal);
				}

				if (approachTimeout > 0f && Time.time - startTime > approachTimeout)
				{
					Log("InteractAction: approach/wait timeout.");
					RestoreWander(wander, wanderWasEnabled, weHoldAutonomy);
					Finish(false);
					yield break;
				}

				yield return null;
			}

			var ag2 = ResolveAgent(actor);
			if (ag2) { ag2.isStopped = true; ag2.ResetPath(); }
			Log("InteractAction: in-range.");
		}

		// === B) Дождаться конца диалога, затем preDelay (с паузой таймера)
		if (waitWhileDialogue && dlg != null)
			while (dlg.IsInDialogue) yield return null;

		if (preDelayBeforeInteract > 0f)
		{
			float end = Time.time + preDelayBeforeInteract;
			while (Time.time < end)
			{
				if (waitWhileDialogue && dlg != null && dlg.IsInDialogue)
				{
					end += Time.deltaTime;
					yield return null;
					continue;
				}
				yield return null;
			}
		}

		// === C) Проверка доступности и Interact
		if (!interactOn.CanInteract(actor))
		{
			Log("InteractAction: CanInteract==false.");
			RestoreWander(wander, wanderWasEnabled, weHoldAutonomy);
			Finish(false);
			yield break;
		}

		bool success = false;
		try { interactOn.Interact(actor, ok => success = ok); }
		catch (Exception e)
		{
			Debug.LogError($"InteractAction: exception during Interact() on '{actor?.name}': {e}");
			RestoreWander(wander, wanderWasEnabled, weHoldAutonomy);
			Finish(false);
			yield break;
		}

		// === D) postDelay (с паузой на диалог)
		if (postDelayAfterInteract > 0f)
		{
			float end = Time.time + postDelayAfterInteract;
			while (Time.time < end)
			{
				if (waitWhileDialogue && dlg != null && dlg.IsInDialogue)
				{
					end += Time.deltaTime;
					yield return null;
					continue;
				}
				yield return null;
			}
		}

		// === E) Завершение
		RestoreWander(wander, wanderWasEnabled, weHoldAutonomy);
		Finish(success);
	}

	private void RestoreWander(NPCWander w, bool wasEnabled, bool weHoldAutonomy)
	{
		if (w == null) return;
		if (weHoldAutonomy) w.SetAutonomySuspended(false); // снимаем только нашу «зажимку»
		w.enabled = wasEnabled;
		Log("InteractAction: Wander restored.");
	}

	private NavMeshAgent ResolveAgent(GameObject actorGO)
	{
		var root = actorGO.transform.root;
		var agent = root.GetComponent<NavMeshAgent>() ?? root.GetComponentInChildren<NavMeshAgent>(true);
		if (agent == null || !agent.enabled || !agent.isOnNavMesh) return null;
		return agent;
	}

	private static Vector3 GetClosestPointToActor(GameObject target, Vector3 actorPos)
	{
		var col = target ? target.GetComponentInChildren<Collider>() : null;
		if (col) return col.ClosestPoint(actorPos);
		return target ? target.transform.position : actorPos;
	}

	// ===== Utils from your original =====

	private void Log(string msg) { if (verboseLogs) Debug.Log(msg, this); }

	private static bool HasMeaningfulTag(GameObject go) => go && !go.CompareTag("Untagged");

	private static System.Type GetDesiredInteractableType(GameObject go)
	{
		if (!go) return null;
		var mb = go.GetComponents<MonoBehaviour>().FirstOrDefault(c => c is IInteractable);
		return mb != null ? mb.GetType() : null;
	}

	private static IInteractable GetInteractable(GameObject go, System.Type preferredType)
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

	private static IInteractable FindActiveSiblingInteractable(GameObject target, System.Type desiredType, out GameObject used)
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

	private static IInteractable FindByTag(string tag, System.Type desiredType, GameObject actor, out GameObject used)
	{
		used = null;
		var pool = GameObject.FindGameObjectsWithTag(tag)
			.Where(g => g && g.activeInHierarchy)
			.Select(g => new Candidate { go = g, inter = GetInteractable(g, desiredType) })
			.Where(c => c.inter != null).ToList();
		return SelectNearest(pool, actor, out used);
	}

	private static IInteractable FindNearestOfType(System.Type desiredType, GameObject actor, out GameObject used)
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
