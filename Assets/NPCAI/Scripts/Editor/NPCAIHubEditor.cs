#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

[CustomEditor(typeof(NPCAIHub)), CanEditMultipleObjects]
public class NPCAIHubEditor : Editor
{
	bool foldDialogue = true;
	bool foldMovement = true;
	bool foldWander = false;
	bool foldCommentator = false;

	GUIStyle _title, _tag, _box, _featureOn, _featureOff;
	Texture2D _fallbackLogo;

	Editor edDialogueMgr, edSpeechBubble, edProfile;
	Editor edWalk, edResolve, edInteract, edCommentator, edWander, edAI;
	Editor edTalkStart, edTalkDuring, edTalkFinish;

	void OnEnable()
	{
		_fallbackLogo = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
		var hub = (NPCAIHub)target;
		NPCAIHub.EnsureHolder(hub);
	}

	void OnDisable()
	{
		edDialogueMgr = null;
		edSpeechBubble = null;
		edProfile = null;
		edWalk = null;
		edResolve = null;
		edInteract = null;
		edCommentator = null;
		edWander = null;
		edAI = null;
		edTalkStart = null;
		edTalkDuring = null;
		edTalkFinish = null;
	}

	bool _stylesBuilt;

	public override void OnInspectorGUI()
	{
		if (!_stylesBuilt)
		{
			BuildStyles();
			_stylesBuilt = true;
		}
		if (target == null) return;
		if (serializedObject == null) return;

		serializedObject.Update();
		var hub = target as NPCAIHub;
		if (hub == null) return;

		DrawHeader(hub);
		DrawFeaturesBar(hub);
		EditorGUILayout.Space(6);


		// ===== DIALOGUE =====
		if (hub.featureDialogue)
		{
			foldDialogue = DrawSectionHeader("Dialogue", foldDialogue);
			if (foldDialogue)
			{
				using (new EditorGUILayout.VerticalScope(_box))
				{
					var prof = FindFlat<NPCProfile>(hub, "Dialogue.Profile");
					if (prof != null)
					{
						EditorGUILayout.LabelField("Profile", _tag);
						CreateCachedEditorSafe(prof, ref edProfile);
						edProfile?.OnInspectorGUI();
					}

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
						EditorGUILayout.LabelField("NPCAI", _tag);
						CreateCachedEditorSafe(ai, ref edAI);
						edAI?.OnInspectorGUI();
					}

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
		if (targetObj == null)
		{
			if (editor != null) editor = null;
			return;
		}
		if (editor == null || editor.target != targetObj)
		{
			CreateCachedEditor(targetObj, null, ref editor);
		}
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
		var rect = EditorGUILayout.GetControlRect(false, 72);
		using (new GUI.GroupScope(rect))
		{
			EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f, EditorGUIUtility.isProSkin ? 0.9f : 0.6f));
			var logo = hub.logo ? hub.logo : _fallbackLogo;
			var logoRect = new Rect(rect.x + 10, rect.y + 8, 56, 56);
			if (logo) GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit, true);
			var titleRect = new Rect(logoRect.xMax + 8, rect.y + 8, rect.width - logoRect.width - 18, 24);
			GUI.Label(titleRect, "NPCAI Hub — One-Click Controller", _title);
			var subRect = new Rect(logoRect.xMax + 8, rect.y + 32, rect.width - logoRect.width - 18, 20);
			GUI.Label(subRect, "Quick setup of all modules in one inspector", EditorStyles.miniLabel);
		}
	}

	void DrawFeaturesBar(NPCAIHub hub)
	{
		using (new EditorGUILayout.VerticalScope(_box))
		{
			EditorGUILayout.LabelField("Functions", _tag);
			using (new EditorGUILayout.HorizontalScope())
			{
				ToggleFeatureProp("Dialogue", "featureDialogue", "d_AudioSource Icon", "Dialogue", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Movement", "featureMovement", "d_NavMeshAgent Icon", "Movement", () => hub.PerformOneClickSetup());
			}
			using (new EditorGUILayout.HorizontalScope())
			{
				ToggleFeatureProp("Wander", "featureWander", "d_NavMeshObstacle Icon", "Wander", () => hub.PerformOneClickSetup());
				ToggleFeatureProp("Commentator", "featureCommentator", "d_Profiler.Audio", "Commentator", () => hub.PerformOneClickSetup());
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
