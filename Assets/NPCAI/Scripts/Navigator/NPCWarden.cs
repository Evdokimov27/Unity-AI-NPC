using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class NPCWander : MonoBehaviour
{
	public List<Vector3> graphNodes = new List<Vector3>();
	[System.Serializable] public struct Edge { public int a; public int b; }
	public List<Edge> graphEdges = new List<Edge>();

	public bool startFromNearestNode = true;
	[Min(0.05f)] public float nodeArriveDistance = 0.5f;
	public Vector2 idleBetweenNodes = new Vector2(0.25f, 0.7f);

	NavMeshAgent agent;
	int currentNodeIndex = -1;
	int previousNodeIndex = -1;
	bool waitingAtNode;
	int queuedNodeIndex = -1;
	float nextHopTime;
	bool _autonomySuspended;

	NavMeshAgent EnsureAgent()
	{
		if (agent != null) return agent;
		agent = GetComponent<NavMeshAgent>();
		if (agent == null) agent = GetComponentInParent<NavMeshAgent>();
		return agent;
	}

	void OnEnable()
	{
		EnsureAgent();
		waitingAtNode = false;
		queuedNodeIndex = -1;

		if (graphNodes != null && graphNodes.Count > 0)
		{
			if (EnsureAgent() != null) BindStartNode();
		}
	}

	void Update()
	{
		if (EnsureAgent() == null || !agent.enabled) return;
		if (_autonomySuspended) return;
		UpdateFollowGraph();
	}

	public void SetAutonomySuspended(bool value)
	{
		_autonomySuspended = value;
		var a = EnsureAgent();
		if (a != null && a.enabled && a.isOnNavMesh)
		{
			a.isStopped = value;
			if (value) a.ResetPath();
			else if (graphNodes != null && graphNodes.Count > 0 && currentNodeIndex >= 0)
				a.SetDestination(graphNodes[currentNodeIndex]);
		}
	}

	void BindStartNode()
	{
		if (graphNodes == null || graphNodes.Count == 0) { var a0 = EnsureAgent(); if (a0) a0.isStopped = true; return; }

		var a = EnsureAgent();
		if (a == null) return;

		int startIdx = 0;
		if (startFromNearestNode)
		{
			float best = float.MaxValue;
			for (int i = 0; i < graphNodes.Count; i++)
			{
				float d = (graphNodes[i] - transform.position).sqrMagnitude;
				if (d < best) { best = d; startIdx = i; }
			}
		}

		currentNodeIndex = startIdx;
		previousNodeIndex = -1;

		float arrive = Mathf.Max(0.05f, nodeArriveDistance);
		a.stoppingDistance = Mathf.Max(a.stoppingDistance, arrive);
		a.isStopped = false;
		a.SetDestination(graphNodes[currentNodeIndex]);
	}

	void UpdateFollowGraph()
	{
		var a = EnsureAgent();
		if (a == null) return;

		if (graphNodes == null || graphNodes.Count == 0) { a.isStopped = true; return; }
		if (currentNodeIndex < 0 || currentNodeIndex >= graphNodes.Count) { BindStartNode(); return; }
		if (waitingAtNode)
		{
			if (Time.time >= nextHopTime)
			{
				waitingAtNode = false;
				if (queuedNodeIndex >= 0)
				{
					previousNodeIndex = currentNodeIndex;
					currentNodeIndex = queuedNodeIndex;
					queuedNodeIndex = -1;
					a.isStopped = false;
					a.SetDestination(graphNodes[currentNodeIndex]);
				}
			}
			return;
		}

		if (a.pathPending) return;

		float arrive = Mathf.Max(0.05f, nodeArriveDistance);
		bool reached = a.remainingDistance != Mathf.Infinity &&
					   a.remainingDistance <= arrive &&
					   a.velocity.sqrMagnitude < 0.02f;
		if (!reached && (transform.position - graphNodes[currentNodeIndex]).sqrMagnitude <= arrive * arrive)
			reached = true;

		if (!reached) return;

		var neighbors = GetNeighbors(currentNodeIndex);
		if (neighbors.Count == 0) { a.isStopped = true; return; }

		var pool = new List<int>(neighbors);
		if (previousNodeIndex >= 0) pool.Remove(previousNodeIndex);
		if (pool.Count == 0) pool = neighbors;
		int chosen = pool[Random.Range(0, pool.Count)];

		queuedNodeIndex = chosen;
		waitingAtNode = true;
		a.isStopped = true;
		nextHopTime = Time.time + Random.Range(idleBetweenNodes.x, idleBetweenNodes.y);
	}

	List<int> GetNeighbors(int idx)
	{
		var list = new List<int>();
		if (graphEdges == null) return list;
		for (int i = 0; i < graphEdges.Count; i++)
		{
			var e = graphEdges[i];
			if (e.a == idx && e.b >= 0 && e.b < graphNodes.Count) list.Add(e.b);
			else if (e.b == idx && e.a >= 0 && e.a < graphNodes.Count) list.Add(e.a);
		}
		return list;
	}
}
