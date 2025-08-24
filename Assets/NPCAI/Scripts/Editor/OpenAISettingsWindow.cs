#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class NPCAIOpenAISettingsWindow : EditorWindow
{
	NPCAIOpenAISettings settings;

	[MenuItem("NPCAI/OpenAI Settings")]
	public static void Open()
	{
		var wnd = GetWindow<NPCAIOpenAISettingsWindow>("OpenAI Settings");
		wnd.maxSize = new Vector2(420, 220);
		wnd.minSize = new Vector2(420, 220);
		wnd.Show();
	}

	void OnEnable()
	{
		settings = NPCAIOpenAISettings.Instance;
	}

	void OnGUI()
	{
		if (settings == null)
		{
			EditorGUILayout.HelpBox("Settings asset not found/created.", MessageType.Warning);
			if (GUILayout.Button("Create/Reload Settings"))
			{
				settings = NPCAIOpenAISettings.Instance;
			}
			return;
		}

		EditorGUILayout.LabelField("OpenAI Settings", EditorStyles.boldLabel);
		EditorGUI.BeginChangeCheck();
		settings.apiKey = EditorGUILayout.TextField("Api Key", settings.apiKey);
		settings.model = EditorGUILayout.TextField("Model", settings.model);
		settings.temperature = EditorGUILayout.Slider("Temperature", settings.temperature, 0f, 1f);

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("Language", EditorStyles.boldLabel);
		settings.defaultResponseLanguage = EditorGUILayout.TextField("Default Response Language", settings.defaultResponseLanguage);

		if (EditorGUI.EndChangeCheck())
		{
			EditorUtility.SetDirty(settings);
		}

		EditorGUILayout.Space(12);
		using (new EditorGUILayout.HorizontalScope())
		{
			if (GUILayout.Button("Ensure Global Client in Scene"))
			{
				var client = GlobalChatGPTClient.GetOrCreateClientInScene();
				GlobalChatGPTClient.ApplySettingsToClient(client, settings);
				EditorGUIUtility.PingObject(client);
			}
			if (GUILayout.Button("Apply To All NPCs In Scene"))
			{
				GlobalChatGPTClient.ApplyToAllNPCsInScene();
			}
		}

		GUILayout.FlexibleSpace();
		EditorGUILayout.HelpBox("These settings are used by all NPCs through the global client/settings.", MessageType.Info);
	}
}
#endif
