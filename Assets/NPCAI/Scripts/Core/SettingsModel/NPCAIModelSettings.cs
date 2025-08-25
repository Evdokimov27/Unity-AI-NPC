using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIProviderType { OpenAI, DeepSeek, Anthropic }

[Serializable]
public class ProviderProfile
{
	public AIProviderType provider;
	public string apiKey = "";
	public string model = "gpt-4o-mini"; // ������ �� ��������� ��� OpenAI
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

	/// ������� �������� ������� (������� ������, ���� �����������).
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

	/// �������/������� ������� ��� ����������� ���������� (�� ����� ���������).
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

	/// ������������ ���������� � ������ ������ active flag, ������ ��� ����� � ��������.
	public void SwitchProvider(AIProviderType newProvider)
	{
		provider = newProvider;
		// ������ �������� �������, ���� ��� ���
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
				p.model = "deepseek-chat";         // ���������� ������ DeepSeek
				break;
			case AIProviderType.Anthropic:
				p.model = "claude-3-5-sonnet-latest";
				p.maxTokens = 2048;                // ���� ����� ������� �����
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
			// �����������, ��� � ��������� ���������� ���� �������
			_instance.GetActiveProfile();
			return _instance;
		}
	}
#endif
}
