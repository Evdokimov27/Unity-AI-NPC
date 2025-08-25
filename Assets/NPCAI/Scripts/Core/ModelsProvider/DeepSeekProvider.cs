using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// DeepSeek (OpenAI-compatible Chat Completions)
public class DeepSeekProvider : ChatProviderBase
{
	private const string Endpoint = "https://api.deepseek.com/v1/chat/completions";

	public DeepSeekProvider(NPCAIModelSettings s) : base(s) { }

	public override IEnumerator SendChat(string systemPrompt, string userPrompt, Action<string> onReply)
	{
		var prof = S.GetActiveProfile();
		if (string.IsNullOrEmpty(prof.apiKey))
		{
			Debug.LogError("DeepSeekProvider: API key is empty.");
			onReply?.Invoke("");
			yield break;
		}

		var req = new ChatRequest
		{
			model = string.IsNullOrWhiteSpace(prof.model) ? "deepseek-chat" : prof.model, // например: deepseek-chat / deepseek-reasoner
			temperature = Mathf.Clamp01(prof.temperature),
			messages = new List<ChatMessage>
			{
				new ChatMessage { role = "system", content = systemPrompt ?? "" },
				new ChatMessage { role = "user",   content = userPrompt   ?? "" }
			}
		};

		string json = JsonUtility.ToJson(req);

		using (var r = new UnityWebRequest(Endpoint, "POST"))
		{
			r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
			r.downloadHandler = new DownloadHandlerBuffer();
			r.SetRequestHeader("Content-Type", "application/json");
			r.SetRequestHeader("Authorization", "Bearer " + prof.apiKey);

			yield return r.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
			bool ok = r.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(r.isNetworkError || r.isHttpError);
#endif
			if (!ok)
			{
				Debug.LogError("DeepSeekProvider Error: " + r.error + "\n" + r.downloadHandler.text);
				onReply?.Invoke("");
				yield break;
			}

			try
			{
				var resp = JsonUtility.FromJson<ChatResponse>(r.downloadHandler.text);
				string text = (resp != null && resp.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
					? (resp.choices[0].message.content ?? "").Trim()
					: "";
				onReply?.Invoke(text);
			}
			catch (Exception e)
			{
				Debug.LogError("DeepSeekProvider parse error: " + e);
				onReply?.Invoke("");
			}
		}
	}

	// ---- DTO shared with OpenAI-like APIs ----
	[Serializable] private class ChatMessage { public string role; public string content; }
	[Serializable] private class ChatRequest { public string model; public float temperature; public List<ChatMessage> messages; }
	[Serializable] private class ChatChoice { public ChatMessage message; }
	[Serializable] private class ChatResponse { public ChatChoice[] choices; }
}
