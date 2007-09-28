//
// LocalFileCopyHandler.cs
//
// Author:
//   Michael Hutchinson <m.j.hutchinson@gmail.com>
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (C) 2006 Michael Hutchinson
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Collections.Generic;

using MonoDevelop.Core;
using MonoDevelop.Projects;

namespace MonoDevelop.Deployment
{
	public class LocalFileCopyHandler : IFileCopyHandler
	{
		public virtual string Id {
			get { return "MonoDevelop.LocalFileCopyHandler"; }
		}
		
		public virtual string Name {
			get { return GettextCatalog.GetString ("Local Filesystem"); }
		}

		public FileCopyConfiguration CreateConfiguration ()
		{
			return new LocalFileCopyConfiguration ();
		}
		
		public virtual void CopyFiles (IProgressMonitor monitor, IFileReplacePolicy replacePolicy, FileCopyConfiguration copyConfig, DeployFileCollection deployFiles, DeployContext context)
		{
			string targetDirectory = ((LocalFileCopyConfiguration) copyConfig).TargetDirectory;
			
			if (string.IsNullOrEmpty (copyConfig.FriendlyLocation) || string.IsNullOrEmpty (targetDirectory))
				throw new InvalidOperationException ("Cannot deploy to unconfigured location.");
			
			List<DeployFileConf> files = new List<DeployFileConf> ();
			long totalFileSize = 0;
			
			//pre-scan: ask all copy/replace questions first so user doesn't have to wait, and get 
			foreach (DeployFile df in deployFiles) {
				if (!context.IncludeFile (df))
					continue;
				
				DeployFileConf dfc = new DeployFileConf ();
				files.Add (dfc);
				dfc.SourceFile = df.SourcePath;
				dfc.FileSize = FileSize (dfc.SourceFile);
				totalFileSize += dfc.FileSize;
				dfc.TargetFile = Path.Combine (targetDirectory, context.GetResolvedPath (df.TargetDirectoryID, df.RelativeTargetPath));
				if (dfc.TargetFile == null)
					throw new InvalidOperationException (GettextCatalog.GetString ("Could not resolve target directory ID \"{0}\"", df.TargetDirectoryID));
				
				
				if (FileExists (dfc.TargetFile)) {
					dfc.SourceModified = File.GetLastWriteTime (dfc.SourceFile);
					dfc.TargetModified = GetTargetModificationTime (dfc.TargetFile);
					dfc.ReplaceMode = replacePolicy.GetReplaceAction (dfc.SourceFile, dfc.SourceModified, dfc.TargetFile, dfc.TargetModified);
					if (dfc.ReplaceMode == FileReplaceMode.Abort) {
						monitor.Log.WriteLine (GettextCatalog.GetString ("Deployment aborted: target file {0} already exists.", dfc.TargetFile));
						throw new OperationCanceledException ();
					}
				}
			}
			
			//PROBLEM: monitor takes ints, file sizes are longs
			//HOWEVER: longs are excessively long for a progress bar
			//SOLUTION: assume total task has a length of 1000 (longer than this is probably unnecessary for a progress bar),
			//  and set up a callback system for translating the actual long number of bytes into a portion of this
			const int progressBarLength = 1000;
			long stepSize = totalFileSize / progressBarLength;
			long carry = 0; 
			monitor.BeginTask (copyConfig.FriendlyLocation, progressBarLength);
			CopyReportCallback copyCallback = delegate (long bytes) {
				if (monitor.IsCancelRequested)
					return false;
				int steps = (int) (bytes / stepSize);
				carry += bytes % stepSize;
				if (carry > stepSize) {
					steps += 1;
					carry -= stepSize;
				}
				monitor.Step (steps);
				return true;
			};
			
			//now the actual copy
			foreach (DeployFileConf file in files) {
				//abort the copy if cancelling
				if (monitor.IsCancelRequested)
					break;
				
				EnsureDirectoryExists (Path.GetDirectoryName (file.TargetFile));
				
				if (file.ReplaceMode != FileReplaceMode.NotSet) {
					switch (file.ReplaceMode) {
					case FileReplaceMode.Skip:
						monitor.Log.WriteLine (GettextCatalog.GetString ("Skipped {0}: file exists.", file.TargetFile));
						copyCallback (file.FileSize);
						continue; //next file
					
					case FileReplaceMode.Replace:
						monitor.Log.WriteLine (GettextCatalog.GetString ("Replaced {0}.", file.TargetFile));
						break;
					
					case FileReplaceMode.ReplaceOlder:
						if (file.SourceModified > file.TargetModified) {
							monitor.Log.WriteLine (GettextCatalog.GetString ("Replacing {0}: existing file is older.", file.TargetFile));
						} else {
							if (file.SourceModified == file.TargetModified)
								monitor.Log.WriteLine (GettextCatalog.GetString ("Skipped {0}: existing file is the same age.", file.TargetFile));
							else
								monitor.Log.WriteLine (GettextCatalog.GetString ("Skipped {0}: existing file is newer.", file.TargetFile));
							copyCallback (file.FileSize);
							continue; //next file
						}
						break;
					}
				}
				else {
					monitor.Log.WriteLine (GettextCatalog.GetString ("Deployed file {0}.", file.TargetFile));
				}
				
				CopyFile (file.SourceFile, file.TargetFile, copyCallback);
			}
			
			monitor.EndTask ();
		}
		
		private class DeployFileConf
		{
			public FileReplaceMode ReplaceMode = FileReplaceMode.NotSet;
			public long FileSize;
			public string TargetFile = null;
			public DateTime TargetModified;
			public string SourceFile = null;
			public DateTime SourceModified;
		}
		
		// These simple access routines are used by the base implementation of CopyFiles.
		// They can be overridden so that CopyFiles works with other filesystems.
		// They can be ignored if CopyFiles is overridden.
		
		protected virtual void SetPrefix ()
		{
		}
		
		protected virtual bool FileExists (string file)
		{
			return File.Exists (file);
		}
		
		protected virtual long FileSize (string file)
		{
			FileInfo fInfo = new FileInfo (file);
			return fInfo.Length;
		}
		
		protected virtual void CopyFile (string source, string target, CopyReportCallback report)
		{
			File.Copy (source, target, true);
			report (FileSize (source));
		}
		 
		protected virtual DateTime GetTargetModificationTime (string targetFile)
		{
			return File.GetLastWriteTime (targetFile);
		}
		
		protected virtual void EnsureDirectoryExists (string directory)
		{
			if (!Directory.Exists (directory))
				Directory.CreateDirectory (directory);
		}
		
		//returns false if operation aborted
		protected delegate bool CopyReportCallback (long bytes);
	}
}

