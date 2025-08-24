using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GlobalChatGPTClient : MonoBehaviour
{
	private const string GlobalName = "NPCAI_GlobalClient";
	private static ChatGPTClient _cached;

	void OnEnable()
	{
		var client = GetOrCreateClientInScene();
		ApplySettingsToClient(client, NPCAIOpenAISettings.Instance);
	}

	public static ChatGPTClient GetOrCreateClientInScene()
	{
		if (_cached != null) return _cached;

		var go = GameObject.Find(GlobalName);
		if (go == null)
		{
			go = new GameObject(GlobalName);
#if UNITY_EDITOR
			Undo.RegisterCreatedObjectUndo(go, "Create " + GlobalName);
#endif
		}
		var client = go.GetComponent<ChatGPTClient>();
		if (client == null)
		{
#if UNITY_EDITOR
			client = Undo.AddComponent<ChatGPTClient>(go);
#else
            client = go.AddComponent<ChatGPTClient>();
#endif
		}

		_cached = client;
		return _cached;
	}

	public static void ApplySettingsToClient(ChatGPTClient client, NPCAIOpenAISettings s)
	{
		if (client == null || s == null) return;
		client.apiKey = s.apiKey;
		client.model = string.IsNullOrWhiteSpace(s.model) ? "gpt-4o-mini" : s.model;
		client.temperature = Mathf.Clamp01(s.temperature);
		client.defaultResponseLanguage = string.IsNullOrWhiteSpace(s.defaultResponseLanguage) ? "auto" : s.defaultResponseLanguage;
#if UNITY_EDITOR
		EditorUtility.SetDirty(client);
#endif
	}

#if UNITY_EDITOR
	[MenuItem("NPCAI/Apply Settings To Scene")]
	public static void ApplyToAllNPCsInScene()
	{
		var client = GetOrCreateClientInScene();
		ApplySettingsToClient(client, NPCAIOpenAISettings.Instance);

		var hubs = GameObject.FindObjectsOfType<NPCAIHub>(true);
		foreach (var hub in hubs)
		{
			var holder = NPCAIHub.EnsureHolder(hub);
			var tMgr = holder.Find("Dialogue.Manager");
			if (tMgr != null)
			{
				var mgr = tMgr.GetComponent<NPCDialogueManager>();
				if (mgr != null)
				{
					var fClient = typeof(NPCDialogueManager).GetField("client",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					if (fClient != null && fClient.GetValue(mgr) == null)
					{
						fClient.SetValue(mgr, client);
						EditorUtility.SetDirty(mgr);
					}
				}
			}
		}

		EditorUtility.DisplayDialog("NPCAI", "Settings applied to scene NPCs.", "OK");
	}
#endif
}
