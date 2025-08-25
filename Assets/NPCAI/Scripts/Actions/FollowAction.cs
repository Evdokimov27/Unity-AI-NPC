using System;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class FollowAction : MonoBehaviour, IActionStep
{
	public string StepName => "Follow";

	[Header("NavMesh")]
	public NavMeshAgent agentOverride;

	[Header("Target")]
	public GameObject defaultTarget;

	[Header("Behavior")]
	[Min(0.1f)] public float followDistance = 1.75f;
	[Min(0.02f)] public float repathInterval = 0.1f;
	[Min(0f)] public float repathDistanceThreshold = 0.25f;

	[Header("Completion")]
	[Tooltip("0 — unlimited.")]
	[Min(0f)] public float maxDurationSeconds = 0f;

	NavMeshAgent _agent;
	Action<bool> _onComplete;
	GameObject _target;
	float _startedAt;
	float _lastRepathTime;
	Vector3 _lastTargetPos;

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onComplete = onComplete;

		_agent = agentOverride ? agentOverride
				: (GetComponent<NavMeshAgent>() ?? GetComponentInParent<NavMeshAgent>());
		if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
		{
			Debug.LogWarning("FollowAction: NavMeshAgent missing or not on NavMesh.");
			_onComplete?.Invoke(false);
			return;
		}

		_target = context?.explicitTarget ? context.explicitTarget : defaultTarget;
		if (_target == null)
		{
			Debug.LogWarning("FollowAction: No follow target.");
			_onComplete?.Invoke(false);
			return;
		}

		if (context != null && context.waitSeconds > 0f)
			maxDurationSeconds = context.waitSeconds;

		_agent.isStopped = false;
		_agent.stoppingDistance = Mathf.Max(_agent.stoppingDistance, followDistance);

		_startedAt = Time.time;
		_lastRepathTime = -999f;
		_lastTargetPos = GetFollowPoint(_target, _agent.transform.position, followDistance);

		_agent.SetDestination(_lastTargetPos);
	}

	public void Tick(ActionContext context)
	{
		if (_onComplete == null) return;
		if (_target == null || !_target.activeInHierarchy)
		{
			Complete(false);
			return;
		}

		if (maxDurationSeconds > 0f && Time.time - _startedAt >= maxDurationSeconds)
		{
			Complete(true);
			return;
		}

		Vector3 wanted = GetFollowPoint(_target, _agent.transform.position, followDistance);

		bool targetMovedFar = (wanted - _lastTargetPos).sqrMagnitude >= repathDistanceThreshold * repathDistanceThreshold;
		bool timeForRepath = (Time.time - _lastRepathTime) >= repathInterval;

		if (targetMovedFar || timeForRepath)
		{
			_agent.SetDestination(wanted);
			_lastTargetPos = wanted;
			_lastRepathTime = Time.time;
		}
	}

	public void Cancel(ActionContext context)
	{
		if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
		{
			_agent.isStopped = true;
			_agent.ResetPath();
		}
		_onComplete = null;
	}

	void Complete(bool ok)
	{
		var cb = _onComplete;
		_onComplete = null;
		cb?.Invoke(ok);
	}

	static Vector3 GetFollowPoint(GameObject target, Vector3 from, float buffer)
	{
		if (!target) return from;

		var col = target.GetComponentInChildren<Collider>();
		Vector3 toPoint = col ? col.ClosestPoint(from) : target.transform.position;

		Vector3 dir = toPoint - from; dir.y = 0f;
		if (dir.sqrMagnitude < 1e-6f) return toPoint;

		return toPoint - dir.normalized * buffer;
	}
}
