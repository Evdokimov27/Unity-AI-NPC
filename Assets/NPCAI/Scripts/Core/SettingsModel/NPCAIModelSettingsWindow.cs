#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class NPCAIModelSettingsWindow : EditorWindow
{
	NPCAIModelSettings s;

	[MenuItem("NPCAI/Model Settings")]
	public static void Open()
	{
		var wnd = GetWindow<NPCAIModelSettingsWindow>("AI Model Settings");
		wnd.minSize = new Vector2(520, 320);
		wnd.Show();
	}

	void OnEnable() { s = NPCAIModelSettings.Instance; }

	void OnGUI()
	{
		if (s == null)
		{
			EditorGUILayout.HelpBox("Settings asset not found/created.", MessageType.Warning);
			if (GUILayout.Button("Create/Reload Settings"))
				s = NPCAIModelSettings.Instance;
			return;
		}

		// Provider (переключение — моментально меняет активный профиль)
		EditorGUILayout.LabelField("Provider", EditorStyles.boldLabel);
		var newProv = (AIProviderType)EditorGUILayout.EnumPopup("Type", s.provider);
		if (newProv != s.provider)
		{
			Undo.RecordObject(s, "Switch AI Provider");
			s.SwitchProvider(newProv);
		}

		// Активный профиль
		var p = s.GetActiveProfile();

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("Credentials", EditorStyles.boldLabel);
		EditorGUI.BeginChangeCheck();
		p.apiKey = EditorGUILayout.TextField("API Key", p.apiKey);

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("Model & Generation", EditorStyles.boldLabel);
		p.model = EditorGUILayout.TextField("Model", p.model);
		p.temperature = EditorGUILayout.Slider("Temperature", p.temperature, 0f, 1f);
		p.maxTokens = EditorGUILayout.IntField("Max Tokens", p.maxTokens);

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("Language", EditorStyles.boldLabel);
		p.defaultResponseLanguage = EditorGUILayout.TextField("Default Response Language", p.defaultResponseLanguage);

		if (EditorGUI.EndChangeCheck())
		{
			Undo.RecordObject(s, "Edit AI Profile");
			EditorUtility.SetDirty(s);
		}

		EditorGUILayout.Space(12);
		using (new EditorGUILayout.HorizontalScope())
		{
			if (GUILayout.Button("Ensure Global Client in Scene"))
			{
				var client = NPCAI_GlobalClient.GetOrCreateClientInScene();
				NPCAI_GlobalClient.ApplySettingsToClient(client, s);
				EditorGUIUtility.PingObject(client);
			}
			if (GUILayout.Button("Apply To All NPCs In Scene"))
			{
				NPCAI_GlobalClient.ApplyToAllNPCsInScene();
			}
		}

		GUILayout.FlexibleSpace();
		EditorGUILayout.HelpBox(
			"Настройки сохраняются в профиль для каждого провайдера. "
			+ "При переключении Provider подставляются сохранённые данные.",
			MessageType.Info);
	}
}
#endif
