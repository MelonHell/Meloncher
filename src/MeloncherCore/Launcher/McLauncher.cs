﻿using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Downloader;
using CmlLib.Core.Installer;
using CmlLib.Core.Version;
using CmlLib.Core.VersionLoader;
using MeloncherCore.Launcher.Events;
using MeloncherCore.Optifine;
using MeloncherCore.Options;
using MeloncherCore.Version;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace MeloncherCore.Launcher
{
	public class McLauncher
	{
		public ExtMinecraftPath MinecraftPath { get; set; }
		public McLauncher(ExtMinecraftPath MinecraftPath)
		{
			this.MinecraftPath = MinecraftPath;
		}

		public event McDownloadFileChangedHandler FileChanged;
		public event ProgressChangedEventHandler ProgressChanged;

		public McVersion Version { get; set; }
		public MSession Session { get; set; }
		public bool Offline { get; set; } = false;
		public bool UseOptifine { get; set; } = true;

		public async Task Update()
		{
			if (Offline) return;
			var path = MinecraftPath.CloneWithProfile("versions", Version.ProfileName);
			var launcher = new CMLauncher(path);
			launcher.FileChanged += (e) => FileChanged?.Invoke(new McDownloadFileChangedEventArgs(e.FileKind.ToString()));
			launcher.ProgressChanged += (s, e) => ProgressChanged?.Invoke(s, e);
			var launchOption = new MLaunchOption
			{
				StartVersion = launcher.GetVersion(Version.Name),
				MaximumRamMb = 1024,
				Session = Session,
				VersionType = "Meloncher",
				GameLauncherName = "Meloncher"
			};
			await launcher.CheckAndDownloadAsync(launchOption.StartVersion);
			if (UseOptifine)
			{
				var optifineInstaller = new OptifineInstallerBobcat();
				optifineInstaller.ProgressChanged += (s, e) => ProgressChanged?.Invoke(s, e);
				string ofVerName = null;
				ofVerName = await optifineInstaller.IsLatestInstalled(Version.Name, MinecraftPath);
				if (ofVerName == null)
				{
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, null));
					FileChanged?.Invoke(new McDownloadFileChangedEventArgs("Optifine"));
					ofVerName = await optifineInstaller.installOptifine(Version.Name, MinecraftPath, launchOption.StartVersion.JavaBinaryPath);
				}
			}
		}

		public async Task Launch()
		{
			var path = MinecraftPath.CloneWithProfile("versions", Version.ProfileName);
			var launcher = new CMLauncher(path);
			launcher.VersionLoader = new LocalVersionLoader(MinecraftPath);
			launcher.FileDownloader = null;
			launcher.FileChanged += (e) => FileChanged?.Invoke(new McDownloadFileChangedEventArgs(e.FileKind.ToString()));
			launcher.ProgressChanged += (s, e) => ProgressChanged?.Invoke(s, e);
			var launchOption = new MLaunchOption
			{
				StartVersion = launcher.GetVersion(Version.Name),
				MaximumRamMb = 1024,
				Session = Session,
				VersionType = "Meloncher",
				GameLauncherName = "Meloncher"
			};

			var sync = new McOptionsSync(path);
			FixJavaBinaryPath(MinecraftPath, launchOption.StartVersion);

			if (UseOptifine)
			{
				var optifineInstaller = new OptifineInstallerBobcat();
				string ofVerName = optifineInstaller.GetLatestInstalled(Version.Name, MinecraftPath);
				if (ofVerName != null)
				{
					launchOption.StartVersion = new LocalVersionLoader(MinecraftPath).GetVersionMetadatas().GetVersion(ofVerName);
					FixJavaBinaryPath(MinecraftPath, launchOption.StartVersion);
				}
			}

			var process = await launcher.CreateProcessAsync(launchOption);
			sync.Load();
			process.Start();
			var wt = new WindowTweaks(process);
			_ = wt.Borderless();
			process.WaitForExit();
			sync.Save();
		}
		private void FixJavaBinaryPath(MinecraftPath path, MVersion version)
		{
			if (!string.IsNullOrEmpty(version.JavaBinaryPath) && File.Exists(version.JavaBinaryPath))
				return;

			var javaVersion = version.JavaVersion;
			if (string.IsNullOrEmpty(javaVersion))
				javaVersion = "jre-legacy";

			version.JavaBinaryPath = Path.Combine(path.Runtime, javaVersion, "bin", MJava.GetDefaultBinaryName());
		}
	}
}