using System;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class WalkAction : MonoBehaviour, IActionStep
{
	public string StepName => "Walk";

	public GameObject fixedTarget;
	public GameObject[] availableTargets;
	public float arriveDistance = 0.5f;

	public bool stopAgentOnArrive = true;
	public float velocityEpsilon = 0.05f;

	private NavMeshAgent _agent;
	private Action<bool> _onComplete;
	private GameObject _target;

	private void Awake()
	{
		_agent = GetComponent<NavMeshAgent>();

	}

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;

		if (!_agent || !_agent.isOnNavMesh)
		{
			Debug.LogError("WalkAction: NavMeshAgent missing or not on NavMesh.");
			_onComplete?.Invoke(false);
			return;
		}

		_target = IsActiveObj(fixedTarget) ? fixedTarget : null;

		if (_target == null && IsActiveObj(context?.explicitTarget))
			_target = context.explicitTarget;

		if (_target == null && availableTargets != null && availableTargets.Length > 0)
		{
			string nameToFind = context?.mappedTargetName;
			if (!string.IsNullOrEmpty(nameToFind))
			{
				_target = availableTargets.FirstOrDefault(t =>
					IsActiveObj(t) &&
					string.Equals(t.name, nameToFind, StringComparison.OrdinalIgnoreCase));
			}
		}

		if (_target == null)
		{
			Debug.LogWarning("WalkAction: No active target to walk to.");
			_onComplete?.Invoke(false);
			return;
		}

		float effectiveStop = Mathf.Max(arriveDistance, _agent.stoppingDistance);
		_agent.stoppingDistance = effectiveStop;

		_agent.isStopped = false;
		_agent.SetDestination(_target.transform.position);
	}

	public void Tick(ActionContext context)
	{
		if (_onComplete == null || _target == null) return;

		if (!IsActiveObj(_target))
		{
			Debug.LogWarning("WalkAction: Target became inactive while walking.");
			Complete(false);
			return;
		}

		if (_agent.pathPending) return;
		if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) { Complete(false); return; }

		float effectiveStop = Mathf.Max(arriveDistance, _agent.stoppingDistance);

		bool closeEnough = _agent.remainingDistance != Mathf.Infinity &&
						   _agent.remainingDistance <= effectiveStop;

		bool almostStopped = _agent.velocity.sqrMagnitude <= (velocityEpsilon * velocityEpsilon);

		if (closeEnough && almostStopped)
		{
			if (stopAgentOnArrive)
			{
				_agent.isStopped = true;
				_agent.ResetPath();
			}
			Complete(true);
		}
	}

	public void Cancel(ActionContext context)
	{
		if (_agent && _agent.isOnNavMesh)
		{
			_agent.isStopped = true;
			_agent.ResetPath();
		}
		_onComplete = null;
	}

	private void Complete(bool ok)
	{
		var cb = _onComplete;
		_onComplete = null;
		cb?.Invoke(ok);
	}

	private static bool IsActiveObj(GameObject go) => go && go.activeInHierarchy;
}
