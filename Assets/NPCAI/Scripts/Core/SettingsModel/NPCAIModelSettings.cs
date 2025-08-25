using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIProviderType { OpenAI, DeepSeek, Anthropic }

[Serializable]
public class ProviderProfile
{
	public AIProviderType provider;
	public string apiKey = "";
	public string model = "gpt-4o-mini"; // пример по умолчанию дл€ OpenAI
	[Range(0f, 1f)] public float temperature = 0.7f;
	public int maxTokens = 1024;
	public string defaultResponseLanguage = "auto";
}

public class NPCAIModelSettings : ScriptableObject
{
	private const string DefaultAssetPath = "Assets/NPCAI/Model_Settings.asset";

	[Header("Active Provider")]
	public AIProviderType provider = AIProviderType.OpenAI;

	[SerializeField] private List<ProviderProfile> profiles = new List<ProviderProfile>();

	// ==== API ====

	/// “екущий активный профиль (создаст дефолт, если отсутствует).
	public ProviderProfile GetActiveProfile()
	{
		var p = profiles.Find(x => x.provider == provider);
		if (p == null)
		{
			p = CreateDefaultProfile(provider);
			profiles.Add(p);
		}
		return p;
	}

	/// ¬ернуть/создать профиль дл€ конкретного провайдера (не мен€€ активного).
	public ProviderProfile GetProfile(AIProviderType prov)
	{
		var p = profiles.Find(x => x.provider == prov);
		if (p == null)
		{
			p = CreateDefaultProfile(prov);
			profiles.Add(p);
		}
		return p;
	}

	/// ѕереключение провайдера Ч просто мен€ем active flag, данные уже лежат в профил€х.
	public void SwitchProvider(AIProviderType newProvider)
	{
		provider = newProvider;
		// лениво создадим профиль, если ещЄ нет
		GetActiveProfile();
#if UNITY_EDITOR
		UnityEditor.EditorUtility.SetDirty(this);
#endif
	}

	// ==== Helpers ====

	private static ProviderProfile CreateDefaultProfile(AIProviderType prov)
	{
		var p = new ProviderProfile { provider = prov };
		switch (prov)
		{
			case AIProviderType.OpenAI:
				p.model = "gpt-4o-mini";
				break;
			case AIProviderType.DeepSeek:
				p.model = "deepseek-chat";         // попул€рна€ модель DeepSeek
				break;
			case AIProviderType.Anthropic:
				p.model = "claude-3-5-sonnet-latest";
				p.maxTokens = 2048;                // чаще нужен больший лимит
				break;
		}
		return p;
	}

#if UNITY_EDITOR
	private static NPCAIModelSettings _instance;
	public static NPCAIModelSettings Instance
	{
		get
		{
			if (_instance != null) return _instance;
			var so = UnityEditor.AssetDatabase.LoadAssetAtPath<NPCAIModelSettings>(DefaultAssetPath);
			if (so == null)
			{
				System.IO.Directory.CreateDirectory("Assets/NPCAI");
				so = CreateInstance<NPCAIModelSettings>();
				UnityEditor.AssetDatabase.CreateAsset(so, DefaultAssetPath);
				UnityEditor.AssetDatabase.SaveAssets();
				UnityEditor.AssetDatabase.Refresh();
			}
			_instance = so;
			// гарантируем, что у активного провайдера есть профиль
			_instance.GetActiveProfile();
			return _instance;
		}
	}
#endif
}
