using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class NPCAIOpenAISettings : ScriptableObject
{
	private const string DefaultAssetPath = "Assets/NPCAI/OpenAI_Settings.asset";

	[Header("OpenAI Settings")]
	public string apiKey = "";
	public string model = "gpt-4o-mini";
	[Range(0f, 1f)] public float temperature = 0.7f;

	[Header("Language")]
	public string defaultResponseLanguage = "auto";

	private static NPCAIOpenAISettings _instance;
	public static NPCAIOpenAISettings Instance
	{
		get
		{
			if (_instance != null) return _instance;

#if UNITY_EDITOR
			_instance = AssetDatabase.LoadAssetAtPath<NPCAIOpenAISettings>(DefaultAssetPath);
			if (_instance == null)
			{
				var guids = AssetDatabase.FindAssets("t:NPCAIOpenAISettings");
				if (guids != null && guids.Length > 0)
				{
					var path = AssetDatabase.GUIDToAssetPath(guids[0]);
					_instance = AssetDatabase.LoadAssetAtPath<NPCAIOpenAISettings>(path);
				}
			}
			if (_instance == null)
			{
				System.IO.Directory.CreateDirectory("Assets/NPCAI/Settings");
				_instance = ScriptableObject.CreateInstance<NPCAIOpenAISettings>();
				AssetDatabase.CreateAsset(_instance, DefaultAssetPath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}
#else
            _instance = Resources.Load<NPCAIOpenAISettings>("NPCAIOpenAISettings");
            if (_instance == null)
            {
                _instance = ScriptableObject.CreateInstance<NPCAIOpenAISettings>();
            }
#endif
			return _instance;
		}
	}
}
