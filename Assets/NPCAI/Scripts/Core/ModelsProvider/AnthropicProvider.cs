using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// Anthropic Claude (Messages API v1)
public class AnthropicProvider : ChatProviderBase
{
	private const string Endpoint = "https://api.anthropic.com/v1/messages";
	// Док. версия заголовка — проверь актуальную при необходимости
	private const string AnthropicVersion = "2023-06-01";

	public AnthropicProvider(NPCAIModelSettings s) : base(s) { }

	public override IEnumerator SendChat(string systemPrompt, string userPrompt, Action<string> onReply)
	{
		var prof = S.GetActiveProfile();
		if (string.IsNullOrEmpty(prof.apiKey))
		{
			Debug.LogError("AnthropicProvider: API key is empty.");
			onReply?.Invoke("");
			yield break;
		}

		var req = new AnthReq
		{
			model = string.IsNullOrWhiteSpace(prof.model) ? "claude-3-5-sonnet-latest" : prof.model,
			max_tokens = Mathf.Max(16, prof.maxTokens),
			temperature = Mathf.Clamp01(prof.temperature),
			system = systemPrompt ?? "",
			messages = new List<AnthMsg> {
				new AnthMsg {
					role = "user",
					content = new List<AnthContent> {
						new AnthContent { type = "text", text = userPrompt ?? "" }
					}
				}
			}
		};

		string json = JsonUtility.ToJson(req);

		using (var r = new UnityWebRequest(Endpoint, "POST"))
		{
			r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
			r.downloadHandler = new DownloadHandlerBuffer();
			r.SetRequestHeader("Content-Type", "application/json");
			r.SetRequestHeader("x-api-key", prof.apiKey);
			r.SetRequestHeader("anthropic-version", AnthropicVersion);

			yield return r.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
			bool ok = r.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(r.isNetworkError || r.isHttpError);
#endif
			if (!ok)
			{
				Debug.LogError("AnthropicProvider Error: " + r.error + "\n" + r.downloadHandler.text);
				onReply?.Invoke("");
				yield break;
			}

			try
			{
				// JsonUtility плохо маппит вложенные массивы Anthropic; вытащим первый текст грубой вытяжкой.
				string raw = r.downloadHandler.text;
				string text = ExtractFirstText(raw);
				onReply?.Invoke(text);
			}
			catch (Exception e)
			{
				Debug.LogError("AnthropicProvider parse error: " + e);
				onReply?.Invoke("");
			}
		}
	}

	// --- простая вытяжка первого "text":"..." ---
	private static string ExtractFirstText(string json)
	{
		const string key = "\"text\":\"";
		int i = json.IndexOf(key, StringComparison.Ordinal);
		if (i < 0) return "";
		i += key.Length;
		int j = i;
		var sb = new StringBuilder();
		bool esc = false;
		while (j < json.Length)
		{
			char c = json[j++];
			if (esc) { sb.Append(c); esc = false; continue; }
			if (c == '\\') { esc = true; continue; }
			if (c == '"') break;
			sb.Append(c);
		}
		return sb.ToString().Trim();
	}

	// ---- DTO для Anthropic ----
	[Serializable] private class AnthContent { public string type = "text"; public string text; }
	[Serializable] private class AnthMsg { public string role; public List<AnthContent> content; }
	[Serializable]
	private class AnthReq
	{
		public string model;
		public List<AnthMsg> messages;
		public int max_tokens;
		public float temperature;
		public string system;
	}
}
