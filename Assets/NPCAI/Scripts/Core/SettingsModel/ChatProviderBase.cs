using System;
using System.Collections;
using System.Text;
using UnityEngine;

public abstract class ChatProviderBase : IChatProvider
{
	protected readonly NPCAIModelSettings S;
	protected ChatProviderBase(NPCAIModelSettings settings) { S = settings; }

	protected bool ValidateKey()
	{
		if (string.IsNullOrEmpty(GetApiKey()))
		{
			Debug.LogError($"{GetType().Name}: API key is empty.");
			return false;
		}
		return true;
	}

	protected virtual string GetApiKey() => S.GetActiveProfile().apiKey;

	public abstract IEnumerator SendChat(string systemPrompt, string userPrompt, Action<string> onReply);

	protected static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s ?? "");
}
