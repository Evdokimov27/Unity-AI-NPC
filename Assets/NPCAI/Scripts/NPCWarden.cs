using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class NPCWander : MonoBehaviour
{
	[SerializeField, Min(0.1f)] private float minIdleTime = 1.2f;
	[SerializeField, Min(0.1f)] private float maxIdleTime = 3.5f;
	[SerializeField] private Vector2 speedJitter = new Vector2(-0.5f, 0.5f);
	[SerializeField] private Vector2 angularJitter = new Vector2(-60f, 60f);

	[Min(1f)] public float poiSearchRadius = 18f;
	public string[] poiTags = new[] { "Target" };
	public bool includeInteractablesAsPOI = true;
	public Vector2 investigateTimeRange = new Vector2(2.0f, 4.0f);
	public bool facePOIOnInvestigate = true;
	public bool tryInteractAtPOI = true;
	public float interactTriggerDistance = 1.6f;

	[SerializeField, Min(0.5f)] private float chatRange = 3.0f;
	[SerializeField, Min(0.5f)] private float chatCooldown = 20f;
	[SerializeField] private LayerMask npcLayer = ~0;
	[SerializeField] private bool facePartnerOnChat = true;
	public UnityEvent<MonoBehaviour, MonoBehaviour> OnChatRequested;

	[SerializeField] private bool drawDebugGizmos = true;

	private enum FacePriority { None = 0, Investigate = 1, Chat = 2 }

	private NavMeshAgent agent;
	private INPCBusy busy;
	private InteractAction interactAction;

	private float lastChatTime = -999f;
	private float nextRepathTime;
	private readonly Collider[] overlap = new Collider[16];
	private int selfId;

	private Vector3 lastDestination = Vector3.positiveInfinity;
	private bool isInvestigating;
	private GameObject currentPOI;
	private GameObject lastVisitedPOI;
	private bool autonomySuspended;

	private Coroutine faceRoutine;
	private Transform faceTarget;
	private FacePriority currentFacePriority = FacePriority.None;

	void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
		busy = GetComponent<INPCBusy>() ?? gameObject.AddComponent<NPCBusyFlag>();
		interactAction = GetComponent<InteractAction>();
		selfId = GetInstanceID();
	}

	void OnEnable() => ScheduleNextWander();

	void Update()
	{
		if (!agent.enabled) return;
		if (autonomySuspended) return;

		if (!busy.IsBusy && !isInvestigating)
		{
			if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
			{
				if (currentPOI)
				{
					StartCoroutine(InvestigatePOI(currentPOI));
					return;
				}
				if (Time.time >= nextRepathTime)
					StartCoroutine(PickNextDestinationWithIdle());
			}
		}

		TryFindPartnerAndChat();
	}

	public void SetAutonomySuspended(bool value)
	{
		autonomySuspended = value;
		if (agent && agent.enabled) agent.isStopped = value;
	}

	public void OnDialogueEnded()
	{
		if (agent && agent.enabled) agent.isStopped = false;
		ScheduleNextWander();
	}

	public void OnDialogueStarted(Transform partner, float faceDuration = 0.5f)
	{
		RequestFace(partner, faceDuration, FacePriority.Chat);
	}

	void CancelFace()
	{
		if (faceRoutine != null)
		{
			StopCoroutine(faceRoutine);
			faceRoutine = null;
		}
		faceTarget = null;
		currentFacePriority = FacePriority.None;
	}

	void RequestFace(Transform target, float duration, FacePriority priority)
	{
		if (!target) return;
		if (priority < currentFacePriority) return;
		if (faceRoutine != null) StopCoroutine(faceRoutine);
		faceRoutine = StartCoroutine(FaceTransformFlatPriority(target, duration, priority));
	}

	IEnumerator FaceTransformFlatPriority(Transform target, float duration, FacePriority priority)
	{
		currentFacePriority = priority;
		faceTarget = target;

		bool hadAgent = agent && agent.enabled;
		bool prevUpdateRot = false;
		if (hadAgent) { prevUpdateRot = agent.updateRotation; agent.updateRotation = false; }

		Quaternion startRot = transform.rotation;

		Vector3 dir = (target.position - transform.position);
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
		Quaternion targetRot = Quaternion.LookRotation(dir);

		float t = 0f;
		while (t < duration)
		{
			if (!target) break;

			Vector3 d = (target.position - transform.position);
			d.y = 0f;
			if (d.sqrMagnitude > 0.0001f)
				targetRot = Quaternion.LookRotation(d);

			t += Time.deltaTime;
			transform.rotation = Quaternion.Slerp(startRot, targetRot, Mathf.Clamp01(t / duration));
			yield return null;

			if (priority < currentFacePriority) { break; }
		}

		if (priority == currentFacePriority && target)
		{
			Vector3 d = (target.position - transform.position);
			d.y = 0f;
			if (d.sqrMagnitude > 0.0001f)
				transform.rotation = Quaternion.LookRotation(d);
		}

		if (hadAgent && priority == currentFacePriority)
			agent.updateRotation = prevUpdateRot;

		if (priority == currentFacePriority)
		{
			faceRoutine = null;
			faceTarget = null;
			currentFacePriority = FacePriority.None;
		}
	}

	IEnumerator PickNextDestinationWithIdle()
	{
		float idle = Random.Range(minIdleTime, maxIdleTime);
		nextRepathTime = Time.time + idle;
		yield return new WaitForSeconds(idle);
		if (!busy.IsBusy && !isInvestigating && !autonomySuspended)
			SetNextDestination();
	}

	void SetNextDestination()
	{
		ApplyAgentJitter();
		currentPOI = null;

		var poi = GetRandomNearbyPOI();
		if (poi)
		{
			currentPOI = poi;
			if (TryGetPointNear(poi.transform.position, out var pos))
			{
				if ((pos - lastDestination).sqrMagnitude < 4f)
				{
					var alt = GetRandomNearbyPOI(exclude: poi);
					if (alt && TryGetPointNear(alt.transform.position, out var pos2))
						pos = pos2;
				}

				lastDestination = pos;
				agent.isStopped = false;
				agent.SetDestination(pos);
				return;
			}
		}

		agent.isStopped = true;
		currentPOI = null;
		ScheduleNextWander();
	}

	void ScheduleNextWander() =>
		nextRepathTime = Time.time + Random.Range(minIdleTime, maxIdleTime);

	void ApplyAgentJitter()
	{
		if (agent == null) return;
		float baseSpeed = Mathf.Max(0.1f, agent.speed);
		float baseAngular = Mathf.Max(10f, agent.angularSpeed);
		agent.speed = Mathf.Max(0.1f, baseSpeed + Random.Range(speedJitter.x, speedJitter.y));
		agent.angularSpeed = Mathf.Max(10f, baseAngular + Random.Range(angularJitter.x, angularJitter.y));
	}

	bool TryGetPointNear(Vector3 target, out Vector3 pos)
	{
		const float radius = 1.5f;
		for (int i = 0; i < 5; i++)
		{
			Vector3 offset = Random.insideUnitSphere * radius;
			offset.y = 0;
			Vector3 p = target + offset;
			if (NavMesh.SamplePosition(p, out NavMeshHit hit, 1.8f, NavMesh.AllAreas))
			{
				pos = hit.position;
				return true;
			}
		}
		if (NavMesh.SamplePosition(target, out NavMeshHit hit2, 2.5f, NavMesh.AllAreas))
		{
			pos = hit2.position;
			return true;
		}
		pos = target;
		return false;
	}

	GameObject GetRandomNearbyPOI(GameObject exclude = null)
	{
		var candidates = CollectNearbyPOIs();
		if (lastVisitedPOI) candidates.Remove(lastVisitedPOI);
		if (exclude) candidates.Remove(exclude);
		candidates.RemoveAll(go => (go.transform.position - lastDestination).sqrMagnitude < 4f);
		if (candidates.Count == 0) return null;
		int idx = Random.Range(0, candidates.Count);
		return candidates[idx];
	}

	List<GameObject> CollectNearbyPOIs()
	{
		var list = new List<GameObject>();
		Vector3 origin = transform.position;
		float r2 = poiSearchRadius * poiSearchRadius;

		if (poiTags != null && poiTags.Length > 0)
		{
			foreach (var tag in poiTags)
			{
				if (string.IsNullOrWhiteSpace(tag)) continue;
				try { list.AddRange(GameObject.FindGameObjectsWithTag(tag)); } catch { }
			}
		}

		if (includeInteractablesAsPOI)
		{
			var interactables = GameObject.FindObjectsOfType<MonoBehaviour>(false)
				.Where(mb => mb && mb.isActiveAndEnabled && mb is IInteractable)
				.Select(mb => mb.gameObject);
			list.AddRange(interactables);
		}

		list = list
			.Where(go => go && go.activeInHierarchy)
			.Where(go => (go.transform.position - origin).sqrMagnitude <= r2)
			.Distinct()
			.ToList();

		return list;
	}

	IEnumerator InvestigatePOI(GameObject poi)
	{
		isInvestigating = true;
		if (agent.enabled) agent.isStopped = true;

		lastVisitedPOI = poi;

		if (facePOIOnInvestigate && poi)
			RequestFace(poi.transform, Random.Range(0.25f, 0.4f), FacePriority.Investigate);

		if (tryInteractAtPOI && interactAction && poi)
		{
			float dist = Vector3.Distance(transform.position, poi.transform.position);
			if (dist <= interactTriggerDistance)
			{
				busy.SetBusy(true);
				bool finished = false;
				bool result = false;
				var ctx = new ActionContext(gameObject) { explicitTarget = poi, mappedTargetName = poi.name };
				interactAction.Begin(ctx, ok => { result = ok; finished = true; });
				float t = 0f, cap = 6f;
				while (!finished && t < cap) { t += Time.deltaTime; yield return null; }
				busy.SetBusy(false);
			}
		}

		float dwell = Random.Range(investigateTimeRange.x, investigateTimeRange.y);
		float t0 = Time.time;
		while (Time.time - t0 < dwell) { yield return null; }

		currentPOI = null;
		isInvestigating = false;
		if (agent && agent.enabled) agent.isStopped = false;

		ScheduleNextWander();
	}

	void TryFindPartnerAndChat()
	{
		if (busy.IsBusy) return;
		if (autonomySuspended) return;
		if (Time.time - lastChatTime < chatCooldown) return;

		int count = Physics.OverlapSphereNonAlloc(transform.position, chatRange, overlap, npcLayer, QueryTriggerInteraction.Ignore);
		if (count <= 0) return;

		MonoBehaviour selfHandle = GetSelfHandle();
		if (selfHandle == null) return;

		for (int i = 0; i < count; i++)
		{
			var col = overlap[i];
			if (!col) continue;
			var otherGo = col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
			if (!otherGo || otherGo == gameObject) continue;
			var other = otherGo.GetComponent<NPCWander>();
			if (!other) continue;
			if (other.busy.IsBusy) continue;
			if (other.autonomySuspended) continue;
			if (Time.time - other.lastChatTime < other.chatCooldown) continue;
			if (selfId <= other.selfId) continue;

			var otherHandle = other.GetSelfHandle();
			if (!otherHandle) continue;

			busy.SetBusy(true);
			other.busy.SetBusy(true);
			lastChatTime = Time.time;
			other.lastChatTime = Time.time;

			if (agent && agent.enabled) agent.isStopped = true;
			if (other.agent && other.agent.enabled) other.agent.isStopped = true;

			if (facePartnerOnChat)
			{
				RequestFace(otherGo.transform, 0.6f, FacePriority.Chat);
				other.RequestFace(transform, 0.6f, FacePriority.Chat);
			}

			OnChatRequested?.Invoke(selfHandle, otherHandle);
			return;
		}
	}

	MonoBehaviour GetSelfHandle()
	{
		var ai = GetComponent<NPCAI>();
		if (ai) return ai;
		var profile = GetComponent<NPCProfile>();
		if (profile) return profile;
		return this;
	}

	void OnDrawGizmosSelected()
	{
		if (!drawDebugGizmos) return;
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireSphere(transform.position, poiSearchRadius);
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(transform.position, chatRange);
		if (currentPOI)
		{
			Gizmos.color = Color.magenta;
			Gizmos.DrawWireSphere(currentPOI.transform.position, interactTriggerDistance);
		}
	}
}

public interface INPCBusy
{
	bool IsBusy { get; }
	void SetBusy(bool value);
}

public class NPCBusyFlag : MonoBehaviour, INPCBusy
{
	[SerializeField] private bool isBusy;
	public bool IsBusy => isBusy;
	public void SetBusy(bool value) => isBusy = value;
}
