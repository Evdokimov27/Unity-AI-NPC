using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class ChatGPTClient : MonoBehaviour
{
	[Header("OpenAI Settings")]
	[Tooltip("Your OpenAI API key (sk-...)")]
	public string apiKey = "sk-...";

	[Tooltip("Model to use (e.g. gpt-4o-mini or gpt-3.5-turbo)")]
	public string model = "gpt-4o-mini";

	[Tooltip("Temperature controls randomness: 0 = stable, 1 = creative")]
	[Range(0f, 1f)] public float temperature = 0.7f;

	[Header("Language")]
	[Tooltip("Default response language. Use 'auto' to not enforce a language. Examples: 'ru', 'en', 'de-DE', 'Spanish'.")]
	public string defaultResponseLanguage = "auto";

	private const string endpoint = "https://api.openai.com/v1/chat/completions";

	/// <summary>
	/// Ask ChatGPT and get reply via callback.
	/// responseLanguageOverride: if not null/empty and not 'auto', forces responses in that language.
	/// </summary>
	public void Ask(string systemPrompt, string userPrompt, Action<string> onReply, string responseLanguageOverride = null)
	{
		string finalSystem = WithLanguageDirective(systemPrompt, responseLanguageOverride);
		StartCoroutine(Send(finalSystem, userPrompt, onReply));
	}

	private IEnumerator Send(string systemPrompt, string userPrompt, Action<string> onReply)
	{
		if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("sk-") == false)
		{
			Debug.LogError("ChatGPTClient: Invalid API key. Set it in the inspector.");
			onReply?.Invoke("");
			yield break;
		}

		var req = new ChatRequest
		{
			model = model,
			temperature = temperature,
			messages = new[]
			{
				new ChatMessage { role = "system", content = systemPrompt },
				new ChatMessage { role = "user",   content = userPrompt   }
			}
		};

		string json = JsonUtility.ToJson(req);

		using (var request = new UnityWebRequest(endpoint, "POST"))
		{
			byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
			request.uploadHandler = new UploadHandlerRaw(bodyRaw);
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Authorization", "Bearer " + apiKey);

			yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
			if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
			{
				Debug.LogError("ChatGPTClient Error: " + request.error + "\n" + request.downloadHandler.text);
				onReply?.Invoke("");
			}
			else
			{
				string responseText = request.downloadHandler.text;
				string parsed = ParseResponse(responseText);
				onReply?.Invoke(parsed);
			}
		}
	}

	private string ParseResponse(string json)
	{
		try
		{
			ChatGPTResponse response = JsonUtility.FromJson<ChatGPTResponse>(json);
			if (response != null && response.choices != null && response.choices.Length > 0)
			{
				return response.choices[0]?.message?.content?.Trim() ?? "";
			}
		}
		catch (Exception e)
		{
			Debug.LogError("ChatGPTClient: Failed to parse response. " + e);
		}
		return "";
	}

	private string WithLanguageDirective(string baseSystem, string overrideLang)
	{
		string lang = (overrideLang ?? defaultResponseLanguage ?? "auto").Trim();
		if (string.IsNullOrEmpty(lang) || string.Equals(lang, "auto", StringComparison.OrdinalIgnoreCase))
			return baseSystem ?? "";

		string directive =
			$"Always respond in {lang}. " +
			"If the task requires a strict format (e.g., JSON or code), keep that exact format and do not translate keys or code identifiers; " +
			"only translate free-form natural language.";

		if (string.IsNullOrWhiteSpace(baseSystem)) return directive;
		return baseSystem + "\n\n" + directive;
	}

	[Serializable]
	private class ChatMessage
	{
		public string role;
		public string content;
	}

	[Serializable]
	private class ChatRequest
	{
		public string model;
		public float temperature;
		public ChatMessage[] messages;
	}

	[Serializable]
	private class ChatGPTResponse
	{
		public Choice[] choices;
	}

	[Serializable]
	private class Choice
	{
		public Message message;
	}

	[Serializable]
	private class Message
	{
		public string role;
		public string content;
	}
}
