﻿using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using PassWinmenu.Configuration;

namespace PassWinmenu.ExternalPrograms
{
	/// <summary>
	/// Simple wrapper over git.
	/// </summary>
	internal class Git : IDisposable
	{
		private readonly Repository repo;
		private readonly FetchOptions fetchOptions;
		private readonly PushOptions pushOptions;

		/// <summary>
		/// Initialises the wrapper.
		/// </summary>
		/// <param name="repositoryPath">The repository git should operate on.</param>
		public Git(string repositoryPath)
		{
			repo = new Repository(repositoryPath);
			fetchOptions = new FetchOptions();
			pushOptions = new PushOptions();
		}

		public BranchTrackingDetails GetTrackingDetails() => repo.Head.TrackingDetails;

		private Signature BuildSignature() => repo.Config.BuildSignature(DateTimeOffset.Now);

		public void UseSsh()
		{
			// TODO: Implement new SSH feature
			//fetchOptions.CredentialsProvider = SshCredentialsProvider;
			//pushOptions.CredentialsProvider = SshCredentialsProvider;
		}

		public bool IsSshRemote() => IsSshUrl(repo.Network.Remotes[repo.Head.RemoteName].Url);

		private static bool IsSshUrl(string url)
		{
			// Git considers 'user@server:project.git' to be a valid remote URL, but it's
			// not actually a URL, so parsing it as one would fail.
			// Therefore, we need to check for this condition first.
			if (Regex.IsMatch(url, @".*@.*:.*")) return true;
			else return new Uri(url).Scheme == "git";
		}
		
		/// <summary>
		/// Rebases the current branch onto the branch it is tracking.
		/// </summary>
		public void Rebase()
		{
			var head = repo.Head;
			var tracked = head.TrackedBranch;

			var sig = BuildSignature();
			var result = repo.Rebase.Start(head, tracked, null, new Identity(sig.Name, sig.Email), new RebaseOptions());
			if (result.Status != RebaseStatus.Complete)
			{
				repo.Rebase.Abort();
				throw new InvalidOperationException($"Could not rebase {head.FriendlyName} onto {head.TrackedBranch.FriendlyName}");
			}
			else if (result.CompletedStepCount > 0)
			{
				// One or more commits were rebased
			}
			else
			{
				// Fast-forward or no upstream changes
			}
		}

		public void Fetch()
		{
			var head = repo.Head;
			var remote = repo.Network.Remotes[head.RemoteName];
			Commands.Fetch(repo, head.RemoteName, remote.FetchRefSpecs.Select(rs => rs.Specification), fetchOptions, null);
		}

		/// <summary>
		/// Pushes changes to remote.
		/// </summary>
		public void Push()
		{
			repo.Network.Push(repo.Head, pushOptions);
		}

		//private Credentials SshCredentialsProvider(string url, string usernameFromUrl, SupportedCredentialTypes types)
		//{
		//	if (!types.HasFlag(SupportedCredentialTypes.Ssh))
		//	{
		//		throw new InvalidOperationException("Cannot use the SSH credentials provider for non-SSH protocols.");
		//	}
		//	return FindSshKey(usernameFromUrl);
		//}

		//public Credentials FindSshKey(string username)
		//{
		//	var searchLocations = ConfigManager.Config.SshKeySearchLocations;

		//	foreach (var location in searchLocations)
		//	{
		//		var privateRsaKey = Path.Combine(location, "id_rsa");
		//		var publicRsaKey = Path.Combine(location, "id_rsa.pub");

		//		if (File.Exists(privateRsaKey) && File.Exists(publicRsaKey))
		//		{
		//			var sshUserKeyCredentials = new SshUserKeyCredentials
		//			{
		//				PrivateKey = privateRsaKey,
		//				PublicKey = publicRsaKey,
		//				Username = username,
		//				Passphrase = ""
		//			};
		//			return sshUserKeyCredentials;
		//		}
		//	}
		//	return null;
		//}

		public RepositoryStatus Commit()
		{
			var status = repo.RetrieveStatus();
			var staged = status.Where(e => (e.State
											& (FileStatus.DeletedFromIndex
											   | FileStatus.ModifiedInIndex
											   | FileStatus.NewInIndex
											   | FileStatus.RenamedInIndex
											   | FileStatus.TypeChangeInIndex)) > 0)
											   .ToList();
			if (staged.Any())
			{
				Commands.Unstage(repo, staged.Select(entry => entry.FilePath));
			}

			var filesToCommit = repo.RetrieveStatus();

			foreach (var entry in filesToCommit)
			{
				Commands.Stage(repo, entry.FilePath);
				var sig = repo.Config.BuildSignature(DateTimeOffset.Now);
				repo.Commit($"{GetVerbFromGitFileStatus(entry.State)} password store file {entry.FilePath}\n\n" +
							$"This commit was automatically generated by pass-winmenu.", sig, sig);
			}

			return filesToCommit;
		}

		private string GetVerbFromGitFileStatus(FileStatus status)
		{
			switch (status)
			{
				case FileStatus.DeletedFromWorkdir:
					return "Delete";
				case FileStatus.NewInWorkdir:
					return "Add";
				case FileStatus.ModifiedInWorkdir:
					return "Modify";
				case FileStatus.RenamedInWorkdir:
					return "Rename";
				case FileStatus.TypeChangeInWorkdir:
					return "Change filetype for";
				default:
					throw new ArgumentException(nameof(status));
			}
		}

		public void EditPassword(string passwordFilePath)
		{
			var status = repo.RetrieveStatus(passwordFilePath);
			if (status == FileStatus.ModifiedInWorkdir)
			{
				Commands.Stage(repo, passwordFilePath);
				var sig = BuildSignature();
				repo.Commit($"Edit password file {passwordFilePath}\n\n" +
							$"This commit was automatically generated by pass-winmenu.", sig, sig);
			}
		}

		public void AddPassword(string passwordFilePath)
		{
			var status = repo.RetrieveStatus(passwordFilePath);
			if (status == FileStatus.NewInWorkdir)
			{
				Commands.Stage(repo, passwordFilePath);
				var sig = BuildSignature();
				repo.Commit($"Add password file {passwordFilePath}\n\n" +
							$"This commit was automatically generated by pass-winmenu.", sig, sig);
			}
		}

		public void Dispose()
		{
			repo?.Dispose();
		}
	}
}
