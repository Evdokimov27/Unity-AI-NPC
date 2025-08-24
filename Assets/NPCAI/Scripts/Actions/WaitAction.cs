using System;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class WaitAction : MonoBehaviour, IActionStep
{
	public string StepName => "Wait";
	[Min(0f)] public float fallbackSeconds = 3f;
	public bool stopAgent = true;

	float _endTime;
	Action<bool> _onDone;
	bool _running;
	NavMeshAgent _agent;

	void Awake()
	{
		_agent = GetComponent<NavMeshAgent>() ?? GetComponentInParent<NavMeshAgent>();
	}

	public void Begin(ActionContext context, Action<bool> onComplete)
	{
		_onDone = onComplete;
		float seconds = (context != null && context.waitSeconds > 0f) ? context.waitSeconds : fallbackSeconds;
		_endTime = Time.time + Mathf.Max(0f, seconds);
		_running = true;

		if (stopAgent && _agent && _agent.enabled)
		{
			_agent.isStopped = true;
			_agent.ResetPath();
		}
	}

	public void Tick(ActionContext context)
	{
		if (!_running) return;
		if (Time.time >= _endTime)
		{
			_running = false;
			_onDone?.Invoke(true);
			_onDone = null;
		}
	}

	public void Cancel(ActionContext context)
	{
		_running = false;
		_onDone = null;
	}
}
