using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class WalkAction : MonoBehaviour, IActionStep
{
	public string StepName => "Walk";

	[Header("Target")]
	[Tooltip("If ActionContext.explicitTarget is null, this Transform will be used as the destination.")]
	public Transform fallbackTarget;
	public Vector3 localOffset;

	[Header("Navigation")]
	[Min(0.1f)] public float arriveDistance = 0.6f;
	[Tooltip("Stop NavMeshAgent when arrived (recommended for precise stop).")]
	public bool stopAgentOnArrive = true;
	[Tooltip("Allow agent to update rotation while moving.")]
	public bool agentUpdateRotation = true;
	[Tooltip("Recompute destination while target moves.")]
	public bool trackMovingTarget = false;

	[Header("Timing")]
	[Tooltip("Extra wait after arrival, before finishing the step.")]
	[Min(0f)] public float postArriveWait = 1.5f;

	[Header("Dialogue Integration")]
	[Tooltip("Pause walking while a dialogue is running and resume afterwards.")]
	public bool waitWhileDialogue = true;

	[Header("Failure")]
	[Tooltip("If destination cannot be resolved, consider the step as failed.")]
	public bool failIfNoTarget = true;

	private Action<bool> _onComplete;
	private Coroutine _runner;

	private NavMeshAgent _agent;
	private bool _prevStopped;
	private bool _prevUpdateRotation;

	private NPCDialogueManager _dlg;
	private bool _pausedByDialogue;           
	private Vector3 _savedDestination;        
	private bool _hasSavedDestination;

	private void Awake()
	{
		_agent = GetComponentInParent<NavMeshAgent>();
	}

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;

		var actor = context?.actor ? context.actor : gameObject;
		if (!_agent) _agent = actor.GetComponentInChildren<NavMeshAgent>(true);
		if (_agent == null || !_agent.isOnNavMesh)
		{
			Debug.LogWarning("WalkAction: NavMeshAgent missing or not on NavMesh.");
			Finish(false);
			return;
		}

		if (waitWhileDialogue && _dlg == null)
		{
			var root = _agent.transform.root;
			_dlg = root.GetComponentInChildren<NPCDialogueManager>(true);
		}

		Transform tgt = context?.explicitTarget ? context.explicitTarget.transform : fallbackTarget;
		if (tgt == null)
		{
			if (failIfNoTarget) { Finish(false); } else { Finish(true); }
			return;
		}

		_prevStopped = _agent.isStopped;
		_prevUpdateRotation = _agent.updateRotation;

		_agent.updateRotation = agentUpdateRotation;
		_agent.isStopped = false;

		var dest = GetTargetPosition(tgt);
		_savedDestination = dest;
		_hasSavedDestination = true;

		_agent.SetDestination(dest);

		if (_runner != null) StopCoroutine(_runner);
		_runner = StartCoroutine(RunToTarget(tgt));
	}

	public void Tick(ActionContext context) { }

	public void Cancel(ActionContext context)
	{
		if (_runner != null) { StopCoroutine(_runner); _runner = null; }
		SafeRestoreAgent();
		_onComplete = null;
	}

	private IEnumerator RunToTarget(Transform tgt)
	{
		while (true)
		{
			if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
			{
				Finish(false);
				yield break;
			}

			if (waitWhileDialogue && _dlg != null && _dlg.IsInDialogue)
			{
				if (!_pausedByDialogue)
				{
					if (trackMovingTarget && tgt != null)
						_savedDestination = GetTargetPosition(tgt);
					else if (_agent.hasPath)
						_savedDestination = _agent.destination;

					_hasSavedDestination = true;
					_pausedByDialogue = true;
				}

				_agent.isStopped = true;
				_agent.updateRotation = false;
				yield return null;
				continue;
			}
			else if (_pausedByDialogue)
			{
				_pausedByDialogue = false;

				if (_hasSavedDestination && !_agent.hasPath)
				{
					_agent.SetDestination(_savedDestination);
				}

				_agent.isStopped = false;
				_agent.updateRotation = agentUpdateRotation;
			}

			if (trackMovingTarget && tgt != null)
			{
				var goal = GetTargetPosition(tgt);
				if ((_agent.destination - goal).sqrMagnitude > 0.05f * 0.05f)
				{
					_agent.SetDestination(goal);
					_savedDestination = goal; 
					_hasSavedDestination = true;
				}
			}
			else
			{
				if (_hasSavedDestination && !_agent.hasPath)
					_agent.SetDestination(_savedDestination);
			}

			float dist;
			if (_agent.hasPath)
			{
				dist = _agent.remainingDistance;
				if (float.IsInfinity(dist) || float.IsNaN(dist))
					dist = Vector3.Distance(_agent.transform.position, _agent.destination);
			}
			else
			{
				var goal = _hasSavedDestination ? _savedDestination : (_agent.destination);
				dist = Vector3.Distance(_agent.transform.position, goal);
			}

			if (dist <= Mathf.Max(0.01f, arriveDistance))
				break;

			yield return null;
		}

		if (stopAgentOnArrive)
		{
			_agent.isStopped = true;
			_agent.ResetPath();
		}

		if (postArriveWait > 0f)
			yield return new WaitForSeconds(postArriveWait);

		SafeRestoreAgent();
		Finish(true);
	}

	private Vector3 GetTargetPosition(Transform t) => t.TransformPoint(localOffset);

	private void SafeRestoreAgent()
	{
		if (_agent == null) return;
		if (!stopAgentOnArrive)
			_agent.isStopped = _prevStopped;
		_agent.updateRotation = _prevUpdateRotation;
	}

	private void Finish(bool ok)
	{
		var cb = _onComplete;
		_onComplete = null;
		_runner = null;
		cb?.Invoke(ok);
	}
}
