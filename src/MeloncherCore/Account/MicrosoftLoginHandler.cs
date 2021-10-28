﻿using System;
using System.IO;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using XboxAuthNet.OAuth;
using XboxAuthNet.XboxLive;

namespace MeloncherCore.Account
{
	public class MicrosoftLoginHandler
	{
		public static readonly string DefaultClientId = "00000000402B5328";

		public MicrosoftLoginHandler() :
			this(new MicrosoftOAuth(DefaultClientId, XboxAuth.XboxScope))
		{
		}

		public MicrosoftLoginHandler(MicrosoftOAuth oAuth)
		{
			var defaultPath = Path.Combine(MinecraftPath.GetOSDefaultPath(), "cml_xsession.json");

			// this.cacheManager = new JsonFileCacheManager<SessionCache>(defaultPath);
			OAuth = oAuth;
		}

		// public MicrosoftLoginHandler(ICacheManager<SessionCache> cacheManager)
		// {
		//     this.cacheManager = cacheManager;
		//     this.OAuth = new MicrosoftOAuth(DefaultClientId, XboxAuth.XboxScope);
		// }
		//
		// public MicrosoftLoginHandler(MicrosoftOAuth oAuth, ICacheManager<SessionCache> cacheManager)
		// {
		//     this.cacheManager = cacheManager;
		//     this.OAuth = oAuth;
		// }

		public MicrosoftOAuth OAuth { get; }
		// private readonly ICacheManager<SessionCache> cacheManager;

		// public MinecraftAccount? sessionCache;

		// private void readSessionCache()
		// {
		//     sessionCache = cacheManager.ReadCache();
		// }
		//
		// private void saveSessionCache()
		// {
		//     cacheManager.SaveCache(sessionCache ?? new SessionCache());
		// }


		public bool Validate(McAccount mcAccount)
		{
			return mcAccount.XboxSession != null && DateTime.Now <= mcAccount.XboxSession.ExpiresOn;
		}

		public bool Refresh(McAccount mcAccount)
		{
			var mcToken = mcAccount?.XboxSession;
			var msToken = mcAccount?.MicrosoftOAuthSession;

			if (!OAuth.TryGetTokens(out msToken, msToken?.RefreshToken)) // failed to refresh ms
				return false;

			// success to refresh ms
			mcToken = mcLogin(msToken);
			var newAcc = getGameSession(msToken, mcToken);
			mcAccount.GameSession = newAcc.GameSession;
			mcAccount.XboxSession = newAcc.XboxSession;
			mcAccount.MicrosoftOAuthSession = newAcc.MicrosoftOAuthSession;
			return true;
		}

		// public MinecraftAccount? LoginFromCache(MinecraftAccount minecraftAccount)
		// {
		//
		//     var mcToken = minecraftAccount?.XboxSession;
		//     var msToken = minecraftAccount?.MicrosoftOAuthSession;
		//
		//     if (mcToken == null || DateTime.Now > mcToken.ExpiresOn) // invalid mc session
		//     {
		//         if (!OAuth.TryGetTokens(out msToken, msToken?.RefreshToken)) // failed to refresh ms
		//             return null;
		//         
		//         // success to refresh ms
		//         mcToken = mcLogin(msToken);
		//     }
		//
		//     return getGameSession(msToken, mcToken);
		// }

		public bool CheckOAuthLoginSuccess(string url)
		{
			return OAuth.CheckLoginSuccess(url);
		}

		public McAccount LoginFromOAuth()
		{
			var result = OAuth.TryGetTokens(out var msToken); // get token
			if (!result)
				throw new MicrosoftOAuthException(msToken);

			var mcToken = mcLogin(msToken);
			return getGameSession(msToken, mcToken);
		}

		public string CreateOAuthUrl()
		{
			return OAuth.CreateUrl();
		}

		// public void ClearCache()
		// {
		//     if (sessionCache != null)
		//     {
		//         sessionCache.XboxSession = null;
		//         sessionCache.GameSession = null;
		//         sessionCache.MicrosoftOAuthSession = null;
		//     }
		//
		//     // saveSessionCache();
		// }

		private McAccount getGameSession(MicrosoftOAuthResponse? msToken, AuthenticationResponse mcToken)
		{
			var sessionCache = new McAccount(getSession(mcToken), msToken, mcToken);
			return sessionCache;
		}

		// private MSession getGameSession(MicrosoftOAuthResponse? msToken, AuthenticationResponse mcToken)
		// {
		//     if (sessionCache == null)
		//         sessionCache = new MinecraftAccount();
		//     
		//     sessionCache.GameSession ??= getSession(mcToken);
		//     sessionCache.XboxSession = mcToken;
		//     sessionCache.MicrosoftOAuthSession = msToken;
		//
		//     // saveSessionCache();
		//     return sessionCache.GameSession;
		// }

		private AuthenticationResponse mcLogin(MicrosoftOAuthResponse? msToken)
		{
			if (msToken == null)
				throw new ArgumentNullException(nameof(msToken));
			if (!msToken.IsSuccess)
				throw new ArgumentException("msToken was failed");
			if (msToken.AccessToken == null)
				throw new ArgumentNullException(nameof(msToken.AccessToken));

			var xbox = new XboxAuth();
			var rps = xbox.ExchangeRpsTicketForUserToken(msToken.AccessToken);

			if (!rps.IsSuccess || string.IsNullOrEmpty(rps.Token))
				throw new XboxAuthException($"ExchangeRpsTicketForUserToken\n{rps.Error}\n{rps.Message}", null);

			var xsts = xbox.ExchangeTokensForXstsIdentity(
				rps.Token,
				null,
				null,
				XboxMinecraftLogin.RelyingParty,
				null);

			if (!xsts.IsSuccess || string.IsNullOrEmpty(xsts.UserHash) || string.IsNullOrEmpty(xsts.Token)) throw createXboxException(xsts);

			var mcLogin = new XboxMinecraftLogin();
			var mcToken = mcLogin.LoginWithXbox(xsts.UserHash, xsts.Token);
			return mcToken;
		}

		private Exception createXboxException(XboxAuthResponse xsts)
		{
			string msg = "";
			if (xsts.Error == XboxAuthResponse.ChildError || xsts.Error == "2148916236")
				msg = "xbox_error_child";
			else if (xsts.Error == XboxAuthResponse.NoXboxAccountError)
				msg = "xbox_error_noaccount";
			else if (string.IsNullOrEmpty(xsts.UserHash))
				msg = "empty_userhash";
			else if (string.IsNullOrEmpty(xsts.Token))
				msg = "empty_token";

			string errorCode;
			try
			{
				var errorCodeStr = xsts.Error?.Trim();
				if (string.IsNullOrEmpty(errorCodeStr))
				{
					errorCode = "no_error_msg";
				}
				else
				{
					var errorInt = long.Parse(errorCodeStr);
					errorCode = errorInt.ToString("x");
				}
			}
			catch
			{
				errorCode = xsts.Error ?? "no_error_msg";
			}

			if (string.IsNullOrEmpty(msg))
				msg = errorCode;

			return new XboxAuthException(msg, errorCode, xsts.Message ?? "no_error_msg");
			//return new XboxAuthException(msg, null);
		}

		private MSession getSession(AuthenticationResponse mcToken)
		{
			// 6. get minecraft profile (username, uuid)

			if (mcToken == null)
				throw new ArgumentNullException(nameof(mcToken));
			if (mcToken.AccessToken == null)
				throw new ArgumentNullException(nameof(mcToken.AccessToken));

			if (!CmlLib.Core.Mojang.MojangAPI.CheckGameOwnership(mcToken.AccessToken))
				throw new InvalidOperationException("mojang_nogame");

			var profile = CmlLib.Core.Mojang.MojangAPI.GetProfileUsingToken(mcToken.AccessToken);
			return new MSession
			{
				AccessToken = mcToken.AccessToken,
				UUID = profile.UUID,
				Username = profile.Name
				// UserType = "msa"
			};
		}
	}
}