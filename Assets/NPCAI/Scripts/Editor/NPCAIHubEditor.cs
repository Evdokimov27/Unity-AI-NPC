#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

[CustomEditor(typeof(NPCAIHub)), CanEditMultipleObjects]
public class NPCAIHubEditor : Editor
{
	// foldouts
	bool foldProfile = true;
	bool foldDialogue = true;
	bool foldMovement = true;
	bool foldFollow = false;
	bool foldWander = false;
	bool foldInteractive = false;
	bool foldCommentator = false;
	bool foldLogs = false;

	// styles
	GUIStyle _title, _tag, _box, _featureOn, _featureOff;
	Texture2D _fallbackLogo;

	// cached editors
	Editor edProfile, edDialogueMgr, edSpeechBubble;
	Editor edWalk, edFollow, edResolve, edInteract;
	Editor edCommentator, edWander, edAI;
	Editor edTalkStart, edTalkDuring, edTalkFinish;

	bool _stylesBuilt;
	string commandInput = "";


	void OnEnable()
	{
		_fallbackLogo = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
		var hub = (NPCAIHub)target;
		NPCAIHub.EnsureHolder(hub);
	}

	void OnDisable()
	{
		edProfile = edDialogueMgr = edSpeechBubble = null;
		edWalk = edFollow = edResolve = edInteract = null;
		edCommentator = edWander = edAI = null;
		edTalkStart = edTalkDuring = edTalkFinish = null;
	}

	public override void OnInspectorGUI()
	{
		if (!_stylesBuilt) { BuildStyles(); _stylesBuilt = true; }
		if (target == null || serializedObject == null) return;

		serializedObject.Update();
		var hub = target as NPCAIHub;
		if (hub == null) return;

		DrawHeader(hub);
		DrawFeaturesBar(hub);
		EditorGUILayout.Space(8);
		DrawCommandConsole(hub);
		EditorGUILayout.Space(6);

		// ===== ANIMATION =====
		using (new EditorGUILayout.VerticalScope(_box))
		{
			EditorGUILayout.LabelField("Animator", _tag);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("animator"));

			EditorGUILayout.Space(2);
			EditorGUILayout.LabelField("Bool Parameters (optional)", EditorStyles.miniBoldLabel);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("walkBool"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("talkBool"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("interactBool"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("followBool"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("waitBool"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wanderBool"));
		}

		// ===== PROFILE =====
		foldProfile = DrawSectionHeader("Dialogue.Profile", foldProfile);
		if (foldProfile)
		{
			using (new EditorGUILayout.VerticalScope(_box))
			{
				var prof = FindFlat<NPCProfile>(hub, "Dialogue.Profile");
				if (prof != null)
				{
					EditorGUILayout.LabelField("NPC Profile", _tag);
					CreateCachedEditorSafe(prof, ref edProfile);
					edProfile?.OnInspectorGUI();
				}
			}
		}

		// ===== DIALOGUE =====
		if (hub.featureDialogue)
		{
			foldDialogue = DrawSectionHeader("Dialogue", foldDialogue);
			if (foldDialogue)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var bubble = FindFlat<NPCSpeechBubble>(hub, "Dialogue.SpeechBubble");
					if (bubble != null)
					{
						EditorGUILayout.LabelField("Speech Bubble", _tag);
						CreateCachedEditorSafe(bubble, ref edSpeechBubble);
						edSpeechBubble?.OnInspectorGUI();
					}

					var mgr = FindFlat<NPCDialogueManager>(hub, "Dialogue.Manager");
					if (mgr != null)
					{
						EditorGUILayout.LabelField("Dialogue Manager", _tag);
						CreateCachedEditorSafe(mgr, ref edDialogueMgr);
						edDialogueMgr?.OnInspectorGUI();
					}
				}
			}
		}

		// ===== MOVEMENT =====
		if (hub.featureMovement)
		{
			foldMovement = DrawSectionHeader("Movement", foldMovement);
			if (foldMovement)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var rootAgent = hub.GetComponent<NavMeshAgent>();
					using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
					{
						if (rootAgent)
						{
							EditorGUILayout.LabelField("NavMeshAgent on Hub", _tag);
							EditorGUILayout.LabelField($"Speed: {rootAgent.speed}, Accel: {rootAgent.acceleration}, Radius: {rootAgent.radius}", EditorStyles.miniLabel);
						}
						else
						{
							EditorGUILayout.HelpBox("NavMeshAgent not found!", MessageType.Warning);
						}
					}

					var walk = FindFlat<WalkAction>(hub, "Movement.Walk");
					if (walk != null)
					{
						walk.agentOverride = rootAgent;
						EditorGUILayout.LabelField("WalkAction", _tag);
						CreateCachedEditorSafe(walk, ref edWalk);
						edWalk?.OnInspectorGUI();
					}

					var ai = FindFlat<NPCAI>(hub, "Core.AI");
					if (ai != null)
					{
						EditorGUILayout.LabelField("NPCAI (Movement)", _tag);
						ai.allowMovement = EditorGUILayout.Toggle("Allow Movement", ai.allowMovement);
						ai.taskMode = (NPCAI.TaskMode)EditorGUILayout.EnumPopup("Task Mode", ai.taskMode);
					}
				}
			}
		}

		// ===== FOLLOW =====
		if (hub.featureFollow)
		{
			foldFollow = DrawSectionHeader("Follow", foldFollow);
			if (foldFollow)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var follow = FindFlat<FollowAction>(hub, "Follow.Module");
					if (follow != null)
					{
						EditorGUILayout.LabelField("FollowAction", _tag);
						CreateCachedEditorSafe(follow, ref edFollow);
						edFollow?.OnInspectorGUI();
					}
				}
			}
		}

		// ===== WANDER =====
		if (hub.featureWander)
		{
			foldWander = DrawSectionHeader("Wander", foldWander);
			if (foldWander)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var wz = FindFlat<NPCWander>(hub, "Movement.Wander");
					if (wz != null)
					{
						EditorGUILayout.LabelField("NPCWander", _tag);
						CreateCachedEditorSafe(wz, ref edWander);
						edWander?.OnInspectorGUI();
					}
				}
			}
		}

		// ===== INTERACTIVE =====
		if (hub.featureInteractive)
		{
			foldInteractive = DrawSectionHeader("Interactive", foldInteractive);
			if (foldInteractive)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var resolve = FindFlat<ResolveTargetAuto>(hub, "Interact.ResolveAndUse");
					if (resolve != null)
					{
						EditorGUILayout.LabelField("Resolve Target", _tag);
						CreateCachedEditorSafe(resolve, ref edResolve);
						edResolve?.OnInspectorGUI();

						var inter = resolve.GetComponent<InteractAction>();
						if (inter != null)
						{
							EditorGUILayout.LabelField("Interact Action", _tag);
							CreateCachedEditorSafe(inter, ref edInteract);
							edInteract?.OnInspectorGUI();
						}
					}
				}
			}
		}

		// ===== COMMENTATOR =====
		if (hub.featureCommentator)
		{
			foldCommentator = DrawSectionHeader("Commentator", foldCommentator);
			if (foldCommentator)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var comm = FindFlat<NPCActionCommentator>(hub, "Commentator");
					if (comm != null)
					{
						EditorGUILayout.LabelField("Action Commentator", _tag);
						CreateCachedEditorSafe(comm, ref edCommentator);
						edCommentator?.OnInspectorGUI();
					}

					var talkStart = FindFlat<TalkAction>(hub, "Movement.TalkOnStart");
					var talkDuring = FindFlat<TalkAction>(hub, "Movement.TalkDuring");
					var talkFinish = FindFlat<TalkAction>(hub, "Movement.TalkFinish");

					if (talkStart != null)
					{
						EditorGUILayout.LabelField("Talk On Start", _tag);
						CreateCachedEditorSafe(talkStart, ref edTalkStart);
						edTalkStart?.OnInspectorGUI();
					}
					if (talkDuring != null)
					{
						EditorGUILayout.LabelField("Talk During", _tag);
						CreateCachedEditorSafe(talkDuring, ref edTalkDuring);
						edTalkDuring?.OnInspectorGUI();
					}
					if (talkFinish != null)
					{
						EditorGUILayout.LabelField("Talk On Finish", _tag);
						CreateCachedEditorSafe(talkFinish, ref edTalkFinish);
						edTalkFinish?.OnInspectorGUI();
					}
				}
			}
		}
		serializedObject.ApplyModifiedProperties();
	}

	// ===== Helpers =====
	static T FindFlat<T>(NPCAIHub hub, string flatName) where T : Component
	{
		var holder = NPCAIHub.EnsureHolder(hub);
		var t = holder.Find(flatName);
		return t ? t.GetComponent<T>() : null;
	}

	void CreateCachedEditorSafe(Object targetObj, ref Editor editor)
	{
		if (targetObj == null) { editor = null; return; }
		if (editor == null || editor.target != targetObj)
			CreateCachedEditor(targetObj, null, ref editor);
	}

	bool DrawSectionHeader(string title, bool expanded)
	{
		var rect = EditorGUILayout.GetControlRect(false, 24);
		rect = EditorGUI.IndentedRect(rect);
		var bg = new Rect(rect.x, rect.y + 2, rect.width, rect.height - 4);
		EditorGUI.DrawRect(bg, new Color(0.12f, 0.12f, 0.12f, EditorGUIUtility.isProSkin ? 0.6f : 0.2f));
		var foldRect = new Rect(rect.x + 4, rect.y + 3, 16, 16);
		var labelRect = new Rect(rect.x + 22, rect.y + 3, rect.width - 22, 18);
		expanded = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
		EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
		return expanded;
	}

	void DrawHeader(NPCAIHub hub)
	{
		var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
										GUILayout.Height(72), GUILayout.ExpandWidth(true));

		EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f,
			EditorGUIUtility.isProSkin ? 0.9f : 0.6f));

		var logo = hub.logo ? hub.logo : _fallbackLogo;
		var logoRect = new Rect(rect.x + 10, rect.y + 8, 56, 56);
		if (logo) GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit, true);

		var titleRect = new Rect(logoRect.xMax + 8, rect.y + 8,
								 rect.width - logoRect.width - 18, 36);
		GUI.Label(titleRect, "NPCAI Hub — One-Click Controller", _title);

		var subRect = new Rect(logoRect.xMax + 8, rect.y + 32,
							   rect.width - logoRect.width - 18, 28);
		GUI.Label(subRect, "Quick setup of all modules in one inspector", EditorStyles.miniLabel);
		EditorGUILayout.Space(6);

	}

	void DrawFeaturesBar(NPCAIHub hub)
	{
		using (new EditorGUILayout.VerticalScope(_box))
		{
			EditorGUILayout.LabelField("Functions", _tag);
			using (new EditorGUILayout.HorizontalScope())
			{
				ToggleFeatureProp("Movement", "featureMovement", "d_NavMeshAgent Icon", "Movement", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Wander", "featureWander", "d_NavMeshObstacle Icon", "Wander", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Follow", "featureFollow", "d_StepButton", "Follow module", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Interactive", "featureInteractive", "d_ToolHandleLocal", "Interactive", () => hub.PerformOneClickSetup());
			}
			using (new EditorGUILayout.HorizontalScope())
			{
				ToggleFeatureProp("Dialogue", "featureDialogue", "d_AudioSource Icon", "Dialogue", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Commentator", "featureCommentator", "d_Profiler.Audio", "Commentator", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Logs", "featureLogs", "d_UnityEditor.ConsoleWindow", "Logging", () => hub.PerformOneClickSetup());
			}
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
			}
		}
	}

	void ToggleFeatureProp(string label, string propPath, string iconName, string tooltip, System.Action onAfterApply)
	{
		var prop = serializedObject.FindProperty(propPath);
		bool value = prop != null && prop.boolValue;
		var icon = EditorGUIUtility.IconContent(iconName).image;
		var content = new GUIContent(label, icon, tooltip);
		var style = value ? _featureOn : _featureOff;

		EditorGUI.BeginChangeCheck();
		bool newVal = GUILayout.Toggle(value, content, style, GUILayout.Height(28));
		if (EditorGUI.EndChangeCheck())
		{
			Undo.RecordObject(target, $"Toggle {label}");
			if (prop != null) prop.boolValue = newVal;
			serializedObject.ApplyModifiedProperties();
			EditorUtility.SetDirty(target);
			onAfterApply?.Invoke();
		}
	}

	

	void DrawCommandConsole(NPCAIHub hub)
	{
		using (new EditorGUILayout.VerticalScope(_box))
		{
			EditorGUILayout.LabelField("Command Console", _tag);
			using (new EditorGUILayout.HorizontalScope())
			{
				commandInput = EditorGUILayout.TextField(commandInput);
				if (GUILayout.Button("Run", GUILayout.Width(60)))
				{
					RunForSelectedHubs(h =>
					{
						Undo.RecordObject(h, "Execute Command");
						h.ExecuteCommand(commandInput);
						EditorUtility.SetDirty(h);
					});
				}
			}
		}
	}

	void RunForSelectedHubs(System.Action<NPCAIHub> action)
	{
		foreach (var obj in targets)
		{
			var hub = obj as NPCAIHub;
			if (!hub) continue;
			action?.Invoke(hub);
		}
	}

	void BuildStyles()
	{
		_title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
		_tag = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
		_box = new GUIStyle("HelpBox") { padding = new RectOffset(8, 8, 8, 8) };
		_featureOn = new GUIStyle("Button") { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fixedHeight = 28, padding = new RectOffset(10, 10, 4, 4) };
		_featureOff = new GUIStyle("Button") { alignment = TextAnchor.MiddleLeft, fixedHeight = 28, padding = new RectOffset(10, 10, 4, 4) };
	}
}
#endif
