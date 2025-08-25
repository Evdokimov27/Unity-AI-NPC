using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class NPCAI_GlobalClient : MonoBehaviour
{
	private const string GlobalName = "NPCAI_GlobalClient";
	private static MultiAIClient _cached;

	void OnEnable()
	{
		var client = GetOrCreateClientInScene();
#if UNITY_EDITOR
		ApplySettingsToClient(client, NPCAIModelSettings.Instance);
#else
        // В рантайме можно подложить ScriptableObject из ресурсов, если нужно
        ApplySettingsToClient(client, client.settingsAsset != null ? client.settingsAsset : null);
#endif
	}

	public static MultiAIClient GetOrCreateClientInScene()
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
		var client = go.GetComponent<MultiAIClient>();
		if (client == null)
		{
#if UNITY_EDITOR
			client = Undo.AddComponent<MultiAIClient>(go);
#else
            client = go.AddComponent<MultiAIClient>();
#endif
		}

		_cached = client;
		return _cached;
	}

	public static void ApplySettingsToClient(MultiAIClient client, NPCAIModelSettings s)
	{
		if (client == null) return;

		if (s != null)
		{
			client.settingsAsset = s;

			var p = s.GetActiveProfile();
			client.provider = s.provider;
			client.apiKey = p.apiKey;
			client.model = string.IsNullOrWhiteSpace(p.model) ? client.model : p.model;
			client.temperature = Mathf.Clamp01(p.temperature);
			client.maxTokens = Mathf.Max(16, p.maxTokens);
			client.defaultResponseLanguage = string.IsNullOrWhiteSpace(p.defaultResponseLanguage) ? "auto" : p.defaultResponseLanguage;
		}

#if UNITY_EDITOR
		UnityEditor.EditorUtility.SetDirty(client);
#endif
	}


#if UNITY_EDITOR
	[MenuItem("NPCAI/Apply Settings To Scene")]
	public static void ApplyToAllNPCsInScene()
	{
		var client = GetOrCreateClientInScene();
		ApplySettingsToClient(client, NPCAIModelSettings.Instance);

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
