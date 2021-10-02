﻿using Apex.MVVM;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft.UI.Wpf;
using MeloncherCore.Account;
using MeloncherCore.Launcher;
using MeloncherCore.Version;
using MeloncherWPF.Views.Windows;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace MeloncherWPF.ViewModels
{
	internal class MainViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public string TestConsole { get; set; } = "";
		public string Title { get; set; } = "Meloncher";
		public string McVersionName { get; set; } = "1.12.2";
		public string Username { get; set; } = "Steve";
		public bool Optifine { get; set; } = true;
		public bool Offline { get; set; } = false;
		public int ProgressValue { get; set; }
		public string ProgressText { get; set; }
		public bool ProgressHidden { get; set; } = true;
		public bool IsNotStarted { get; set; } = true;

		private McLauncher mcLauncher;
		public MainViewModel()
		{
			ServicePointManager.DefaultConnectionLimit = 512;
			PlayButtonCommand = new Command(OnPlayButtonCommandExecuted);
			OpenAccountsWindowCommand = new Command(OnOpenAccountsWindowCommandExecuted);
			DeleteAccountCommand = new Command(OnDeleteAccountCommandExecuted);
			AddMicrosoftCommand = new Command(OnAddMicrosoftCommandExecuted);
			AddMojangCommand = new Command(OnAddMojangCommandExecuted);
			AddOfflineCommand = new Command(OnAddOfflineCommandExecuted);
			//mcLauncher = new McLauncher(new ExtMinecraftPath("D:\\MeloncherNetTest"));
			mcLauncher = new McLauncher(new ExtMinecraftPath());
			mcLauncher.FileChanged += (e) =>
			{
				//TestLog(String.Format("[{0}] {1} - {2}/{3}", e.FileKind.ToString(), e.FileName, e.ProgressedFileCount, e.TotalFileCount));
				//ProgressText = String.Format("[{0}] {1} - {2}/{3}", e.FileKind.ToString(), e.FileName, e.ProgressedFileCount, e.TotalFileCount);
				ProgressText = "Загрузка " + e.Type;
			};
			mcLauncher.ProgressChanged += (s, e) =>
			{
				ProgressValue = e.ProgressPercentage;
			};
			accountStorage = new AccountStorage(mcLauncher.MinecraftPath);
			accountStorage.CollectionChanged += (object sender, NotifyCollectionChangedEventArgs e) => Accounts = new ObservableCollection<MSession>(accountStorage);
			Accounts = new ObservableCollection<MSession>(accountStorage);
		}

		private AccountStorage accountStorage;
		public ObservableCollection<MSession> Accounts { get; set; }
		public int selectedAccount { get; set; } = 0;

		public Command DeleteAccountCommand { get; }
		private void OnDeleteAccountCommandExecuted(Object p)
		{
			accountStorage.RemoveAt(selectedAccount);
		}

		public Command AddMicrosoftCommand { get; }
		private void OnAddMicrosoftCommandExecuted(object p)
		{
			MicrosoftLoginWindow loginWindow = new MicrosoftLoginWindow();
			MSession session = loginWindow.ShowLoginDialog();
			accountStorage.Add(session);
		}

		public Command AddMojangCommand { get; }
		private void OnAddMojangCommandExecuted(object p)
		{
			var dialog = new AddAccountWindow();
			if (dialog.ShowDialog() == true)
			{
				var login = new MLogin();
				var resp = login.Authenticate(dialog.ResponseLogin, dialog.ResponsePass);
				MSession session = resp.Session;
				accountStorage.Add(session);
			}
		}

		public Command AddOfflineCommand { get; }
		private void OnAddOfflineCommandExecuted(object p)
		{
			var dialog = new AddAccountWindow();
			if (dialog.ShowDialog() == true)
			{
				MSession session = MSession.GetOfflineSession(dialog.ResponseLogin);
				accountStorage.Add(session);
			}
		}

		void TestLog(string text)
		{
			TestConsole = text + "\n" + TestConsole;
		}

		public Command OpenAccountsWindowCommand { get; }
		private void OnOpenAccountsWindowCommandExecuted(object p)
		{
			//accountsWindow.Activate();
		}

		public ICommand PlayButtonCommand { get; }
		private void OnPlayButtonCommandExecuted(object p)
		{
			//MicrosoftLoginWindow loginWindow = new MicrosoftLoginWindow();
			//MSession session = loginWindow.ShowLoginDialog();
			MSession session = MSession.GetOfflineSession("Gomosek228");
			if (Accounts.Count > 0)
				session = Accounts[selectedAccount];
			new Task(async () => {

				//var path = new ExtMinecraftPath("D:\\MeloncherNetTest", $"profiles\\qwe");
				//AccountStorage accountStorage = new AccountStorage(mcLauncher.MinecraftPath);
				//await accountStorage.ReadFile();
				//accountStorage.Add(session);
				//await accountStorage.SaveFile();

				IsNotStarted = false;
				ProgressHidden = false;
				Title = "Meloncher " + McVersionName;
				//MSession asdsdaf = MSession.GetOfflineSession(Username);
				await mcLauncher.Launch(new McVersion(McVersionName, "Test", "Test-" + McVersionName), session, Offline, Optifine);
				ProgressValue = 0;
				ProgressText = null;
				ProgressHidden = true;
				IsNotStarted = true;
				Title = "Meloncher";
			}).Start();
		}
	}
}
