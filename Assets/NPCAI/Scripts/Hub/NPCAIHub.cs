using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static UnityEngine.Rendering.VolumeComponent;
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(CapsuleCollider))]
public class NPCAIHub : MonoBehaviour
{
	[Header("Logo")]
	public Texture2D logo;

	[Header("Feature Switches")]
	public bool featureDialogue = false;
	public bool featureInteractive = false;
	public bool featureMovement = false;
	public bool featureWander = false;
	public bool featureInteract = true;
	public bool featureCommentator = false;
	public bool featureFollow = false;
	public bool featureLogs = false;

	[Header("Animation")]
	public Animator animator;
	public float speedMovement;
	public string walkBool = "isWalking";
	public string talkBool = "isTalking";
	public string interactBool = "isInteracting";
	public string followBool = "isFollowing";
	public string waitBool = "isWaiting";
	public string wanderBool = "isWander";

	[Header("Core — Dialogue")]
	public string npcName = "";
	public string npcMood = "";
	[TextArea] public string npcBackstory = "";
	public float bubbleVisibleTime = 4f;

	[Header("Behavior — Movement")]
	public float arriveDistance = 0.5f;
	public bool stopAgentOnArrive = true;

	[Header("Behavior — Interact")]
	public float interactRadius = 1.2f;

	[Header("Behavior — Wander")]
	public bool proximityDialogue = true;

	public const string HolderName = "Components";

	bool _isSyncing;

	void OnEnable() { AutoSync(); }
	void Awake() { AutoSync(); }
#if UNITY_EDITOR
	void OnValidate() { AutoSync(); 
	if (logo == null)
				logo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NPCAI/Resourses/Icon/icon.png");
	}
#endif

	void AutoSync()
	{
		if (!isActiveAndEnabled) return;
		if (_isSyncing) return;
		_isSyncing = true;
		try
		{
			EnsureOnChild<NPCAI>("Core.AI");
			EnsureSpeedMovement();
			ApplyAutoDefaults();
			EnsureHolder(this);
			EnsureAgentOnRoot();
			EnsureProfile();
			EnsureDialogue();
			EnsureMovement();
			EnsureFollow();
			EnsureWander();
			EnsureCommentator();
			ApplyFeatureEnablement();
			WireReferences();
			EnsureNavAgentWiring();
		}
		finally { _isSyncing = false; }
	}
#if UNITY_EDITOR
	GameObject LoadBubblePrefab()
	{
		return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
			"Assets/NPCAI/Prefab/BubbleDialogue.prefab"
		);
	}
#endif

	public static Transform EnsureHolder(NPCAIHub hub)
	{
		var t = hub.transform.Find(HolderName);
		if (!t)
		{
			var go = new GameObject(HolderName);
			go.transform.SetParent(hub.transform, false);
			t = go.transform;
		}
		return t;
	}

	Transform EnsureOnChildGO(string flatName)
	{
		var holder = EnsureHolder(this);
		var ct = holder.Find(flatName);
		if (!ct)
		{
			var go = new GameObject(flatName);
			go.transform.SetParent(holder, false);
			ct = go.transform;
		}
		return ct;
	}

	T EnsureOnChild<T>(string flatName, System.Action<T> init = null) where T : Component
	{
		var ct = EnsureOnChildGO(flatName);
		var c = ct.GetComponent<T>();
		if (!c)
		{
			c = ct.gameObject.AddComponent<T>();
			init?.Invoke(c);
		}
		return c;
	}

	T FindOnChild<T>(string flatName) where T : Component
	{
		var holder = EnsureHolder(this);
		var t = holder.Find(flatName);
		return t ? t.GetComponent<T>() : null;
	}

	NavMeshAgent EnsureAgentOnRoot()
	{
		var agent = GetComponent<NavMeshAgent>();
		if (!agent) agent = gameObject.AddComponent<NavMeshAgent>();
		if (agent.radius <= 0.01f) agent.radius = 0.5f;
		if (agent.speed <= 0.01f) agent.speed = 3.5f;
		if (agent.acceleration <= 0.01f) agent.acceleration = 8f;
		agent.enabled = true;
		return agent;
	}
	void EnsureProfile()
	{
		EnsureOnChild<NPCProfile>("Dialogue.Profile", p =>
		{
			if (string.IsNullOrWhiteSpace(p.npcName)) p.npcName = gameObject.name;
			if (string.IsNullOrWhiteSpace(p.mood)) p.mood = "neutral";
			if (string.IsNullOrWhiteSpace(p.backstory)) p.backstory = $"This is {p.npcName}.";
		});
	}
	void EnsureSpeedMovement()
	{
		EnsureOnChild<NPCAI>("Core.AI", f =>
		{
			f.speedMovement = speedMovement;
		});
	}
	void EnsureFollow()
	{
		EnsureOnChild<FollowAction>("Movement.Follow", f =>
		{
			if (f.followDistance <= 0.01f) f.followDistance = 1.75f;
		});
	}
	void EnsureWander()
	{
		EnsureOnChild<NPCWander>("Movement.Wander", w =>
		{
			w.SetAutonomySuspended(!featureWander);
		});
	}

	void EnsureDialogue()
	{
		var profile = EnsureOnChild<NPCProfile>("Dialogue.Profile", p =>
		{
			if (string.IsNullOrWhiteSpace(p.npcName)) p.npcName = gameObject.name;
			if (string.IsNullOrWhiteSpace(p.mood)) p.mood = "neutral";
			if (string.IsNullOrWhiteSpace(p.backstory)) p.backstory = $"This is {p.npcName}.";
		});

		var manager = EnsureOnChild<NPCDialogueManager>("Dialogue.Manager");
		manager.enableProximityDialogue = proximityDialogue;

		var bubble = EnsureBubbleFromPrefab("Dialogue.SpeechBubble");

		var client = NPCAI_GlobalClient.GetOrCreateClientInScene();
		TrySetField(manager, "client", client);
		TrySetProperty(manager, "Client", client);

		TrySetField(manager, "npc", profile);
		TrySetProperty(manager, "Npc", profile);

		TrySetField(manager, "bubble", bubble);
		TrySetField(manager, "_bubble", bubble);
		TrySetField(manager, "speechBubble", bubble);
		TrySetProperty(manager, "Bubble", bubble);
	}

	NPCSpeechBubble EnsureBubbleFromPrefab(string flatName)
	{
		var holder = EnsureHolder(this);
		var t = holder.Find(flatName);
		if (t)
		{
			var existing = t.GetComponent<NPCSpeechBubble>();
			if (existing) return existing;
		}

		var prefab = LoadBubblePrefab();
		if (!prefab)
		{
			Debug.LogError("NPCSpeechBubble prefab not found in Resources/Prefabs/NPCSpeechBubble");
			return EnsureOnChild<NPCSpeechBubble>(flatName);
		}

#if UNITY_EDITOR
		var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, holder);
#else
    var go = Instantiate(prefab, holder);
#endif
		go.name = flatName;
		return go.GetComponent<NPCSpeechBubble>();
	}
	void EnsureMovement()
	{
		EnsureAgentOnRoot();

		var walk = EnsureOnChild<WalkAction>("Movement.Walk", w =>
		{
			w.arriveDistance = arriveDistance;
			w.stopAgentOnArrive = stopAgentOnArrive;
		});

		EnsureOnChild<WaitAction>("Core.Wait");
		EnsureOnChild<DialogueAction>("Core.Dialogue");

		var follow = EnsureOnChild<FollowAction>("Movement.Follow", f =>
		{
			f.followDistance = 1.75f;
		});

		EnsureOnChild<TalkAction>("Movement.TalkOnStart", t => { t.mode = TalkAction.TalkMode.FixedText; t.fixedLine = "On my way."; });
		EnsureOnChild<TalkAction>("Movement.TalkDuring", t => { t.mode = TalkAction.TalkMode.GenerateLoop; t.llmPrompt = "Say one short line about walking."; });
		EnsureOnChild<TalkAction>("Movement.TalkFinish", t => { t.mode = TalkAction.TalkMode.GenerateOnce; t.llmPrompt = "Say one short line after arriving."; });

		EnsureOnChild<ResolveTargetAuto>("Movement.ResolveTarget");   
		EnsureOnChild<InteractAction>("Interact.ResolveAndUse");      
	}



	void EnsureCommentator()
	{
		EnsureOnChild<NPCActionCommentator>("Commentator");
	}

	void ApplyFeatureEnablement()
	{
		var profile = FindOnChild<NPCProfile>("Dialogue.Profile");
		if (profile) profile.gameObject.SetActive(featureDialogue);
		var manager = FindOnChild<NPCDialogueManager>("Dialogue.Manager");
		if (manager)
		{
			manager.gameObject.SetActive(featureDialogue);
			manager.enableProximityDialogue = featureDialogue && proximityDialogue;

		}
		var walk = FindOnChild<WalkAction>("Movement.Walk");
		if (walk) walk.gameObject.SetActive(featureMovement);

		var follow = FindOnChild<FollowAction>("Movement.Follow");  
		if (follow) follow.gameObject.SetActive(featureMovement);

		var ai = FindOnChild<NPCAI>("Core.AI");
		if (ai)
		{
			ai.gameObject.SetActive(true);
		}
		var resolve = FindOnChild<ResolveTargetAuto>("Movement.ResolveTarget");
		if (resolve) resolve.gameObject.SetActive(featureMovement || featureFollow);

		var inter = FindOnChild<InteractAction>("Interact.ResolveAndUse");
		if (inter) inter.enabled = featureInteractive;

		bool talkOn = featureMovement && featureCommentator;
		var talkStart = FindOnChild<TalkAction>("Movement.TalkOnStart");
		var talkDuring = FindOnChild<TalkAction>("Movement.TalkDuring");
		var talkFinish = FindOnChild<TalkAction>("Movement.TalkFinish");
		if (talkStart) talkStart.gameObject.SetActive(talkOn);
		if (talkDuring) talkDuring.gameObject.SetActive(talkOn);
		if (talkFinish) talkFinish.gameObject.SetActive(talkOn);

		var wander = FindOnChild<NPCWander>("Movement.Wander");
		if (wander)
		{
			wander.enabled = featureWander;
			wander.SetAutonomySuspended(!featureWander);
		}

		var comm = FindOnChild<NPCActionCommentator>("Commentator");
		if (comm) comm.gameObject.SetActive(featureCommentator);
		if (follow) follow.gameObject.SetActive(featureFollow);

	}

	void TrySetField(object obj, string fieldName, object value)
	{
		if (obj == null) return;
		var f = obj.GetType().GetField(fieldName,
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
		if (f == null) return;
		var cur = f.GetValue(obj);
		if (!Equals(cur, value))
			f.SetValue(obj, value);
	}

	void TrySetProperty(object obj, string propName, object value)
	{
		if (obj == null) return;
		var p = obj.GetType().GetProperty(propName,
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
		if (p == null || !p.CanWrite) return;
		var cur = p.GetValue(obj, null);
		if (!Equals(cur, value))
			p.SetValue(obj, value, null);
	}

	void TrySetFieldIfNull(object obj, string fieldName, object value)
	{
		if (obj == null || value == null) return;
		var f = obj.GetType().GetField(fieldName,
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		if (f == null) return;
		var cur = f.GetValue(obj);
		if (cur == null)
			f.SetValue(obj, value);
	}

	void TrySetIntentClient(object obj, MultiAIClient client)
	{
		if (obj == null || client == null) return;
		TrySetField(obj, "intentClient", client);
		TrySetProperty(obj, "IntentClient", client);
		TrySetField(obj, "client", client);
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
		foreach (var f in obj.GetType().GetFields(flags))
		{
			if (f.FieldType == typeof(MultiAIClient))
			{
				var cur = f.GetValue(obj);
				if (!Equals(cur, client)) f.SetValue(obj, client);
			}
		}
	}

	void WireNPCAITalkAndIntent()
	{
		var ai = FindOnChild<NPCAI>("Core.AI");
		if (!ai) return;
		var talkStart = FindOnChild<TalkAction>("Movement.TalkOnStart");
		var talkDuring = FindOnChild<TalkAction>("Movement.TalkDuring");
		var talkFinish = FindOnChild<TalkAction>("Movement.TalkFinish");
		TrySetField(ai, "talkOnStartMove", talkStart);
		TrySetField(ai, "talkDuringMove", talkDuring);
		TrySetField(ai, "talkOnFinish", talkFinish);

		var globalClient = NPCAI_GlobalClient.GetOrCreateClientInScene();
		TrySetIntentClient(ai, globalClient);
		var resolve = FindOnChild<ResolveTargetAuto>("Interact.ResolveAndUse");
		TrySetIntentClient(resolve, globalClient);
	}

	void WireDialogueIntoAll()
	{
		var mgr = FindOnChild<NPCDialogueManager>("Dialogue.Manager");
		if (mgr == null) return;
		if (mgr)
		{
			TrySetField(mgr, "animator", animator);
			TrySetField(mgr, "talkBool", talkBool);
		}
		var ai = FindOnChild<NPCAI>("Core.AI");
		if (ai)
		{
			TrySetField(ai, "dialogueManager", mgr);
			TrySetField(ai, "_dialogueManager", mgr);
		}
		var talkStart = FindOnChild<TalkAction>("Movement.TalkOnStart");
		var talkDuring = FindOnChild<TalkAction>("Movement.TalkDuring");
		var talkFinish = FindOnChild<TalkAction>("Movement.TalkFinish");
		var wander = FindOnChild<NPCWander>("Movement.Wander");
		var comm = FindOnChild<NPCActionCommentator>("Commentator");
		var dialogueAction = FindOnChild<DialogueAction>("Core.Dialogue");


		if (talkStart) TrySetField(talkStart, "dialogueManager", mgr);
		if (talkDuring) TrySetField(talkDuring, "dialogueManager", mgr);
		if (talkFinish) TrySetField(talkFinish, "dialogueManager", mgr);
		if (wander) TrySetFieldIfNull(wander, "dialogueManager", mgr);
		if (comm)
		{
			TrySetField(comm, "dialogueManager", mgr);
			TrySetProperty(comm, "DialogueManager", mgr);
		}
		if (dialogueAction) TrySetField(dialogueAction, "dialogueManager", mgr);
	}

	void WireReferences()
	{

		var ai = FindOnChild<NPCAI>("Core.AI");
		if (ai) ai.agent = GetComponent<NavMeshAgent>();
		if (ai)
		{
			TrySetField(ai, "animator", animator);
			TrySetField(ai, "walkBool", walkBool);
			TrySetField(ai, "talkBool", talkBool);
			TrySetField(ai, "interactBool", interactBool);
			TrySetField(ai, "followBool", followBool);
			TrySetField(ai, "waitBool", waitBool);
			TrySetField(ai, "speedMovement", speedMovement);
			TrySetField(ai, "verboseLogs", featureLogs);

			var wander = FindOnChild<NPCWander>("Movement.Wander");
			if (wander)
			{
				TrySetField(ai, "_wander", wander);
				TrySetField(wander, "animator", animator);
				TrySetField(wander, "wanderBool", wanderBool);
				TrySetField(ai, "wanderBool", wanderBool);
			}

		}

		var walk = FindOnChild<WalkAction>("Movement.Walk");
		if (ai && walk) TrySetField(ai, "walkStep", walk);

		var follow = FindOnChild<FollowAction>("Movement.Follow");           
		if (ai && follow) TrySetField(ai, "followStep", follow);

		var resolve = FindOnChild<ResolveTargetAuto>("Movement.ResolveTarget");
		if (ai && resolve)
		{
			TrySetField(ai, "resolveStep", resolve);
			TrySetField(resolve, "_client", ai.intentClient);

		}

		var comm = FindOnChild<NPCActionCommentator>("Commentator");
		if (ai) TrySetField(ai, "commentator", comm);

		var mgr = FindOnChild<NPCDialogueManager>("Dialogue.Manager");
		var prof = FindOnChild<NPCProfile>("Dialogue.Profile");
		var gpt = NPCAI_GlobalClient.GetOrCreateClientInScene();
		if (mgr)
		{
			TrySetField(mgr, "client", gpt); TrySetProperty(mgr, "Client", gpt);
			TrySetField(mgr, "npc", prof); TrySetProperty(mgr, "Npc", prof);
		}
		var wait = FindOnChild<WaitAction>("Core.Wait");
		if (ai && wait) TrySetField(ai, "waitStep", wait);

		var dialogue = FindOnChild<DialogueAction>("Core.Dialogue");
		if (ai && dialogue) TrySetField(ai, "dialogueStep", dialogue);

		var interact = FindOnChild<InteractAction>("Interact.ResolveAndUse");
		if (interact) TrySetField(interact, "verboseLogs", featureLogs);

		WireDialogueIntoAll();
		WireNPCAITalkAndIntent();
	}


	void EnsureNavAgentWiring()
	{
		var agent = EnsureAgentOnRoot();
		var holder = EnsureHolder(this);
		var ai = FindOnChild<NPCAI>("Core.AI");
		var walk = FindOnChild<WalkAction>("Movement.Walk");
		var wz = FindOnChild<NPCWander>("Movement.Wander");
		var res = FindOnChild<ResolveTargetAuto>("Interact.ResolveAndUse");
		TrySetFieldIfNull(ai, "agent", agent);
		TrySetFieldIfNull(walk, "agentOverride", agent);
		TrySetFieldIfNull(wz, "agent", agent);
		TrySetFieldIfNull(res, "agent", agent);

		foreach (var mb in holder.GetComponentsInChildren<MonoBehaviour>(true))
		{
			TrySetFieldIfNull(mb, "agent", agent);
			TrySetFieldIfNull(mb, "agentOverride", agent);
		}
	}

	void ApplyAutoDefaults()
	{
		if (string.IsNullOrWhiteSpace(npcName)) npcName = gameObject.name;
		if (string.IsNullOrWhiteSpace(npcMood)) npcMood = "neutral";
		if (string.IsNullOrWhiteSpace(npcBackstory)) npcBackstory = $"This is {npcName}. Keeps a low profile, but is helpful.";
		arriveDistance = Mathf.Max(0.01f, arriveDistance);
		interactRadius = Mathf.Max(0.1f, interactRadius);
		bubbleVisibleTime = Mathf.Max(0.5f, bubbleVisibleTime);
	}

	public void PerformOneClickSetup() => AutoSync();
	public void ApplyRecommended()
	{
		featureDialogue = true;
		featureMovement = true;
		featureInteract = true;
		featureCommentator = true;
		featureWander = false;
		AutoSync();
	}

	public void ExecuteCommand(string userCommand)
	{
		var ai = FindOnChild<NPCAI>("Core.AI");
		if (!ai)
		{
			Debug.LogWarning("NPCAIHub.ExecuteCommand: NPCAI not found.");
			return;
		}

		ai.allowDialogue = featureDialogue;
		ai.allowMovement = featureMovement;
		ai.allowInteractive = featureInteractive;
		ai.allowFollow = featureFollow;

		var inter = FindOnChild<InteractAction>("Interact.ResolveAndUse");
		ai.interactStep = (inter && featureInteractive && (featureMovement || featureFollow)) ? inter : null;

		ai.GoTo(userCommand);
	}


	public bool TryStartDialogue(string text)
	{
		if (!featureDialogue) return false;
		var mgr = FindOnChild<NPCDialogueManager>("Dialogue.Manager");
		if (!mgr) return false;
		mgr.SendDialogue(string.IsNullOrWhiteSpace(text) ? "" : text);
		return true;
	}
}
