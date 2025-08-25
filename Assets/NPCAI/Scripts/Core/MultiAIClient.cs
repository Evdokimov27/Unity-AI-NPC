using System;
using System.Collections.Generic;
using UnityEngine;

public class MultiAIClient : MonoBehaviour
{
	[Header("Settings (optional override)")]
	public NPCAIModelSettings settingsAsset;

	// »нлайн-пол€ оставим как fallback, но если есть settingsAsset Ч берЄм из активного профил€
	public AIProviderType provider = AIProviderType.OpenAI;
	public string apiKey = "";
	public string model = "gpt-4o-mini";
	[Range(0, 1)] public float temperature = 0.7f;
	public int maxTokens = 1024;
	public string defaultResponseLanguage = "auto";

	public void Ask(string systemPrompt, string userPrompt, Action<string> onReply, string responseLanguageOverride = null)
	{
		var s = BuildEffectiveSettings();
		var prof = s.GetActiveProfile(); // активный профиль провайдера
		string finalSystem = WithLanguageDirective(systemPrompt, responseLanguageOverride ?? prof.defaultResponseLanguage);

		IChatProvider providerImpl = CreateProvider(s);
		StartCoroutine(providerImpl.SendChat(finalSystem, userPrompt, reply => onReply?.Invoke(reply ?? "")));
	}
	private NPCAIModelSettings BuildEffectiveSettings()
	{
		if (settingsAsset != null)
		{
			var prof = settingsAsset.GetActiveProfile();

			// формируем временный SO, чтобы провайдеры читали единый источник
			var tmp = ScriptableObject.CreateInstance<NPCAIModelSettings>();
			tmp.provider = settingsAsset.provider;
			// переносим пол€ из профил€
			var tprof = new ProviderProfile
			{
				provider = prof.provider,
				apiKey = prof.apiKey,
				model = prof.model,
				temperature = prof.temperature,
				maxTokens = prof.maxTokens,
				defaultResponseLanguage = string.IsNullOrWhiteSpace(prof.defaultResponseLanguage) ? "auto" : prof.defaultResponseLanguage
			};
			// сохраним его внутрь tmp, чтобы GetActiveProfile() не падал, если кто-то вызовет
			var listField = typeof(NPCAIModelSettings).GetField("profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var list = new List<ProviderProfile> { tprof };
			listField?.SetValue(tmp, list);

			return tmp;
		}
		else
		{
			var tmp = ScriptableObject.CreateInstance<NPCAIModelSettings>();
			tmp.provider = provider;
			// аналогично создаЄм профиль из инлайн-полей
			var tprof = new ProviderProfile
			{
				provider = provider,
				apiKey = apiKey,
				model = model,
				temperature = temperature,
				maxTokens = maxTokens,
				defaultResponseLanguage = string.IsNullOrWhiteSpace(defaultResponseLanguage) ? "auto" : defaultResponseLanguage
			};
			var listField = typeof(NPCAIModelSettings).GetField("profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			listField?.SetValue(tmp, new List<ProviderProfile> { tprof });
			return tmp;
		}
	}

	private IChatProvider CreateProvider(NPCAIModelSettings s)
	{
		switch (s.provider)
		{
			case AIProviderType.DeepSeek:
				return new DeepSeekProvider(s);
			case AIProviderType.Anthropic:
				return new AnthropicProvider(s);
			default:
				return new OpenAIProvider(s);
		}
	}

	private string WithLanguageDirective(string baseSystem, string lang)
	{
		if (string.IsNullOrWhiteSpace(lang) || lang.Equals("auto", StringComparison.OrdinalIgnoreCase))
			return baseSystem ?? "";
		string directive = $"Always respond in {lang}. "
		  + "If the task requires a strict format (e.g., JSON or code), keep that exact format and do not translate keys or code identifiers; only translate free-form natural language.";
		return string.IsNullOrWhiteSpace(baseSystem) ? directive : baseSystem + "\n\n" + directive;
	}
}
