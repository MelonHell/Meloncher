﻿using System;
using System.IO;
using System.Net;
using System.Text;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.MojangLauncher;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// use new library:
// https://github.com/CmlLib/MojangAPI

namespace MeloncherCore.Account
{
	// public enum MLoginResult { Success, BadRequest, WrongAccount, NeedLogin, UnknownError, NoProfile }

	public class MojangLogin
	{
		public static readonly string DefaultLoginSessionFile = Path.Combine(MinecraftPath.GetOSDefaultPath(), "logintoken.json");

		public MojangLogin() : this(DefaultLoginSessionFile)
		{
		}

		public MojangLogin(string sessionCacheFilePath)
		{
			SessionCacheFilePath = sessionCacheFilePath;
		}

		public string SessionCacheFilePath { get; }
		public bool SaveSession { get; set; } = true;

		private string CreateNewClientToken()
		{
			return Guid.NewGuid().ToString().Replace("-", "");
		}

		private MSession createNewSession()
		{
			var session = new MSession();
			if (SaveSession)
			{
				session.ClientToken = CreateNewClientToken();
				writeSessionCache(session);
			}

			return session;
		}

		private void writeSessionCache(MSession session)
		{
			if (!SaveSession) return;
			var directoryPath = Path.GetDirectoryName(SessionCacheFilePath);
			if (!string.IsNullOrEmpty(directoryPath))
				Directory.CreateDirectory(directoryPath);

			var json = JsonConvert.SerializeObject(session);
			File.WriteAllText(SessionCacheFilePath, json, Encoding.UTF8);
		}

		public MSession ReadSessionCache()
		{
			if (File.Exists(SessionCacheFilePath))
			{
				var fileData = File.ReadAllText(SessionCacheFilePath, Encoding.UTF8);
				try
				{
					var session = JsonConvert.DeserializeObject<MSession>(fileData, new JsonSerializerSettings
					{
						NullValueHandling = NullValueHandling.Ignore
					}) ?? new MSession();

					if (SaveSession && string.IsNullOrEmpty(session.ClientToken))
						session.ClientToken = CreateNewClientToken();

					return session;
				}
				catch (JsonReaderException) // invalid json
				{
					return createNewSession();
				}
			}

			return createNewSession();
		}

		private HttpWebResponse mojangRequest(string endpoint, string postdata)
		{
			HttpWebRequest http = WebRequest.CreateHttp(MojangServer.Auth + endpoint);
			http.ContentType = "application/json";
			http.Method = "POST";

			using StreamWriter req = new(http.GetRequestStream());
			req.Write(postdata);
			req.Flush();

			HttpWebResponse res = http.GetResponseNoException();
			return res;
		}

		private MLoginResponse parseSession(string json, string? clientToken)
		{
			var job = JObject.Parse(json); //json parse

			var profile = job["selectedProfile"];
			if (profile == null) return new MLoginResponse(MLoginResult.NoProfile, null, null, json);

			var session = new MSession
			{
				AccessToken = job["accessToken"]?.ToString(),
				UUID = profile["id"]?.ToString(),
				Username = profile["name"]?.ToString(),
				ClientToken = clientToken
			};

			writeSessionCache(session);
			return new MLoginResponse(MLoginResult.Success, session, null, null);
		}

		private MLoginResponse errorHandle(string json)
		{
			try
			{
				JObject job = JObject.Parse(json);

				string error = job["error"]?.ToString() ?? ""; // error type
				string errorMessage = job["message"]?.ToString() ?? ""; // detail error message
				MLoginResult result;

				switch (error)
				{
					case "Method Not Allowed":
					case "Not Found":
					case "Unsupported Media Type":
						result = MLoginResult.BadRequest;
						break;
					case "IllegalArgumentException":
					case "ForbiddenOperationException":
						result = MLoginResult.WrongAccount;
						break;
					default:
						result = MLoginResult.UnknownError;
						break;
				}

				return new MLoginResponse(result, null, errorMessage, json);
			}
			catch (Exception ex)
			{
				return new MLoginResponse(MLoginResult.UnknownError, null, ex.ToString(), json);
			}
		}

		public MLoginResponse Authenticate(string id, string pw)
		{
			var clientToken = ReadSessionCache().ClientToken;
			return Authenticate(id, pw, clientToken);
		}

		public MLoginResponse Authenticate(string id, string pw, string? clientToken)
		{
			JObject req = new()
			{
				{"username", id},
				{"password", pw},
				{"clientToken", clientToken},
				{
					"agent", new JObject
					{
						{"name", "Minecraft"},
						{"version", 1}
					}
				}
			};

			HttpWebResponse resHeader = mojangRequest("authenticate", req.ToString());

			using StreamReader res = new(resHeader.GetResponseStream());
			string rawResponse = res.ReadToEnd();
			if (resHeader.StatusCode == HttpStatusCode.OK) // ResultCode == 200
				return parseSession(rawResponse, clientToken);
			return errorHandle(rawResponse);
		}

		public MLoginResponse TryAutoLogin()
		{
			MSession session = ReadSessionCache();
			return TryAutoLogin(session);
		}

		public MLoginResponse TryAutoLogin(MSession session)
		{
			try
			{
				MLoginResponse result = Validate(session);
				if (result.Result != MLoginResult.Success)
					result = Refresh(session);
				return result;
			}
			catch (Exception ex)
			{
				return new MLoginResponse(MLoginResult.UnknownError, null, ex.ToString(), null);
			}
		}

		public MLoginResponse TryAutoLoginFromMojangLauncher()
		{
			var mojangAccounts = MojangLauncherAccounts.FromDefaultPath();
			var activeAccount = mojangAccounts.GetActiveAccount();

			if (activeAccount == null)
				return new MLoginResponse(MLoginResult.NeedLogin, null, null, null);

			return TryAutoLogin(activeAccount.ToSession());
		}

		public MLoginResponse TryAutoLoginFromMojangLauncher(string accountFilePath)
		{
			var mojangAccounts = MojangLauncherAccounts.FromFile(accountFilePath);
			var activeAccount = mojangAccounts.GetActiveAccount();

			if (activeAccount == null)
				return new MLoginResponse(MLoginResult.NeedLogin, null, null, null);

			return TryAutoLogin(activeAccount.ToSession());
		}

		public MLoginResponse Refresh()
		{
			MSession session = ReadSessionCache();
			return Refresh(session);
		}

		public MLoginResponse Refresh(MSession session)
		{
			JObject req = new()
			{
				{"accessToken", session.AccessToken},
				{"clientToken", session.ClientToken}
				// { "selectedProfile", new JObject
				//     {
				//         { "id", session.UUID },
				//         { "name", session.Username }
				//     }
				// }
			};

			HttpWebResponse resHeader = mojangRequest("refresh", req.ToString());
			using StreamReader res = new(resHeader.GetResponseStream());
			string rawResponse = res.ReadToEnd();

			if ((int) resHeader.StatusCode / 100 == 2)
				return parseSession(rawResponse, session.ClientToken);
			return errorHandle(rawResponse);
		}

		public MLoginResponse Validate()
		{
			MSession session = ReadSessionCache();
			return Validate(session);
		}

		public MLoginResponse Validate(MSession session)
		{
			JObject req = new()
			{
				{"accessToken", session.AccessToken},
				{"clientToken", session.ClientToken}
			};

			HttpWebResponse resHeader = mojangRequest("validate", req.ToString());
			if (resHeader.StatusCode == HttpStatusCode.NoContent) // StatusCode == 204
				return new MLoginResponse(MLoginResult.Success, session, null, null);
			return new MLoginResponse(MLoginResult.NeedLogin, null, null, null);
		}

		public void DeleteTokenFile()
		{
			if (File.Exists(SessionCacheFilePath))
				File.Delete(SessionCacheFilePath);
		}

		public bool Invalidate()
		{
			MSession session = ReadSessionCache();
			return Invalidate(session);
		}

		public bool Invalidate(MSession session)
		{
			JObject job = new()
			{
				{"accessToken", session.AccessToken},
				{"clientToken", session.ClientToken}
			};

			HttpWebResponse res = mojangRequest("invalidate", job.ToString());
			return res.StatusCode == HttpStatusCode.NoContent; // 204
		}

		public bool Signout(string id, string pw)
		{
			JObject job = new()
			{
				{"username", id},
				{"password", pw}
			};

			HttpWebResponse res = mojangRequest("signout", job.ToString());
			return res.StatusCode == HttpStatusCode.NoContent; // 204
		}
	}

	internal static class HttpWebResponseExt
	{
		public static HttpWebResponse GetResponseNoException(this HttpWebRequest req)
		{
			try
			{
				return (HttpWebResponse) req.GetResponse();
			}
			catch (WebException we)
			{
				var resp = we.Response as HttpWebResponse;
				if (resp == null)
					throw;
				return resp;
			}
		}
	}
}